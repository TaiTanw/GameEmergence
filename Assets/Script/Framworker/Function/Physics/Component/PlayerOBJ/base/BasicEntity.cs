using PhyData;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色与箱子共用的物理执行主体，负责自身的物理相位及候选环境判定。
/// 阅读主线：相位 1 几何 → 2 环境 → 3 速度/预测 → 4 管理器接触解算与本实体环境约束 → 5 提交。
/// 环境关系由 EntityEnvironmentContext 管理，累计速度由 EntityEffectState 持有；
/// 本类保存同帧运动快照、候选查询缓存和解算结果，最终经 Unity 后端入口提交一次位移。
/// </summary>
public abstract class BasicEntity : MonoBehaviour, IForceAction, IDynamicAddForce, IEnvironmentReceiver
{
    /// <summary>Unity 查询与提交端；其它后端可在实体层替换此入口。</summary>
    protected virtual IPhysicsWorldAccess2D PhysicsWorld => UnityPhysicsWorldAccess2D.Instance;
    protected virtual bool MotionUpdatesEnabled => true;
    protected virtual bool UsesWorldAxisMovement => false;

    /// <summary>当前环境请求策略；与 Unity 查询和提交入口分别替换。</summary>
    internal IEntityRequestSolveStrategy RequestSolveStrategy { get; set; } =
        CurrentEntityRequestSolver.Instance;

    protected Rigidbody2D rb; //刚体
    protected BoxCollider2D boxCollider;//碰撞器

    /// <summary>
    /// 物理配置数据
    /// </summary>
    [SerializeField]
    protected SO_CPhysics cPhysics;
    /// <summary>
    /// 地面检测中心（外部拖拽关联
    /// </summary>
    [SerializeField]
    protected Transform groundV;
    /// <summary>
    /// 左墙检测
    /// </summary>
    [SerializeField]
    protected Transform leftV;
    /// <summary>
    /// 右墙检测
    /// </summary>
    [SerializeField]
    protected Transform rightV;
    /// <summary>
    /// 头顶检测
    /// </summary>
    [SerializeField]
    protected Transform upV;

    public float gravity = -35f;    //重力速度

    public float airResistance = 1;//空气阻力（0-1范围，影响下落情况
    /// <summary>
    /// 受到环境影响程度（近似理解为质量的倒数
    /// </summary>
    public float envImpact = 1;

    #region 运行时数据
    /// <summary>
    /// 当前几何检测数据
    /// </summary>
    protected GeometryPhysicsData nowGemetry;
    /// <summary>
    /// 物理职能数据
    /// </summary>
    protected BasePhyFunData nowPhyFun;
    /// <summary>
    /// 当前移动属性
    /// </summary>
    protected PlayerPhysicsData playerPhysicsData;

    // 环境关系和效果数值分别由两个对象持有，不重复累计。
    private EntityEnvironmentContext environmentContext;
    public EntityEnvironmentContext EnvironmentContext => environmentContext ??= new EntityEnvironmentContext(this, this);
    protected EntityEnvironmentFrame EnvironmentFrame => EnvironmentContext.Frame;

    // 数值状态与实体同生命周期；来源关系仍由 environmentContext 管理。
    private readonly EntityEffectState effectState = new EntityEffectState();
    /// <summary>
    /// 自身阻力系数
    /// </summary>
    protected float self_resistanceCoefficient = 3;

    /// <summary>
    /// 本帧未约束运动快照：相位 3 在速度结算后整体替换，预测和相位 4 请求位移共同读取。
    /// 已包含现有地面运动规则与平台补偿；不保存接触偏置，也不承担跨帧速度累计。
    /// </summary>
    private EntityMotionFrame motionFrame;

    /// <summary>
    /// 实体解算结果：相位 4 合成请求位移；相位 5 只提交。
    /// </summary>
    protected EntitySolutionResult entitySolutionResult;
    private readonly EntityCandidateGeometry candidateGeometry = new EntityCandidateGeometry(); // 候选查询与请求裁剪工作区

    // 相位 5 只留下待观察的请求；下一物理步相位 1 才发布实际位置。
    private int displacementStep;
    private int pendingDisplacementStep;
    private Vector2 pendingDisplacementOrigin;
    private Vector2 pendingRequestedDelta;
    private bool hasPendingDisplacement;
    private EntityDisplacementObservation lastDisplacementObservation;
    private bool hasDisplacementObservation;

