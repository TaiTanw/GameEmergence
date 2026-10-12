using PhyData;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


/// <summary>
/// 输入解析结果
/// </summary>
public class PlayerInputData
{
    public float moveInput;     //水平输入
    public float verticalInput; // 上下，负数为下
    public bool jumpPressed;     // 这一帧是否按下
    public bool jumpRelease;       //跳跃是否长按中
    public bool interactTapped;    // 交互短按，与长按互斥
    public bool interactLongPressed; // 交互长按，只成立一次
    public bool sprintHeld; // 玩家按住疾跑。松开即为假，不表示角色此刻正在疾跑
    public bool sprintReleased; // 两次逻辑更新之间出现过松开。状态机读过后清掉
}
public enum PushPullAction { None, Push, Pull }

/// <summary>
/// 持续性动作执行数据（瞬时动作靠事件触发
/// </summary>
public class MovementData 
{
    /// <summary>
    /// 移动方向，-1到1，最终速度由物理组件计算
    /// </summary>
    public float onMove;
    /// <summary>
    /// 上下输入。负数为下，由状态机每帧写入，物理只读。
    /// </summary>
    public float verticalInput;
    /// <summary>
    /// 逻辑层速率倍率。1 为行走原速，疾跑时由状态机写成更大的数。物理只读。
    /// </summary>
    public float speedScale = 1f;
    public bool motionUpdates = true;
    /// <summary>探索待命实体保持待机画面；表现只读，不从空中物理自行改播下落。</summary>
    public bool holdIdle;
    public bool surfaceJumpInProgress; // 行为层拥有：跃出期间等待冲量消费和上升结束
    /// <summary>当前选中关系托起推拉能力的箱子；无选中为空。由 Player 写入，执行与表现只读。</summary>
    public PhysicalBox pushPullBox;
    /// <summary>当帧推箱行为：只在选中、地面且有水平操作时为推或拉；表现只读，不以能力开关代替。</summary>
    public PushPullAction pushPullAction;
    /// <summary>本次按键将作用的目标与预期结果；只有受控且有操作许可时才有。表现只读，用于单目标高光与提示。</summary>
    public PlayerInteractHint interactHint;
    public PhysicalBox interactTarget;
    public MechanismCart interactCart;
    /// <summary>
    /// 当前状态
    /// </summary>
    public PlayerStateMachine.E_playerState nowState; 
}

/// <summary>
/// 只读包装类（仅包装需要读的属性）
/// </summary>
public class ReadOnly_ActionData
{
    private readonly MovementData _data;
    public ReadOnly_ActionData(MovementData data) => _data = data;
    //只读属性
    public float onMove => _data.onMove;
    public float verticalInput => _data.verticalInput;
    public float speedScale => _data.speedScale;
    public bool motionUpdates => _data.motionUpdates;
    public bool HoldIdle => _data.holdIdle;
    public PlayerStateMachine.E_playerState NowState =>_data.nowState;
    public bool isSurfaceSwimming => NowState == PlayerStateMachine.E_playerState.surfaceSwimming;
    public bool isSwimming => NowState == PlayerStateMachine.E_playerState.swimming || isSurfaceSwimming;
    public bool surfaceJumpInProgress => _data.surfaceJumpInProgress;
    public PhysicalBox PushPullBox => _data.pushPullBox;
    public PushPullAction PushPullAction => _data.pushPullAction;
    public PlayerInteractHint InteractHint => _data.interactHint;
    public PhysicalBox InteractTarget => _data.interactTarget;
    public MechanismCart InteractCart => _data.interactCart;
    public bool PushPullEnabled => _data.pushPullBox != null;
    public bool isFlying => NowState == PlayerStateMachine.E_playerState.flying;
    public FourWayMode FourWay => isFlying ? FourWayMode.Flying : isSurfaceSwimming ? FourWayMode.SurfaceSwimming
        : isSwimming ? FourWayMode.Swimming : FourWayMode.None;

}

