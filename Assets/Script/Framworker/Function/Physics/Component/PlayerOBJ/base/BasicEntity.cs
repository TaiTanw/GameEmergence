using PhyData;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色/箱子的共用执行：1 感知、2 环境、3 运动命令、4 公共原生模拟、5 实际结果。
/// 相位 3 写下本步计划并收静墙。模拟前由侧面推动把箱子的水平速度写上，再只模拟一次。
/// </summary>
public abstract class BasicEntity : MonoBehaviour, IForceAction, IEnvironmentReceiver
{
    protected virtual IPhysicsWorldAccess2D PhysicsWorld => UnityPhysicsWorldAccess2D.Instance;
    protected virtual bool MotionExecutionReady => true;
    protected virtual bool MotionUpdatesEnabled => true;
    protected virtual bool UsesWorldAxisMovement => false;
    protected virtual bool HasHorizontalDrive => false;
    protected virtual bool UsesCollisionFreeMovement => false;
    protected virtual Vector2 CollisionFreeVelocity => Vector2.zero;
    protected virtual bool ParticipatesInWorldPause => true;
    private static readonly HashSet<BasicEntity> activeEntities = new HashSet<BasicEntity>();
    private static bool worldPaused;
    private bool motionParticipation = true;
    public bool IsMotionReady => rb != null && boxCollider != null && MotionExecutionReady;
    public bool IsMotionPermitted => motionParticipation && MotionUpdatesEnabled
        && !(worldPaused && ParticipatesInWorldPause);
    public bool IsMotionParticipating => isActiveAndEnabled && IsMotionReady && IsMotionPermitted && boxCollider.enabled;
    public Bounds BodyBounds => boxCollider != null ? PhysicsWorld.ReadAabb(boxCollider) : default;
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

    /// <summary>
    /// 当前几何检测数据
    /// </summary>
    protected GeometryPhysicsData nowGemetry;
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


    [SerializeField, Min(0f), Tooltip("主动水平驱动每秒可改变的速度，开发期可调")]
    private float horizontalAcceleration = 300f;
    // 命令与实际接受的持续分量只用于避免流速/平台重复叠加，不独立积分动量。
    private Vector2 submittedVelocity;
    private Vector2 plannedVelocity;
    private Vector2 prescribedVelocity;
    private Vector2 acceptedPrescribedVelocity;
    private float activeHorizontalVelocity;
    private Vector2 requestOrigin;
    private float commandedVerticalSpeed;
    private bool hasMotionCommand;
    private int displacementStep;
    private EntityDisplacementObservation lastDisplacementObservation;
    private bool hasDisplacementObservation;
    private readonly List<Collider2D> regionOverlapHits = new List<Collider2D>();
    private readonly List<RaycastHit2D> groundProbeHits = new List<RaycastHit2D>();
    private readonly List<RaycastHit2D> terrainHits = new List<RaycastHit2D>();
    private readonly List<ContactPoint2D> nativeContacts = new List<ContactPoint2D>();
    private readonly List<Collider2D> walkingRestoreHits = new List<Collider2D>();
    protected const float GroundProbeDistance = 0.02f;
    internal IReadOnlyList<Collider2D> oneWayDropColliders;

    public void SetMotionParticipation(bool active)
    {
        motionParticipation = active;
        SynchronizeMotionParticipation();
    }

    /// <summary>规则侧暂停箱子、小车等世界实体；角色自身许可另管，暂停不清位置和速度。</summary>
    public static void SetWorldPaused(bool paused)
    {
        // 实际逻辑：世界开关同值不重复切换原生参与，避免干扰插值和现场速度。
        if (worldPaused == paused) return;
        worldPaused = paused;
        foreach (BasicEntity entity in activeEntities)
            if (entity != null) entity.SynchronizeMotionParticipation();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWorldParticipation()
    {
        activeEntities.Clear();
        worldPaused = false;
    }

    protected void SynchronizeMotionParticipation()
    {
        // 安全：规则注入可能早于 Awake。
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider2D>();
        bool participates = IsMotionParticipating;
        // 只写参与变化，避免同值写 simulated 将插值显示位置回写刚体。
        if (rb != null && rb.simulated != participates) rb.simulated = participates;
        if (!participates)
        {
            hasMotionCommand = false;
            OnMotionSuspended();
        }
    }

    protected virtual void OnMotionSuspended() { }
    public Vector2 GetPlanarVelocity() => rb != null ? rb.velocity : Vector2.zero;
    public bool TryGetLastDisplacementObservation(out EntityDisplacementObservation observation)
    {
        observation = lastDisplacementObservation;
        return hasDisplacementObservation;
    }

    protected virtual void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        // 重力由动作执行统一提交，原生负责积分命令与碰撞，避免双重重力。
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Dynamic;
        // 墙跳和高速下落可能一步跨过薄面，扫掠由原生连续碰撞承担。
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.freezeRotation = true;
        rb.angularVelocity = 0f;
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        gravity *= airResistance;
        Init();
        SynchronizeMotionParticipation();
    }