    // 相位 1 overlap 工作缓存；有效 Trigger 再写入 nowGemetry.regionOverlaps。
    private readonly List<Collider2D> regionOverlapHits = new List<Collider2D>();
    private readonly List<RaycastHit2D> groundProbeHits = new List<RaycastHit2D>(); // 脚下查询命中缓存
    protected const float GroundProbeDistance = 0.02f; // 脚下允许的微小间隙

#if UNITY_EDITOR
    // [EditorOnly] 只读输出口；编辑器侧负责采样、缓存、快照组装与跨帧差值计算。
    public RigidbodyType2D DebugBodyType => rb != null ? rb.bodyType : RigidbodyType2D.Static;
    public Vector2 DebugActualPosition => rb != null ? rb.position : (Vector2)transform.position;
    public float DebugRotation => rb != null ? rb.rotation : transform.eulerAngles.z;
    public float DebugAngularVelocity => rb != null ? rb.angularVelocity : 0f;
    public Vector2 DebugPlanarVelocity => GetPlanarVelocity();
    public bool DebugIsGrounded => nowGemetry != null && nowGemetry.isGrounded;
    public bool DebugIsTopBlocked => nowGemetry != null && nowGemetry.istop;
    public bool DebugIsOnLeftWall => nowGemetry != null && nowGemetry.onLeftWall;
    public bool DebugIsOnRightWall => nowGemetry != null && nowGemetry.onRightWall;
    public Vector2 DebugGroundNormal => nowGemetry != null ? nowGemetry.groundNormal : Vector2.zero;
    public Collider2D DebugGroundCollider => nowGemetry?.groundCollider;
    public Collider2D DebugLeftWallCollider => nowGemetry?.leftWallCollider;
    public Collider2D DebugRightWallCollider => nowGemetry?.rightWallCollider;
    public Vector2 DebugCandidateCenter => candidateGeometry.candidateCenter;
    public int DebugCandidatePathCount => candidateGeometry.pathHits.Count;
    public Collider2D DebugCandidateFirstPathCollider => candidateGeometry.pathHits.Count > 0 ? candidateGeometry.pathHits[0].collider : null;
    public Vector2 DebugCandidateFirstPathNormal => candidateGeometry.pathHits.Count > 0 ? candidateGeometry.pathHits[0].normal : Vector2.zero;
    public float DebugCandidateFirstPathDistance => candidateGeometry.pathHits.Count > 0 ? candidateGeometry.pathHits[0].distance : 0f;
    public bool DebugCandidateFirstPathZeroDistance => candidateGeometry.pathHits.Count > 0 &&
        candidateGeometry.pathHits[0].distance <= 0f;
    public int DebugCandidateOverlapCount => candidateGeometry.endpointOverlaps.Count;
    public Collider2D DebugCandidateFirstOverlapCollider => candidateGeometry.endpointOverlaps.Count > 0 ? candidateGeometry.endpointOverlaps[0] : null;
    public int DebugCandidateOneWayCount => candidateGeometry.oneWayEffectors.Count;
    public int DebugCandidateDownwardCount => candidateGeometry.downwardHits.Count;
    public Vector2 DebugCandidateFirstDownwardNormal => candidateGeometry.downwardHits.Count > 0 ? candidateGeometry.downwardHits[0].normal : Vector2.zero;
    public float DebugCandidateFirstDownwardGap => candidateGeometry.downwardHits.Count > 0
        ? candidateGeometry.downwardHits[0].distance - candidateGeometry.downwardCastLift : 0f;
    public bool DebugCandidateFirstDownwardZeroDistance => candidateGeometry.downwardHits.Count > 0 &&
        candidateGeometry.downwardHits[0].distance <= 0f;
    public int DebugEnvironmentSourceCount => environmentContext?.DebugSourceCount ?? 0;
    public int DebugMovementModifierCount => effectState.DebugMovementModifierCount;
    public int DebugStateVelocityCount => effectState.DebugStateVelocityCount;
    public int DebugDynamicForceCount => effectState.DebugDynamicForceCount;
    public float DebugEnvironmentSlowingMultiplier => environmentContext?.Frame.slowingMultiplier ?? 1f;
    public float DebugEnvironmentJumpHeightOffset => environmentContext?.Frame.jumpHeightOffset ?? 0f;

    public bool TryGetEditorGizmoData(out PhysicsEditorObservationBridge.GizmoData data)
    {
        data = default;
        if (cPhysics == null) return false;

        data.hasGround = groundV != null;
        data.hasTop = upV != null;
        data.hasLeft = leftV != null;
        data.hasRight = rightV != null;
        data.groundCenter = data.hasGround ? groundV.position : default;
        data.topCenter = data.hasTop ? upV.position : default;
        data.leftCenter = data.hasLeft ? leftV.position : default;
        data.rightCenter = data.hasRight ? rightV.position : default;
        data.horizontalSize = cPhysics.boxCastH;
        data.verticalSize = cPhysics.boxCastV;
        return true;
    }
#endif

    #endregion

    #region 实体推动与接触

    /// <summary>
    /// 受环境影响程度（质量倒数，接触挤出权重）
    /// </summary>
    public float EnvImpact => envImpact;

    // F_6.2_7-1：供相位 4 接触对回算读取的本帧最终请求位移。
    internal Vector2 RequestedWorldDelta => entitySolutionResult.requestedWorldDelta;
    // 残余回算期间：该实体自己被静墙裁剪的水平进入方向，0 表示未阻挡。
    // 水平传播写回之后：还包含本帧从上游接触传来的同一方向。
    internal float BlockedHorizontalDirection => entitySolutionResult.blockedHorizontalDirection;

