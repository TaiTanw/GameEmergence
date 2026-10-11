using PhyData;
using UnityEngine;
using System.Collections.Generic;

/// <summary>环境唯一入口：制作配置生成双列表与默认快照；编辑草稿确认后才替换生效配置。</summary>
[DisallowMultipleComponent]
public class BasicPhysicalObject : MonoBehaviour, IEnvironmentSource, IEnvironmentParameterSource
{
    private EnvironmentRegistration environmentRegistration;
    [SerializeField, Min(1), Tooltip("实心与触发器列表各自拥有此数量的槽，独特几何槽不占用此上限。")]
    private int environmentSlotCapacity = 6;
    [SerializeField, Min(1), Tooltip("每个数值性质槽允许附加的计算点数；实心与触发器列表共用此配置。")]
    private int environmentCalculationPointLimit = 3;
    [SerializeReference, Tooltip("有限环境性质；同类最多一项，按照性质种类进入实心或触发器列表。")]
    private List<EnvironmentParameter> environmentParameters = new List<EnvironmentParameter>();
    [SerializeField, Range(0f, 1f), Tooltip("实心时主体精灵的不透明度；0 完全透明，1 完全不透明。初始外观由制作工具同步。")]
    private float colliderOpacity = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("触发器时主体精灵的不透明度；直接使用此值，不乘旧颜色 Alpha。")]
    private float triggerOpacity = .5f;
    private EnvironmentParameter[] colliderSlots, triggerSlots, defaultColliderSlots, defaultTriggerSlots;
    private bool parametersInitialized, parameterConfigurationValid, triggerEnvironment, defaultTriggerEnvironment;
    private int defaultEnvironmentLayer, solidEnvironmentLayer;
    private Collider2D[] defaultGeometry;
    private bool[] defaultGeometryTriggers;
    private bool mixedGeometryConfiguration;
    private ConfiguredWallSlideSurface configuredWallSurface;
    private ConfiguredWaterRegion configuredWaterRegion;
    public int EnvironmentParameterVersion { get; private set; }
    public int EnvironmentSlotCapacity => EnsureEnvironmentParameters() ? colliderSlots.Length : 0;
    public int EnvironmentCalculationPointLimit => environmentCalculationPointLimit;
    public bool IsTriggerEnvironment => EnsureEnvironmentParameters() && triggerEnvironment;
    public bool AllowsEnvironmentEditing => IsEnvironmentActive
        && (SceneLevelSetup.Current == null || SceneLevelSetup.Current.RisingWater != this);
    public bool IsEnvironmentActive => this != null && isActiveAndEnabled;
    public EnvironmentRegistration EnvironmentRegistration => environmentRegistration ??= new EnvironmentRegistration(this);

    // 旧数字接口仍枚举当前列表的非空项；固定空槽与两个列表由新的草稿入口读取。
    public int EnvironmentParameterCount
    {
        get
        {
            if (!EnsureEnvironmentParameters()) return 0;
            int count = 0;
            foreach (EnvironmentParameter parameter in ActiveSlots) if (parameter != null) count++;
            return count;
        }
    }
    private EnvironmentParameter[] ActiveSlots => triggerEnvironment ? triggerSlots : colliderSlots;

    public bool TryGetEnvironmentParameter(int index, out EnvironmentParameter parameter)
    {
        EnvironmentParameter current = FindOccupiedParameter(index);
        // 参数现在有草稿修改方法；旧查询也返回副本，不能绕过宿主提交或版本更新。
        parameter = current?.CopyRuntime();
        return parameter != null;
    }
    private EnvironmentParameter FindOccupiedParameter(int index)
    {
        if (!EnsureEnvironmentParameters() || index < 0) return null;
        foreach (EnvironmentParameter candidate in ActiveSlots)
        {
            if (candidate == null) continue;
            if (index-- != 0) continue;
            return candidate;
        }
        return null;
    }
    public bool TrySetEnvironmentParameter(int index, float value)
    {
        // 安全：旧数值改口仍拒绝非负倍率以外的输入；阶段资格由规则层负责。
        if (!AllowsEnvironmentEditing || !EnvironmentParameter.IsValidValue(value)) return false;
        EnvironmentParameter parameter = FindOccupiedParameter(index);
        if (parameter == null || !parameter.SupportsCalculation) return false;
        if (parameter.SetRuntimeValue(value)) EnvironmentParameterVersion++;
        return true;
    }
    public EnvironmentParameter[] CopyEnvironmentSlots(bool trigger)
    {
        if (!EnsureEnvironmentParameters()) return new EnvironmentParameter[0];
        return CopySlots(trigger ? triggerSlots : colliderSlots);
    }

    /// <summary>先校验整个草稿；不写配置、碰撞体、费用或接收者运动。</summary>
    public bool ValidateEnvironmentEdit(EnvironmentParameter[] colliderSlots, EnvironmentParameter[] triggerSlots,
        bool trigger, out string error)
    {
        error = null;
        if (!AllowsEnvironmentEditing || !EnsureEnvironmentParameters())
        { error = "此环境当前不可编辑。"; return false; }
        Collider2D[] geometry = GetComponents<Collider2D>();
        if (geometry.Length == 0)
        { error = "此环境没有可修改的碰撞范围。"; return false; }
        if (mixedGeometryConfiguration)
        { error = "此环境同时包含实心与触发器范围，不能使用单一几何槽编辑。"; return false; }
        foreach (Collider2D shape in geometry)
        {
            // 首版转换要求有自身面积；复合形状归另一碰撞体，不在这里拆分或改写。
            if (shape.usedByComposite || (!(shape is BoxCollider2D) && !(shape is CircleCollider2D)
                && !(shape is CapsuleCollider2D) && !(shape is PolygonCollider2D)))
            { error = "此环境的碰撞形状不支持实心／触发器转换。"; return false; }
        }
        if (colliderSlots == null || triggerSlots == null || colliderSlots.Length != EnvironmentSlotCapacity
            || triggerSlots.Length != EnvironmentSlotCapacity)
        { error = "环境能力槽数与配置上限不一致。"; return false; }
        if (!trigger && solidEnvironmentLayer < 0)
        { error = "此触发器缺少可被地面查询识别的实心层。"; return false; }
        var calculations = new HashSet<EnvironmentCalculationPoint>();
        return ValidateSlots(colliderSlots, false, calculations, out error)
            && ValidateSlots(triggerSlots, true, calculations, out error);
    }
    public bool TryCommitEnvironmentEdit(EnvironmentParameter[] colliderSlots, EnvironmentParameter[] triggerSlots,
        bool trigger, out string error)
    {
        if (!ValidateEnvironmentEdit(colliderSlots, triggerSlots, trigger, out error)) return false;
        bool geometryChanged = triggerEnvironment != trigger;
        bool changed = geometryChanged || !SameSlots(this.colliderSlots, colliderSlots)
            || !SameSlots(this.triggerSlots, triggerSlots);
        // 确认时复制草稿，后续界面修改不能回写已提交的来源配置。
        this.colliderSlots = CopySlots(colliderSlots);
        this.triggerSlots = CopySlots(triggerSlots);
        triggerEnvironment = trigger;
        ApplyGeometryMode(trigger);
        // 只有已确认的形态切换更新外观；能力改值与面板草稿不覆盖制作颜色。
        if (geometryChanged) ApplyEnvironmentOpacity(trigger);
        if (changed) EnvironmentParameterVersion++;
        return true;
    }
    /// <summary>重刷新恢复完整默认列表、空槽与几何；轻刷新及启停不调用或重捕获默认。</summary>
    public void RestoreEnvironmentDefaults()
    {
        if (!EnsureEnvironmentParameters()) return;
        bool geometryChanged = triggerEnvironment != defaultTriggerEnvironment;
        bool changed = geometryChanged
            || !SameSlots(colliderSlots, defaultColliderSlots) || !SameSlots(triggerSlots, defaultTriggerSlots);
        colliderSlots = CopySlots(defaultColliderSlots);
        triggerSlots = CopySlots(defaultTriggerSlots);
        triggerEnvironment = defaultTriggerEnvironment;
        // 制作默认可能含额外传感器；重刷新逐个恢复原值，不把未开放的复合配置扁平化。
        for (int i = 0; i < defaultGeometry.Length; i++)
            if (defaultGeometry[i] != null) defaultGeometry[i].isTrigger = defaultGeometryTriggers[i];
        gameObject.layer = defaultEnvironmentLayer;
        Physics2D.SyncTransforms();
        // 重刷新随默认形态恢复外观；同形态、启停与初次读取不做颜色补正。
        if (geometryChanged) ApplyEnvironmentOpacity(defaultTriggerEnvironment);
        if (changed) EnvironmentParameterVersion++;
    }
    public float GetEnvironmentOpacity(bool trigger) => Mathf.Clamp01(trigger ? triggerOpacity : colliderOpacity);

    private void ApplyEnvironmentOpacity(bool trigger)
    {
        SpriteRenderer visual = GetComponent<SpriteRenderer>();
        // 无主体精灵的环境仍可按原流程切换几何。
        if (visual == null) return;
        Color color = visual.color;
        color.a = GetEnvironmentOpacity(trigger);
        visual.color = color;
    }
    private void ApplyGeometryMode(bool trigger)
    {
        foreach (Collider2D collider in GetComponents<Collider2D>()) collider.isTrigger = trigger;
        // 旧水域常在 Default 层；恢复实心时转入原地面查询层，回触发器恢复制作层。
        gameObject.layer = trigger ? defaultEnvironmentLayer : solidEnvironmentLayer;
        // 项目采用显式同步；下一次原查询读取已确认的几何。
        Physics2D.SyncTransforms();
    }

    public bool TryGetGroundSlowingMultiplier(out float value) => TryGetParameterValue(EnvironmentParameterKind.GroundSlowing, out value);
    public bool TryGetGroundJumpMultiplier(out float value) => TryGetParameterValue(EnvironmentParameterKind.GroundJumpMultiplier, out value);
    public bool TryGetWallSlideMultiplier(out float value) => TryGetParameterValue(EnvironmentParameterKind.WallSlideMultiplier, out value);
    internal bool TryGetMovementSpeedOffset(out float value) => TryGetParameterValue(EnvironmentParameterKind.MovementSpeedOffset, out value);
    internal bool TryGetPassiveVelocity(out Vector2 value)
    {
        value = Vector2.zero;
        if (!(FindActiveParameter(EnvironmentParameterKind.PassiveVelocity) is PassiveVelocityParameter passive)) return false;
        value = passive.EffectiveVelocity;
        return true;
    }
    internal bool TryGetWaterSwimming(out bool enabled)
    {
        enabled = false;
        if (!(FindActiveParameter(EnvironmentParameterKind.WaterSwimming) is WaterSwimmingParameter water)) return false;
        enabled = water.SwimmingEnabled;
        return true;
    }
    protected void SetWaterSwimmingState(bool enabled)
    {
        if (!EnsureEnvironmentParameters()) return;
        foreach (EnvironmentParameter parameter in triggerSlots)
            if (parameter is WaterSwimmingParameter && parameter.SetRuntimeValue(enabled ? 1f : 0f))
                EnvironmentParameterVersion++;
    }
    internal IWallSlideSurface GetConfiguredWallSlideSurface()
    {
        if (!TryGetWallSlideMultiplier(out _)) return null;
        return configuredWallSurface ??= new ConfiguredWallSlideSurface(this);
    }
    internal IWaterRegion GetConfiguredWaterRegion()
    {
        if (!TryGetWaterSwimming(out _)) return null;
        return configuredWaterRegion ??= new ConfiguredWaterRegion(this);
    }
    internal bool IsNativeGeometryMode => EnsureEnvironmentParameters() && triggerEnvironment == defaultTriggerEnvironment;
    private bool TryGetParameterValue(EnvironmentParameterKind kind, out float value)
    {
        EnvironmentParameter parameter = FindActiveParameter(kind);
        value = parameter?.Value ?? 1f;
        return parameter != null;
    }
    private EnvironmentParameter FindActiveParameter(EnvironmentParameterKind kind)
    {
        if (!EnsureEnvironmentParameters()) return null;
        foreach (EnvironmentParameter parameter in ActiveSlots)
            if (parameter != null && parameter.Kind == kind) return parameter;
        return null;
    }

    private bool EnsureEnvironmentParameters()
    {
        if (parametersInitialized) return parameterConfigurationValid;
        parametersInitialized = true;
        if (!TryBuildParameterConfiguration(out List<EnvironmentParameter> configuration, out string error))
        { Debug.LogError(error, this); return false; }
        colliderSlots = new EnvironmentParameter[environmentSlotCapacity];
        triggerSlots = new EnvironmentParameter[environmentSlotCapacity];
        int colliderIndex = 0, triggerIndex = 0;
        foreach (EnvironmentParameter parameter in configuration)
        {
            EnvironmentParameter runtime = parameter.CreateRuntimeCopy();
            runtime.CaptureDefaults();
            if (runtime.IsTriggerProperty) triggerSlots[triggerIndex++] = runtime;
            else colliderSlots[colliderIndex++] = runtime;
        }
        Collider2D geometry = GetComponent<Collider2D>();
        triggerEnvironment = defaultTriggerEnvironment = geometry != null && geometry.isTrigger;
        defaultGeometry = GetComponents<Collider2D>();
        defaultGeometryTriggers = new bool[defaultGeometry.Length];
        for (int i = 0; i < defaultGeometry.Length; i++)
        {
            defaultGeometryTriggers[i] = defaultGeometry[i].isTrigger;
            mixedGeometryConfiguration |= defaultGeometryTriggers[i] != defaultTriggerEnvironment;
        }
        defaultEnvironmentLayer = gameObject.layer;
        int groundLayer = LayerMask.NameToLayer("Ground"), wallLayer = LayerMask.NameToLayer("Wall");
        solidEnvironmentLayer = defaultTriggerEnvironment && defaultEnvironmentLayer != groundLayer
            && defaultEnvironmentLayer != wallLayer ? groundLayer : defaultEnvironmentLayer;
        defaultColliderSlots = CopySlots(colliderSlots);
        defaultTriggerSlots = CopySlots(triggerSlots);
        parameterConfigurationValid = true;
        return true;
    }
    public bool ValidateEnvironmentParameters(out string error) => TryBuildParameterConfiguration(out _, out error);
    private bool TryBuildParameterConfiguration(out List<EnvironmentParameter> configuration, out string error)
    {
        error = null;
        configuration = new List<EnvironmentParameter>();
        var kinds = new HashSet<EnvironmentParameterKind>();
        if (environmentSlotCapacity < 1)
        { error = $"{name} 的环境能力槽上限至少为 1。"; return false; }
        if (environmentCalculationPointLimit < 1)
        { error = $"{name} 的单槽计算点上限至少为 1。"; return false; }
        if (environmentParameters != null)
            foreach (EnvironmentParameter parameter in environmentParameters)
            {
                if (!ValidateParameter(parameter, false, out error)) return false;
                if (!kinds.Add(parameter.Kind))
                { error = $"{name} 的{parameter.DisplayName}配置重复；同一来源每类只允许一项。"; return false; }
                configuration.Add(parameter);
            }
        AddLegacyEnvironmentParameters(configuration);
        int colliderCount = 0, triggerCount = 0;
        foreach (EnvironmentParameter parameter in configuration)
        {
            if (!ValidateParameter(parameter, false, out error)) return false;
            if (parameter.IsTriggerProperty) triggerCount++; else colliderCount++;
        }
        if (colliderCount > environmentSlotCapacity || triggerCount > environmentSlotCapacity)
        { error = $"{name} 的性质数超过单个列表的能力槽上限。"; return false; }
        return true;
    }
    private bool ValidateSlots(EnvironmentParameter[] slots, bool trigger,
        HashSet<EnvironmentCalculationPoint> calculations, out string error)
    {
        var kinds = new HashSet<EnvironmentParameterKind>();
        error = null;
        foreach (EnvironmentParameter parameter in slots)
        {
            // 空槽合法；独特触发器性质不能塞进普通列表。
            if (parameter == null) continue;
            if (!ValidateParameter(parameter, true, out error)) return false;
            if (parameter.IsTriggerProperty != trigger)
            { error = "此性质不属于当前能力列表。"; return false; }
            if (!kinds.Add(parameter.Kind))
            { error = "同一列表不能注入重复类型的性质。"; return false; }
            if (parameter.CalculationCount > EnvironmentCalculationPointLimit)
            { error = "此性质超过环境的单槽计算点上限。"; return false; }
            foreach (EnvironmentCalculationPoint point in parameter.CalculationPoints)
                if (!calculations.Add(point))
                { error = "同一个计算点不能同时用于多个性质。"; return false; }
        }
        return true;
    }
    private bool ValidateParameter(EnvironmentParameter parameter, bool runtime, out string error)
    {
        error = null;
        if (!(parameter is GroundSlowingParameter) && !(parameter is GroundJumpMultiplierParameter)
            && !(parameter is WallSlideMultiplierParameter) && !(parameter is WaterSwimmingParameter)
            && !(parameter is MovementSpeedOffsetParameter) && !(parameter is PassiveVelocityParameter))
        { error = $"{name} 的环境参数配置无效：只允许已开放的六类性质。"; return false; }
        if (!parameter.IsConfigurationValid || (runtime && !parameter.IsRuntimeValid))
        { error = $"{name} 的{parameter.DisplayName}数值无效。"; return false; }
        return true;
    }
    private static EnvironmentParameter[] CopySlots(EnvironmentParameter[] slots)
    {
        var copy = new EnvironmentParameter[slots.Length];
        for (int i = 0; i < slots.Length; i++) copy[i] = slots[i]?.CopyRuntime();
        return copy;
    }
    private static bool SameSlots(EnvironmentParameter[] left, EnvironmentParameter[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            EnvironmentParameter a = left[i], b = right[i];
            if (a == null || b == null) { if (a != b) return false; continue; }
            if (a.Kind != b.Kind || a.BaseValue != b.BaseValue || a.CalculationCount != b.CalculationCount
                || a.ValueText != b.ValueText) return false;
            for (int j = 0; j < a.CalculationCount; j++)
                if (a.CalculationPoints[j] != b.CalculationPoints[j]) return false;
        }
        return true;
    }
    protected virtual void AddLegacyEnvironmentParameters(List<EnvironmentParameter> parameters) { }
    protected static void AddLegacyEnvironmentParameter(List<EnvironmentParameter> parameters, EnvironmentParameter fallback)
    {
        foreach (EnvironmentParameter parameter in parameters) if (parameter.Kind == fallback.Kind) return;
        parameters.Add(fallback);
    }
    protected void OnEnable() => OnSourceEnabled();
    protected void OnDisable()
    {
        environmentRegistration?.ReleaseAll();
        OnSourceDisabled();
    }
#if UNITY_EDITOR
    protected void OnValidate()
    {
        if (GetComponents<BasicPhysicalObject>().Length > 1)
            Debug.LogError("同一物体只能有一个 BasicPhysicalObject 环境入口（包括不同子类）。", this);
        if (!ValidateEnvironmentParameters(out string error)) Debug.LogError(error, this);
    }
#endif
    protected virtual void OnSourceEnabled() { }
    protected virtual void OnSourceDisabled() { }
}
