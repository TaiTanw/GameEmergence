using System;
using UnityEngine;

/// <summary>角色制作的有效属性范围；合法效果只在完整计算后限界一次。</summary>
[Serializable]
public struct PlayerAttributeRange
{
    public float Min;
    public float Max;
    public PlayerAttributeRange(float minimum, float maximum) { Min = minimum; Max = maximum; }

    public bool Validate(double basis, bool integer, out string error)
    {
        error = null;
        if (!PlayerUpgradeDefinition.Finite(Min) || !PlayerUpgradeDefinition.Finite(Max)
            || Min < 0f || Max < Min || double.IsNaN(basis) || double.IsInfinity(basis)
            || basis < Min || basis > Max)
            error = "基本值须位于有限非负的最小／最大范围内。";
        else if (integer && (Min != Math.Truncate(Min) || Max != Math.Truncate(Max)
            || Max > (double)int.MaxValue || basis != Math.Truncate(basis)))
            error = "空中跳基本值及上下限须为非负整数。";
        return error == null;
    }

    public float Limit(double value)
    {
        // 安全：非法运算与制作范围不能通过限界掩盖。
        if (!PlayerUpgradeDefinition.Finite(Min) || !PlayerUpgradeDefinition.Finite(Max)
            || Min < 0f || Max < Min || double.IsNaN(value) || double.IsInfinity(value))
            throw new InvalidOperationException("角色属性运算或有效范围无效。");
        return (float)Math.Min(Max, Math.Max(Min, value));
    }
}
