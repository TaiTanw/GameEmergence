using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum EnvironmentParameterKind
{
    GroundSlowing, GroundJumpMultiplier, WallSlideMultiplier,
    WaterSwimming, MovementSpeedOffset, PassiveVelocity, TriggerEnvironment, GroundMovementMultiplier
}

/// <summary>
/// 环境宿主中的一项配置：制作值只读，局内编辑只改变非序列化的生效值。
/// 不保存接收者、接触关系或运动；有限种类的接触条件固定，由原查询和动作执行判断。
/// </summary>
[Serializable]
public abstract class EnvironmentParameter
{
    [NonSerialized] private bool defaultsCaptured;
    [NonSerialized] private float defaultValue;
    [NonSerialized] private float runtimeValue;
    [SerializeField, Tooltip("制作时预附加的计算点，按列表顺序相乘；开局不扣费，运行中可抽取。")]
    private List<EnvironmentCalculationPoint> calculationPoints = new List<EnvironmentCalculationPoint>();
    [NonSerialized] private System.Collections.ObjectModel.ReadOnlyCollection<EnvironmentCalculationPoint> readOnlyCalculationPoints;

    public abstract string DisplayName { get; }
    public abstract EnvironmentParameterKind Kind { get; }
    public abstract string ContactCondition { get; }
    protected abstract float ConfiguredValue { get; }
    public float DefaultValue => defaultsCaptured ? defaultValue : ConfiguredValue;
    public float BaseValue => defaultsCaptured ? runtimeValue : ConfiguredValue;
    protected virtual string BaseValueText => BaseValue.ToString("g");
    public float Value
    {
        get
        {
            float result = BaseValue;
            foreach (EnvironmentCalculationPoint point in calculationPoints) result *= point.Multiplier;
            return result;
        }
    }
    public virtual string ValueText => Value.ToString("g");
    public virtual bool IsTriggerProperty => false;
    public virtual bool SupportsCalculation => true;
    public IReadOnlyList<EnvironmentCalculationPoint> CalculationPoints => readOnlyCalculationPoints ??= calculationPoints.AsReadOnly();
    public int CalculationCount => calculationPoints.Count;
    public string CalculationText
    {
        get
        {
            var text = new StringBuilder();
            foreach (EnvironmentCalculationPoint point in calculationPoints)
            {
                if (text.Length > 0) text.Append(" → ");
                text.Append(point.DisplayName);
            }
            return text.ToString();
        }
    }
    public string CalculationFormula => BaseValueText
        + (CalculationCount == 0 ? "" : " " + CalculationText.Replace(" → ", " ")) + " = " + ValueText;

    internal virtual bool IsConfigurationValid => IsValidBaseValue(ConfiguredValue)
        && (defaultsCaptured || CalculationsValid(ConfiguredValue));
    internal bool IsRuntimeValid
    {
        get => IsValidBaseValue(BaseValue) && CalculationsValid(BaseValue);
    }
    bool CalculationsValid(float basis)
    {
        if (calculationPoints == null || (CalculationCount > 0 && !SupportsCalculation)) return false;
        foreach (EnvironmentCalculationPoint point in calculationPoints)
        {
            if (point == null || !IsValidValue(point.Multiplier)) return false;
            basis *= point.Multiplier;
            if (!IsValidBaseValue(basis)) return false;
        }
        return true;
    }
    protected virtual bool IsValidBaseValue(float value) => IsValidValue(value);
    internal static bool IsValidValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    internal abstract EnvironmentParameter CreateRuntimeCopy();

    /// <summary>编辑草稿复制参数，附加点仍使用同一内容物身份；不会重复生成计算点。</summary>
    public EnvironmentParameter CopyRuntime()
    {
        EnvironmentParameter copy = CreateRuntimeCopy();
        copy.defaultsCaptured = true;
        copy.defaultValue = DefaultValue;
        copy.runtimeValue = BaseValue;
        // 制作默认点为每个运行实例单独创建；草稿和默认快照保留已有点身份。
        foreach (EnvironmentCalculationPoint point in calculationPoints)
            copy.calculationPoints.Add(defaultsCaptured ? point : new EnvironmentCalculationPoint(point.Multiplier));
        return copy;
    }

