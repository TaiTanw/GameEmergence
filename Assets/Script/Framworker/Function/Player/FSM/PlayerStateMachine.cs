using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using PhyData;

/// <summary>
/// 玩家状态机
/// </summary>
public class PlayerStateMachine
{
    /// <summary>
    /// 触发性动作枚举（用于局部事件系统
    /// </summary>
    public enum E_playEvent
    {
        jump,
        jumpRelease,//跳跃松开（处理长按跳高）
        wallJump,//墙跳
    }
    /// <summary>
    /// 状态机内部状态枚举（显式声明避免新增状态而外部未处理新逻辑分支
    /// </summary>
    public enum E_playerState
    {
        isOnGround,
        inAir,
        onWallSliding,
        swimming,
        flying,
        surfaceSwimming
    }

    LocalEventSystem<E_playEvent> eventSystem;
    /// <summary>
    /// 内部事件系统，由状态机保持唯一实例
    /// </summary>
    public LocalEventSystem<E_playEvent> EventSystem=>eventSystem;

    /// <summary>
    /// 当前状态实例
    /// </summary>
    private IBehavioralState onState;
    /// <summary>
    /// 当前动作
    /// </summary>
    MovementData nowMovement;

    //私有状态类实例，外部通过传递枚举转换指定状态
     IsOnGround onGround;
     IsInAir inAir;
     OnWallSliding onWallSliding;
    private readonly FourWayMovementState fourWay = new FourWayMovementState();
    private readonly SurfaceSwimmingState surfaceSwimming = new SurfaceSwimmingState();
    private readonly PlayerInputData neutralInput = new PlayerInputData();
    private ReadOnly_GeometryPhysicsData geometry;
    private bool surfaceJumpAwaitingImpulse;
    internal float FrameDeltaTime { get; private set; }
    #region 配置数据
    /// <summary>
    /// 在空中可跳跃跳跃数
    /// </summary>
    public int jumpNum = 1;
    /// <summary>
    /// 土狼时间
    /// </summary>
    public float coyoteTime = 0.1f;

    /// <summary>
    /// 跳跃缓冲时间
    /// </summary>
    public float jumpBuffer = 0.2f;
    /// <summary>
    /// 最小起跳时间
    /// </summary>
    public float jumpUpTime = 0.1f;
    /// <summary>疾跑时相对行走的速率倍率。以后定标，不写入环境移速。</summary>
    public float sprintSpeedScale = 1.5f;
    /// <summary>体力上限。以后定标。</summary>
    public float staminaMax = 100f;
    /// <summary>疾跑开启时每秒扣除的体力。</summary>
    public float staminaDrainPerSecond = 25f;
    /// <summary>疾跑关闭后每秒恢复的体力。</summary>
    public float staminaRecoverPerSecond = 20f;
    /// <summary>首版氧气基础值，和体力同归当前角色资源，后续由效果出口定标。</summary>
    public float oxygenMax = 100f;
    public float oxygenDrainPerSecond = 10f;
    public float oxygenRecoverPerSecond = 20f;

    #endregion

    #region 运行时数据
    /// <summary>
    /// 当前空中可跳跃次数
    /// </summary>
    public int jumpCount = 0;
    /// <summary>
    /// 跳跃缓冲开关
    /// </summary>
    public bool jbtB;
    /// <summary>
    /// 是否可使用地面跳跃
    /// </summary>
    public bool groundJump;
    /// <summary>当前体力。</summary>
    public float stamina = 100f;
    /// <summary>因体力归零而关闭。玩家松开时不置位。摔倒以后接在这个结果上。</summary>
    public bool closedBecauseEmpty;
    /// <summary>耗尽后必须先松开。不改写玩家是否仍按着。</summary>
    bool sprintMustRelease;
    public float oxygen = 100f;
    private bool oxygenDeathRequested;
    public bool OxygenDepleted => oxygen <= 0f;
    #endregion

    public PlayerStateMachine()
    {
        onGround = new IsOnGround();
        inAir = new IsInAir();
        onWallSliding = new OnWallSliding();
        eventSystem = new LocalEventSystem<E_playEvent>();
        //当前状态信息初始化
        onState = onGround;

        jumpCount = jumpNum;
    }

    public void InitData(ReadOnly_GeometryPhysicsData playData, PlayerInputData input, MovementData actionData)
    {
        nowMovement = actionData;
        geometry = playData;
        onGround.Init(playData, input, this, actionData);
        inAir.Init(playData, input, this, actionData);
        onWallSliding.Init(playData, input, this, actionData);
        fourWay.Init(playData, input, this, actionData);
        surfaceSwimming.Init(playData, input, this, actionData);
        onState.Enter();
    }

