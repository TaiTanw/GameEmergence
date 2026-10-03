using PhyData;
using UnityEngine;

/// <summary>
/// 场景环境入口：持有 Registration，启用时接入调度，禁用时统一通知退出。
/// 移速修饰和持续速度由具体组件自行声明并持有参数。
/// 职能：现有场景组件的 Unity 兼容外壳；能力接口不要求新组件继承本类。
/// 子类只扩展参数、算法或启停钩子；本类固定入口负责转发，不再要求子类成对重写登记/撤销。
/// F_5.3 入口约束：同一物体只挂本类家族中的一个组件，该组件可实现多个能力接口。
/// </summary>
[DisallowMultipleComponent]
public class BasicPhysicalObject : MonoBehaviour, IEnvironmentSource
{
    private EnvironmentRegistration environmentRegistration;
    // Unity 适配：接口引用不会自动处理已销毁组件，因此先检查存活，再读取启用状态。
    public bool IsEnvironmentActive => this != null && isActiveAndEnabled;
    public EnvironmentRegistration EnvironmentRegistration => environmentRegistration ??=
        new EnvironmentRegistration(this);

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
    }
#endif

    /// <summary>供平台等注册自身运动回调；环境登记清理由固定入口统一执行。</summary>
    protected virtual void OnSourceEnabled() { }
    protected virtual void OnSourceDisabled() { }
}
