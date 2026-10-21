using System.Collections.Generic;
using PhyData;
using UnityEngine;

/// <summary>
/// 例如角色踩上冰面，这里记下“这块冰面正在影响这个角色，以及已经登记了哪些效果”。
/// 同一来源即使同时通过脚下支撑和区域重叠接入，也只登记一份；最后一个依据消失才撤销。
/// 职能：实体持有的接入关系层；数值仍写入 IForceAction 的原容器，本类不积分速度。
/// 阅读：RefreshEnvironment → CollectSources（核对事实）→ SynchronizeSources（核对管线）→ SampleGroundFrame（采样）。
/// 核对管线内：Attach → RegisterEffects 登记；Detach 撤销。
/// </summary>
public sealed class EntityEnvironmentContext
{
    /// <summary>
    /// 一个来源对当前实体的登记单，只记“退出时要撤销什么”。
    /// 来源是否继续存在由本帧完整事实集合判断，不再保存接入方式。
    /// 职能：Context 私有关系记录；不存 ForceData 或累计速度。
    /// </summary>
    private sealed class Binding
    {
        public bool hasMovement;
        public bool hasStateVelocity;
        public IDynamicEnvironmentForce dynamicForce;
        public int parameterVersion;
    }

    // 写入端指向实体自身。通过既有窄口更新数值，不直接接管实体的字典。
    private readonly IForceAction receiver;
    private readonly Behaviour owner;
    // 唯一关系记录：一个 Registration 对应一张登记单。
    private readonly Dictionary<EnvironmentRegistration, Binding> bindings = new Dictionary<EnvironmentRegistration, Binding>();
    // 以下均为可复用工作缓存；不是另一份关系或物理状态。
    private readonly List<EnvironmentRegistration> removalBuffer = new List<EnvironmentRegistration>();
    // 本帧脚下与区域的完整来源事实；同一来源只存一份，收齐后统一核对绑定。
    private readonly HashSet<EnvironmentRegistration> currentSources = new HashSet<EnvironmentRegistration>();

    public EntityEnvironmentContext(IForceAction receiver, Behaviour owner)
    {
        this.receiver = receiver;
        this.owner = owner;
    }

    public bool IsActive => owner != null && owner.isActiveAndEnabled;
    /// <summary>
    /// 瞬时物理快照
    /// </summary>
    public EntityEnvironmentFrame Frame { get; private set; } = EntityEnvironmentFrame.Empty;
    public EntityWaterFrame WaterFrame { get; private set; }

#if UNITY_EDITOR
    /// <summary>[EditorOnly] 当前有效的环境来源数，不另存已退出接触力。</summary>
    public int DebugSourceCount => bindings.Count;
#endif

    /// <summary>
    /// [ENV-03] 首次绑定入口：有效、尚未登记且具有持续能力的来源，才建立关系并登记效果。
    /// 持续存在直接保留；退出由核对管线调用 Detach，不在本入口处理。
    /// </summary>
    private void Attach(EnvironmentRegistration source)
    {
        // 生命周期保护：禁用或已销毁的双方不能新建关系。
        if (!IsActive || !source.IsActive) return;
        // 接入逻辑：持续命中的来源不重复登记，保留实体已积累的运动状态。
        if (bindings.TryGetValue(source, out Binding existing))
        {
            // 同来源仅在真正改值后的正常相位重写参数；不退源、不清刚体真实动量。
            if (source.Source is IEnvironmentParameterSource parameters
                && existing.parameterVersion != parameters.EnvironmentParameterVersion)
            {
                RegisterEffects(source, existing);
                // 最后一个持续项被抽走后不留下空绑定；表面／水事实仍独立采样。
                if (!existing.hasMovement && !existing.hasStateVelocity && existing.dynamicForce == null)
                    Detach(source);
            }
            return;
        }
        // 能力选择：三类持续能力都缺席时不建绑定；零值仍是有效能力，表面采样独立进行。
        IEnvironmentSource provider = source.Source;
        if (!EnvironmentCapabilities.TryGetMovement(provider, out _)
            && !EnvironmentCapabilities.TryGetStateVelocity(provider, out _)
            && EnvironmentCapabilities.ResolveDynamicForce(provider) == null) return;
        //首次接入
        var binding = new Binding();
        bindings.Add(source, binding);
        //环境容器关联受力者相关数据
        source.Track(this);
        RegisterEffects(source, binding);
    }