/// <summary>
/// 玩家身上持续资源的当前事实。表现和调试只读这些数，不回写开关或体力。
/// 道具效果还没落地时，数值就是状态机上的原始值。
/// </summary>
public readonly struct PlayerResourceFacts
{
    public readonly float stamina;
    public readonly float staminaMax;
    public readonly bool sprintHeld;
    public readonly bool canSprint;
    public readonly bool closedBecauseEmpty;
    public readonly float oxygen;
    public readonly float oxygenMax;
    public readonly bool submerged;
    public readonly bool oxygenDepleted;

    public PlayerResourceFacts(float stamina, float staminaMax, bool sprintHeld, bool canSprint, bool closedBecauseEmpty,
        float oxygen = 0f, float oxygenMax = 0f, bool submerged = false, bool oxygenDepleted = false)
    {
        this.stamina = stamina;
        this.staminaMax = staminaMax;
        this.sprintHeld = sprintHeld;
        this.canSprint = canSprint;
        this.closedBecauseEmpty = closedBecauseEmpty;
        this.oxygen = oxygen;
        this.oxygenMax = oxygenMax;
        this.submerged = submerged;
        this.oxygenDepleted = oxygenDepleted;
    }
}

public class Player : MonoBehaviour
{
    /// <summary>玩家层。同层角色不互相碰撞；小车独立使用世界渲染层和碰撞排除配置。</summary>
    public const int PlayerLayer = 8;
    [SerializeField] private PlayerIdentity identity;
    [SerializeField, Tooltip("选中外部箱的最大水平碰撞框间隙；选中后须接触才开始共同推拉，开发期参数")] private float interactContactGap = 0.5f;
    [SerializeField, Tooltip("选中后超过此水平碰撞框间隙即视为走远并解除；须大于选中间隙，开发期参数")] private float interactReleaseGap = 0.8f;
    [SerializeField, Tooltip("发现收纳箱以便取出的最大水平碰撞框间隙，开发期参数")] private float storedReachGap = 0.6f;
    [SerializeField, Tooltip("与带电箱／固定带电实心表面的接触容差；触发器须实际重叠，开发期参数")] private float electrifiedContactTolerance = 0.03f;
    private PlayerCapabilityContext capabilities;
    private IPlayerDeathReceiver deathReceiver;
    private PlayerDeathRequest? pendingDeath;
    private uint deathCycle;
    private bool deathCycleClosed;
    private IPlayerFormRules formRules;
    private IPlayerFlightExecution flightExecution;
    private PlayerBoxSelection boxSelection;
    private bool electrifiedDeathLatched;
    private bool crushedDeathLatched;
    private readonly List<Collider2D> crushOverlaps = new List<Collider2D>();
    public PlayerDeathRequest? PendingDeath => pendingDeath;
    public bool IsLevelClearing { get; private set; }
    public PlayerDeathStatus DeathStatus { get; private set; } = PlayerDeathStatus.Handled;
    public PlayerIdentity Identity => identity;
    public float FormCooldownRemaining => (formRules as PlayerLifecycleRules)?.ManualFormCooldownRemaining ?? 0f;
    public PlayerCapabilityPermissions Permissions => capabilities?.Permissions ?? PlayerCapabilityPermissions.Walking;
    public FourWayMode ResolveFourWayAbility() => capabilities?.ResolveFourWay(WaterFacts) ?? FourWayMode.None;
    internal Bounds BodyBounds => playPDate.BodyBounds;
    internal float InteractContactGap => interactContactGap;
    internal float StoredReachGap => storedReachGap;
    internal bool OnGroundForInteraction => _ActionData.NowState == PlayerStateMachine.E_playerState.isOnGround;
    // 长期本体在 DontDestroyOnLoad 场景；机关／电源归当前关卡，不归角色所在的持久场景。
    internal bool IsInGameplayScene(UnityEngine.SceneManagement.Scene scene)
        => scene == (SceneLevelSetup.Current != null ? SceneLevelSetup.Current.gameObject.scene : gameObject.scene);

