using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using PhyData;

/// <summary>
/// 角色物理组件
/// </summary>
public class CharacterPhysics : BasePhysicsEntity, ICanMove, IPlayerFlightExecution
{
    #region 玩家物理配置数据
    /// <summary>
    /// 贴墙下滑最大速度
    /// </summary>
    private float wallDownSpeed = 2f;
    /// <summary>
    /// 贴墙反跳速度倍率
    /// </summary>
    private float wallJumpV = 2f;
    /// <summary>
    /// 跳跃斩断系数
    /// </summary>
    private float jumpRelNum = 0.6f;

    public float speed = 15;    // 基础水平移速
    [SerializeField, Min(0f)] private float fourWaySpeed = 6f; // 首版可调四向主动速度，后续定标。

    public float upSpeed = 15;//基础跳跃速度
    private PlayerEffectLedger effectLedger;
    public float EffectiveMovementSpeed => speed * (effectLedger?.MovementMultiplier ?? 1f);
    public float EffectiveJumpSpeed => upSpeed * (effectLedger?.JumpMultiplier ?? 1f);
    private float MovementMultiplier => effectLedger?.MovementMultiplier ?? 1f;
    /// <summary>只绑定共享记录；消费时从角色自身基础值重算，不回写配置或积累加成。</summary>
    internal void BindEffectLedger(PlayerEffectLedger ledger) => effectLedger = ledger;
    const float SpawnGroundSearchDistance = 4f;
    readonly List<RaycastHit2D> spawnDropHits = new List<RaycastHit2D>();
    #endregion


    #region 控制流数据=================================================================
    //角色状态机内部事件系统引用(一次性注册事件（因为生命周期和局部事件系统一致，所以暂时不用考虑注销物体（随物体删除一并销毁
    //LocalEventSystem<PlayerStateMachine.E_playEvent> fsmEventSystem;
    /// <summary>
    /// 角色可执行动作
    /// </summary>
    ReadOnly_ActionData playActionData;
    //事件开关
    bool wallJump;
    // 墙跳触发时的选侧：负数为左墙，正数为右墙。物理消费前不再跟随后续输入。
    float wallJumpSide;
    bool jump;
    internal bool HasPendingJump => jump;
    //跳跃斩断按钮
    bool jumpReleaseSide;
    public float MovingDirection => playActionData.onMove;

    public float Mobility => EffectiveMovementSpeed;
    // 死亡演出沿箱子的共用底层运动，只退出角色主动驱动和动作执行。
    internal bool IsPassiveMotion { get; private set; }
    internal void BeginPassiveMotion() => IsPassiveMotion = true;
    protected override bool HasHorizontalDrive => !IsPassiveMotion;
    protected override bool MotionExecutionReady => playActionData != null;
    protected override bool MotionUpdatesEnabled => playActionData == null || playActionData.motionUpdates;
    protected override bool UsesWorldAxisMovement => !IsPassiveMotion && playActionData != null && playActionData.FourWay != FourWayMode.None;
    private bool flying;
    protected override bool UsesCollisionFreeMovement => flying;
    protected override bool ParticipatesInWorldPause => false;
    protected override Vector2 CollisionFreeVelocity
    {
        get
        {
            if (playActionData == null) return Vector2.zero;
            Vector2 input = Vector2.ClampMagnitude(new Vector2(playActionData.onMove, playActionData.verticalInput), 1f);
            // 实际逻辑：倍率乘在已归一的方向上，斜向两轴一起缩放，不单改水平。
            return input * fourWaySpeed * MovementMultiplier;
        }
    }
    [SerializeField, Tooltip("选中箱与玩家碰撞框水平间隙不超过此值才开始共同推拉，开发期参数")]
    private float pushPullEngageGap = 0.05f;

    private SliderJoint2D pushPullJoint;
    [SerializeField, Min(0f), Tooltip("飞转行允许的浅层重叠深度；通过后由原生物理处理接触，默认 0.02 个世界单位。")]
    private float walkingRestoreTolerance = 0.02f;

