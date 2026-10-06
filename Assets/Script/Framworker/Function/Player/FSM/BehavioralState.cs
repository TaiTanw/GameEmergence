using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using static CharacterPhysics;
using PhyData;

/// <summary>
/// 角色行为状态接口
/// </summary>
public interface IBehavioralState
{
    /// <summary>
    /// 状态进入
    /// </summary>
    /// <param name="p"></param>
    public void Enter();

    /// <summary>
    /// 状态离开
    /// </summary>
    /// <param name="p"></param>
    public void Exit();
    /// <summary>
    /// 状态更新
    /// </summary>
    /// <param name="p"></param>
    public void Update();

}

/// <summary>
/// 玩家状态类基类
/// </summary>
public class BasePlayerState : IBehavioralState
{
    //玩家物理状态/只读
    protected ReadOnly_GeometryPhysicsData playPhyData;
    //原始输入/只读
    protected PlayerInputData input;
    //过滤后的动作输出/读写
    protected MovementData playActionData;
    //依附的逻辑状态机/调用
    protected PlayerStateMachine stateMachine;
    //此状态机内部的事件系统
    protected LocalEventSystem<PlayerStateMachine.E_playEvent> localEventSystem;

    public void Init(ReadOnly_GeometryPhysicsData playPhyData, PlayerInputData input, PlayerStateMachine stateMachine,MovementData playActionData)
    {
        this.playPhyData = playPhyData;
        this.input = input;
        this.stateMachine = stateMachine;
        localEventSystem =this.stateMachine.EventSystem;
        this.playActionData = playActionData;
    }

    /// <summary>本帧改用玩家此刻持有的输入。初始化时留下的引用不再继续使用。</summary>
    public void SetFrameInput(PlayerInputData input)
    {
        this.input = input;
    }

    public virtual void Enter()
    {
        
    }


    public virtual void Exit()
    {
        
    }

    public virtual void Update()
    {
        
    }


}
/// <summary>
/// 是否在地面
/// </summary>
public class IsOnGround : BasePlayerState
{
    public override void Enter()
    {
        //Debug.Log("地面状态进入");
        stateMachine.JumpCountToNum();
        stateMachine.groundJump=true;
    }

    public override void Exit()
    {
        //Debug.Log("地面状态退出");
    }



    public override void Update()
    {
        //处理跳跃缓冲
        if (stateMachine.jbtB && stateMachine.groundJump)
        {
            //playActionData.onJump = true;
            localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jump);
            //Debug.Log("消费缓冲");
            stateMachine.jbtB = false;
            stateMachine.groundJump=false;
        }
        if (input.jumpPressed && stateMachine.groundJump)
        {
            //动作响应
            //playActionData.onJump = true;
            localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jump);
            //消耗地面跳跃
            stateMachine.groundJump = false;
            //触发状态复原，此处已经交给状态机
        }
        //移动动作响应
        playActionData.onMove = input.moveInput;
        //若已经不在地面，则改变状态
        if (!playPhyData.isGrounded)
        {
            //直接靠成员变量来传入，避免频繁GC
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.inAir);
        }
    }
}

public class IsInAir : BasePlayerState
{
    internal void BeginJumpReleaseWindow(bool released) => canJumpCut = released;
    public void ClearPendingInput()
    {
        jbt = 0f;
        canJumpCut = false;
    }
    /// <summary>
    /// 当前此状态时间
    /// </summary>
    float nowTime;
    /// <summary>
    /// 跳跃缓冲计时器
    /// </summary>
    float jbt;
    /// <summary>
    /// 起跳窗口时间
    /// </summary>
    float jumpWtime;
    /// <summary>
    /// 跳跃斩断开关
    /// </summary>
    bool canJumpCut;
    public override void Enter()
    {
        //Debug.Log("啊啊啊");
        //Debug.Log("空中状态进入");
        nowTime = 0;
        jbt = 0;
        jumpWtime = 0;
        // 实际逻辑：重新进入空中时不带回入水前尚未执行的跳跃斩断。
        canJumpCut = false;
    }

