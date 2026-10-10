using UnityEngine;

namespace PhyData
{
    /// <summary>
    /// 职能：支撑参数的只读采样结果，不是全部环境速度，也不是持续效果的 Binding。
    /// [ENV-S1] 脚下表面本帧提供的三个数：消退阻力倍率、起跳速度加成、平台已走的世界位移。
    /// 它由 Context.SampleGroundFrame 生成；没有对应能力时分别使用 1、0、零位移。
    /// </summary>
    public readonly struct EntityEnvironmentFrame
    {
        public readonly float slowingMultiplier; // BasicEntity.PhyFunUpdate：乘本帧基础消退阻力。
        public readonly float jumpHeightOffset; // CharacterPhysics：起跳时读取。
        public readonly Vector2 platformDelta; // BasicEntity.SpeedCalculation：一次并入运动快照。

        public EntityEnvironmentFrame(float slowingMultiplier, float jumpHeightOffset, Vector2 platformDelta)
        {
            this.slowingMultiplier = slowingMultiplier;
            this.jumpHeightOffset = jumpHeightOffset;
            this.platformDelta = platformDelta;
        }

        public static EntityEnvironmentFrame Empty => new EntityEnvironmentFrame(1f, 0f, Vector2.zero);
    }
}

/// <summary>
/// 从实际碰到的 Collider 查“有没有墙滑/支撑参数”等能力；没有脚本的墙仍然是几何障碍。
/// 职能：查询工具，不保存接入关系。表面能力统一取自 Collider 同物体的环境入口；实体身份另可从 attachedRigidbody 找。
/// 第一遍主线可跳过本工具；需要理解 ENV-S1 的默认值或复合碰撞体时再读。
/// </summary>
public static class EnvironmentCapabilities
{
    public static bool IsActive(object capability)
    {
        // 安全：接口引用不会自动执行 Unity 判空，先处理 CLR null，再识别已销毁的 Unity 对象。
        if (capability == null) return false;
        if (capability is ConfiguredWallSlideSurface wallView) return wallView.IsActive;
        // 生命周期规则：Behaviour 还必须启用；其它 Unity 对象只核对存活。
        if (capability is Behaviour behaviour) return behaviour != null && behaviour.isActiveAndEnabled;
        if (capability is Object unityObject) return unityObject != null;
        return true;
    }

    /// <summary>从 Collider 同物体的唯一宿主取得环境来源；没有有效来源时返回 null。</summary>
    public static IEnvironmentSource FindHost(Collider2D collider)
    {
        // 有效性保护：无有效几何命中时没有能力，调用者使用默认参数。
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return null;
        // 入口家族的唯一性由基类挂载约束及配置校验负责；禁用入口不提供表面能力。
        BasicPhysicalObject host = collider.GetComponent<BasicPhysicalObject>();
        return IsActive(host) ? host as IEnvironmentSource : null;
    }

    /// <summary>从唯一宿主解析现有接口或有限墙配置；普通宿主没有墙项时仍返回缺席。</summary>
    public static T Find<T>(Collider2D collider) where T : class
    {
        IEnvironmentSource source = FindHost(collider);
        if (typeof(T) == typeof(IWallSlideSurface) && source is BasicPhysicalObject host && !(host is Wall))
        {
            IWallSlideSurface configured = host.GetConfiguredWallSlideSurface();
            if (configured != null) return configured as T;
        }
        // 旧 Wall 的 getter 已读取生效配置，保留其原接口身份；其它未配置接口也继续兼容。
        return source as T;
    }

    public static BasicEntity FindEntity(Collider2D collider)
    {
        // 安全：没有命中时不能访问组件。
        if (collider == null) return null;
        // 复合碰撞体先以 attachedRigidbody 定位实体，避免只查子 Collider 丢失箱体身份。
        if (collider.attachedRigidbody != null &&
            collider.attachedRigidbody.TryGetComponent(out BasicEntity body)) return body;
        return collider.GetComponent<BasicEntity>();
    }
}

/// <summary>只读墙能力视图，参数和存活状态始终归原宿主；不创建第二份环境来源。</summary>
internal sealed class ConfiguredWallSlideSurface : IWallSlideSurface
{
    private readonly BasicPhysicalObject host;
    internal ConfiguredWallSlideSurface(BasicPhysicalObject source) => host = source;
    internal bool IsActive => host != null && host.IsEnvironmentActive && host.TryGetWallSlideMultiplier(out _);
    public float WallSlideMultiplier => host != null && host.TryGetWallSlideMultiplier(out float value) ? value : 1f;
}