    public bool TryAttachCalculation(EnvironmentCalculationPoint point)
    {
        // 安全：同一个点不能叠加两次；上限由目标环境校验，非法结果不改现有序列。
        if (!SupportsCalculation || point == null || ContainsCalculation(point)
            || !IsRuntimeValid || !IsValidValue(point.Multiplier) || !IsValidBaseValue(Value * point.Multiplier)) return false;
        CaptureDefaults();
        calculationPoints.Add(point);
        return true;
    }

    public EnvironmentCalculationPoint RemoveCalculation()
    {
        if (CalculationCount == 0) return null;
        EnvironmentCalculationPoint point = calculationPoints[CalculationCount - 1];
        calculationPoints.RemoveAt(CalculationCount - 1);
        return point;
    }

    public bool ContainsCalculation(EnvironmentCalculationPoint point) => calculationPoints.Contains(point);

    // 未确认点改选目标时只解除该点，其余附加点的顺序不变；正式抽取始终出栈。
    internal bool RemoveCalculation(EnvironmentCalculationPoint point)
    {
        if (!ContainsCalculation(point)) return false;
        // 移走中间的零倍率可能暴露溢出；先重算，再解除预览归属。
        float result = BaseValue;
        foreach (EnvironmentCalculationPoint remaining in calculationPoints)
        {
            if (remaining == point) continue;
            result *= remaining.Multiplier;
            if (!IsValidBaseValue(result)) return false;
        }
        return calculationPoints.Remove(point);
    }

    internal void CaptureDefaults()
    {
        // 默认只捕获一次；启停与重刷新不会把已编辑值重新当作制作默认。
        if (defaultsCaptured) return;
        defaultValue = runtimeValue = ConfiguredValue;
        defaultsCaptured = true;
    }

    internal bool SetRuntimeValue(float value)
    {
        CaptureDefaults();
        if (runtimeValue == value) return false;
        runtimeValue = value;
        return true;
    }

    internal bool RestoreDefault()
    {
        CaptureDefaults();
        bool changed = CalculationCount > 0;
        calculationPoints.Clear();
        return SetRuntimeValue(defaultValue) || changed;
    }
}

/// <summary>水只提供游泳与淹没事实；关闭游泳许可仍保留水性质，抽走整项才移除水身份。</summary>
[Serializable]
public sealed class WaterSwimmingParameter : EnvironmentParameter
{
    [SerializeField] private bool swimmingEnabled = true;
    public override string DisplayName => "四向游泳";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.WaterSwimming;
    public override string ContactCondition => "水域／身体淹没";
    protected override float ConfiguredValue => swimmingEnabled ? 1f : 0f;
    public override bool IsTriggerProperty => true;
    public override bool SupportsCalculation => false;
    public bool SwimmingEnabled => Value > 0f;
    public override string ValueText => SwimmingEnabled ? "允许游泳" : "游泳关闭";
    public WaterSwimmingParameter() { }
    public WaterSwimmingParameter(bool enabled) => swimmingEnabled = enabled;
    internal override EnvironmentParameter CreateRuntimeCopy() => new WaterSwimmingParameter(swimmingEnabled);
}

/// <summary>主动移动加性修饰沿原数值状态合成；负值表示减速，计算点乘原修饰值。</summary>
[Serializable]
public sealed class MovementSpeedOffsetParameter : EnvironmentParameter
{
    [SerializeField] private float offset;
    public override string DisplayName => "主动速度修饰";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.MovementSpeedOffset;
    public override string ContactCondition => "触发器区域";
    protected override float ConfiguredValue => offset;
    public override bool IsTriggerProperty => true;
    protected override bool IsValidBaseValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public MovementSpeedOffsetParameter() { }
    public MovementSpeedOffsetParameter(float value) => offset = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new MovementSpeedOffsetParameter(offset);
}

/// <summary>被动二维速度保存原方向；附加点等比缩放两轴，复用持续速度通道。</summary>
[Serializable]
public sealed class PassiveVelocityParameter : EnvironmentParameter
{
    [SerializeField] private Vector2 velocity;
    public override string DisplayName => "被动速度";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.PassiveVelocity;
    public override string ContactCondition => "触发器区域";
    protected override float ConfiguredValue => velocity.magnitude;
    public override bool IsTriggerProperty => true;
    internal override bool IsConfigurationValid => base.IsConfigurationValid
        && !float.IsNaN(velocity.x) && !float.IsInfinity(velocity.x)
        && !float.IsNaN(velocity.y) && !float.IsInfinity(velocity.y);
    public Vector2 EffectiveVelocity => velocity.sqrMagnitude > 0f ? velocity.normalized * Value : Vector2.zero;
    protected override string BaseValueText => $"({velocity.x:g}, {velocity.y:g})";
    public override string ValueText => $"({EffectiveVelocity.x:g}, {EffectiveVelocity.y:g})";
    public PassiveVelocityParameter() { }
    public PassiveVelocityParameter(Vector2 value) => velocity = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new PassiveVelocityParameter(velocity);
}