    /// <summary>
    /// 状态改变
    /// </summary>
    public void ChangeState(E_playerState state)
    {
        nowMovement.nowState = state;
        IBehavioralState nowState;
        switch (state)
        {
            case E_playerState.isOnGround:
                nowState=onGround;
                break;
            case E_playerState.inAir:
                nowState=inAir;
                break;
            case E_playerState.onWallSliding:
                nowState=onWallSliding;
                break;
            case E_playerState.swimming:
            case E_playerState.flying:
                nowState = fourWay;
                break;
            case E_playerState.surfaceSwimming:
                nowState = surfaceSwimming;
                break;
            default:
                nowState=null;
                Debug.LogError("有多余枚举未处理状态的逻辑分支");
                break;
        }
        onState.Exit();
        onState = nowState;
        onState.Enter();
    }

    /// <summary>
    /// 供玩家mono执行的玩家更新逻辑
    /// </summary>
    /// <param name="playData">玩家当前状态</param>
    /// <param name="input">当前输入信息</param>
    /// <param name="stateMachine">玩家自身状态机</param>
    public void Update(PlayerInputData input, FourWayMode mode = FourWayMode.None,
        bool movementAllowed = true, bool resourceUpdates = true, float? deltaTime = null,
        bool jumpPending = false, float totalVerticalSpeed = 0f)
    {
        FrameDeltaTime = Mathf.Max(0f, deltaTime ?? Time.deltaTime);
        // 实际逻辑：输入缺席只替换为零操作，环境判断和获准更新的资源仍继续。
        PlayerInputData frameInput = input != null && movementAllowed ? input : neutralInput;
        ObserveSprintRelease(input);
        nowMovement.verticalInput = frameInput.verticalInput;
        onGround.SetFrameInput(frameInput);
        inAir.SetFrameInput(frameInput);
        onWallSliding.SetFrameInput(frameInput);
        fourWay.SetFrameInput(frameInput);
        surfaceSwimming.SetFrameInput(frameInput);
        // 跃出边界：等待物理消费后，仅上升阶段抑制水面重捕获；飞行/完整淹没立即交回各自行为。
        if (nowMovement.surfaceJumpInProgress)
        {
            // 先记录本次跃出已消费，本帧追加的空中跳跃不能重开这段等待。
            if (!jumpPending) surfaceJumpAwaitingImpulse = false;
            if (mode == FourWayMode.Flying || mode == FourWayMode.Swimming
                || (!jumpPending && totalVerticalSpeed <= 0f))
            {
                nowMovement.surfaceJumpInProgress = surfaceJumpAwaitingImpulse = false;
            }
            else if (mode == FourWayMode.SurfaceSwimming) mode = FourWayMode.None;
        }
        SelectFourWayState(mode);
        onState.Update();
        bool boosting = SprintBoostActive(frameInput);
        nowMovement.speedScale = boosting ? sprintSpeedScale : 1f;
        if (resourceUpdates) TickStamina(Mathf.Max(0f, deltaTime ?? Time.deltaTime), boosting);
        if (boosting && stamina > 0f)
            closedBecauseEmpty = false;
        // 实际逻辑：无控制权时不清其它角色持有的共享输入边沿。
        if (input == null) return;
        //效果已经触发，则关闭，防止持续响应
        input.jumpPressed = false;//触发类型按键全部统一由状态机复原
        input.jumpRelease = false;
        input.interactTapped = false;
        input.interactLongPressed = false;
        input.sprintReleased = false;
    }

    private void SelectFourWayState(FourWayMode mode)
    {
        E_playerState desired = mode == FourWayMode.Flying ? E_playerState.flying
            : mode == FourWayMode.SurfaceSwimming ? E_playerState.surfaceSwimming : E_playerState.swimming;
        // 行为优先级：已解析的四向原因高于着地和墙滑。
        if (mode != FourWayMode.None)
        {
            if (nowMovement.nowState != desired) ChangeState(desired);
            return;
        }
        // 离水或能力撤销：按当前几何恢复，不恢复进入水前的状态。
        if (nowMovement.nowState == E_playerState.swimming || nowMovement.nowState == E_playerState.flying
            || nowMovement.nowState == E_playerState.surfaceSwimming)
            ChangeState(geometry.isGrounded ? E_playerState.isOnGround : E_playerState.inAir);
    }

    const float WalkInputDeadZone = 0.01f;

    public void ObserveSprintRelease(PlayerInputData input)
    {
        // 实际逻辑：只接收真实设备事实，不用控制权移除或缺席输入冒充松键。
        if (input != null && (input.sprintReleased || !input.sprintHeld)) sprintMustRelease = false;
    }