    /// <summary>
    /// [ENV-04] 首次接入或来源配置改值后，按能力写入原有参数容器，Binding 保留撤销凭据。
    /// 冰面的动态参数去 ENV-05；下一物理速度阶段如何消费这些容器去 ENV-06。
    /// </summary>
    private void RegisterEffects(EnvironmentRegistration source, Binding binding)
    {
        IEnvironmentSource provider = source.Source;
        // 同源只撤销已经缺席的单项，其余来源关系和刚体真实动量保持。
        bool hasMovement = EnvironmentCapabilities.TryGetMovement(provider, out float movement);
        if (hasMovement)
        {
            receiver.StatePowerRegistration(source, movement);
        }
        else if (binding.hasMovement) receiver.StatePowerCancellation(source);
        binding.hasMovement = hasMovement;
        // 能力选择：持续速度独立于移速修饰，以 Registration 为稳定来源键。
        bool hasVelocity = EnvironmentCapabilities.TryGetStateVelocity(provider, out Vector2 velocity);
        if (hasVelocity)
        {
            receiver.AddSpeedStatus(source, velocity);
        }
        else if (binding.hasStateVelocity) receiver.RemoveSpeedStatus(source);
        binding.hasStateVelocity = hasVelocity;
        // 能力选择：仅动态施力需要具体组件按受力者产生参数；动态键仍是该组件接口。
        IDynamicEnvironmentForce dynamicForce = EnvironmentCapabilities.ResolveDynamicForce(provider);
        if (binding.dynamicForce != null && binding.dynamicForce != dynamicForce)
            receiver.RemoveForce(binding.dynamicForce);
        if (dynamicForce != null)
        {
            receiver.AddForce(dynamicForce, dynamicForce.CreateEnvironmentForce(receiver));
        }
        binding.dynamicForce = dynamicForce;
        binding.parameterVersion = provider is IEnvironmentParameterSource parameters
            ? parameters.EnvironmentParameterVersion : 0;
    }

    /// <summary>
    /// [ENV-07] 撤销一份登记，先同步双方关系，再按登记单撤销实际效果。
    /// 普通离开经核对管线到这里；源或实体禁用经 ENV-L1 的 ReleaseAll 到这里。
    /// </summary>
    internal void Detach(EnvironmentRegistration source)
    {
        // 幂等清理：双方可能先后发起解除，第二次直接结束。
        if (!bindings.TryGetValue(source, out Binding binding)) return;
        bindings.Remove(source);
        source.Untrack(this);
        // 按登记凭据撤销，而不是重新询问来源当前有什么能力。
        if (binding.hasMovement) receiver.StatePowerCancellation(source);
        if (binding.hasStateVelocity) receiver.RemoveSpeedStatus(source);
        // RemoveForce 只撤销环境控制；取得的真实惯性仍在刚体中，不在这里清零。
        if (binding.dynamicForce != null) receiver.RemoveForce(binding.dynamicForce);
    }

    /// <summary>
    /// 环境更新入口  
    /// 相位 2 一次核对脚下和区域：脚下仍按支撑 Collider，区域按实体本帧重叠到的 Trigger。
    /// [ENV-02] 职能：实体环境刷新入口；核对事实 → 核对管线 → 采样。
    /// </summary>
    public void RefreshEnvironment(Collider2D ground, List<Collider2D> regionOverlaps, Vector2? waterSamplePoint = null,
        Bounds? waterBody = null)
    {
        // 核对事实：先收齐脚下与区域身份，同一来源只在完整集合中保留一次。
        IEnvironmentSource groundSource = EnvironmentCapabilities.FindHost(ground);
        CollectSources(groundSource, regionOverlaps);

        // 核对关系：完整事实收齐后才决定进出，接入方式转换不会中途撤销效果。
        SynchronizeSources();
        // 采样：复用脚下宿主，区域命中不提供脚下参数。
        SampleGroundFrame(groundSource);
        SampleWaterFrame(regionOverlaps, waterSamplePoint, waterBody);
    }

