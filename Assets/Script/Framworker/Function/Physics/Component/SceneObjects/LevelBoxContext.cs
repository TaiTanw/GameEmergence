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

    /// <summary>找与身体侧面贴合、水平间隙不超过 maxGap 的最近箱子；站在箱顶或箱底不算贴合。</summary>
    public static PhysicalBox FindSideContact(Bounds body, float maxGap)
    {
        PhysicalBox best = null;
        float bestGap = float.MaxValue;
        foreach (PhysicalBox box in boxes)
        {
            // 安全：已销毁但尚未注销的残留引用。
            if (box == null) continue;
            // 实际逻辑：暂停或关闭碰撞的箱子不接受新选中。
            if (!box.IsMotionParticipating) continue;
            // 实际逻辑：严格更近才替换，平局保持登记顺序。
            if (!IsSideContact(body, box.BodyBounds, maxGap, out float gap) || gap >= bestGap) continue;
            best = box;
            bestGap = gap;
        }
        return best;
    }

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
