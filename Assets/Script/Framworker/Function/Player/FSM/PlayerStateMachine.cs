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
        onWallSliding
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
        onGround.Init(playData, input, this, actionData);
        inAir.Init(playData, input, this, actionData);
        onWallSliding.Init(playData, input, this, actionData);
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
    public void Update(PlayerInputData input)
    {
        // 实际逻辑：没有控制权时停掉本角色的移动输出，不清理管理器那份共享边沿。
        if (input == null)
        {
            if (nowMovement != null)
            {
                nowMovement.onMove = 0f;
                nowMovement.verticalInput = 0f;
                nowMovement.speedScale = 1f;
            }
            return;
        }
        // 实际逻辑：上下是持续输入，各状态共用，不在单个状态里再写一遍。
        if (nowMovement != null)
            nowMovement.verticalInput = input.verticalInput;
        // 实际逻辑：更新之间松开再按下时，帧末仍是按住。用松开事实解除禁跑，不改 sprintHeld。
        if (input.sprintReleased || !input.sprintHeld)
            sprintMustRelease = false;
        onGround.SetFrameInput(input);
        inAir.SetFrameInput(input);
        onWallSliding.SetFrameInput(input);
        onState.Update();
        bool boosting = SprintBoostActive(input);
        if (nowMovement != null)
            nowMovement.speedScale = boosting ? sprintSpeedScale : 1f;
        TickStamina(Time.deltaTime, boosting);
        if (boosting && stamina > 0f)
            closedBecauseEmpty = false;
        //效果已经触发，则关闭，防止持续响应
        input.jumpPressed = false;//触发类型按键全部统一由状态机复原
        input.jumpRelease = false;
        input.interactTapped = false;
        input.interactLongPressed = false;
        input.sprintReleased = false;
    }

    const float WalkInputDeadZone = 0.01f;

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
