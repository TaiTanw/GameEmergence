using System;
using UnityEngine;

public enum EnvironmentParameterKind { GroundSlowing, GroundJumpMultiplier, WallSlideMultiplier }

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

    public abstract string DisplayName { get; }
    public abstract EnvironmentParameterKind Kind { get; }
    public abstract string ContactCondition { get; }
    protected abstract float ConfiguredValue { get; }
    public float DefaultValue => defaultsCaptured ? defaultValue : ConfiguredValue;
    public float Value => defaultsCaptured ? runtimeValue : ConfiguredValue;

    internal bool IsConfigurationValid => IsValidValue(ConfiguredValue);
    internal static bool IsValidValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    internal abstract EnvironmentParameter CreateRuntimeCopy();

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
        return SetRuntimeValue(defaultValue);
    }
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