/// <summary>独特的触发器性质只切换几何，不进入普通能力列表或计算。</summary>
[Serializable]
public sealed class TriggerEnvironmentParameter : EnvironmentParameter
{
    public override string DisplayName => "触发器性质";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.TriggerEnvironment;
    public override string ContactCondition => "独特几何槽";
    protected override float ConfiguredValue => 1f;
    public override bool IsTriggerProperty => true;
    public override bool SupportsCalculation => false;
    public override string ValueText => "触发器";
    internal override EnvironmentParameter CreateRuntimeCopy() => new TriggerEnvironmentParameter();
}

/// <summary>地面消退倍率：支撑采样提供倍率，不修改原生物理材质，也不改变消退公式。</summary>
[Serializable]
public sealed class GroundSlowingParameter : EnvironmentParameter
{
    [SerializeField, Min(0f), Tooltip("脚下支撑时使用的消退倍率；1 为原响应，0 为不消退。")]
    private float multiplier = 1f;
    public override string DisplayName => "地面消退倍率";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.GroundSlowing;
    public override string ContactCondition => "脚下支撑";
    protected override float ConfiguredValue => multiplier;

    public GroundSlowingParameter() { }
    public GroundSlowingParameter(float value) => multiplier = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new GroundSlowingParameter(multiplier);
}

/// <summary>脚下来源提供走跑速度倍率；与消退阻力、区域加性移速分别采样。</summary>
[Serializable]
public sealed class GroundMovementMultiplierParameter : EnvironmentParameter
{
    [SerializeField, Min(0f), Tooltip("站在此地面上的走跑速度倍率；1 为原移速，0 为停止主动走跑。")]
    private float multiplier = 1f;
    public override string DisplayName => "地面移速倍率";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.GroundMovementMultiplier;
    public override string ContactCondition => "脚下支撑／走跑";
    protected override float ConfiguredValue => multiplier;

    public GroundMovementMultiplierParameter() { }
    public GroundMovementMultiplierParameter(float value) => multiplier = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new GroundMovementMultiplierParameter(multiplier);
}

/// <summary>脚下采样为普通起跳倍率；以 multiplier−1 接回原加成字段，不影响墙跳。</summary>
[Serializable]
public sealed class GroundJumpMultiplierParameter : EnvironmentParameter
{
    [SerializeField, Min(0f), Tooltip("普通起跳速度倍率；1 为原起跳，0 对应旧加成 −1。")]
    private float multiplier = 1f;
    public override string DisplayName => "普通起跳倍率";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.GroundJumpMultiplier;
    public override string ContactCondition => "脚下支撑／普通起跳";
    protected override float ConfiguredValue => multiplier;

    public GroundJumpMultiplierParameter() { }
    public GroundJumpMultiplierParameter(float value) => multiplier = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new GroundJumpMultiplierParameter(multiplier);
}

/// <summary>有效墙面提供墙滑下落倍率；普通侧碰与上升仍由原动作条件排除。</summary>
[Serializable]
public sealed class WallSlideMultiplierParameter : EnvironmentParameter
{
    [SerializeField, Min(0f), Tooltip("墙滑下落速度倍率；1 为原下落，0 停止墙滑下落。")]
    private float multiplier = 1f;
    public override string DisplayName => "墙滑下落倍率";
    public override EnvironmentParameterKind Kind => EnvironmentParameterKind.WallSlideMultiplier;
    public override string ContactCondition => "有效墙／墙滑下落";
    protected override float ConfiguredValue => multiplier;

    public WallSlideMultiplierParameter() { }
    public WallSlideMultiplierParameter(float value) => multiplier = value;
    internal override EnvironmentParameter CreateRuntimeCopy() => new WallSlideMultiplierParameter(multiplier);
}