    /// <summary>组装侧绑定本体和精神体共读的效果记录；重复绑定不改基础参数或重复施加。</summary>
    public void BindEffectLedger(PlayerEffectLedger ledger)
    {
        EnsureRuntimeReady();
        playPDate.BindEffectLedger(ledger);
    }

    public void BindDeathReceiver(IPlayerDeathReceiver receiver)
    {
        deathReceiver = receiver;
        RetryPendingDeath();
    }

    public bool TryRequestDeath(PlayerDeathCause cause)
    {
        // 玩法约束：飞行免死；同周期的氧气和深渊请求只保留第一次。
        // 玩法约束：飞行免死。探索待命的实体即使参与物理也不发死亡，无敌不靠关掉刚体。
        if (IsLevelClearing || ResolveFourWayAbility() == FourWayMode.Flying || deathCycleClosed
            || (Identity == PlayerIdentity.Body && !Permissions.movement)
            || (!Permissions.motionUpdates && !Permissions.resourceUpdates)) return false;
        deathCycleClosed = true;
        pendingDeath = new PlayerDeathRequest(this, cause, ++deathCycle);
        DeathStatus = PlayerDeathStatus.Pending;
        RetryPendingDeath();
        return true;
    }

    public void RetryPendingDeath()
    {
        // 依赖缺席：保留请求，不能把无消费者或打印日志算作处理完成。
        if (!pendingDeath.HasValue || deathReceiver == null) return;
        PlayerDeathRequest request = pendingDeath.Value;
        IPlayerDeathReceiver receiver = deathReceiver;
        PlayerDeathStatus result = receiver.ReceiveDeath(request);
        // 生命周期：受理期间可能重刷新并换了规则／周期，旧返回值不能覆盖新角色状态。
        if (deathReceiver != receiver || pendingDeath?.cycle != request.cycle) return;
        DeathStatus = result;
        if (DeathStatus == PlayerDeathStatus.Handled) pendingDeath = null;
    }

    public void BindFormRules(IPlayerFormRules rules, IPlayerFlightExecution execution)
    { formRules = rules; flightExecution = execution; }

    public bool RequestSpiritForm(bool flying, PlayerFormChangeReason reason)
    {
        // 玩法约束：所有主动、死亡和区域强制请求都交给同一个扣费/形态规则。
        if (Identity != PlayerIdentity.Spirit || (flying && !Permissions.flight)) return false;
        // 规则层确认与实际形态提交必须同时成立，避免依赖只返回 true 就确认死亡完成。
        return formRules != null && formRules.TryChangeForm(this, flying, reason)
            && capabilities.FlightForm == flying;
    }

    internal bool TryCommitSpiritForm(bool flying)
    {
        // 依赖约束：执行通过当前占用检查后才提交实际形态；失败保留操作和资源。
        if (Identity != PlayerIdentity.Spirit || (flying && !Permissions.flight)
            || flightExecution == null || !flightExecution.TrySetFlying(flying)) return false;
        if (!capabilities.SetFlightForm(flying)) return false;
        inputOwner?.ClearPlayerInputEdges(this);
        ReleaseBoxSelection();
        ClearInteractHint();
        ClearActionOutput();
        fsm.ResetMotionActions();
        playPDate.ClearPendingActions();
        fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
        if (!flying) ResetDeathCycle();
        return true;
    }

    /// <summary>人工阅读入口：规则绑定同一精神体的真实飞行执行；初始召唤不经过付费的手动转换。</summary>
    internal void BindSpiritRules(PlayerLifecycleRules rules)
    {
        EnsureRuntimeReady();
        BindFormRules(rules, playPDate as IPlayerFlightExecution);
        BindDeathReceiver(rules);
    }

    public void ResetDeathCycle()
    {
        (deathReceiver as PlayerLifecycleRules)?.CancelDeathWait(this);
        animatorFSM?.CancelDeathPlayback();
        pendingDeath = null;
        deathCycleClosed = false;
        DeathStatus = PlayerDeathStatus.Handled;
        // 同步重开耗氧请求；形态切回行走时不补氧，也不重置体力或环境。
        fsm?.ResetOxygenDeathRequest();
    }