    public override void Exit( )
    {
        //Debug.Log("空中状态退出");
        nowTime = 0;
        jbt = 0;
    }


    public override void Update()
    {
        
        if (stateMachine.jbtB)
        {
            jbt += stateMachine.FrameDeltaTime;

            if (jbt >stateMachine.jumpBuffer)
            {
                stateMachine.jbtB = false; // 超时自动关闭
            }
        }

        //响应跳跃键输入
        if (input.jumpPressed)
        {
            if(nowTime < stateMachine.coyoteTime && stateMachine.groundJump)//是否可使用地面跳（土狼时间响应跳跃
            {
                localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jump);
                jumpWtime=nowTime;
                //只要成功触发跳跃，直接关闭斩断开关
                canJumpCut = false;                         //此做法表示状态更新的时效性，避免前次延迟的跳跃斩断影响后次跳跃（空中多段跳情况下
                //避免同一窗口重复消费
                stateMachine.groundJump = false;
            }
            else if (stateMachine.JumpCan())//是否可使用空中跳
            {
                localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jump);
                jumpWtime = nowTime;
                //只要成功触发跳跃，直接关闭斩断开关
                canJumpCut = false;
            }
            else
            {
                //触发跳跃缓冲计时
                jbt = 0;
                stateMachine.jbtB = true;
                //Debug.Log("跳跃缓冲计时触发");
            }
        }
        //响应跳跃松开
        if (input.jumpRelease)
        {
            if (nowTime - jumpWtime >= stateMachine.jumpUpTime)//超时斩断
            {
                localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jumpRelease);
            }
            else//未达到最小时间，延迟执行
            {
                canJumpCut=true;
            }

        }

        //逻辑更新直接使用帧间隔更新时间
        nowTime += stateMachine.FrameDeltaTime;

        if (canJumpCut)
        {
            //当时间计时满足条件，开启斩断开关
            if (nowTime - jumpWtime >= stateMachine.jumpUpTime)
            {
                canJumpCut = false;
                localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.jumpRelease);
            }
        }
        //移动动作响应
        playActionData.onMove= input.moveInput;

        //所有逻辑执行完成，开始根据信息转换状态，靠前优先度高，满足多种状态的转换条件时，优先度高的表示最终转换后状态

        if (playPhyData.isGrounded && !playActionData.surfaceJumpInProgress)//跃出首帧可能仍贴浅水池底
        {
            //直接靠成员变量来传入，避免频繁GC
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.isOnGround);
            return;
        }
        else if ((input.moveInput < 0 && playPhyData.canLeftWall) || (input.moveInput > 0 && playPhyData.canRightWall))//在贴墙,且可贴墙
        {
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
            return;
        }
    }
}


public class OnWallSliding : BasePlayerState
{
    public override void Enter()
    {
        base.Enter();
        //Debug.Log("进入贴墙状态");
    }
    public override void Exit()
    {
        base.Exit();
        //Debug.Log("退出贴墙状态");
    }

    public override void Update()
    {
        // 先输出当前方向，贴墙持续或本次更新提前退出时都不保留旧输入。
        playActionData.onMove = input.moveInput;

        if (playPhyData.isGrounded)//在地面
        {
            //直接靠成员变量来传入，避免频繁GC
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.isOnGround);
            return;
        }
        // 当前动作指向的一侧无墙滑能力，或已松手，则退出贴墙。
        else if (!((playActionData.onMove < 0 && playPhyData.canLeftWall)
            || (playActionData.onMove > 0 && playPhyData.canRightWall)))
        {
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.inAir);
            return;
        }

        if (input.jumpPressed)
        {
            localEventSystem.EventTigger(PlayerStateMachine.E_playEvent.wallJump);
            stateMachine.ChangeState(PlayerStateMachine.E_playerState.inAir);
        }
    }
}