    /// <summary>
    /// 接纳沿水平接触传来的阻挡方向。已有直接裁剪或更早写入的方向时保持原值。
    /// </summary>
    internal void AdoptPropagatedHorizontalBlock(float direction)
    {
        // 安全：0 不是方向；已有方向时不覆盖直接静墙裁剪。
        if (direction == 0f || entitySolutionResult.blockedHorizontalDirection != 0f) return;
        entitySolutionResult.blockedHorizontalDirection = direction;
    }

    /// <summary>读取上一次已观察到的请求与实际落点；当前相位 5 的请求尚未包含在内。</summary>
    public bool TryGetLastDisplacementObservation(out EntityDisplacementObservation observation)
    {
        observation = lastDisplacementObservation;
        return hasDisplacementObservation;
    }

    /// <summary>
    /// 当前平面速度（主动 + 被动；动态接触力在相位 3 从容器累计到被动速度）
    /// 相位 3 构建运动快照的自由速度读口；该值尚未经过地面位移处理，不等于最终世界位移除以 dt。
    /// </summary>
    public Vector2 GetPlanarVelocity()
    {
        return new Vector2(
            playerPhysicsData.horizontalSpeed + playerPhysicsData.phyHSpeed,
            playerPhysicsData.verticalSpeed + playerPhysicsData.phyVSpeed);
    }

    /// <summary>
    /// 相位 4 接触对的纠偏写入口：首轮预测解算与受阻反馈都累加到同一份偏置。
    /// 只写结果，不移动刚体；环境查询和最终合成会读取累加后的值。
    /// </summary>
    public void AccumulateDisplacementOffset(Vector2 offset)
    {
        entitySolutionResult.displacementOffset += offset;
    }

    /// <summary>
    /// 相位 4 查询候选环境并改写环境约束结果的入口。
    /// 相位 4 将预测终点与当前接触偏置组成候选终点，重建路径、终点重叠及脚下命中。
    /// 排除实体碰撞体后，交由环境请求策略得到允许的 X 和额外坡面 Y。
    /// 接触回算后可再次调用；复用列表但重置上轮查询，不改帧初着地事实，也不提交移动。
    /// </summary>
    internal void ProbeCandidateEnvironment(PhysicalBoundingBox prediction)
    {
        entitySolutionResult.environmentOffset = Vector2.zero;
        entitySolutionResult.allowedHorizontalDelta = prediction.motionFrame.plannedWorldDelta.x +
            entitySolutionResult.displacementOffset.x;
        candidateGeometry.pathHits.Clear();
        candidateGeometry.downwardHits.Clear();
        candidateGeometry.liftHits.Clear();
        candidateGeometry.allowedDownwardHits.Clear();
        candidateGeometry.endpointOverlaps.Clear();
        candidateGeometry.oneWayEffectors.Clear();
        candidateGeometry.candidateCenter = Vector2.zero;
        candidateGeometry.candidateDelta = Vector2.zero;
        candidateGeometry.downwardCastLift = 0f;
        // 安全：无碰撞体或查询层配置时，本帧没有可用的候选环境事实。
        if (boxCollider == null || !boxCollider.enabled || cPhysics == null) return;

        Bounds bounds = PhysicsWorld.ReadAabb(boxCollider);
        candidateGeometry.candidateCenter = prediction.point + entitySolutionResult.displacementOffset;
        candidateGeometry.candidateDelta = candidateGeometry.candidateCenter - (Vector2)bounds.center;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask((int)cPhysics.groundLayer | (int)cPhysics.wallLayer);
        float distance = candidateGeometry.candidateDelta.magnitude;
        // 零位移无需路径扫掠；终点重叠和脚下查询仍须执行。
        if (distance > 0f)
            PhysicsWorld.BoxCast(bounds.center, bounds.size, 0f, candidateGeometry.candidateDelta / distance,
                distance, filter, candidateGeometry.pathHits);
        PhysicsWorld.OverlapBox(candidateGeometry.candidateCenter, bounds.size, 0f, filter,
            candidateGeometry.endpointOverlaps);
        filter.SetLayerMask(cPhysics.groundLayer);
        // 从候选框上方回扫到脚下，距离减去 lift 才是候选底部的原始有符号间隙。
        // 回扫可能先命中天花板或单向平台，不能直接把首个结果当作支撑。
        candidateGeometry.downwardCastLift = bounds.size.y + Mathf.Abs(candidateGeometry.candidateDelta.y) + 0.01f;
        PhysicsWorld.BoxCast(candidateGeometry.candidateCenter + Vector2.up * candidateGeometry.downwardCastLift,
            bounds.size, 0f, Vector2.down, candidateGeometry.downwardCastLift + 0.05f,
            filter, candidateGeometry.downwardHits);

        // 三类命中都只保留环境 Collider；实体间接触由 PhysicsSolverMgr 单独解算。
        for (int i = candidateGeometry.pathHits.Count - 1; i >= 0; i--)
        {
            Collider2D hit = candidateGeometry.pathHits[i].collider;
            if (hit == null || hit.attachedRigidbody == rb || EnvironmentCapabilities.FindEntity(hit) != null)
                candidateGeometry.pathHits.RemoveAt(i);
            else
                candidateGeometry.RecordOneWayEffector(hit);
        }
        for (int i = candidateGeometry.endpointOverlaps.Count - 1; i >= 0; i--)
        {
            Collider2D hit = candidateGeometry.endpointOverlaps[i];
            if (hit == null || hit.attachedRigidbody == rb || EnvironmentCapabilities.FindEntity(hit) != null)
                candidateGeometry.endpointOverlaps.RemoveAt(i);
            else
                candidateGeometry.RecordOneWayEffector(hit);
        }
        for (int i = candidateGeometry.downwardHits.Count - 1; i >= 0; i--)
        {
            Collider2D hit = candidateGeometry.downwardHits[i].collider;
            if (hit == null || hit.attachedRigidbody == rb || EnvironmentCapabilities.FindEntity(hit) != null)
                candidateGeometry.downwardHits.RemoveAt(i);
            else
                candidateGeometry.RecordOneWayEffector(hit);
        }
        RequestSolveStrategy.ResolveCandidate(BuildEnvironmentSolveInput(bounds), candidateGeometry,
            PhysicsWorld, ref entitySolutionResult);
    }

