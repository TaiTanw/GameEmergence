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

    public static void Register(PhysicalBox box) { if (!boxes.Contains(box)) boxes.Add(box); }
    public static void Unregister(PhysicalBox box) => boxes.Remove(box);

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

    /// <summary>身体是否碰到任一参与运动的带电方块：任意一面（含站在顶面）间隙不超过 tolerance 即算接触。</summary>
    public static bool TouchesElectrified(Bounds body, float tolerance)
    {
        // 实际逻辑：两轴间隙都不超过容差即接触；用几何接触事实，不看接触是否产生了推力。
        foreach (PhysicalBox box in boxes)
        {
            if (box == null || !box.IsElectrified || !box.IsMotionParticipating) continue;
            Bounds other = box.BodyBounds;
            // 实际逻辑：只比较 XY，Transform 的 Z 不同也不影响二维接触。
            float gapX = Mathf.Max(other.min.x - body.max.x, body.min.x - other.max.x);
            float gapY = Mathf.Max(other.min.y - body.max.y, body.min.y - other.max.y);
            if (gapX <= tolerance && gapY <= tolerance) return true;
        }
        return false;
    }

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