    /// <summary>整关刷新开始前撤销角色操作关系；不恢复资源、不改变认知或形态。</summary>
    internal void PrepareRuntimeRefresh()
    {
        EnsureRuntimeReady();
        inputOwner?.ClearPlayerInputEdges(this);
        ReleaseBoxSelection();
        ClearActionOutput();
        playPDate.ClearPendingActions();
    }

    /// <summary>实体死亡只退出角色操作和资源，保留惯性、重力及共用碰撞运动。</summary>
    internal void BeginDeathMotion()
    {
        PrepareRuntimeRefresh();
        if (inputOwner?.Player == this) inputOwner.InputOpenOrClose(false);
        playPDate.BeginPassiveMotion();
        ApplyCapabilities(new PlayerCapabilityPermissions(false, false, false, true, false, false));
    }

    /// <summary>通关从受理起锁定角色，动画完成和加载由场景编排推进。</summary>
    internal void BeginLevelClear()
    {
        PrepareRuntimeRefresh();
        IsLevelClearing = true;
        if (inputOwner?.Player == this) inputOwner.InputOpenOrClose(false);
        ApplyCapabilities(PlayerCapabilityPermissions.Standby);
    }

    internal void EndLevelClear()
    {
        IsLevelClearing = false;
        animatorFSM?.CancelLevelClearPlayback();
    }

    /// <summary>地图安置只清角色旧位置的运动／操作并重采样，不恢复资源或死亡周期。</summary>
    internal void TeleportRuntime(Vector2 center)
    {
        PrepareRuntimeRefresh();
        playPDate.RefreshMotionState(playPDate.GetPositionForBodyCenter(center));
        fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
        animatorFSM?.ResetMotionHistory();
    }

    public void RefreshRuntime(Vector2? position = null, bool releaseEnvironmentEffects = false, bool alignBodyCenter = false,
        bool preserveLevelClear = false)
    {
        // 普通刷新作废通关等待；跨关安置明确保留加载循环，到淡入结束再释放。
        if (!preserveLevelClear) EndLevelClear();
        // 实际逻辑：进关可能在 Start 之前刷新，先完成状态机接线。
        PrepareRuntimeRefresh();
        fsm.RefreshResources();
        // 地图落点对齐碰撞框中心；出生点和未启用地图的试验起点仍使用原根坐标。
        if (position.HasValue && alignBodyCenter) position = playPDate.GetPositionForBodyCenter(position.Value);
        playPDate.RefreshMotionState(position, releaseEnvironmentEffects);
        electrifiedDeathLatched = crushedDeathLatched = false;
        ResetDeathCycle();
        // 刷新后按当前水/几何重选行为，但不扣资源、不补点按，也不迁移未指定的位置。
        fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
        animatorFSM?.ResetMotionHistory();
    }

    public void ApplyCapabilities(PlayerCapabilityPermissions permissions)
    {
        capabilities.ApplyPermissions(permissions);
        actionData.motionUpdates = permissions.motionUpdates;
        // 实际逻辑：待命实体锁待机；随后暂停运动时保留这幅画面，开放操作才解除。
        actionData.holdIdle = !permissions.movement && (permissions.motionUpdates || actionData.holdIdle);
        // 实际逻辑：暂停运动立即撤销操作输出，但不伪造玩家松键或撤销环境数值。
        if (!permissions.movement || !permissions.motionUpdates) ClearActionOutput();
        if (!permissions.movement || !permissions.motionUpdates)
        {
            // 先读取消费事实，再清物理请求；已执行的跃出由 FSM 保留。
            fsm?.ClearPendingActions(playPDate != null && playPDate.HasPendingJump);
            playPDate.ClearPendingActions();
        }
        playPDate.SetMotionParticipation(permissions.motionUpdates);
        // 实际逻辑：探索待命实体固定碰撞；死亡的被动运动保持动态刚体。
        playPDate.SetImmovableCollision(permissions.motionUpdates && !permissions.movement && !playPDate.IsPassiveMotion);
        UsePlayerLayer(gameObject);
        // 实际逻辑：权限与当前水事实一同提交，当次从飞行恢复游泳，不等下一帧也不更新资源。
        if (runtimeReady) fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
    }