    private EntityEnvironmentSolveInput BuildEnvironmentSolveInput(Bounds bounds) =>
        new EntityEnvironmentSolveInput(bounds, rb,
            cPhysics != null ? cPhysics.groundLayer : default,
            cPhysics != null ? cPhysics.wallLayer : default,
            cPhysics != null ? cPhysics.maxClimbAngle : 0f,
            nowGemetry.groundCollider, nowGemetry.isGrounded,
            playerPhysicsData.verticalSpeed, Time.fixedDeltaTime, motionFrame, oneWayDropColliders);

    /// <summary>
    /// 正在穿过的单向平台只读视图。角色在相位 1 维护，相位 4 只读；箱子保持为空。
    /// </summary>
    internal IReadOnlyList<Collider2D> oneWayDropColliders;

    /// <summary>
    /// 相位 1 共用脚下查询：覆盖实体碰撞框前缘，在多个命中中选法线最朝上的有效支撑。
    /// 排除自身并要求法线向上分量大于 0.7，允许 GroundProbeDistance 内的微小贴地间隙。
    /// 只返回几何命中；着地字段与脚下能力分别由子类 GeometricQuery 和相位 2 更新。
    /// </summary>
    protected RaycastHit2D FindGroundHit(Vector2 center, Vector2 size, float angle,
        Vector2 direction, ContactFilter2D filter)
    {
        groundProbeHits.Clear();
        // F_6.2_6-6：上坡抬升按完整碰撞框前缘计算；着地探针也覆盖该前缘，避免箱体被抬起后中心脚探针悬空。
        if (boxCollider != null && boxCollider.enabled)
            size.x = Mathf.Max(size.x, PhysicsWorld.ReadAabb(boxCollider).size.x);
        PhysicsWorld.BoxCast(center, size, angle, direction, GroundProbeDistance,
            filter, groundProbeHits);
        RaycastHit2D support = default;
        float bestUp = 0.7f;
        foreach (RaycastHit2D hit in groundProbeHits)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody == rb) continue;
            // 实际逻辑：所有尚未穿完的平台都不参加着地选择，避免重选旧平台或截停重力。
            if (IsDroppingThrough(hit.collider)) continue;
            float up = Vector2.Dot(hit.normal, Vector2.up);
            if (up <= bestUp) continue;
            support = hit;
            bestUp = up;
        }
        return support;
    }

    private bool IsDroppingThrough(Collider2D collider)
    {
        // 安全：箱子或尚未形成忽略视图的角色没有要排除的平台。
        if (oneWayDropColliders == null) return false;
        for (int i = 0; i < oneWayDropColliders.Count; i++)
            if (oneWayDropColliders[i] == collider) return true;
        return false;
    }

    /// <summary>把本帧运动与候选环境工作区交给当前策略，生成最终位移请求。</summary>
    internal void FinalizeRequestedDisplacement()
    {
        Bounds bounds = boxCollider != null ? PhysicsWorld.ReadAabb(boxCollider) : default;
        RequestSolveStrategy.FinalizeRequest(BuildEnvironmentSolveInput(bounds), candidateGeometry,
            ref entitySolutionResult);
    }

    /// <summary>
    /// F_6.2_7-4：所有接触回算结束后，按最终静态阻挡清理朝障碍的瞬时速度来源。
    /// 使用净被动速度判断整体清理；旧接触退出时还会通过 ClearBlockedContactSpeed 按来源清理。
    /// </summary>
    internal void ClearTransientSpeedForFinalBlock()
    {
        if (playerPhysicsData.phyHSpeed * entitySolutionResult.blockedHorizontalDirection > 0f)
            effectState.ClearTransientSpeedAtWall();
    }

    /// <summary>
    /// 旧接触力退出时的来源级清理入口：只清掉该来源朝最终静态障碍的累计速度。
    /// 其它来源抵消了净被动速度时，这条失效接触的残留也不会在松开后重新释放。
    /// </summary>
    internal void ClearBlockedContactSpeed(IDynamicAddForce source)
    {
        effectState.ClearBlockedContactSpeed(source, entitySolutionResult.blockedHorizontalDirection);
    }

    /// <summary>
    /// 接触刚成立时，按挤出方向一次性削弱会继续进入接触面的非持续速度来源。
    /// 只改限时速度与 fadeAway 动态力；主动速度、状态速度和仍在 apply 的持续力不属于本次碰撞消耗。
    /// </summary>
    public void AttenuateTransientContactSpeed(float separateSign, float retainRatio)
    {
        effectState.AttenuateTransientContactSpeed(separateSign, retainRatio);
    }

    #endregion

    #region IForceAction 受力入口

    /// <summary>
    /// 写入主动移速修饰；ENV-04 以 Registration 为环境来源键，数值状态在相位 3 汇总。
    /// </summary>
    /// <param name="iD">唯一标识</param>
    /// <param name="num">影响程度</param>
    public void StatePowerRegistration(EnvironmentRegistration iD, float num)
    {
        effectState.RegisterMovementModifier(iD, num);
    }
    /// <summary>
    /// 撤销该来源的主动移速修饰，标记下一速度阶段重新汇总。
    /// </summary>
    /// <param name="iD"></param>
    public void StatePowerCancellation(EnvironmentRegistration iD)
    {
        effectState.RemoveMovementModifier(iD);
    }
    /// <summary>
    /// 外部提供速度(固定时间影响
    /// </summary>
    /// <param name="time">持续时间</param>
    /// <param name="addSpeed">水平速度叠加</param>

    public void AddTimeSpeed(float time, float addSpeed)
    {
        effectState.AddTimedSpeed(time, addSpeed, envImpact);
    }
    /// <summary>
    /// 写入该来源的持续速度；ENV-04 登记，ENV-06 在数值状态中逐来源相加。
    /// </summary>
    /// <param name="iD">来源键；环境状态使用 Registration。</param>
    /// <param name="force">持续附加速度；沿用旧参数名，不代表需要积分的力。</param>
    public void AddSpeedStatus(EnvironmentRegistration iD, Vector2 force)
    {
        effectState.AddStateVelocity(iD, force);
    }
    /// <summary>
    /// 外部取消状态性质速度
    /// </summary>
    /// <param name="iD">需要删除的持续速度来源。</param>
    public void RemoveSpeedStatus(EnvironmentRegistration iD)
    {
        effectState.RemoveStateVelocity(iD);
    }

    /// <summary>接收 ENV-04 创建的动态参数；累计速度归数值状态，来源键仍是动态能力提供者。</summary>
    public void AddForce(IDynamicAddForce iD, ForceData force)
    {
        effectState.AddDynamicForce(iD, force);
    }

    /// <summary>
    /// 新增或刷新动态力控制参数，同时保留已经跨帧累计的 speedStacking。
    /// 接触数据链路：管理器相位 4 调用 → 下一帧数值状态积分 → phyHSpeed 参与位移预测。
    /// IForceAction 窄口只传施力参数，累计速度仍由数值状态持有。
    /// </summary>
    public void SetOrUpdateDynamicForce(IDynamicAddForce source, DynamicForceParameters parameters)
    {
        effectState.SetOrUpdateDynamicForce(source, parameters);
    }

    /// <summary>
    /// 改变受力
    /// </summary>
    /// <param name="iD"></param>
    /// <param name="newForce"></param>
    public void ChangeForce(IDynamicAddForce iD, float newForce)
    {
        effectState.ChangeDynamicForce(iD, newForce);
    }
    /// <summary>
    /// 施力类型变化
    /// </summary>
    /// <param name="iD"></param>
    /// <param name="newSpeed"></param>
    public void ChangeType(IDynamicAddForce iD, E_PhyForceType type)
    {
        effectState.ChangeDynamicType(iD, type);
    }


    /// <summary>ENV-07 的数值出口：停止来源控制，转 fadeAway；剩余速度由 ENV-06 继续衰减。</summary>
    public void RemoveForce(IDynamicAddForce iD)
    {
        effectState.RemoveDynamicForce(iD);
    }


    #endregion

    protected virtual void Awake()
    {
        BindEffectReceiver();
        if (cPhysics == null)
        {
            print("SO_玩家物理配置数据为空，请拖拽引用");
        }
        //必要组件关联
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        //重力由 EPhy 计算；当前保留 Dynamic，让 Unity 2D 负责静态世界的最终非穿透约束
        rb.gravityScale = 0;
        //待静态扫掠、残嵌恢复和接触稳定闭环后，再评估是否切换为全 Kinematic
        //rb.bodyType = RigidbodyType2D.Kinematic;
        //实时物理状态信息初始化
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry =new GeometryPhysicsData();
        //下落加速度受空气阻力影响
        gravity*=airResistance;
        Init();
    }

    /// <summary>供自行实现 Awake 的实体明确完成效果回调关联。</summary>
    protected void BindEffectReceiver() => effectState.BindReceiver(this);
    /// <summary>
    /// 初始化附加逻辑(子类可重写，初始化自身子类详细数据
    /// </summary>
    protected virtual void Init()
    {
        nowPhyFun = new BasePhyFunData();
    }

    /// <summary>显式刷新运动；资源/道具效果不归此入口，环境撤销须由规则调用者单独选择。</summary>
    public void RefreshMotionState(Vector2? position = null, bool releaseEnvironmentEffects = false)
    {
        OnMotionRefresh(position.HasValue);
        hasPendingDisplacement = hasDisplacementObservation = false;
        motionFrame = default;
        entitySolutionResult = default;
        playerPhysicsData.horizontalSpeed = playerPhysicsData.verticalSpeed = 0f;
        playerPhysicsData.phyHSpeed = playerPhysicsData.phyVSpeed = 0f;
        if (releaseEnvironmentEffects) environmentContext?.ReleaseAll();
        effectState.ResetTransientMotion();
        PhysicsWorld.RestoreBodyMotion(rb, position);
        ResampleEnvironment();
    }

    protected virtual void OnMotionRefresh(bool relocated) { }

    public void ResampleEnvironment()
    {
        GeometricQuery();
        CollectRegionOverlapFacts();
        PhyFunUpdate();
    }

    protected virtual void OnEnable()
    {
        displacementStep = 0;
        hasPendingDisplacement = false;
        hasDisplacementObservation = false;
#if UNITY_EDITOR
        if (Application.isPlaying)
            PhysicsEditorObservationBridge.NotifyEntityEnabled(this);
#endif
        //物理更新时序设置
        //几何查询
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(GeometricQueryPhase, 1);
        //物理职能更新
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(PhyFunUpdate, 2);
        //速度计算 → 构建本帧运动快照 → PositionPrediction 上报预测框
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(SpeedCalculation, 3);
        // 相位 4：PhysicsSolverMgr.ContactForSolution（管理器注册）
        // 相位 5：只提交相位 4 合成的 requestedWorldDelta。
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(DisplacementCorrection, 5);
    }

    /// <summary>
    /// 几何查询，最早阶段，检测碰撞信息
    /// </summary>
    protected abstract void GeometricQuery();

    /// <summary>
    /// 相位 1：先做脚下/贴墙查询，再记下本帧区域 Trigger 重叠。
    /// 职能：几何感知入口；登记效果仍在相位 2。
    /// </summary>
    void GeometricQueryPhase()
    {
        ObservePreviousDisplacement();
        GeometricQuery();
        CollectRegionOverlapFacts();
    }

    /// <summary>自动模拟已越过上次相位 5；先采样刚体位置，再更新本步几何事实。</summary>
    private void ObservePreviousDisplacement()
    {
        displacementStep++;
        if (!hasPendingDisplacement || rb == null) return;

        lastDisplacementObservation = new EntityDisplacementObservation(
            pendingDisplacementStep, displacementStep, pendingDisplacementOrigin,
            pendingRequestedDelta, PhysicsWorld.ReadBodyPosition(rb));
        hasDisplacementObservation = true;
        hasPendingDisplacement = false;
    }

    /// <summary>
    /// 用自身碰撞体扫到的区域 Trigger 写入几何事实列表。例如碰到水的触发器只记碰撞体。
    /// 职能：相位 1 感知；不在这里登记或撤销环境效果。
    /// </summary>
    protected void CollectRegionOverlapFacts()
    {
        nowGemetry.regionOverlaps.Clear();
        // 安全：没有可用碰撞体时本帧没有区域重叠事实。
        if (boxCollider == null || !boxCollider.enabled) return;

        ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(PhysicsWorld.GetLayerCollisionMask(boxCollider.gameObject.layer));
        regionOverlapHits.Clear();
        PhysicsWorld.OverlapCollider(boxCollider, filter, regionOverlapHits);
        foreach (Collider2D other in regionOverlapHits)
        {
            // 安全：销毁、禁用或不在激活层级的碰撞体不是有效命中。
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy) continue;
            // 区域选择：只保留对方 Trigger，避免把实心地面/墙写进区域事实。
            if (!other.isTrigger) continue;
            // 区域规则：排除自身与显式忽略对；层矩阵已在 filter 中筛选。
            if (other.gameObject == gameObject || PhysicsWorld.GetIgnoreCollision(boxCollider, other)) continue;
            // 与 Unity 触发条件一致：至少一方具有 Rigidbody2D。
            if (boxCollider.attachedRigidbody == null && other.attachedRigidbody == null) continue;
            nowGemetry.regionOverlaps.Add(other);
        }
    }
    /// <summary>
    /// 物理职能更新（自身
    /// </summary>
    protected virtual void PhyFunUpdate()
    {
        // 相位 1 已拿到脚下 Collider 和区域 Trigger 重叠；从这里进入 RefreshEnvironment。
        // 着地才传脚下；离地传 null，使旧 Ground 依据被撤销。普通无脚本地面也可着地。
        // 相位 1 已按空中/着地重置自身阻力。本相位只乘一次表面倍率，不跨帧累乘。
        Bounds? waterBody = GetWaterSampleBounds();
        EnvironmentContext.RefreshEnvironment(
            nowGemetry.isGrounded ? nowGemetry.groundCollider : null,
            nowGemetry.regionOverlaps, waterBody.HasValue
                ? new Vector2(waterBody.Value.center.x, waterBody.Value.max.y) : (Vector2?)null, waterBody);
        self_resistanceCoefficient *= EnvironmentFrame.slowingMultiplier;
    }

    private Bounds? GetWaterSampleBounds()
    {
        // 安全：没有有效身体几何时不发布淹没。
        if (boxCollider == null || !boxCollider.enabled) return null;
        // 上缘中心仍为淹没口径；整框另用于独立的半露出水面事实。
        return PhysicsWorld.ReadAabb(boxCollider);
    }

    /// <summary>
    /// 相位 3 更新主动与被动速度，再构造一份基础运动快照并上报预测框。
    /// 上一帧登记的接触力在这里积分；本帧相位 4 再决定下一帧仍有效的接触力。
    /// </summary>
    void SpeedCalculation()
    {
        // 实际逻辑：待机角色不积分、不提交预测；区域采样仍沿原相位更新。
        if (!MotionUpdatesEnabled) return;
        //水平速度计算
        HorizontalSpeedCalculation();
        //竖直速度计算
        VerticalSpeedCalculation();
        // 上一帧接触动态力已由上面的速度计算入账。在这里读取相位 2 环境帧的平台位移并构建一次运动快照，
        // 相位 4 以快照为基础求接触偏置和环境可行请求，相位 5 不再重算基础位移。
        Vector2 platformDelta = EnvironmentFrame.platformDelta;
        motionFrame = BuildMotionFrame(
#if UNITY_EDITOR
            this,
#endif
            GetPlanarVelocity(),
            playerPhysicsData.verticalSpeed,
            nowGemetry.isGrounded,
            nowGemetry.groundNormal,
            platformDelta,
            Time.fixedDeltaTime, UsesWorldAxisMovement);
        PositionPrediction();
    }

    /// <summary>
    /// 将相位 3 的速度与帧初支撑转换成基础运动：空中按世界速度积分，着地按地面切线处理自身运动。
    /// 平台位移独立相加；自身接触读速与自身预测位移使用同一公式和步长。
    /// 仍保留旧着地规则：竖直分量只使用 verticalSpeed，phyVSpeed 的支撑语义留待后续建模。
    /// 此处没有接触或障碍约束；编辑器报告只复制结果，不参与计算。
    /// </summary>
    private static EntityMotionFrame BuildMotionFrame(
#if UNITY_EDITOR
        BasicEntity entity,
#endif
        Vector2 freeVelocity,
        float verticalSpeed,
        bool isGrounded,
        Vector2 groundNormal,
        Vector2 platformDelta,
        float fixedDeltaTime, bool worldAxisMovement)
    {
        Vector2 integratedVelocityDelta = freeVelocity * fixedDeltaTime;
        Vector2 motionVelocity = freeVelocity;
        Vector2 moveDelta = integratedVelocityDelta;
        float groundSlopeDeltaY = 0f;
        if (isGrounded && !worldAxisMovement)
        {
            // 普通着地仍走原切线规则；游泳走世界双轴，避免池底吞掉竖直水流。
            // verticalSpeed 本身含重力/跳跃；平台 Y 独立相加，不能用于替代被省略的 phyVSpeed。
            moveDelta = new Vector2(0, verticalSpeed * fixedDeltaTime);
            float moveAmount = freeVelocity.x * fixedDeltaTime;
            Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x);
            if (Mathf.Sign(tangent.x) != Mathf.Sign(freeVelocity.x))
                tangent *= -1;

            // 沿用原规则：总水平速度的大小作为沿地面切线的运动速度。
            Vector2 tangentDirection = tangent.normalized;
            motionVelocity = new Vector2(0f, verticalSpeed) + tangentDirection * Mathf.Abs(freeVelocity.x);
            moveDelta += tangentDirection * Mathf.Abs(moveAmount);
            groundSlopeDeltaY = tangentDirection.y * Mathf.Abs(moveAmount);
        }

        Vector2 appliedPlatformDelta = isGrounded ? platformDelta : Vector2.zero;
        EntityMotionFrame frame = new EntityMotionFrame(freeVelocity, motionVelocity, moveDelta, appliedPlatformDelta,
            groundSlopeDeltaY);