    /// <summary>
    /// 人工阅读：Player 的统一形态提交调用这里。飞行保留原生模拟并把碰撞框改为 Trigger；
    /// 飞转行先检查当前位置，允许配置范围内的浅重叠；通过后恢复实心并重采样，不补角色资源。
    /// </summary>
    public bool TrySetFlying(bool enabled)
    {
        // 安全：角色未完成组装时不能提交物理形态。
        if (!IsMotionReady) return false;
        if (flying == enabled) return true;
        // 实际逻辑：只在这次请求检查一次；浅重叠留给统一模拟，深重叠拒绝，不另找安全点。
        if (!enabled && TryFindWalkingRestoreBlocker(walkingRestoreTolerance, out Collider2D blocker))
        {
            string reason = blocker != null ? $"被「{blocker.name}」占用" : "碰撞框尚未就绪";
            Debug.Log($"{name} 飞转行被拒绝：当前位置{reason}", this);
            return false;
        }
        flying = enabled;
        boxCollider.isTrigger = enabled;
        // 实际逻辑：原下平台忽略对、选中约束和环境命令退出，不把旧水流或墙跳尾速留到恢复行走。
        EndOneWayDrops();
        ClearPendingActions();
        ReleasePushPull();
        ClearExternalMotionEffects();
        playphyFunData.canLeftWall = playphyFunData.canRightWall = null;
        RefreshMotionState(releaseEnvironmentEffects: true);
        return true;
    }

    /// <summary>选中退出立即关闭约束；原生实际惯性不被清零。</summary>
    internal void ReleasePushPull()
    {
        if (pushPullJoint == null) return;
        pushPullJoint.enabled = false;
        pushPullJoint.connectedBody = null;
    }

    private void SynchronizePushPull()
    {
        PhysicalBox box = playActionData?.PushPullBox;
        // 实际逻辑：正朝箱子推时水平改由附加速度负责，关节不能同时钉住这一轴。
        bool closingOnBox = box != null && playerPhysicsData.horizontalSpeed
            * (box.BodyBounds.center.x - BodyBounds.center.x) > 0.05f;
        // 跳跃、落边、停用和非行走运动都解除，禁止把箱子吊在玩家身边。
        if (jump || wallJump || closingOnBox || !IsMotionParticipating || playActionData.FourWay != FourWayMode.None
            || box == null || !box.IsMotionParticipating || !box.IsSupported || !nowGemetry.isGrounded
            || !IsEngagedWith(box))
        {
            ReleasePushPull();
            return;
        }
        Rigidbody2D body = box.GetComponent<Rigidbody2D>();
        if (pushPullJoint != null && pushPullJoint.enabled && pushPullJoint.connectedBody == body) return;
        if (pushPullJoint == null) pushPullJoint = gameObject.AddComponent<SliderJoint2D>();
        pushPullJoint.enabled = false;
        pushPullJoint.autoConfigureAngle = false;
        pushPullJoint.angle = 90f;
        pushPullJoint.autoConfigureConnectedAnchor = false;
        pushPullJoint.connectedBody = body;
        pushPullJoint.anchor = Vector2.zero;
        // Joint 锚点使用局部坐标；用刚体位置差换算旋转/缩放，避开显示插值的平移滞后。
        // 两锚点初始世界 X 相同；竖直滑动自由，不把两刚体中心拉到一起。
        pushPullJoint.connectedAnchor = body.transform.InverseTransformVector(rb.position - body.position);
        pushPullJoint.useMotor = false;
        pushPullJoint.useLimits = false;
        pushPullJoint.enableCollision = true;
        pushPullJoint.enabled = true;
    }

    protected override void OnMotionSuspended() => ReleasePushPull();

    internal override bool HoldsHorizontalLock(BasicEntity other)
    {
        // 安全：关节未连上或对方无效时没有水平锁。
        if (other == null || pushPullJoint == null || !pushPullJoint.enabled) return false;
        // 实际逻辑：仍连着这只箱子时，水平贴合由关节负责。
        return other.HasBody(pushPullJoint.connectedBody);
    }

    /// <summary>与箱子是否已贴合到可共同推拉的距离。</summary>
    internal bool IsEngagedWith(PhysicalBox box) =>
        LevelBoxContext.IsSideContact(BodyBounds, box.BodyBounds, pushPullEngageGap);

    /// <summary>一个物理步内行走输入为 1 时的水平位移，供自动走近时按剩余间隙缩小输入、避免冲撞。</summary>
    internal float WalkStepDistance => EffectiveMovementSpeed * Time.fixedDeltaTime;

    #endregion


    public void ClearPendingActions()
    {
        jump = wallJump = jumpReleaseSide = false;
        wallJumpSide = 0f;
    }

