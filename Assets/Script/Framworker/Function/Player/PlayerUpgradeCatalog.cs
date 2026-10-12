using System;
using System.Collections.Generic;
using UnityEngine;

// 存档使用固定编号；新增项目只能追加，不能重排已有编号。
public enum PlayerUpgradeKind { MovementSpeed = 0, JumpStrength = 1, AirJumps = 2, MapCost = 3, BackpackCost = 4, FormCost = 5 }
public enum PlayerUpgradeOperation { Multiply, Add }

/// <summary>每个能力只使用一条固定运算；所有等级共享价格和步长。</summary>
[Serializable]
public sealed class PlayerUpgradeDefinition
{
    public PlayerUpgradeKind Kind;
    public string Label;
    public bool Enabled = true;
    [Min(0)] public int MaxLevel = 10;
    [Min(0f)] public float Cost = 20f;
    public PlayerUpgradeOperation Operation;
    public float Step = 1.2f;

    public PlayerUpgradeDefinition() { }

    public PlayerUpgradeDefinition(PlayerUpgradeKind kind, string label, PlayerUpgradeOperation operation,
        float step, int maxLevel = 10, float cost = 20f)
    { Kind = kind; Label = label; Operation = operation; Step = step; MaxLevel = maxLevel; Cost = cost; }

    public bool IsConfigurationValid => Validate(out _);

    public bool Validate(out string error)
    {
        error = null;
        // 安全：配置不能把非法枚举、无穷值或负价格带进购买结算。
        if (!PlayerUpgradeCatalog.IsKnown(Kind) || string.IsNullOrWhiteSpace(Label) || MaxLevel < 0
            || !Finite(Cost) || Cost < 0f || !Finite(Step)
            || (Operation != PlayerUpgradeOperation.Multiply && Operation != PlayerUpgradeOperation.Add))
            error = $"能力 {Kind} 的标识、名称、等级上限、价格或运算配置无效。";
        else if (PlayerUpgradeCatalog.IsCost(Kind)
            ? (Operation == PlayerUpgradeOperation.Multiply ? Step <= 0f || Step >= 1f : Step >= 0f)
            : (Operation == PlayerUpgradeOperation.Multiply ? Step <= 1f : Step <= 0f))
            error = $"能力 {Label} 的步长必须使角色属性增加或认知费用减少。";
        else if (Kind == PlayerUpgradeKind.AirJumps && Step != Math.Floor(Step))
            error = "额外空中跳的加值或乘数必须为整数。";
        else if (!Finite(Evaluate(1f, MaxLevel))
            || (Kind == PlayerUpgradeKind.AirJumps && Evaluate(1f, MaxLevel) > int.MaxValue))
            error = $"能力 {Label} 在等级上限产生数值溢出。";
        return error == null;
    }

    /// <summary>从基础属性和等级直接重算，绝不回写基础值；认知费用以零为算术下界。</summary>
    public float Evaluate(float baseValue, int level)
    {
        // 安全：预览和消费者共用有限算式，非法输入返回无效结果供调用侧拒绝。
        if (!Finite(baseValue) || baseValue < 0f || level < 0 || !Finite(Step)
            || (Operation != PlayerUpgradeOperation.Multiply && Operation != PlayerUpgradeOperation.Add)) return float.NaN;
        double value = Operation == PlayerUpgradeOperation.Multiply
            ? baseValue * Math.Pow(Step, level) : baseValue + (double)Step * level;
        // 费用递减到零后保持免费，不出现负费用或反向退款。
        if (PlayerUpgradeCatalog.IsCost(Kind)) value = Math.Max(0d, value);
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > float.MaxValue) return float.NaN;
        if (Kind == PlayerUpgradeKind.AirJumps
            && (value > int.MaxValue || (double)(float)value > int.MaxValue || value != Math.Floor(value))) return float.NaN;
        return (float)value;
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>跨关共用的六项升级配置；候选抽取属于逃生准备编排。</summary>
[CreateAssetMenu(menuName = "GameEmergence/玩家升级配置", fileName = "PlayerUpgradeCatalog")]
public sealed class PlayerUpgradeCatalog : ScriptableObject
{
    public const int KindCount = 6;
    public List<PlayerUpgradeDefinition> Definitions = new List<PlayerUpgradeDefinition>
    {
        new PlayerUpgradeDefinition(PlayerUpgradeKind.MovementSpeed, "移速强化", PlayerUpgradeOperation.Multiply, 1.2f),
        new PlayerUpgradeDefinition(PlayerUpgradeKind.JumpStrength, "起跳强化", PlayerUpgradeOperation.Multiply, 1.2f),
        new PlayerUpgradeDefinition(PlayerUpgradeKind.AirJumps, "额外空中跳", PlayerUpgradeOperation.Add, 1f, 3),
        new PlayerUpgradeDefinition(PlayerUpgradeKind.MapCost, "地图认知降费", PlayerUpgradeOperation.Multiply, .9f),
        new PlayerUpgradeDefinition(PlayerUpgradeKind.BackpackCost, "入包认知降费", PlayerUpgradeOperation.Multiply, .9f),
        new PlayerUpgradeDefinition(PlayerUpgradeKind.FormCost, "转换认知降费", PlayerUpgradeOperation.Multiply, .9f)
    };

    public PlayerUpgradeDefinition Get(PlayerUpgradeKind kind)
    {
        if (Definitions == null || !IsKnown(kind)) return null;
        foreach (PlayerUpgradeDefinition definition in Definitions)
            if (definition != null && definition.Kind == kind) return definition;
        return null;
    }

    public bool Validate(out string error)
    {
        error = null;
        if (Definitions == null || Definitions.Count != KindCount)
        { error = "升级配置必须包含六个固定能力；不开放的能力请关闭 Enabled。"; return false; }
        var seen = new HashSet<PlayerUpgradeKind>();
        foreach (PlayerUpgradeDefinition definition in Definitions)
        {
            // 安全：缺失或重复标识不能由列表位置猜测，避免读档后套到其它能力。
            if (definition == null || !seen.Add(definition.Kind))
            { error = "升级配置有空项或重复能力标识。"; return false; }
            if (!definition.Validate(out error)) return false;
        }
        return true;
    }

    public static bool IsKnown(PlayerUpgradeKind kind) => (int)kind >= 0 && (int)kind < KindCount;
    public static bool IsCost(PlayerUpgradeKind kind) => kind == PlayerUpgradeKind.MapCost
        || kind == PlayerUpgradeKind.BackpackCost || kind == PlayerUpgradeKind.FormCost;
}