    public void ClearPendingActions(bool cancelPendingSurfaceJump = false)
    {
        jbtB = false;
        inAir.ClearPendingInput();
        // 冲量已消费后，清输入不能终止仍在上升的跃出。
        if (cancelPendingSurfaceJump && surfaceJumpAwaitingImpulse && nowMovement != null)
            nowMovement.surfaceJumpInProgress = surfaceJumpAwaitingImpulse = false;
    }

    /// <summary>显式刷新、形态提交或重新进入水面行为时，重置请求及执行保护。</summary>
    internal void ResetMotionActions()
    {
        ClearPendingActions();
        surfaceJumpAwaitingImpulse = false;
        if (nowMovement != null) nowMovement.surfaceJumpInProgress = false;
    }

    internal void BeginSurfaceJump(bool released)
    {
        // 行为先切空中再发原跳跃事件，执行层才能沿已有冲量和短跳规则消费。
        ChangeState(E_playerState.inAir);
        inAir.BeginJumpReleaseWindow(released);
        nowMovement.surfaceJumpInProgress = true;
        surfaceJumpAwaitingImpulse = true;
        groundJump = jbtB = false;
        eventSystem.EventTigger(E_playEvent.jump);
    }

    /// <summary>死亡周期重开时允许零氧气再次发请求；不改变资源数值或其它行为锁存。</summary>
    internal void ResetOxygenDeathRequest() => oxygenDeathRequested = false;

    public void RefreshResources()
    {
        stamina = Mathf.Max(0f, staminaMax);
        oxygen = Mathf.Max(0f, oxygenMax);
        closedBecauseEmpty = sprintMustRelease = oxygenDeathRequested = false;
        ResetMotionActions();
        JumpCountToNum();
    }

    /// <summary>状态机这一侧是否允许疾跑。不看玩家有没有按住。</summary>
    public bool CanSprintNow()
    {
        // 实际逻辑：耗尽后须先松开。有体力、着地，且本帧有行走输入。站立、空中、贴墙都不可。
        if (sprintMustRelease || stamina <= 0f || nowMovement == null) return false;
        if (nowMovement.nowState != E_playerState.isOnGround) return false;
        return Mathf.Abs(nowMovement.onMove) > WalkInputDeadZone;
    }

    bool SprintBoostActive(PlayerInputData input)
    {
        // 实际逻辑：玩家按住，并且状态机当前允许，两则才是疾跑。
        return input.sprintHeld && CanSprintNow();
    }

    void TickStamina(float deltaTime, bool boosting)
    {
        // 实际逻辑：只在疾跑事实成立时扣体力。松开、停步或离地都改为恢复。
        if (boosting)
        {
            stamina -= staminaDrainPerSecond * deltaTime;
            // 实际逻辑：归零只关掉状态机这一侧，并留下摔倒用的结果。不改玩家是否仍按着。
            if (stamina > 0f) return;
            stamina = 0f;
            closedBecauseEmpty = true;
            sprintMustRelease = true;
            if (nowMovement != null)
                nowMovement.speedScale = 1f;
            return;
        }
        if (stamina < staminaMax)
            stamina = Mathf.Min(staminaMax, stamina + staminaRecoverPerSecond * deltaTime);
    }

    /// <summary>每次逻辑更新调用一次；归零只返回一次请求信号，死亡结果由 Player 和规则层处理。</summary>
    public bool TickOxygen(float deltaTime, bool submerged, bool flying, bool resourceUpdates)
    {
        // 许可：待机角色暂停资源，输入/改键开关不参与此判断。
        if (!resourceUpdates) return false;
        float dt = Mathf.Max(0f, deltaTime);
        // 玩法：离水或飞行不耗氧，按同一可调恢复参数恢复。
        if (!submerged || flying)
        {
            oxygen = Mathf.Min(oxygenMax, oxygen + oxygenRecoverPerSecond * dt);
            if (oxygen > 0f) oxygenDeathRequested = false;
            return false;
        }
        oxygen = Mathf.Max(0f, oxygen - oxygenDrainPerSecond * dt);
        // 归零结果：同一耗尽周期不每帧重复发死亡；正值恢复或显式刷新后才能重开。
        if (oxygen > 0f || oxygenDeathRequested) return false;
        oxygenDeathRequested = true;
        return true;
    }
    /// <summary>
    /// 重置可跳次数
    /// </summary>
    public void JumpCountToNum()
    {
        jumpCount = jumpNum;
    }
    /// <summary>
    /// 执行跳跃并判断是否可跳跃
    /// </summary>
    /// <returns></returns>
    public bool JumpCan()
    {
        if (jumpCount <= 0)
        {
            return false;
        }

        jumpCount--;
        return true;

    }
}