    protected void BindEffectReceiver() => effectState.BindReceiver(this);
    protected virtual void Init() { }
    protected virtual void OnMotionRefresh(bool relocated) { }

    public void RefreshMotionState(Vector2? position = null, bool releaseEnvironmentEffects = false)
    {
        OnMotionRefresh(position.HasValue);
        hasMotionCommand = hasDisplacementObservation = false;
        submittedVelocity = prescribedVelocity = acceptedPrescribedVelocity = Vector2.zero;
        activeHorizontalVelocity = 0f;
        playerPhysicsData.horizontalSpeed = playerPhysicsData.verticalSpeed = 0f;
        playerPhysicsData.phyHSpeed = playerPhysicsData.phyVSpeed = 0f;
        // 迁移位置后，旧下降平台不能扩大新位置的脚下探针。
        if (position.HasValue)
        {
            nowGemetry.isGrounded = false;
            nowGemetry.groundCollider = null;
        }
        if (releaseEnvironmentEffects) environmentContext?.ReleaseAll();
        effectState.ResetTransientMotion();
        PhysicsWorld.RestoreBodyMotion(rb, position);
        ResampleEnvironment();
    }

    public void ResampleEnvironment()
    {
        // 实际逻辑：穿墙期间不注入水、地面和墙事实；恢复行走才按当前位置重新采样。
        if (UsesCollisionFreeMovement) { ClearEnvironmentFacts(); return; }
        GeometricQuery();
        SampleNativeContacts();
        CollectRegionOverlapFacts();
        PhyFunUpdate();
    }

    protected virtual void OnEnable()
    {
        activeEntities.Add(this);
        SynchronizeMotionParticipation();
        hasMotionCommand = hasDisplacementObservation = false;
#if UNITY_EDITOR
        PhysicsEditorObservationBridge.NotifyEntityEnabled(this);
#endif
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(GeometricQueryPhase, 1);
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(PhyFunUpdate, 2);
        EntitySidePush.EnsureRegistered();
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(SpeedCalculation, 3);
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(DisplacementCorrection, 5);
    }

    protected abstract void GeometricQuery();
    private void GeometricQueryPhase()
    {
        if (UsesCollisionFreeMovement) { ClearEnvironmentFacts(); return; }
        GeometricQuery();
        SampleNativeContacts();
        CollectRegionOverlapFacts();
    }

    /// <summary>读取上一步原生接触；探针补微小间隙，接触不决定墙滑等能力。</summary>
    private void SampleNativeContacts()
    {
        nativeContacts.Clear();
        // 刷新/迁移后尚未模拟，旧原生接触不能覆盖新位置的探针与区域事实。
        if (!IsMotionParticipating || !hasDisplacementObservation) return;
        rb.GetContacts(new ContactFilter2D { useTriggers = false }, nativeContacts);
        foreach (ContactPoint2D contact in nativeContacts)
        {
            Collider2D other = contact.collider != null && contact.collider.attachedRigidbody == rb
                ? contact.otherCollider : contact.collider;
            // 安全：忽略无效、自身和本步刚放开的单向平台。
            if (other == null || other.attachedRigidbody == rb || IsDroppingThrough(other)) continue;
            Vector2 normal = contact.normal;
            if (normal.y > 0.7f)
            {
                nowGemetry.isGrounded = true;
                nowGemetry.groundCollider = other;
                nowGemetry.groundNormal = normal;
            }
            else if (normal.y < -0.7f) nowGemetry.istop = true;
            if (normal.x > 0.9f) { nowGemetry.onLeftWall = true; nowGemetry.leftWallCollider = other; }
            else if (normal.x < -0.9f) { nowGemetry.onRightWall = true; nowGemetry.rightWallCollider = other; }
        }
    }