    protected override void OnMotionRefresh(bool relocated)
    {
        IsPassiveMotion = false;
        base.OnMotionRefresh(relocated);
        ClearPendingActions();
        ReleasePushPull();
    }

    #region 瞬时触发事件
    /// <summary>
    /// 物理跳跃动作具体实现
    /// </summary>
    void Jump()
    {
        //print("物理跳跃触发");
        jump = true;
    }

    void JumpRelease()
    {
        //print("跳跃斩断1111111111");

        jumpReleaseSide = true;
    }
    void WallJump()
    {
        wallJump = true;
        // 事件与贴墙 Update 同步。此刻 onMove 已是本帧选侧；之后输入改写不再改变这次冲量。
        wallJumpSide = playActionData.onMove < 0 ? -1f : playActionData.onMove > 0 ? 1f : 0f;
    }
    #endregion
    public void Init(ReadOnly_ActionData actionData, LocalEventSystem<PlayerStateMachine.E_playEvent> fsmEventSystem)
    {
        playActionData = actionData;
        SynchronizeMotionParticipation();
        //注册事件
        fsmEventSystem.AddEventListener(PlayerStateMachine.E_playEvent.jump, Jump);
        fsmEventSystem.AddEventListener(PlayerStateMachine.E_playEvent.jumpRelease, JumpRelease);
        fsmEventSystem.AddEventListener(PlayerStateMachine.E_playEvent.wallJump, WallJump);
    }

    /// <summary>
    /// 按住向下时，放开脚下或本步下落路径中命中的单向平台顶面。
    /// </summary>
    protected override bool WantsDropThroughCurrentOneWay(Collider2D support, Vector2 normal)
    {
        // 安全：动作数据还没注入时不能读上下输入。
        if (playActionData == null) return false;
        // 实际逻辑：负向超过一半才算按下。侧面和普通地面不放开。
        return playActionData.verticalInput < -0.5f
            && IsSolidOneWayTop(support, normal);
    }

    protected override float HorizontalSpeedCalculation()
    {
        // 安全：角色尚未组装动作时没有主动操作。
        if (playActionData == null) return 0f;
        if (playActionData.FourWay != FourWayMode.None)
            return playActionData.onMove * fourWaySpeed * MovementMultiplier;
        // 实际逻辑：先乘行为给出的速率倍率，环境修饰仍在后面乘 nowPhyNum。
        return playActionData.onMove * EffectiveMovementSpeed * playActionData.speedScale;
    }

    protected override void PhyEventUpdate()
    {
        if (IsPassiveMotion) return;
        SynchronizePushPull();
        // 四向行为不叠普通跳跃或墙跳；仅清待消费操作，不清环境累计速度。
        if (playActionData != null && playActionData.FourWay != FourWayMode.None)
        {
            ClearPendingActions();
            return;
        }
        // 真正消费动作时才递增，碰撞顶起不触发跳跃起手。
        if (jump || wallJump) playerPhysicsData.jumpSequence++;
        //普通跳跃
        if (jump)
        {
            //受到地面影响程度
            float data = 1f + EnvironmentFrame.jumpHeightOffset;
            //当前竖直速度等于跳跃速度
            playerPhysicsData.verticalSpeed = EffectiveJumpSpeed * data;
            //数据消费
            jump = false;
        }
        //墙跳
        if (wallJump)
        {
            //计算关键数据
            float jumpHeight;     //跳高程度
            float jumpForce;      //墙跳距离

            jumpHeight = upSpeed;
            jumpForce = wallJumpV * speed;

            playerPhysicsData.verticalSpeed = jumpHeight;
            // 离开触发时选定的墙：朝左贴墙向右跳，朝右贴墙向左跳。不读几何 onLeftWall。
            if (wallJumpSide < 0)
                AddTimeSpeed(0.2f, jumpForce);
            else if (wallJumpSide > 0)
                AddTimeSpeed(0.2f, -jumpForce);
            //消费
            wallJump = false;
            wallJumpSide = 0f;
        }
        if (jumpReleaseSide)
        {
            if (playerPhysicsData.verticalSpeed > 0)
            {
                playerPhysicsData.verticalSpeed *= jumpRelNum;
            }
            //消费
            jumpReleaseSide = false;
        }
    }