    private void SampleWaterFrame(List<Collider2D> regions, Vector2? point, Bounds? body)
    {
        bool submerged = false, swimming = false;
        bool partial = false, surfaceSwimming = false;
        float surfaceY = float.NegativeInfinity;
        // 事实缺席：未提供角色采样点或区域命中时，不沿用上一帧水事实。
        if (point.HasValue && regions != null)
            foreach (Collider2D region in regions)
            {
                IEnvironmentSource source = EnvironmentCapabilities.FindHost(region);
                // 实际逻辑：在同一有效水源内核对淹没和游泳，禁止跨来源拼接条件。
                IWaterRegion water = EnvironmentCapabilities.ResolveWater(source);
                if (water == null || !source.IsEnvironmentActive) continue;
                // 上缘入水独立决定淹没，氧气不读取水面动作或按键。
                if (water.ContainsWaterPoint(region, point.Value))
                {
                    submerged = true;
                    swimming |= water.SwimmingEnabled;
                }
                // 水面事实：身体跨过同一水源的上边界，且下缘仍在其有效范围。
                if (!body.HasValue || !water.TryGetSurfaceY(region, body.Value.center.x, out float height)
                    || body.Value.max.y <= height || body.Value.min.y >= height
                    || !water.ContainsWaterPoint(region, new Vector2(body.Value.center.x, body.Value.min.y))) continue;
                partial = true;
                // 多池重叠：优先选允许游泳的水面，再在同类中选最高者；不拼接其它来源的许可。
                if ((water.SwimmingEnabled && !surfaceSwimming) ||
                    (water.SwimmingEnabled == surfaceSwimming && height > surfaceY)) surfaceY = height;
                surfaceSwimming |= water.SwimmingEnabled;
            }
        WaterFrame = new EntityWaterFrame(submerged, swimming, partial, surfaceSwimming, partial ? surfaceY : 0f);
    }

    /// <summary>核对事实：每次重建脚下与区域的完整来源集合，只收集身份，不登记或撤销效果。</summary>
    private void CollectSources(IEnvironmentSource groundSource, List<Collider2D> regionOverlaps)
    {
        currentSources.Clear();
        // 脚下事实：有效宿主提供一份来源；无宿主的普通地面不提供环境效果。
        if (groundSource != null)
            currentSources.Add(groundSource.EnvironmentRegistration);

        // 区域事实缺席时，保留上面收集的脚下来源即可。
        if (regionOverlaps == null) return;
        foreach (Collider2D hit in regionOverlaps)
        {
            IEnvironmentSource source = EnvironmentCapabilities.FindHost(hit);
            // 区域事实：仅收有效来源；重复命中或与脚下同源时，HashSet 自动去重。
            if (source != null && source.EnvironmentRegistration.IsActive)
                currentSources.Add(source.EnvironmentRegistration);
        }
    }

    /// <summary>核对关系：完整事实中消失或已失效的来源退出，其余来源确认首次绑定。</summary>
    private void SynchronizeSources()
    {
        // 遍历保护：先收集待删除来源，避免枚举 bindings 时修改字典。
        removalBuffer.Clear();
        foreach (var entry in bindings)
            // 退出规则：来源失效，或脚下与区域均未再命中它，才撤销整份登记。
            if (!entry.Key.IsActive || !currentSources.Contains(entry.Key))
                removalBuffer.Add(entry.Key);
        foreach (EnvironmentRegistration source in removalBuffer)
            Detach(source);
        foreach (EnvironmentRegistration source in currentSources)
            Attach(source);
    }

    /// <summary>采样参数：表面读值不要求建立持续效果绑定，写成一帧只读结果供原消费点使用。</summary>
    private void SampleGroundFrame(IEnvironmentSource source)
    {
        // 能力仍从发现到的同一个环境来源读取；没有相应接口时采用中性采样值。
        IGroundResponse response = source as IGroundResponse;
        IPlatformMotion platform = source as IPlatformMotion;
        float slowing = response?.SlowingEffect ?? 1f;
        float jumpOffset = response?.JumpHeightNum ?? 0f;
        float movement = 1f;
        // 新列表也可挂在普通唯一宿主上；只有当前脚下来源的项参与支撑采样。
        if (source is BasicPhysicalObject host)
        {
            // 旧地面项一旦抽走就使用中性值，不从旧接口重新补回。
            if (host is BaseGround || host.IsTriggerEnvironment) { slowing = 1f; jumpOffset = 0f; }
            if (host.TryGetGroundSlowingMultiplier(out float configuredSlowing)) slowing = configuredSlowing;
            if (host.TryGetGroundJumpMultiplier(out float configuredJump)) jumpOffset = configuredJump - 1f;
            if (host.TryGetGroundMovementMultiplier(out float configuredMovement)) movement = configuredMovement;
        }
        Frame = new EntityEnvironmentFrame(slowing, jumpOffset,
            platform?.Delta ?? Vector2.zero, movement);
    }

    /// <summary>[ENV-L1] 实体禁用入口：快照全部来源再逐个走 ENV-07，同步源端索引；可重复调用。</summary>
    public void ReleaseAll()
    {
        removalBuffer.Clear();
        removalBuffer.AddRange(bindings.Keys);
        foreach (EnvironmentRegistration source in removalBuffer) Detach(source);
        removalBuffer.Clear();
        currentSources.Clear();
        Frame = EntityEnvironmentFrame.Empty;
        WaterFrame = default;
    }
}