    // 显示插值不移动物理探针。未运行时刚体尚未绑定。
    protected Vector2 ProbePosition(Transform probe)
    {
        // 安全：探针或刚体还没有时，不读取空刚体位置。
        if (probe == null) return rb != null ? rb.position : Vector2.zero;
        if (rb == null) return probe.position;
        // 实际逻辑：用刚体位置加探针相对偏移，画面插值不会带动探针。
        return rb.position + (Vector2)(probe.position - transform.position);
    }
    protected RaycastHit2D FindGroundHit(Vector2 center, Vector2 size, float angle,
        Vector2 direction, ContactFilter2D filter)
    {
        groundProbeHits.Clear();
        if (boxCollider != null && boxCollider.enabled) size.x = Mathf.Max(size.x, BodyBounds.size.x);
        // 下落平台已在相位 0 移动；只为上步脚下的平台补其本步下降距离，保持连续携带。
        IPlatformMotion platform = EnvironmentCapabilities.Find<IPlatformMotion>(nowGemetry?.groundCollider);
        float distance = GroundProbeDistance + (EnvironmentCapabilities.IsActive(platform)
            ? Mathf.Max(0f, -platform.Delta.y) : 0f);
        PhysicsWorld.BoxCast(center, size, angle, direction, distance, filter, groundProbeHits);
        RaycastHit2D support = default;
        float bestUp = 0.7f;
        foreach (RaycastHit2D hit in groundProbeHits)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody == rb || IsDroppingThrough(hit.collider)) continue;
            float up = Vector2.Dot(hit.normal, Vector2.up);
            if (up <= bestUp) continue;
            support = hit;
            bestUp = up;
        }
        return support;
    }

    private bool IsDroppingThrough(Collider2D collider)
    {
        if (oneWayDropColliders == null) return false;
        for (int i = 0; i < oneWayDropColliders.Count; i++)
            if (oneWayDropColliders[i] == collider) return true;
        return false;
    }

    internal static bool IsSolidOneWayTop(Collider2D collider, Vector2 normal)
    {
        if (collider == null || !collider.usedByEffector) return false;
        if (!collider.TryGetComponent(out PlatformEffector2D effector) || !effector.isActiveAndEnabled || !effector.useOneWay) return false;
        return Vector2.Angle(effector.transform.up, normal) <= effector.surfaceArc * 0.5f;
    }

    protected void CollectRegionOverlapFacts()
    {
        nowGemetry.regionOverlaps.Clear();
        if (boxCollider == null || !boxCollider.enabled) return;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(PhysicsWorld.GetLayerCollisionMask(gameObject.layer));
        regionOverlapHits.Clear();
        PhysicsWorld.OverlapCollider(boxCollider, filter, regionOverlapHits);
        foreach (Collider2D other in regionOverlapHits)
        {
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy) continue;
            if (!other.isTrigger || other.attachedRigidbody == rb || PhysicsWorld.GetIgnoreCollision(boxCollider, other)) continue;
            nowGemetry.regionOverlaps.Add(other);
        }
    }

    protected virtual void PhyFunUpdate()
    {
        if (UsesCollisionFreeMovement) return;
        Bounds bounds = BodyBounds;
        EnvironmentContext.RefreshEnvironment(nowGemetry.isGrounded ? nowGemetry.groundCollider : null,
            nowGemetry.regionOverlaps, new Vector2(bounds.center.x, bounds.max.y), bounds);
        self_resistanceCoefficient *= EnvironmentFrame.slowingMultiplier;
    }

    protected void ClearEnvironmentFacts()
    {
        // 安全：初始化尚未建立几何数据时，只撤销已有关系。
        environmentContext?.ReleaseAll();
        if (nowGemetry == null) return;
        nowGemetry.isGrounded = nowGemetry.istop = nowGemetry.onLeftWall = nowGemetry.onRightWall = false;
        nowGemetry.groundCollider = nowGemetry.leftWallCollider = nowGemetry.rightWallCollider = null;
        nowGemetry.groundNormal = Vector2.zero;
        nowGemetry.regionOverlaps.Clear();
    }

    protected void ClearExternalMotionEffects()
    {
        ClearEnvironmentFacts();
        effectState.ClearMotionParameters();
        playerPhysicsData.nowPhyNum = 0f;
    }

    /// <summary>
    /// 人工阅读：飞转行只查本次当前位置的恢复形状。地形和暂停实体共用浅重叠容差，失败不移动角色。
    /// </summary>
    protected bool TryFindWalkingRestoreBlocker(float allowedPenetration, out Collider2D blocker)
    {
        blocker = null;
        // 安全：没有角色碰撞框时不能承诺恢复实心成功。
        if (boxCollider == null || rb == null || !boxCollider.enabled) return true;
        float tolerance = Mathf.Max(0.0001f, allowedPenetration);
        GetBoxShape(this, out Vector2 center, out Vector2 size, out float angle);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        int layerMask = PhysicsWorld.GetLayerCollisionMask(gameObject.layer);
        filter.SetLayerMask(layerMask);
        walkingRestoreHits.Clear();
        PhysicsWorld.OverlapBox(center, size, angle, filter, walkingRestoreHits);
        foreach (Collider2D hit in walkingRestoreHits)
        {
            // 安全：排除自身、触发器与本来就不碰撞的对象。
            if (hit == null || hit.attachedRigidbody == rb || hit.isTrigger
                || PhysicsWorld.GetIgnoreCollision(boxCollider, hit)) continue;
            // 实际逻辑：接触级浅嵌入允许恢复；继续检查其它命中，任何深嵌入仍拒绝。
            if (hit is BoxCollider2D otherBox && boxCollider.edgeRadius == 0f && otherBox.edgeRadius == 0f
                && (otherBox.attachedRigidbody == null || otherBox.attachedRigidbody.transform == otherBox.transform))
            {
                // 矩形统一按真实几何深度判定，避免原生距离的接触边缘与暂停盒判定不一致。
                GetBoxShape(otherBox, out Vector2 otherCenter, out Vector2 otherSize, out float otherAngle);
                if (!BoxesOverlap(center, size, angle, otherCenter, otherSize, otherAngle, tolerance)) continue;
            }
            else
            {
                // 圆角、刚体子物体上的碰撞框和其它形状保留原生距离，不用外包矩形放行。
                ColliderDistance2D distance = boxCollider.Distance(hit);
                if (!distance.isOverlapped || distance.distance >= -tolerance) continue;
            }
            blocker = hit;
            return true;
        }
        // 实际逻辑：simulated=false 的箱子和待机本体对原生查询不可见，只在本次退出补查盒形状。
        foreach (BasicEntity entity in activeEntities)
        {
            if (entity == null || entity == this || entity.rb == null || entity.rb.simulated
                || entity.boxCollider == null || !entity.boxCollider.enabled || entity.boxCollider.isTrigger
                || (layerMask & (1 << entity.gameObject.layer)) == 0
                || PhysicsWorld.GetIgnoreCollision(boxCollider, entity.boxCollider)) continue;
            GetBoxShape(entity, out Vector2 otherCenter, out Vector2 otherSize, out float otherAngle);
            if (!BoxesOverlap(center, size, angle, otherCenter, otherSize, otherAngle, tolerance)) continue;
            blocker = entity.boxCollider;
            return true;
        }
        return false;
    }

    private static void GetBoxShape(BasicEntity entity, out Vector2 center, out Vector2 size, out float angle)
        => GetBoxShape(entity.boxCollider, out center, out size, out angle);

    private static void GetBoxShape(BoxCollider2D box, out Vector2 center, out Vector2 size, out float angle)
    {
        Vector2 scale = box.transform.lossyScale;
        size = Vector2.Scale(box.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        Rigidbody2D body = box.attachedRigidbody;
        angle = body != null ? body.rotation : box.transform.eulerAngles.z;
        float radians = angle * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 up = new Vector2(-right.y, right.x);
        Vector2 offset = Vector2.Scale(box.offset, scale);
        Vector2 origin = body != null ? body.position : (Vector2)box.transform.position;
        center = origin + right * offset.x + up * offset.y;
    }

    private static bool BoxesOverlap(Vector2 center, Vector2 size, float angle,
        Vector2 otherCenter, Vector2 otherSize, float otherAngle, float tolerance)
    {
        float a = angle * Mathf.Deg2Rad, b = otherAngle * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), up = new Vector2(-right.y, right.x);
        Vector2 otherRight = new Vector2(Mathf.Cos(b), Mathf.Sin(b)), otherUp = new Vector2(-otherRight.y, otherRight.x);
        Vector2 offset = otherCenter - center;
        Vector2 half = size * 0.5f, otherHalf = otherSize * 0.5f;
        // 四个盒轴分别判深重叠，允许沿任一分离轴在容差内恢复，不用旋转盒的外包矩形代替。
        return OverlapsOnAxis(right, offset, half, right, up, otherHalf, otherRight, otherUp, tolerance)
            && OverlapsOnAxis(up, offset, half, right, up, otherHalf, otherRight, otherUp, tolerance)
            && OverlapsOnAxis(otherRight, offset, half, right, up, otherHalf, otherRight, otherUp, tolerance)
            && OverlapsOnAxis(otherUp, offset, half, right, up, otherHalf, otherRight, otherUp, tolerance);
    }

    private static bool OverlapsOnAxis(Vector2 axis, Vector2 offset, Vector2 half, Vector2 right, Vector2 up,
        Vector2 otherHalf, Vector2 otherRight, Vector2 otherUp, float tolerance)
    {
        float span = half.x * Mathf.Abs(Vector2.Dot(right, axis)) + half.y * Mathf.Abs(Vector2.Dot(up, axis))
            + otherHalf.x * Mathf.Abs(Vector2.Dot(otherRight, axis)) + otherHalf.y * Mathf.Abs(Vector2.Dot(otherUp, axis));
        return Mathf.Abs(Vector2.Dot(offset, axis)) < span - tolerance;
    }

    /// <summary>自由箱保留真实推动惯性。静墙、坡面和头顶先收进本步速度，实体之间仍交给原生模拟。</summary>
    private void SpeedCalculation()
    {
        SynchronizeMotionParticipation();
        if (!IsMotionParticipating) return;
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f) return;
        // 实际逻辑：飞行仍走本相位写速度及公共模拟，仅消费主动四向命令，零输入即悬停。
        if (UsesCollisionFreeMovement)
        {
            submittedVelocity = plannedVelocity = CollisionFreeVelocity;
            prescribedVelocity = acceptedPrescribedVelocity = Vector2.zero;
            activeHorizontalVelocity = submittedVelocity.x;
            commandedVerticalSpeed = submittedVelocity.y;
            playerPhysicsData.horizontalSpeed = submittedVelocity.x;
            playerPhysicsData.verticalSpeed = submittedVelocity.y;
            playerPhysicsData.phyHSpeed = playerPhysicsData.phyVSpeed = 0f;
            requestOrigin = rb.position;
            rb.velocity = submittedVelocity;
            hasMotionCommand = true;
            return;
        }
        Vector2 actual = rb.velocity;
        Vector2 free = actual - acceptedPrescribedVelocity;
        if (HasHorizontalDrive) free.x -= activeHorizontalVelocity;
        playerPhysicsData.horizontalSpeed = free.x;
        playerPhysicsData.verticalSpeed = free.y;
        // 实际逻辑：着地时丢掉碰撞挤出的向上速度。起跳在本步稍后写入，不在这里重复续上。
        if (nowGemetry.isGrounded && !UsesWorldAxisMovement && commandedVerticalSpeed <= 0f
            && playerPhysicsData.verticalSpeed > 0f)
            playerPhysicsData.verticalSpeed = 0f;
        effectState.UpdateMovementModifier(playerPhysicsData);
        effectState.UpdatePassiveSpeed(playerPhysicsData);
        float dynamicDelta = effectState.CalculateDynamicVelocityDelta(free.x, envImpact, dt);
        // 没有环境控制时，真实被动惯性按表面阻力制动，主动命令另行处理。
        if (!effectState.HasDynamicForces)
            playerPhysicsData.horizontalSpeed = Mathf.MoveTowards(free.x, 0f,
                self_resistanceCoefficient * Mathf.Max(0f, envImpact) * dt);
        float passiveHorizontal = playerPhysicsData.horizontalSpeed + dynamicDelta;
        HActiveSpeedOperation();
        float slopeVertical = 0f;
        if (HasHorizontalDrive)
        {
            // 主动命令单独按原生接受量回读，停止输入不会清掉水流、冰滑或接触动量。
            float target = playerPhysicsData.horizontalSpeed;
            Vector2 tangent = nowGemetry.isGrounded && !UsesWorldAxisMovement
                ? new Vector2(nowGemetry.groundNormal.y, -nowGemetry.groundNormal.x) : Vector2.right;
            target *= tangent.x;
            float acceleration = horizontalAcceleration * Mathf.Max(0f, EnvironmentFrame.slowingMultiplier);
            activeHorizontalVelocity = Mathf.MoveTowards(activeHorizontalVelocity, target, acceleration * dt);
            playerPhysicsData.horizontalSpeed = passiveHorizontal + activeHorizontalVelocity;
            // 实际逻辑：下坡贴合按本步全部水平速度，含惯性、限时速度和状态速度；不能只算主动走路。
            // 上坡抬升仍由地形查询按实际间隙计算，避免多出来的向上速度被回读成跳跃。
            float slopeCarryX = passiveHorizontal + activeHorizontalVelocity + playerPhysicsData.phyHSpeed;
            if (tangent.x > 0.001f)
            {
                float along = slopeCarryX * tangent.y / tangent.x;
                if (along < 0f) slopeVertical = along;
            }
        }
        else playerPhysicsData.horizontalSpeed = passiveHorizontal;
        VUnderForce();
        VActiveSpeedOperation();
        Vector2 own = new Vector2(playerPhysicsData.horizontalSpeed, playerPhysicsData.verticalSpeed);
        prescribedVelocity = new Vector2(playerPhysicsData.phyHSpeed, playerPhysicsData.phyVSpeed)
            + (nowGemetry.isGrounded ? EnvironmentFrame.platformDelta / dt : Vector2.zero)
            + new Vector2(0f, slopeVertical);
        submittedVelocity = own + prescribedVelocity;
        commandedVerticalSpeed = playerPhysicsData.verticalSpeed;
        ClipStaticTerrain(dt, slopeVertical);
        // 实际逻辑：头顶或坡面把本步跳速收短后，下一步只续上收短后的速度。
        float writtenOwnY = submittedVelocity.y - prescribedVelocity.y;
        if (writtenOwnY < commandedVerticalSpeed)
            commandedVerticalSpeed = writtenOwnY;
        plannedVelocity = submittedVelocity;
        requestOrigin = rb.position;
        rb.velocity = submittedVelocity;
        hasMotionCommand = true;
        EntitySidePush.Report(this);
    }

    /// <summary>写入计划前只收静墙、坡面和头顶。箱子侧面留给模拟前的一次推动。</summary>
    void ClipStaticTerrain(float dt, float slopeVertical)
    {
        // 安全：没有地形配置或碰撞框时不能查询。
        if (cPhysics == null || boxCollider == null || dt <= 0f) return;
        Vector2 delta = submittedVelocity * dt;
        if (delta.sqrMagnitude <= 1e-8f) return;
        Bounds bounds = PhysicsWorld.ReadAabb(boxCollider);
        float minUp = Mathf.Cos(Mathf.Clamp(cPhysics.maxClimbAngle, 0f, 89f) * Mathf.Deg2Rad);
        float originalX = delta.x;
        delta.x = ClipAgainstWalls(bounds, delta, minUp);
        // 实际逻辑：墙截短水平后，主动驱动和被动水平按同一比例留下，避免下一步把差额当成反向速度。
        if (Mathf.Abs(originalX) > 0.0001f)
        {
            float retained = Mathf.Clamp01(Mathf.Abs(delta.x / originalX));
            if (retained < 1f)
            {
                if (slopeVertical < 0f)
                {
                    float removed = slopeVertical * (1f - retained);
                    delta.y -= removed * dt;
                    prescribedVelocity.y -= removed;
                }
                prescribedVelocity.x *= retained;
                if (HasHorizontalDrive) activeHorizontalVelocity *= retained;
            }
        }
        // 实际逻辑：起跳这一步不再叠加坡面抬升，避免和跳跃速度加在一起窜高。
        float rise = commandedVerticalSpeed > 0f ? 0f : MeasureSlopeRise(bounds, delta, minUp);
        // 实际逻辑：上坡只补查询到的间隙，并记入本步命令，避免下一步当成自由上升。
        if (rise > 0f)
        {
            delta.y += rise;
            prescribedVelocity.y += rise / dt;
        }
        float clippedY = ClipAgainstCeiling(bounds, delta);
        // 实际逻辑：头顶只收竖直分量。被收掉的抬升从本步命令里扣除，跳跃不写成负的被动速度。
        if (clippedY < delta.y)
        {
            float cut = (delta.y - clippedY) / dt;
            prescribedVelocity.y -= Mathf.Min(cut, Mathf.Max(0f, prescribedVelocity.y));
            delta.y = clippedY;
        }
        submittedVelocity = delta / dt;
    }

    float ClipAgainstWalls(Bounds bounds, Vector2 delta, float minUp)
    {
        if (Mathf.Abs(delta.x) <= 0.0001f) return delta.x;
        float path = delta.magnitude;
        CastTerrain((Vector2)bounds.center, bounds.size, delta / path, path, TerrainMask());
        float allowed = delta.x;
        foreach (RaycastHit2D hit in terrainHits)
        {
            if (!IsStaticTerrain(hit.collider) || PassesOneWay(hit, bounds.min.y, delta)) continue;
            // 可爬坡面和当前脚下不截水平；更陡的迎面表面收到接触前。
            if (hit.normal.y >= minUp || hit.collider == nowGemetry.groundCollider) continue;
            if (hit.normal.x * delta.x >= -0.0001f) continue;
            if (hit.distance <= 0f)
            {
                allowed = 0f;
                continue;
            }
            float span = Mathf.Max(0f, hit.distance - 0.001f) * Mathf.Abs(delta.x) / path;
            allowed = Mathf.Sign(delta.x) * Mathf.Min(Mathf.Abs(allowed), span);
        }
        return allowed;
    }

    internal bool HasPendingStep => hasMotionCommand;
    internal bool DrivesHorizontalStep => HasHorizontalDrive && !UsesWorldAxisMovement;
    internal Vector2 PlannedStepVelocity => plannedVelocity;
    internal bool HasBody(Rigidbody2D body) => body != null && rb == body;
    /// <summary>这一步的水平是否已交给关节。默认没有锁。</summary>
    internal virtual bool HoldsHorizontalLock(BasicEntity other) => false;

    internal bool TrySweepSideEntity(float vx, float dt, List<RaycastHit2D> hits, out BasicEntity other, out float gap)
    {
        other = null;
        gap = 0f;
        // 安全：没有水平计划或碰撞框时不查询侧面。
        if (boxCollider == null || dt <= 0f || Mathf.Abs(vx) <= 0.0001f) return false;
        float minUp = cPhysics == null ? 0.7f
            : Mathf.Cos(Mathf.Clamp(cPhysics.maxClimbAngle, 0f, 89f) * Mathf.Deg2Rad);
        Bounds bounds = BodyBounds;
        Vector2 delta = new Vector2(vx * dt, 0f);
        hits.Clear();
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(PhysicsWorld.GetLayerCollisionMask(gameObject.layer));
        PhysicsWorld.BoxCast((Vector2)bounds.center, bounds.size, 0f, delta.normalized, delta.magnitude, filter, hits);
        float best = float.MaxValue;
        foreach (RaycastHit2D hit in hits)
        {
            // 安全：空命中和自身不是被推对象。
            if (hit.collider == null || hit.collider.attachedRigidbody == rb) continue;
            BasicEntity entity = EnvironmentCapabilities.FindEntity(hit.collider);
            // 实际逻辑：箱顶法线朝上，站在上面不算侧面推动。
            if (entity == null || entity == this || hit.normal.y >= minUp || hit.normal.x * vx >= -0.0001f) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            other = entity;
            float faceGap = vx > 0f
                ? entity.BodyBounds.min.x - bounds.max.x
                : bounds.min.x - entity.BodyBounds.max.x;
            gap = Mathf.Max(0f, faceGap);
        }
        return other != null;
    }

    internal float WallLimitedHorizontal(float vx)
    {
        float dt = Time.fixedDeltaTime;
        // 安全：没有步长或地形配置时不改这股速度。
        if (dt <= 0f || Mathf.Abs(vx) <= 0.0001f || cPhysics == null || boxCollider == null) return vx;
        float minUp = Mathf.Cos(Mathf.Clamp(cPhysics.maxClimbAngle, 0f, 89f) * Mathf.Deg2Rad);
        Vector2 delta = new Vector2(vx * dt, 0f);
        float allowed = ClipAgainstWalls(BodyBounds, delta, minUp);
        return allowed / dt;
    }

    internal void CommitHorizontal(float vx)
    {
        float previous = submittedVelocity.x;
        // 实际逻辑：只在变慢时按比例留下主动速度，加快箱子时不把它记成负的被动速度。
        if (Mathf.Abs(previous) > 0.0001f && Mathf.Abs(vx) < Mathf.Abs(previous) - 0.0001f)
        {
            float retained = Mathf.Clamp01(Mathf.Abs(vx / previous));
            prescribedVelocity.x *= retained;
            if (HasHorizontalDrive) activeHorizontalVelocity *= retained;
        }
        submittedVelocity.x = vx;
        Vector2 velocity = rb.velocity;
        velocity.x = vx;
        rb.velocity = velocity;
    }

    float MeasureSlopeRise(Bounds bounds, Vector2 delta, float minUp)
    {
        // 实际逻辑：未着地或四向运动不把坡面抬升写进命令。
        if (!nowGemetry.isGrounded || UsesWorldAxisMovement || Mathf.Abs(delta.x) <= 0.0001f) return 0f;
        Vector2 center = (Vector2)bounds.center + delta;
        float lift = bounds.size.y + Mathf.Abs(delta.y) + 0.01f;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(cPhysics.groundLayer);
        terrainHits.Clear();
        PhysicsWorld.BoxCast(center + Vector2.up * lift, bounds.size, 0f, Vector2.down,
            lift + 0.05f, filter, terrainHits);
        float rise = 0f;
        Collider2D support = null;
        foreach (RaycastHit2D hit in terrainHits)
        {
            if (!IsStaticTerrain(hit.collider) || PassesOneWay(hit, bounds.min.y, delta)) continue;
            if (hit.normal.y < minUp || hit.normal.x * delta.x >= 0f) continue;
            float needed = lift - hit.distance;
            float slopeCap = Mathf.Abs(delta.x * hit.normal.x / hit.normal.y);
            if (needed <= 0f || needed > slopeCap + 0.01f || needed <= rise) continue;
            support = hit.collider;
            rise = needed;
        }
        if (support == null || LiftIsBlocked(center, bounds.size, support, rise, bounds.min.y)) return 0f;
        return rise;
    }

    bool LiftIsBlocked(Vector2 center, Vector2 size, Collider2D support, float rise, float currentBottom)
    {
        CastTerrain(center, size, Vector2.up, rise, TerrainMask());
        foreach (RaycastHit2D hit in terrainHits)
        {
            if (hit.collider == support || !IsStaticTerrain(hit.collider)) continue;
            if (PassesOneWay(hit, currentBottom, new Vector2(0f, rise))) continue;
            return true;
        }
        return false;
    }

    float ClipAgainstCeiling(Bounds bounds, Vector2 delta)
    {
        if (delta.y <= 0.0001f) return delta.y;
        float path = delta.magnitude;
        CastTerrain((Vector2)bounds.center, bounds.size, delta / path, path, TerrainMask());
        float allowed = delta.y;
        foreach (RaycastHit2D hit in terrainHits)
        {
            if (!IsStaticTerrain(hit.collider) || PassesOneWay(hit, bounds.min.y, delta)) continue;
            // 实际逻辑：脚下表面和身体里的重叠不是头顶，不能把起跳收成零。
            if (hit.collider == nowGemetry.groundCollider || hit.normal.y > -0.7f) continue;
            if (hit.distance <= 0f && hit.point.y <= bounds.center.y) continue;
            if (hit.distance <= 0f)
            {
                allowed = 0f;
                continue;
            }
            float span = Mathf.Max(0f, hit.distance - 0.001f) * delta.y / path;
            allowed = Mathf.Min(allowed, span);
        }
        return allowed;
    }

    void CastTerrain(Vector2 origin, Vector2 size, Vector2 direction, float distance, int mask)
    {
        terrainHits.Clear();
        if (distance <= 0f) return;
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(mask);
        PhysicsWorld.BoxCast(origin, size, 0f, direction, distance, filter, terrainHits);
    }

    int TerrainMask() => (int)cPhysics.groundLayer | (int)cPhysics.wallLayer;

    bool IsStaticTerrain(Collider2D collider)
    {
        // 安全：空命中和自身不是地形。
        if (collider == null || collider.attachedRigidbody == rb) return false;
        // 实际逻辑：其它实体留给原生模拟，不在这里裁剪。
        return EnvironmentCapabilities.FindEntity(collider) == null;
    }

    bool PassesOneWay(RaycastHit2D hit, float currentBottom, Vector2 delta)
    {
        if (IsDroppingThrough(hit.collider)) return true;
        if (!hit.collider.TryGetComponent(out PlatformEffector2D effector)
            || !effector.isActiveAndEnabled || !effector.useOneWay) return false;
        // 实际逻辑：不在单向实心弧内则放行。
        if (Vector2.Angle(effector.transform.up, hit.normal) > effector.surfaceArc * 0.5f) return true;
        return nowGemetry.groundCollider != hit.collider
            && (delta.y > 0f || currentBottom < hit.point.y - 0.01f);
    }

    protected virtual void HActiveSpeedOperation() { }
    private void VUnderForce()
    {
        if (!nowGemetry.isGrounded) playerPhysicsData.verticalSpeed += gravity * Time.fixedDeltaTime;
        else if (playerPhysicsData.verticalSpeed < 0f) playerPhysicsData.verticalSpeed = 0f;
    }
    protected virtual void VActiveSpeedOperation() { }

    /// <summary>相位 5 只回读实际结果，不再提交移动或纠偏。</summary>
    private void DisplacementCorrection()
    {
        if (!hasMotionCommand || !IsMotionParticipating) return;
        Vector2 actual = rb.velocity;
        acceptedPrescribedVelocity = new Vector2(
            AcceptedComponent(actual.x, submittedVelocity.x, prescribedVelocity.x),
            AcceptedComponent(actual.y, submittedVelocity.y, prescribedVelocity.y));
        if (HasHorizontalDrive)
            activeHorizontalVelocity = AcceptedComponent(actual.x, submittedVelocity.x, activeHorizontalVelocity);
        playerPhysicsData.horizontalSpeed = HasHorizontalDrive ? activeHorizontalVelocity
            : actual.x - acceptedPrescribedVelocity.x;
        playerPhysicsData.verticalSpeed = actual.y - acceptedPrescribedVelocity.y;
        // 实际逻辑：没有起跳命令时，着地丢掉碰撞挤出的向上速度。起跳不在这里续成全速。
        if (nowGemetry.isGrounded && !UsesWorldAxisMovement && commandedVerticalSpeed <= 0f
            && playerPhysicsData.verticalSpeed > 0f)
            playerPhysicsData.verticalSpeed = 0f;
        playerPhysicsData.phyHSpeed = actual.x - playerPhysicsData.horizontalSpeed;
        playerPhysicsData.phyVSpeed = acceptedPrescribedVelocity.y;
        displacementStep++;
        lastDisplacementObservation = new EntityDisplacementObservation(displacementStep, displacementStep,
            requestOrigin, submittedVelocity * Time.fixedDeltaTime, rb.position);
        hasDisplacementObservation = true;
        hasMotionCommand = false;
    }

    // 持续速度按原生实际接受量分摊；堵停或反向不保存被碰撞消掉的隐藏分量。
    private static float AcceptedComponent(float actual, float submitted, float prescribed)
    {
        if (Mathf.Abs(actual - submitted) <= 0.001f) return prescribed;
        if (Mathf.Abs(submitted) > 0.001f && actual * submitted > 0f)
            return prescribed * Mathf.Clamp01(Mathf.Abs(actual / submitted));
        return 0f;
    }

    public void StatePowerRegistration(EnvironmentRegistration source, float offset)
    { if (!UsesCollisionFreeMovement) effectState.RegisterMovementModifier(source, offset); }
    public void StatePowerCancellation(EnvironmentRegistration source) => effectState.RemoveMovementModifier(source);
    public void AddTimeSpeed(float duration, float speed)
    { if (!UsesCollisionFreeMovement) effectState.AddTimedSpeed(duration, speed, envImpact); }
    public void AddSpeedStatus(EnvironmentRegistration source, Vector2 velocity)
    { if (!UsesCollisionFreeMovement) effectState.AddStateVelocity(source, velocity); }
    public void RemoveSpeedStatus(EnvironmentRegistration source) => effectState.RemoveStateVelocity(source);
    public void AddForce(IDynamicAddForce source, ForceData force)
    { if (!UsesCollisionFreeMovement) effectState.AddDynamicForce(source, force); }
    public void ChangeForce(IDynamicAddForce source, float force) => effectState.ChangeDynamicForce(source, force);
    public void ChangeType(IDynamicAddForce source, E_PhyForceType type) => effectState.ChangeDynamicType(source, type);
    public void RemoveForce(IDynamicAddForce source) => effectState.RemoveDynamicForce(source);

    protected virtual void OnDisable()
    {
        activeEntities.Remove(this);
        SynchronizeMotionParticipation();
        EntitySidePush.Forget(this);
        hasMotionCommand = hasDisplacementObservation = false;
#if UNITY_EDITOR
        PhysicsEditorObservationBridge.NotifyEntityDisabled(this);
#endif
        if (!MonoPublicMgr.IsQuitting)
        {
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(GeometricQueryPhase, 1);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(PhyFunUpdate, 2);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(SpeedCalculation, 3);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(DisplacementCorrection, 5);
        }
        environmentContext?.ReleaseAll();
    }

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
        data.groundCenter = data.hasGround ? (Vector3)ProbePosition(groundV) : default;
        data.topCenter = data.hasTop ? (Vector3)ProbePosition(upV) : default;
        data.leftCenter = data.hasLeft ? (Vector3)ProbePosition(leftV) : default;
        data.rightCenter = data.hasRight ? (Vector3)ProbePosition(rightV) : default;
        data.horizontalSize = cPhysics.boxCastH;
        data.verticalSize = cPhysics.boxCastV;
        return true;
    }
#endif

}
