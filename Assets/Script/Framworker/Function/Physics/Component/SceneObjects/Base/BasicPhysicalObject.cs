using PhyData;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 场景环境入口：持有 Registration，启用时接入调度，禁用时统一通知退出。
/// 移速修饰和持续速度由具体组件自行声明并持有参数。
/// 职能：现有场景组件的 Unity 兼容外壳；能力接口不要求新组件继承本类。
/// 子类只扩展参数、算法或启停钩子；本类固定入口负责转发，不再要求子类成对重写登记/撤销。
/// F_5.3 入口约束：同一物体只挂本类家族中的一个组件，该组件可实现多个能力接口。
/// </summary>
[DisallowMultipleComponent]
public class BasicPhysicalObject : MonoBehaviour, IEnvironmentSource, IEnvironmentParameterSource
{
    private EnvironmentRegistration environmentRegistration;
    [SerializeReference, Tooltip("有限表面项：地面消退、普通起跳与墙滑下落倍率；每类最多一项。")]
    private List<EnvironmentParameter> environmentParameters = new List<EnvironmentParameter>();
    private List<EnvironmentParameter> runtimeParameters;
    private bool parametersInitialized;
    private bool parameterConfigurationValid;
    private ConfiguredWallSlideSurface configuredWallSurface;
    public int EnvironmentParameterVersion { get; private set; }
    public int EnvironmentParameterCount => EnsureEnvironmentParameters() ? runtimeParameters.Count : 0;
    // Unity 适配：接口引用不会自动处理已销毁组件，因此先检查存活，再读取启用状态。
    public bool IsEnvironmentActive => this != null && isActiveAndEnabled;
    public EnvironmentRegistration EnvironmentRegistration => environmentRegistration ??=
        new EnvironmentRegistration(this);

    /// <summary>只读当前项；编辑资格由阶段规则确认，实际改值必须走宿主入口。</summary>
    public bool TryGetEnvironmentParameter(int index, out EnvironmentParameter parameter)
    {
        parameter = null;
        if (!EnsureEnvironmentParameters() || index < 0 || index >= runtimeParameters.Count) return false;
        parameter = runtimeParameters[index];
        return true;
    }

    /// <summary>规则确认漫游资格后调用：只改此来源生效值，不通知玩家或推进物理。</summary>
    public bool TrySetEnvironmentParameter(int index, float value)
    {
        // 安全：无效来源、索引和数值不产生部分修改。
        if (!IsEnvironmentActive || !EnvironmentParameter.IsValidValue(value)
            || !TryGetEnvironmentParameter(index, out EnvironmentParameter parameter)) return false;
        if (parameter.SetRuntimeValue(value)) EnvironmentParameterVersion++;
        return true;
    }

    /// <summary>整关重刷新调用原来源：先确保默认已捕获，再恢复已开放项；启停不改变默认。</summary>
    public void RestoreEnvironmentDefaults()
    {
        if (!EnsureEnvironmentParameters()) return;
        bool changed = false;
        foreach (EnvironmentParameter parameter in runtimeParameters) changed |= parameter.RestoreDefault();
        if (changed) EnvironmentParameterVersion++;
    }

    /// <summary>支撑采样的有限读口；没有配置项时返回缺席，不将区域命中当作脚下支撑。</summary>
    public bool TryGetGroundSlowingMultiplier(out float multiplier)
    {
        return TryGetParameterValue(EnvironmentParameterKind.GroundSlowing, out multiplier);
    }

    public bool TryGetGroundJumpMultiplier(out float multiplier) =>
        TryGetParameterValue(EnvironmentParameterKind.GroundJumpMultiplier, out multiplier);

    public bool TryGetWallSlideMultiplier(out float multiplier) =>
        TryGetParameterValue(EnvironmentParameterKind.WallSlideMultiplier, out multiplier);

    /// <summary>有限墙能力视图；未配置墙项的普通宿主不能凭空取得墙滑能力。</summary>
    internal IWallSlideSurface GetConfiguredWallSlideSurface()
    {
        if (!TryGetWallSlideMultiplier(out _)) return null;
        return configuredWallSurface ??= new ConfiguredWallSlideSurface(this);
    }

