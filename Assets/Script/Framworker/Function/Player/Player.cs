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
    public bool surfaceJumpInProgress; // 行为层拥有：跃出期间等待冲量消费和上升结束
    /// <summary>当前选中关系托起推拉能力的箱子；无选中为空。由 Player 写入，执行与表现只读。</summary>
    public PhysicalBox pushPullBox;
    /// <summary>当帧推箱行为：只在选中、地面且有水平操作时为推或拉；表现只读，不以能力开关代替。</summary>
    public PushPullAction pushPullAction;
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
    public PlayerStateMachine.E_playerState NowState =>_data.nowState;
    public bool isSurfaceSwimming => NowState == PlayerStateMachine.E_playerState.surfaceSwimming;
    public bool isSwimming => NowState == PlayerStateMachine.E_playerState.swimming || isSurfaceSwimming;
    public bool surfaceJumpInProgress => _data.surfaceJumpInProgress;
    public PhysicalBox PushPullBox => _data.pushPullBox;
    public PushPullAction PushPullAction => _data.pushPullAction;
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
    [SerializeField] private PlayerIdentity identity;
    [SerializeField, Tooltip("交互发现箱子的最大水平碰撞框间隙，开发期参数")] private float interactContactGap = 0.1f;
    [SerializeField, Tooltip("选中后超过此水平碰撞框间隙即视为走远并解除，开发期参数")] private float interactReleaseGap = 0.3f;
    [SerializeField, Tooltip("与带电方块碰撞框间隙不超过此值即算触碰，开发期参数")] private float electrifiedContactTolerance = 0.03f;
    private PlayerCapabilityContext capabilities;
    private IPlayerDeathReceiver deathReceiver;
    private PlayerDeathRequest? pendingDeath;
    private uint deathCycle;
    private bool deathCycleClosed;
    private IPlayerFormRules formRules;
    private IPlayerFlightExecution flightExecution;
    private PlayerBoxSelection boxSelection;
    private bool electrifiedDeathLatched;
    public PlayerDeathRequest? PendingDeath => pendingDeath;
    public PlayerDeathStatus DeathStatus { get; private set; } = PlayerDeathStatus.Handled;
    public PlayerIdentity Identity => identity;
    public PlayerCapabilityPermissions Permissions => capabilities?.Permissions ?? PlayerCapabilityPermissions.Walking;
    public FourWayMode ResolveFourWayAbility() => capabilities?.ResolveFourWay(WaterFacts) ?? FourWayMode.None;
    internal Bounds BodyBounds => playPDate.BodyBounds;
    internal float InteractContactGap => interactContactGap;
    internal bool OnGroundForInteraction => _ActionData.NowState == PlayerStateMachine.E_playerState.isOnGround;

    public void BindDeathReceiver(IPlayerDeathReceiver receiver)
    {
        deathReceiver = receiver;
        RetryPendingDeath();
    }

    public bool TryRequestDeath(PlayerDeathCause cause)
    {
        // 玩法约束：飞行免死；同周期的氧气和深渊请求只保留第一次。
        if (ResolveFourWayAbility() == FourWayMode.Flying || deathCycleClosed
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
        DeathStatus = deathReceiver.ReceiveDeath(pendingDeath.Value);
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
        // 依赖约束：穿墙与安全落点执行未接入时不把普通四向移动冒充飞行。
        if (Identity != PlayerIdentity.Spirit || (flying && !Permissions.flight)
            || flightExecution == null || !flightExecution.TrySetFlying(flying)) return false;
        if (!capabilities.SetFlightForm(flying)) return false;
        fsm.ResetMotionActions();
        playPDate.ClearPendingActions();
        fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
        if (!flying) ResetDeathCycle();
        return true;
    }

    public void ResetDeathCycle()
    {
        pendingDeath = null;
        deathCycleClosed = false;
        DeathStatus = PlayerDeathStatus.Handled;
        // 同步重开耗氧请求；形态切回行走时不补氧，也不重置体力或环境。
        fsm?.ResetOxygenDeathRequest();
    }

    public void RefreshRuntime(Vector2? position = null, bool releaseEnvironmentEffects = false)
    {
        inputOwner?.ClearPlayerInputEdges(this);
        ReleaseBoxSelection();
        ClearActionOutput();
        fsm.RefreshResources();
        playPDate.RefreshMotionState(position, releaseEnvironmentEffects);
        ResetDeathCycle();
        // 刷新后按当前水/几何重选行为，但不扣资源、不补点按，也不迁移未指定的位置。
        fsm.Update(null, ResolveFourWayAbility(), false, false, 0f);
        animatorFSM?.ResetMotionHistory();
    }

    public void ApplyCapabilities(PlayerCapabilityPermissions permissions)
    {
        capabilities.ApplyPermissions(permissions);
        actionData.motionUpdates = permissions.motionUpdates;
        // 实际逻辑：暂停运动立即撤销操作输出，但不伪造玩家松键或撤销环境数值。
        if (!permissions.movement || !permissions.motionUpdates) ClearActionOutput();
        if (!permissions.movement || !permissions.motionUpdates)
        {
            // 先读取消费事实，再清物理请求；已执行的跃出由 FSM 保留。
            fsm?.ClearPendingActions(playPDate != null && playPDate.HasPendingJump);
            playPDate.ClearPendingActions();
        }
        playPDate.SetMotionParticipation(permissions.motionUpdates);
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
    /// <summary>
    /// 表现层状态机
    /// </summary>
    PresentationLayer animatorFSM;

    void Awake()
    {

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
    private void Start()
    {
        //保证可用
        fsm.InitData(playPDate.ReadOnly_GeometryPhysicsData, inputData, actionData);
        playPDate.Init(_ActionData, fsm.EventSystem);
        animatorFSM.Init(playPDate.ReadOnly_PlayersPhysicsData,playPDate.ReadOnly_GeometryPhysicsData, _ActionData);
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
        // 实际逻辑：控制撤销只退出本角色的选中，不影响新目标的关系。
        ReleaseBoxSelection();
        ClearActionOutput();
        playPDate?.ClearPendingActions();
    }

    private void OnEnable()
    {
        // 相位 2：全体实体相位 1 采样完成后、相位 3 预测前核对箱子关系。
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(UpdateBoxRelations, 2);
    }

    private void OnDisable()
    {
        // 生命周期：只有仍控制自己的管理器才解除绑定，旧角色不能解绑新目标。
        inputOwner?.UnbindPlayer(this);
        if (actionData != null) RemoveInput();
        if (!MonoPublicMgr.IsQuitting) MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(UpdateBoxRelations, 2);
    }

    private void UpdateBoxRelations()
    {
        ValidateBoxSelection();
        // 实际逻辑：碰到带电方块走统一死亡入口，飞行免死由 TryRequestDeath 判断；同一段持续接触只成功请求一次，
        // 离开后才重开，避免死亡处理完成后仍贴着方块时每个物理步重复死亡。
        if (!LevelBoxContext.TouchesElectrified(BodyBounds, electrifiedContactTolerance)) electrifiedDeathLatched = false;
        else if (!electrifiedDeathLatched && TryRequestDeath(PlayerDeathCause.Electrocuted)) electrifiedDeathLatched = true;
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
        // 交互边沿由 FSM 末尾清除，须在其之前消费。
        ConsumeInteraction();
        //逻辑状态机更新，接收输入信息发送动作数据
        fsm.Update(inputData, ResolveFourWayAbility(), Permissions.movement && Permissions.motionUpdates,
            Permissions.resourceUpdates, deltaTime, playPDate.HasPendingJump, playPDate.GetPlanarVelocity().y);
        UpdatePushPullAction();
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
        // 实际逻辑：撤权、推拉许可关闭或本帧按下跳跃先退出选中，并丢弃同帧交互，不能先收纳再跳。
        if (!(permissions.movement && permissions.motionUpdates && permissions.pushPull)
            || (inputData != null && inputData.jumpPressed))
            boxSelection.Release();
        // 实际逻辑：长按与点按互斥，长按成功的这一次不再按点按处理；无控制权时没有边沿可读。
        else if (inputData != null && inputData.interactLongPressed) boxSelection.Consume(PlayerInteractKind.LongPress);
        else if (inputData != null && inputData.interactTapped) boxSelection.Consume(PlayerInteractKind.Tap);
        // 有效推拉能力只由成立的选中关系托起。
        actionData.pushPullBox = boxSelection.Selected;
        if (previous != boxSelection.Selected) ForgetPairContacts(previous);
    }

    private void UpdatePushPullAction()
    {
        PhysicalBox box = boxSelection.Selected;
        // 实际逻辑：选中、地面行走状态且本帧有水平操作才算推箱行为；跳跃、游泳／飞行或站立都不输出。
        if (box == null || _ActionData.NowState != PlayerStateMachine.E_playerState.isOnGround
            || Mathf.Abs(actionData.onMove) <= 0.01f)
        {
            actionData.pushPullAction = PushPullAction.None;
            return;
        }
        // 实际逻辑：操作方向朝向箱子为推，背离为拉。
        float towardBox = Mathf.Sign(box.BodyBounds.center.x - BodyBounds.center.x);
        actionData.pushPullAction = actionData.onMove * towardBox > 0f ? PushPullAction.Push : PushPullAction.Pull;
    }

    private void ForgetPairContacts(PhysicalBox box)
    {
        // 实际逻辑：选中建立或解除时立即清双方旧接触累计与尾效，共同运动期间及解除后都不残留拖动；
        // 无关环境来源保留。
        if (box != null && playPDate != null) PhysicsSolverMgr.Instance.ForgetContactForcesBetween(playPDate, box);
        if (boxSelection?.Selected != null && playPDate != null)
            PhysicsSolverMgr.Instance.ForgetContactForcesBetween(playPDate, boxSelection.Selected);
    }

}

