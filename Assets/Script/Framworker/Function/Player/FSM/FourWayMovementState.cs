using UnityEngine;

/// <summary>
/// 职能：游泳与飞行共用的四向行为策略，不积分速度。
/// 只输出归一化方向；水流等被动速度留在实体原有容器，不用零输入清掉。
/// </summary>
public sealed class FourWayMovementState : BasePlayerState
{
    public override void Enter()
    {
        stateMachine.jbtB = false;
        stateMachine.groundJump = false;
    }

    public override void Update()
    {
        Vector2 direction = Vector2.ClampMagnitude(new Vector2(input.moveInput, input.verticalInput), 1f);
        playActionData.onMove = direction.x;
        playActionData.verticalInput = direction.y;
        playActionData.speedScale = 1f;
    }
}
