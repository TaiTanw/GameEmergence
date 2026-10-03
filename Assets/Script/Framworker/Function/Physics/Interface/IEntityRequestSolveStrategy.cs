using PhyData;
using UnityEngine;

/// <summary>当前实体环境判定所需的当帧快照；仍使用 Unity 2D 类型。</summary>
internal readonly struct EntityEnvironmentSolveInput
{
    internal readonly Bounds bounds;
    internal readonly Rigidbody2D body;
    internal readonly LayerMask groundLayer;
    internal readonly LayerMask wallLayer;
    internal readonly float maxClimbAngle;
    internal readonly Collider2D groundCollider;
    internal readonly bool isGrounded;
    internal readonly float verticalSpeed;
    internal readonly float fixedDeltaTime;
    internal readonly EntityMotionFrame motionFrame;

    internal EntityEnvironmentSolveInput(Bounds bounds, Rigidbody2D body, LayerMask groundLayer,
        LayerMask wallLayer, float maxClimbAngle, Collider2D groundCollider, bool isGrounded,
        float verticalSpeed, float fixedDeltaTime, EntityMotionFrame motionFrame)
    {
        this.bounds = bounds;
        this.body = body;
        this.groundLayer = groundLayer;
        this.wallLayer = wallLayer;
        this.maxClimbAngle = maxClimbAngle;
        this.groundCollider = groundCollider;
        this.isGrounded = isGrounded;
        this.verticalSpeed = verticalSpeed;
        this.fixedDeltaTime = fixedDeltaTime;
        this.motionFrame = motionFrame;
    }
}

/// <summary>消费候选命中，计算环境限制与最终请求；Unity 查询由独立 world 入口完成。</summary>
internal interface IEntityRequestSolveStrategy
{
    void ResolveCandidate(in EntityEnvironmentSolveInput input, EntityCandidateGeometry geometry,
        IPhysicsWorldAccess2D world, ref EntitySolutionResult result);
    void FinalizeRequest(in EntityEnvironmentSolveInput input, EntityCandidateGeometry geometry,
        ref EntitySolutionResult result);
}
