using UnityEngine;

/// <summary>
/// 职能：水面游泳的专用行为策略，只输出方向和跃出事件。
/// 半露出事实由环境采样提供；按下只输出下潜，不直接把行为改成水下游泳。
/// </summary>
public sealed class SurfaceSwimmingState : BasePlayerState
{
    public override void Enter()
    {
        stateMachine.ResetMotionActions();
        stateMachine.groundJump = false;
        stateMachine.JumpCountToNum();
    }

    public override void Update()
    {
        Vector2 direction = Vector2.ClampMagnitude(new Vector2(input.moveInput, Mathf.Min(0f, input.verticalInput)), 1f);
        playActionData.onMove = direction.x;
        playActionData.verticalInput = direction.y;
        // 跃出是明确动作；水面/水下之间的切换仍由下一次几何事实决定。
        if (input.jumpPressed) stateMachine.BeginSurfaceJump(input.jumpRelease);
    }
}