    /// <summary>出生点只需在地面上方有限距离内。只在进入探索时贴一次，不开启重力积分。</summary>
    internal void SnapSpawnToGround()
    {
        EnsureRuntimeReady();
        playPDate.TrySnapDownToGround();
    }

    static void UsePlayerLayer(GameObject root)
    {
        Physics2D.IgnoreLayerCollision(PlayerLayer, PlayerLayer, true);
        SetLayer(root);
    }

    static void SetLayer(GameObject item)
    {
        item.layer = PlayerLayer;
        for (int i = 0; i < item.transform.childCount; i++) SetLayer(item.transform.GetChild(i).gameObject);
    }

    private void ClearActionOutput()
    {
        actionData.onMove = actionData.verticalInput = 0f;
        actionData.speedScale = 1f;
    }
    /// <summary>
    /// 物理组件
    /// </summary>
    CharacterPhysics playPDate;
    /// <summary>
    /// 物理碰撞信息
    /// </summary>
    public ReadOnly_GeometryPhysicsData NowPhyData => playPDate.ReadOnly_GeometryPhysicsData;

    public ReadOnly_PlayerPhysicsData NowSpeedData => playPDate.ReadOnly_PlayersPhysicsData;
    public EntityWaterFrame WaterFacts => playPDate != null ? playPDate.EnvironmentContext.WaterFrame : default;
    /// <summary>
    /// 玩家当前输入数据,初始化交给外部
    /// </summary>
    PlayerInputData inputData;
    private InputControlMgr inputOwner;
    /// <summary>
    /// 玩家当前所执行动作
    /// </summary>
    MovementData actionData;
    public ReadOnly_ActionData _ActionData;

    /// <summary>
    /// 当前体力、玩家是否按住疾跑、状态机是否允许疾跑，以及归零结果。
    /// 正在加速另看 <see cref="ReadOnly_ActionData.speedScale"/>。
    /// </summary>
    public PlayerResourceFacts ResourceFacts => fsm == null
        ? default
        : new PlayerResourceFacts(fsm.stamina, fsm.staminaMax,
            inputData != null && inputData.sprintHeld, fsm.CanSprintNow(), fsm.closedBecauseEmpty,
            fsm.oxygen, fsm.oxygenMax, WaterFacts.submerged, fsm.OxygenDepleted);

    /// <summary>
    /// 玩家逻辑状态机对象
    /// </summary>
    PlayerStateMachine fsm;
    bool runtimeReady;
    /// <summary>
    /// 表现层状态机
    /// </summary>
    PresentationLayer animatorFSM;
    internal PresentationLayer Presentation => animatorFSM;

    void Awake()
    {
        UsePlayerLayer(gameObject);
        capabilities = new PlayerCapabilityContext(identity);
        boxSelection = new PlayerBoxSelection(this);

        //可执行动作数据初始化
        actionData = new MovementData();
        _ActionData = new ReadOnly_ActionData(actionData);
        //逻辑状态机/物理状态/表现层初始化（保证存在
        fsm = new PlayerStateMachine();
        playPDate = gameObject.GetComponentInChildren<CharacterPhysics>();
        animatorFSM = gameObject.GetComponentInChildren<PresentationLayer>();

    }
    private void Start() => EnsureRuntimeReady();

