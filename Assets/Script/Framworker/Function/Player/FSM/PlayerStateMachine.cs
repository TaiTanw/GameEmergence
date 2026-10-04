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
                nowMovement.onMove = 0f;
            return;
        }
        onGround.SetFrameInput(input);
        inAir.SetFrameInput(input);
        onWallSliding.SetFrameInput(input);
        onState.Update();
        //效果已经触发，则关闭，防止持续响应
        input.jumpPressed = false;//触发类型按键全部统一由状态机复原
        input.jumpRelease = false;
        input.interactTapped = false;
        input.interactLongPressed = false;
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