    protected override void VerticalTransmission()
    {
        if (IsPassiveMotion) return;
        // 水面悬浮：零输入和向上输入只稳定碰撞框中心；下潜仍用游泳速度，不靠按键改事实。
        if (playActionData != null && (playActionData.isSurfaceSwimming || playActionData.FourWay == FourWayMode.Swimming)
            && playActionData.verticalInput >= 0f && !EnvironmentContext.WaterFrame.submerged
            && EnvironmentContext.WaterFrame.surfaceSwimmingAvailable)
        {
            float dt = Time.fixedDeltaTime;
            // 安全：无有效步长时不能用高度差反推速度。
            if (dt <= 0f) return;
            float maxStep = Mathf.Max(0f, fourWaySpeed * (1f + playerPhysicsData.nowPhyNum)) * dt;
            float heightDelta = EnvironmentContext.WaterFrame.surfaceY - PhysicsWorld.ReadAabb(boxCollider).center.y;
            float targetDelta = Mathf.Clamp(heightDelta, -maxStep, maxStep);
            float carriedY = nowGemetry.isGrounded ? EnvironmentFrame.platformDelta.y : 0f;
            // 只在水面保持目标高度；水下仍完整保留被动水流，平台本步仍只合成一次。
            playerPhysicsData.verticalSpeed = (targetDelta - carriedY) / dt - playerPhysicsData.phyVSpeed;
            return;
        }
        // 四向行为覆盖本步主动竖直速度；被动水流仍由原 phyVSpeed 合入运动快照。
        if (playActionData != null && playActionData.FourWay != FourWayMode.None)
        {
            // 实际逻辑：游泳竖直与水平使用同一移动倍率，环境修饰仍只乘这一次。
            playerPhysicsData.verticalSpeed = playActionData.verticalInput * fourWaySpeed * MovementMultiplier
                * (1f + playerPhysicsData.nowPhyNum);
            return;
        }
        if (playActionData == null) return;
        // 只有逻辑处于贴墙状态且正在下落时，才应用墙滑限速。
        if (playActionData.NowState != PlayerStateMachine.E_playerState.onWallSliding
            || playerPhysicsData.verticalSpeed >= 0) return;

        // 按动作方向选取相位 2 解析的本帧能力；松手不选择任何一侧。
        IWallSlideSurface wall = playActionData.onMove < 0 ? playphyFunData.canLeftWall
            : playActionData.onMove > 0 ? playphyFunData.canRightWall : null;
        // 安全：能力缺席、来源禁用或销毁时，不能读取乘数。
        if (!EnvironmentCapabilities.IsActive(wall)) return;

        playerPhysicsData.verticalSpeed = Mathf.Max(playerPhysicsData.verticalSpeed,
            -wallDownSpeed * wall.WallSlideMultiplier);
    }

    /// <summary>
    /// 进探索时从当前碰撞框向下找一段地面并贴上。没有命中就留在出生点。
    /// 不改每帧脚下探针的贴肤距离。
    /// </summary>
    internal bool TrySnapDownToGround()
    {
        // 安全：没有碰撞框或地面层时不能查询。
        if (cPhysics == null || rb == null || boxCollider == null || !boxCollider.enabled) return false;
        Physics2D.SyncTransforms();
        Bounds bounds = PhysicsWorld.ReadAabb(boxCollider);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(cPhysics.groundLayer);
        spawnDropHits.Clear();
        PhysicsWorld.BoxCast(bounds.center, bounds.size, 0f, Vector2.down, SpawnGroundSearchDistance, filter, spawnDropHits);
        float best = SpawnGroundSearchDistance;
        bool found = false;
        float drop = 0f;
        foreach (RaycastHit2D hit in spawnDropHits)
        {
            // 安全：自身和陡面不是这次可贴的地面。
            if (hit.collider == null || hit.collider.attachedRigidbody == rb) continue;
            if (Vector2.Dot(hit.normal, Vector2.up) <= 0.7f || hit.distance > best) continue;
            best = hit.distance;
            drop = hit.distance;
            found = true;
        }
        if (!found) return false;
        // 实际逻辑：碰撞框底贴到命中面，并留出与每帧探针相同的贴肤间隙。
        Vector2 center = (Vector2)bounds.center + Vector2.down * drop;
        center.y += GroundProbeDistance;
        RefreshMotionState(GetPositionForBodyCenter(center));
        return true;
    }
}