    private bool TryGetParameterValue(EnvironmentParameterKind kind, out float value)
    {
        value = 1f;
        if (!EnsureEnvironmentParameters()) return false;
        foreach (EnvironmentParameter parameter in runtimeParameters)
            if (parameter.Kind == kind)
            {
                value = parameter.Value;
                return true;
            }
        return false;
    }

    private bool EnsureEnvironmentParameters()
    {
        if (parametersInitialized) return parameterConfigurationValid;
        parametersInitialized = true;
        if (!TryBuildParameterConfiguration(out List<EnvironmentParameter> configuration, out string error))
        {
            Debug.LogError(error, this);
            return false;
        }
        runtimeParameters = new List<EnvironmentParameter>();
        // 每类显式项优先；缺项才沿旧字段补默认，配置与生效项不共用可变对象。
        foreach (EnvironmentParameter parameter in configuration)
            runtimeParameters.Add(parameter.CreateRuntimeCopy());
        foreach (EnvironmentParameter parameter in runtimeParameters) parameter.CaptureDefaults();
        parameterConfigurationValid = true;
        return true;
    }

    /// <summary>配置错误拒绝整份列表，不按顺序选一个重复项；编辑器也复用同一校验。</summary>
    public bool ValidateEnvironmentParameters(out string error) => TryBuildParameterConfiguration(out _, out error);

    private bool TryBuildParameterConfiguration(out List<EnvironmentParameter> configuration, out string error)
    {
        error = null;
        configuration = new List<EnvironmentParameter>();
        var kinds = new HashSet<EnvironmentParameterKind>();
        if (environmentParameters != null)
            foreach (EnvironmentParameter parameter in environmentParameters)
            {
                if (!ValidateParameter(parameter, out error)) return false;
                if (!kinds.Add(parameter.Kind))
                {
                    error = $"{name} 的{parameter.DisplayName}配置重复；同一来源每类只允许一项。";
                    return false;
                }
                configuration.Add(parameter);
            }
        AddLegacyEnvironmentParameters(configuration);
        foreach (EnvironmentParameter parameter in configuration)
            if (!ValidateParameter(parameter, out error)) return false;
        return true;
    }

    private bool ValidateParameter(EnvironmentParameter parameter, out string error)
    {
        error = null;
        if (!(parameter is GroundSlowingParameter) && !(parameter is GroundJumpMultiplierParameter)
            && !(parameter is WallSlideMultiplierParameter))
        {
            error = $"{name} 的环境参数配置无效：只允许地面消退、普通起跳与墙滑下落三类项。";
            return false;
        }
        if (!parameter.IsConfigurationValid)
        {
            error = $"{name} 的{parameter.DisplayName}必须为有限非负数。";
            return false;
        }
        return true;
    }

    /// <summary>旧家族只补显式列表里缺少的项；不会保存或迁移场景数据。</summary>
    protected virtual void AddLegacyEnvironmentParameters(List<EnvironmentParameter> parameters) { }

    protected static void AddLegacyEnvironmentParameter(List<EnvironmentParameter> parameters, EnvironmentParameter fallback)
    {
        foreach (EnvironmentParameter parameter in parameters)
            if (parameter.Kind == fallback.Kind) return;
        parameters.Add(fallback);
    }

    protected void OnEnable()
    {
        OnSourceEnabled();
    }

    protected void OnDisable()
    {
        // [ENV-L1] 源端禁用：已创建辅助对象才清理；最终走各实体 ENV-07。
        environmentRegistration?.ReleaseAll();
        OnSourceDisabled();
    }

#if UNITY_EDITOR
    /// <summary>[EditorOnly] 提示旧资源中的重复入口；不自动删除组件或修改参数。</summary>
    protected void OnValidate()
    {
        // 配置约束：禁用的组件也占入口身份；不同派生类仍属于同一个环境入口家族。
        if (GetComponents<BasicPhysicalObject>().Length > 1)
            Debug.LogError("同一物体只能有一个 BasicPhysicalObject 环境入口（包括不同子类）；请将所需能力放到同一组件。", this);
        if (!ValidateEnvironmentParameters(out string error)) Debug.LogError(error, this);
    }
#endif

    /// <summary>供平台等注册自身运动回调；环境登记清理由固定入口统一执行。</summary>
    protected virtual void OnSourceEnabled() { }
    protected virtual void OnSourceDisabled() { }
}
