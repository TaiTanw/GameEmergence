using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 职能：本关可交互箱子的候选登记与几何发现；不持有选中关系，不进入物理接触查询。
/// 具体：箱子启用时登记、停用时注销；玩家按交互时用双方世界包围盒找侧面贴合的那一只。
/// 场景卸载时箱子逐个停用，列表随之清空。
/// </summary>
public static class LevelBoxContext
{
    private static readonly List<PhysicalBox> boxes = new List<PhysicalBox>();

    public static void Register(PhysicalBox box)
    {
        if (!boxes.Contains(box)) boxes.Add(box);
        LethalContact.Register(box, box.GetComponent<Collider2D>());
    }
    public static void Unregister(PhysicalBox box)
    {
        boxes.Remove(box);
        LethalContact.Unregister(box);
    }

    /// <summary>面板的占用提示：读已登记箱子的实际矩形，暂停未模拟仍可查到；不决定转换许可。</summary>
    public static int CountPushableOverlaps(BasicPhysicalObject region)
    {
        if (region == null || !region.gameObject.activeInHierarchy) return 0;
        Collider2D[] geometry = region.GetComponents<Collider2D>();
        int count = 0;
        foreach (PhysicalBox box in boxes)
        {
            // 收纳不在物理世界；带电外部箱仍能被推动，和普通暂停箱一样计入提示。
            if (box == null || box.IsStored || !box.isActiveAndEnabled
                || box.gameObject.scene != region.gameObject.scene) continue;
            BoxCollider2D boxGeometry = box.GetComponent<BoxCollider2D>();
            if (boxGeometry == null || !boxGeometry.enabled) continue;
            Bounds body = ReadBoxGeometry(boxGeometry);
            foreach (Collider2D collider in geometry)
            {
                if (collider == null || !collider.enabled) continue;
                Bounds area = collider is BoxCollider2D rectangle ? ReadBoxGeometry(rectangle) : collider.bounds;
                float overlapX = Mathf.Min(body.max.x, area.max.x) - Mathf.Max(body.min.x, area.min.x);
                float overlapY = Mathf.Min(body.max.y, area.max.y) - Mathf.Max(body.min.y, area.min.y);
                // 两轴正重合才在范围内；一个箱子碰到多个同源形状只计一次。
                if (overlapX <= OverlapEpsilon || overlapY <= OverlapEpsilon) continue;
                count++;
                break;
            }
        }
        return count;
    }

    private static Bounds ReadBoxGeometry(BoxCollider2D collider)
    {
        // 暂停刚体可能退出原生查询；直接从制作形状与实际刚体位置计算矩形。
        Vector2 half = collider.size * .5f;
        Vector3 first = collider.transform.TransformPoint(collider.offset - half);
        Bounds bounds = new Bounds(first, Vector3.zero);
        bounds.Encapsulate(collider.transform.TransformPoint(collider.offset + half));
        bounds.Encapsulate(collider.transform.TransformPoint(collider.offset + new Vector2(half.x, -half.y)));
        bounds.Encapsulate(collider.transform.TransformPoint(collider.offset + new Vector2(-half.x, half.y)));
        Rigidbody2D body = collider.attachedRigidbody;
        // 使用实际物理位置，避免显示插值的位置成为占用依据。
        if (body != null) bounds.center += (Vector3)(body.position - (Vector2)body.transform.position);
        return bounds;
    }

    /// <summary>规则显式恢复当前关的全部箱子；格子不裁剪刷新范围，不另存一份箱子清单。</summary>
    public static void RestoreDefaults(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (PhysicalBox box in boxes)
        {
            // 安全：已销毁引用或其它已加载场景的箱子不属于本次恢复。
            if (box == null || box.gameObject.scene != scene) continue;
            box.RestoreDefault();
        }
    }

    /// <summary>所有箱子的位置与参与资格同步后，再采样最终世界，避免读取其它箱子的旧位置。</summary>
    public static void Resample(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (PhysicalBox box in boxes)
        {
            if (box == null || box.gameObject.scene != scene) continue;
            box.ResampleEnvironment();
        }
    }

    /// <summary>
    /// 找本次交互的箱子：外部箱须侧面贴合（间隙不超过 outsideGap），收纳箱用较宽的 storedGap；站在箱顶或箱底不算。
    /// 距离取非负的矩形边缘间隙。只要有未收纳且可推动的侧面箱子，就先选它；否则才选收纳箱。同类里间隙更小者优先。
    /// </summary>
    public static PhysicalBox FindInteractTarget(Bounds body, float outsideGap, float storedGap)
    {
        PhysicalBox best = null;
        float bestGap = float.MaxValue;
        foreach (PhysicalBox box in boxes)
        {
            // 安全：已销毁但尚未注销的残留引用。
            if (box == null) continue;
            // 实际逻辑：不能被选中的外部箱（带电、暂停、无支撑）先排除，不抢走其它有效候选；收纳箱不参与运动仍可被发现。
            if (!box.IsStored && (box.IsElectrified || !box.IsMotionParticipating || !box.IsSupported)) continue;
            float maxGap = box.IsStored ? storedGap : outsideGap;
            if (!IsSideContact(body, box.InteractionBounds, maxGap, out float gap)) continue;
            gap = Mathf.Max(0f, gap);
            if (!PreferCandidate(box, gap, best, bestGap)) continue;
            best = box;
            bestGap = gap;
        }
        return best;
    }

    static bool PreferCandidate(PhysicalBox box, float gap, PhysicalBox best, float bestGap)
    {
        if (best == null) return true;
        // 实际逻辑：人在收纳矩形里时间隙会被记成 0，不能因此压过旁边能推的箱子。
        if (box.IsStored != best.IsStored) return !box.IsStored;
        return gap < bestGap;
    }

    /// <summary>返回与矩形重合的、除 self 外的第一只已收纳箱；仅接触边界不算重合，无重合为空。</summary>
    public static PhysicalBox FindOverlappingStored(Bounds rect, PhysicalBox self)
    {
        foreach (PhysicalBox box in boxes)
        {
            // 安全：残留引用或自身不参与比较。
            if (box == null || box == self || !box.IsStored) continue;
            Bounds other = box.InteractionBounds;
            float overlapX = Mathf.Min(rect.max.x, other.max.x) - Mathf.Max(rect.min.x, other.min.x);
            float overlapY = Mathf.Min(rect.max.y, other.max.y) - Mathf.Max(rect.min.y, other.min.y);
            // 实际逻辑：两轴都有正的重叠才算重合，避免贴边并排的箱子被误拒。
            if (overlapX > OverlapEpsilon && overlapY > OverlapEpsilon) return box;
        }
        return null;
    }

    private const float OverlapEpsilon = .001f;

    /// <summary>两个世界包围盒是否左右贴合：竖直重叠至少为较矮一方的一半，水平间隙不超过 maxGap。</summary>
    public static bool IsSideContact(Bounds body, Bounds other, float maxGap) => IsSideContact(body, other, maxGap, out _);

    private static bool IsSideContact(Bounds body, Bounds other, float maxGap, out float gap)
    {
        // 实际逻辑：间隙为负表示轻微嵌入，仍算贴合。
        gap = Mathf.Max(other.min.x - body.max.x, body.min.x - other.max.x);
        float overlapY = Mathf.Min(body.max.y, other.max.y) - Mathf.Max(body.min.y, other.min.y);
        // 实际逻辑：竖直重叠不足时视为上下关系（站在箱顶、箱子落下），不是侧面贴合。
        return gap <= maxGap && overlapY >= 0.5f * Mathf.Min(body.size.y, other.size.y);
    }
}