    void EnsureRuntimeReady()
    {
        if (runtimeReady) return;
        fsm.InitData(playPDate.ReadOnly_GeometryPhysicsData, inputData, actionData);
        playPDate.Init(_ActionData, fsm.EventSystem);
        animatorFSM.Init(playPDate.ReadOnly_PlayersPhysicsData, playPDate.ReadOnly_GeometryPhysicsData, _ActionData);
        runtimeReady = true;
    }
    /// <summary>
    /// 注入玩家按键数据行为
    /// </summary>
    /// <param name="data"></param>
    public void ChangeInputAsset(PlayerInputData data, InputControlMgr owner = null)
    {
        inputData = data;
        inputOwner = owner;
    }
    public void RemoveInput()
    {
        fsm?.ObserveSprintRelease(inputData);
        // 控制撤销清待执行操作，不能把已经执行的跃出当作旧按键取消。
        fsm?.ClearPendingActions(playPDate != null && playPDate.HasPendingJump);
        inputData=null;
        inputOwner = null;
        // 实际逻辑：控制撤销只退出本角色的选中与提示，不影响新目标的关系。
        ReleaseBoxSelection();
        ClearInteractHint();
        ClearActionOutput();
        playPDate?.ClearPendingActions();
    }

    private void OnEnable()
    {
        // 相位 2：全体实体相位 1 采样完成后、相位 3 预测前核对箱子关系。
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(UpdateBoxRelations, 2);
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(CheckMechanismCrush, 5);
    }

    private void OnDisable()
    {
        (deathReceiver as PlayerLifecycleRules)?.CancelDeathWait(this);
        animatorFSM?.CancelDeathPlayback();
        // 生命周期：只有仍控制自己的管理器才解除绑定，旧角色不能解绑新目标。
        inputOwner?.UnbindPlayer(this);
        if (actionData != null) RemoveInput();
        if (!MonoPublicMgr.IsQuitting)
        {
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(UpdateBoxRelations, 2);
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(CheckMechanismCrush, 5);
        }
    }

    private void UpdateBoxRelations()
    {
        if (pendingDeath.HasValue || IsLevelClearing) return;
        ValidateBoxSelection();
        // 实际逻辑：碰到带电方块走统一死亡入口，飞行免死由 TryRequestDeath 判断；同一段持续接触只成功请求一次，
        // 离开后才重开，避免死亡处理完成后仍贴着方块时每个物理步重复死亡。
        if (!ElectricContact.Touches(this, electrifiedContactTolerance)) electrifiedDeathLatched = false;
        else if (!electrifiedDeathLatched && TryRequestDeath(PlayerDeathCause.Electrocuted)) electrifiedDeathLatched = true;
    }

