using UnityEngine;

namespace PhyData
{
    /// <summary>
    /// 职能：支撑参数的只读采样结果，不是全部环境速度，也不是持续效果的 Binding。
    /// [ENV-S1] 脚下表面本帧提供消退阻力、起跳、走跑倍率及平台世界位移。
    /// 它由 Context.SampleGroundFrame 生成；没有对应能力时分别使用 1、0、零位移。
    /// </summary>
    public readonly struct EntityEnvironmentFrame
    {
        public readonly float slowingMultiplier; // BasicEntity.PhyFunUpdate：乘本帧基础消退阻力。
        public readonly float jumpHeightOffset; // CharacterPhysics：起跳时读取。
        public readonly Vector2 platformDelta; // BasicEntity.SpeedCalculation：一次并入运动快照。
        public readonly float movementMultiplier; // CharacterPhysics：只乘当前主动走跑速度。

        public EntityEnvironmentFrame(float slowingMultiplier, float jumpHeightOffset, Vector2 platformDelta, float movementMultiplier = 1f)
        {
            this.slowingMultiplier = slowingMultiplier;
            this.jumpHeightOffset = jumpHeightOffset;
            this.platformDelta = platformDelta;
            this.movementMultiplier = movementMultiplier;
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
        if (capability is ConfiguredWaterRegion waterView) return waterView.IsActive;
        if (capability is Wall wall) return wall.IsEnvironmentActive && wall.TryGetWallSlideMultiplier(out _);
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
        if (typeof(T) == typeof(IWallSlideSurface) && source is BasicPhysicalObject host)
        {
            IWallSlideSurface configured = host.GetConfiguredWallSlideSurface();
            // 原 Wall 保持接口身份，但抽取墙项或转成触发器后不再提供墙能力。
            if (configured != null) return host is Wall ? source as T : configured as T;
            if (host is Wall || !host.IsNativeGeometryMode) return null;
        }
        if (typeof(T) == typeof(IWaterRegion)) return ResolveWater(source) as T;
        // 自定义旧宿主的固有接口保留；可移除有限项由上面的有效配置覆盖。
        return source as T;
    }

    internal static IWaterRegion ResolveWater(IEnvironmentSource source)
    {
        if (source is BasicPhysicalObject host)
        {
            IWaterRegion configured = host.GetConfiguredWaterRegion();
            if (configured != null) return configured;
            if (host is WaterRegion || !host.IsNativeGeometryMode) return null;
        }
        return source as IWaterRegion;
    }

    internal static bool TryGetMovement(IEnvironmentSource source, out float value)
    {
        value = 0f;
        if (source is BasicPhysicalObject host)
        {
            if (host.TryGetMovementSpeedOffset(out value)) return true;
            // 水／区域旧数值已经进入可移除列表，不再从旧接口回补抽走的项。
            if (host is WaterWithEffects || host is ForceField || !host.IsNativeGeometryMode) return false;
        }
        if (!(source is IMovementSpeedModifier movement)) return false;
        value = movement.MovementSpeedOffset;
        return true;
    }

    internal static bool TryGetStateVelocity(IEnvironmentSource source, out Vector2 value)
    {
        value = Vector2.zero;
        if (source is BasicPhysicalObject host)
        {
            if (host.TryGetPassiveVelocity(out value)) return true;
            if (host is WaterWithEffects || !host.IsNativeGeometryMode) return false;
        }
        if (!(source is IStateVelocitySource velocity)) return false;
        value = velocity.StateVelocity;
        return true;
    }

    internal static IDynamicEnvironmentForce ResolveDynamicForce(IEnvironmentSource source)
    {
        // 固有算法不随意赠送给转换后的材料；源键和已有惯性仍归原物理入口。
        if (source is BasicPhysicalObject host && !host.IsNativeGeometryMode) return null;
        return source as IDynamicEnvironmentForce;
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

/// <summary>有限水性质的只读视图；水范围和有效开关始终读取同一个宿主。</summary>
internal sealed class ConfiguredWaterRegion : IWaterRegion
{
    private readonly BasicPhysicalObject host;
    internal ConfiguredWaterRegion(BasicPhysicalObject source) => host = source;
    internal bool IsActive => host != null && host.IsEnvironmentActive && host.TryGetWaterSwimming(out _);
    public bool SwimmingEnabled => IsActive && host.TryGetWaterSwimming(out bool enabled) && enabled;
    public bool ContainsWaterPoint(Collider2D region, Vector2 point)
    {
        // 安全：失效、不同宿主或实心几何不发布水事实。
        if (!IsActive || region == null || !region.enabled || !region.isTrigger
            || region.gameObject != host.gameObject) return false;
        return region.OverlapPoint(point);
    }
    public bool TryGetSurfaceY(Collider2D region, float worldX, out float surfaceY)
    {
        surfaceY = 0f;
        // 水面动作首版仅支持水平矩形；其它形状仍可按原几何提供淹没。
        if (!IsActive || !(region is BoxCollider2D) || !region.enabled || !region.isTrigger
            || region.gameObject != host.gameObject || Mathf.Abs(region.transform.up.y) < .9999f) return false;
        Bounds bounds = region.bounds;
        if (worldX < bounds.min.x || worldX > bounds.max.x || bounds.size.y <= 0f) return false;
        surfaceY = bounds.max.y;
        return ContainsWaterPoint(region, new Vector2(worldX, surfaceY - Mathf.Min(.001f, bounds.size.y * .5f)));
    }
}