#if UNITY_EDITOR
        PhysicsEditorObservationBridge.ReportMotionBuilt(
            entity,
            frame.freeVelocity,
            integratedVelocityDelta,
            frame.motionDelta,
            frame.platformDelta,
            frame.plannedWorldDelta);
#endif
        return frame;
    }

    /// <summary>
    /// 位置预测：按本帧运动快照投射 AABB 到管理器，不再单独使用速度乘 dt。
    /// 预测形状包含地面运动和平台补偿，但尚不包含相位 4 偏置、环境约束或 Unity 修正。
    /// </summary>
    void PositionPrediction()
    {
        // 相位 4 会重新累加本帧偏置，先清零避免无接触时沿用旧结果。
        // 接触速度不再写入解算结果，而由实体数值状态在下一帧相位 3 跨帧累计。
        entitySolutionResult.displacementOffset = Vector2.zero;
        entitySolutionResult.environmentOffset = Vector2.zero;
        entitySolutionResult.requestedWorldDelta = motionFrame.plannedWorldDelta;
        entitySolutionResult.blockedHorizontalDirection = 0f;

        if (boxCollider == null || rb == null)
            return;

        PhysicalBoundingBox box = new PhysicalBoundingBox();
        Bounds bounds = PhysicsWorld.ReadAabb(boxCollider);
        box.point = (Vector2)bounds.center + motionFrame.plannedWorldDelta;
        box.size = bounds.extents; // 半长宽，与解算器 v3=a.size+b.size 一致
        box.motionFrame = motionFrame;
        box.myPhyBox = this;
#if UNITY_EDITOR
        PhysicsEditorObservationBridge.ReportPredictionBuilt(this, box.point, box.size);
#endif
        PhysicsSolverMgr.Instance.AddPhyBoX(box);
    }

    /// <summary>
    /// 水平速度计算
    /// </summary>
    void HorizontalSpeedCalculation()
    {
        // [ENV-06] 第一遍只看这三次调用：移速修饰、被动速度、主动速度。登记层到这里才变成速度。
        //计算环境约束
        effectState.UpdateMovementModifier(playerPhysicsData);
        //计算被动速度
        UpdatePassiveEffectSpeed();
        //计算主动操作的速度影响（计算主动速度,以及被动速度的特殊影响
        HActiveSpeedOperation();
    }

    /// <summary>
    /// ENV-06 的被动速度入口；限时、动态、持续速度的具体更新归 EntityEffectState。
    /// </summary>
    void UpdatePassiveEffectSpeed()
    {
        effectState.UpdatePassiveSpeed(playerPhysicsData, envImpact, self_resistanceCoefficient,
            entitySolutionResult.blockedHorizontalDirection);
    }
    /// <summary>
    /// 子类实现水平速度主动操作(后处理
    /// </summary>
    protected virtual void HActiveSpeedOperation()
    {

    }

    /// <summary>
    /// 竖直速度计算
    /// </summary>
    void VerticalSpeedCalculation()
    {
        //先计算被动速度
        VUnderForce();
        //再计算主动操作影响：表示主动操作可覆盖被动速度（例如跳跃
        VActiveSpeedOperation();
    }
    /// <summary>
    /// 计算垂直被动速度
    /// </summary>
    void VUnderForce()
    {
        // 重力
        if (!nowGemetry.isGrounded)
        {
            playerPhysicsData.verticalSpeed += gravity * Time.fixedDeltaTime;     //纵向速度改变（增减处理
        }
        else if (playerPhysicsData.verticalSpeed < 0)
        {
            playerPhysicsData.verticalSpeed = 0;
        }
    }
    /// <summary>
    /// 子类实现竖直速度主动操作（子类可选操作使用虚函数
    /// </summary>
    protected virtual void VActiveSpeedOperation()
    {

    }

    /// <summary>
    /// 位移应用
    /// 只提交相位 4 求出的请求位移；基础位移已经包含平台携带。
    /// </summary>
    void DisplacementCorrection()
    {
        // 实际逻辑：暂停运动不提交上一次残留请求。
        if (!MotionUpdatesEnabled) return;
        Vector2 requestOrigin = PhysicsWorld.ReadBodyPosition(rb);
        Vector2 requestedDelta = entitySolutionResult.requestedWorldDelta;
#if UNITY_EDITOR
        Vector2 debugUnconstrainedDelta = motionFrame.plannedWorldDelta + entitySolutionResult.displacementOffset;
        PhysicsEditorObservationBridge.ReportMovementRequested(
            this,
            entitySolutionResult.displacementOffset,
            debugUnconstrainedDelta,
            requestedDelta,
            requestOrigin + requestedDelta,
            entitySolutionResult.blockedHorizontalDirection != 0f);
#endif
        // F_6.2_6-5：相位 5 只提交一次，不再按帧初贴墙标志裁剪。
        PhysicsWorld.SubmitPosition(rb, requestOrigin + requestedDelta);
        pendingDisplacementStep = displacementStep;
        pendingDisplacementOrigin = requestOrigin;
        pendingRequestedDelta = requestedDelta;
        hasPendingDisplacement = true;
    }

    protected virtual void OnDisable()
    {
        hasPendingDisplacement = false;
        hasDisplacementObservation = false;
#if UNITY_EDITOR
        if (Application.isPlaying)
            PhysicsEditorObservationBridge.NotifyEntityDisabled(this);
#endif
        //事件注销
        if (!MonoPublicMgr.IsQuitting)
        {
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(GeometricQueryPhase, 1);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(PhyFunUpdate, 2);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(SpeedCalculation, 3);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(DisplacementCorrection, 5);
        }

        environmentContext?.ReleaseAll();
    }

    public virtual void ForceCalculation(IForceAction IF)
    {
        // 实体接触力由 PhysicsSolverMgr 在相位 4 统一刷新。
        // 下一帧相位 3 的数值状态更新不重复推导接触力，直接使用相位 4 留下的参数。
    }
}