    private void CheckMechanismCrush()
    {
        // 安全：暂停和未初始化角色不查夹击，保留暂停前的接触周期。
        if (pendingDeath.HasValue || playPDate == null || !playPDate.IsMotionParticipating) return;
        Collider2D body = GetComponent<Collider2D>();
        if (body == null || !body.isActiveAndEnabled) return;
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer) & ~body.excludeLayers.value);
        crushOverlaps.Clear();
        body.OverlapCollider(filter, crushOverlaps);
        bool inDoor = false, inFixedWall = false, deepOverlap = false;
        foreach (Collider2D other in crushOverlaps)
        {
            // 安全：明确排除非碰撞对和单向台阶，不把平台脚下接触当门夹击。
            if (other == null || other.isTrigger || Physics2D.GetIgnoreCollision(body, other)
                || (other.excludeLayers.value & (1 << gameObject.layer)) != 0) continue;
            PlatformEffector2D effector = other.GetComponent<PlatformEffector2D>();
            if (other.usedByEffector && effector != null && effector.enabled) continue;
            ColliderDistance2D distance = body.Distance(other);
            // 实际逻辑：模拟后两侧仍重叠，且至少一侧超出 0.03 接触容差；原生可能只把另一侧压浅。
            if (!distance.isValid || !distance.isOverlapped || distance.distance >= -.001f) continue;
            Taijie target = other.GetComponent<Taijie>();
            if (target != null && target.isActiveAndEnabled && target.HasMechanismMotion)
            { inDoor = true; deepOverlap |= distance.distance < -.03f; }
            else if (target == null && (other.attachedRigidbody == null
                || other.attachedRigidbody.bodyType == RigidbodyType2D.Static))
            { inFixedWall = true; deepOverlap |= distance.distance < -.03f; }
        }
        if (!inDoor || !inFixedWall || !deepOverlap) crushedDeathLatched = false;
        else if (!crushedDeathLatched && TryRequestDeath(PlayerDeathCause.Crushed)) crushedDeathLatched = true;
    }

    private void ValidateBoxSelection()
    {
        PhysicalBox box = boxSelection.Selected;
        // 安全：没有选中关系时无需核对。
        if (ReferenceEquals(box, null)) return;
        // 实际逻辑：箱子销毁或退出运动、失去箱底支撑、或走远都解除；箱子保留自身速度按原重力继续运动。
        if (box == null || !box.IsMotionParticipating || !box.IsSupported
            || !LevelBoxContext.IsSideContact(BodyBounds, box.BodyBounds, interactReleaseGap))
            ReleaseBoxSelection();
    }

    private void ReleaseBoxSelection()
    {
        // 安全：Awake 之前没有关系与动作数据。
        PhysicalBox previous = boxSelection?.Selected;
        boxSelection?.Release();
        if (actionData != null)
        {
            actionData.pushPullBox = null;
            actionData.pushPullAction = PushPullAction.None;
        }
        ForgetPairContacts(previous);
    }
    //float time;
    void Update()
    {
        TickLogic(Time.deltaTime);
    }

    public void TickLogic(float deltaTime)
    {
        // 通关期间不消耗资源、不处理点按或重新选择行为。
        if (IsLevelClearing) return;
        // 实际逻辑：死亡期间只重试本次完成事实；资源、交互和行为不再推进。
        if (pendingDeath.HasValue)
        {
            RetryPendingDeath();
            return;
        }
        // 交互边沿由 FSM 末尾清除，须在其之前消费。
        ConsumeInteraction();
        // 本次点按可能刚进入通关，不能在同帧继续推进资源与行为。
        if (IsLevelClearing) return;
        //逻辑状态机更新，接收输入信息发送动作数据
        fsm.Update(inputData, ResolveFourWayAbility(), Permissions.movement && Permissions.motionUpdates,
            Permissions.resourceUpdates, deltaTime, playPDate.HasPendingJump, playPDate.GetPlanarVelocity().y);
        ApproachSelectedBox();
        UpdatePushPullAction();
        UpdateInteractHint();
        // 实际逻辑：逻辑入水后立即撤销先前等待物理消费的跳跃，避免入水又离水时补执行。
        if (_ActionData.FourWay != FourWayMode.None) playPDate.ClearPendingActions();
        // 资源只在逻辑更新扣一次；多个 FixedUpdate 和重叠池不会各扣一份。
        if (fsm.TickOxygen(deltaTime, WaterFacts.submerged,
            ResolveFourWayAbility() == FourWayMode.Flying, Permissions.resourceUpdates))
            TryRequestDeath(PlayerDeathCause.OxygenDepleted);
        if (fsm.oxygen > 0f && !pendingDeath.HasValue && DeathStatus == PlayerDeathStatus.Handled)
            deathCycleClosed = false;
    }

    private void ConsumeInteraction()
    {
        PhysicalBox previous = boxSelection.Selected;
        PlayerCapabilityPermissions permissions = Permissions;
        // 实际逻辑：箱子优先；无箱子时小车独占点按，再无小车才交给道具。
        // 漫游飞行仍可领普通道具，推拉许可只约束箱子；小车自行拒绝飞行角色。
        bool freeTap = permissions.movement && permissions.motionUpdates && inputData != null
            && inputData.interactTapped && !inputData.interactLongPressed && !inputData.jumpPressed
            && boxSelection.ResolveTarget() == null;
        // 实际逻辑：撤权、推拉许可关闭或本帧按下跳跃先退出选中，并丢弃同帧交互，不能先收纳再跳。
        if (!(permissions.movement && permissions.motionUpdates && permissions.pushPull)
            || (inputData != null && inputData.jumpPressed))
            boxSelection.Release();
        // 实际逻辑：长按与点按互斥，长按成功的这一次不再按点按处理；无控制权时没有边沿可读。
        else if (inputData != null && inputData.interactLongPressed) boxSelection.Consume(PlayerInteractKind.LongPress);
        else if (inputData != null && inputData.interactTapped) boxSelection.Consume(PlayerInteractKind.Tap);
        if (freeTap)
        {
            MechanismCart cart = MechanismCart.FindInteractTarget(this);
            if (cart != null) cart.Interact(new PlayerInteractRequest(this, PlayerInteractKind.Tap, false));
            else SceneProp.OfferInteract(this);
        }
        // 有效推拉能力只由成立的选中关系托起。
        actionData.pushPullBox = boxSelection.Selected;
        if (previous != boxSelection.Selected) ForgetPairContacts(previous);
    }

    private void ApproachSelectedBox()
    {
        PhysicalBox box = boxSelection.Selected;
        // 实际逻辑：隔距选中后、尚未贴合时，自动朝箱子走到箱边；期间方向输入不生效，跳跃仍会解除选中。
        if (box == null || playPDate.IsEngagedWith(box)
            || _ActionData.NowState != PlayerStateMachine.E_playerState.isOnGround) return;
        Bounds body = BodyBounds, target = box.BodyBounds;
        float toward = Mathf.Sign(target.center.x - body.center.x);
        float gap = Mathf.Max(target.min.x - body.max.x, body.min.x - target.max.x);
        // 实际逻辑：按剩余间隙缩小输入，最后一步不冲进箱子；保留最小幅度避免停在贴合阈值外。
        float remaining = gap - ApproachStopGap;
        actionData.onMove = toward * Mathf.Clamp(remaining / playPDate.WalkStepDistance, MinApproachInput, 1f);
        actionData.speedScale = 1f;
    }

    private const float ApproachStopGap = .02f;
    private const float MinApproachInput = .05f;

    private void UpdatePushPullAction()
    {
        PhysicalBox box = boxSelection.Selected;
        // 实际逻辑：选中且已贴合、地面行走状态且本帧有水平操作才算推箱行为；自动走近、跳跃、游泳／飞行或站立都不输出。
        if (box == null || !playPDate.IsEngagedWith(box)
            || _ActionData.NowState != PlayerStateMachine.E_playerState.isOnGround
            || Mathf.Abs(actionData.onMove) <= 0.01f)
        {
            actionData.pushPullAction = PushPullAction.None;
            return;
        }
        // 实际逻辑：操作方向朝向箱子为推，背离为拉。
        float towardBox = Mathf.Sign(box.BodyBounds.center.x - BodyBounds.center.x);
        actionData.pushPullAction = actionData.onMove * towardBox > 0f ? PushPullAction.Push : PushPullAction.Pull;
    }

    private void UpdateInteractHint()
    {
        PlayerCapabilityPermissions permissions = Permissions;
        // 实际逻辑：无控制权或无操作／推拉许可时按键不会被消费，不显示目标。
        if (inputData == null || !(permissions.movement && permissions.motionUpdates && permissions.pushPull))
        {
            ClearInteractHint();
            return;
        }
        actionData.interactHint = boxSelection.Preview(out actionData.interactTarget);
        actionData.interactCart = actionData.interactTarget == null ? MechanismCart.FindInteractTarget(this) : null;
    }

    private void ClearInteractHint()
    {
        // 安全：Awake 之前没有动作数据。
        if (actionData == null) return;
        actionData.interactHint = PlayerInteractHint.None;
        actionData.interactTarget = null;
        actionData.interactCart = null;
    }

    // 关系变化只撤销原生水平约束，不清箱子或玩家的真实受力速度。
    internal void ReleasePushPullConstraint() => playPDate?.ReleasePushPull();
    private void ForgetPairContacts(PhysicalBox box) => ReleasePushPullConstraint();
}
