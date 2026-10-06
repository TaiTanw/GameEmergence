using System.Collections.Generic;
using UnityEngine;

/// <summary>把当前默认 PhysicsScene2D 的读写集中在一个 Unity 后端实现。</summary>
public sealed class UnityPhysicsWorldAccess2D : IPhysicsWorldAccess2D
{
    public static readonly UnityPhysicsWorldAccess2D Instance = new UnityPhysicsWorldAccess2D();

    private UnityPhysicsWorldAccess2D() { }

    public Bounds ReadAabb(BoxCollider2D collider) => collider.bounds;
    public Vector2 ReadBodyPosition(Rigidbody2D body) => body.position;

    public RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction,
        float distance, ContactFilter2D filter) =>
        Physics2D.defaultPhysicsScene.BoxCast(origin, size, angle, direction, distance, filter);

    public int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction,
        float distance, ContactFilter2D filter, List<RaycastHit2D> results) =>
        Physics2D.defaultPhysicsScene.BoxCast(origin, size, angle, direction, distance, filter, results);

    public int OverlapBox(Vector2 center, Vector2 size, float angle, ContactFilter2D filter,
        List<Collider2D> results) =>
        Physics2D.defaultPhysicsScene.OverlapBox(center, size, angle, filter, results);

    public int OverlapCollider(Collider2D collider, ContactFilter2D filter,
        List<Collider2D> results) => collider.OverlapCollider(filter, results);

    public int GetLayerCollisionMask(int layer) => Physics2D.GetLayerCollisionMask(layer);
    public bool GetIgnoreCollision(Collider2D first, Collider2D second) =>
        Physics2D.GetIgnoreCollision(first, second);

    public void SetIgnoreCollision(Collider2D first, Collider2D second, bool ignore) =>
        Physics2D.IgnoreCollision(first, second, ignore);

    public void SubmitPosition(Rigidbody2D body, Vector2 target) => body.MovePosition(target);

    public void RestoreBodyMotion(Rigidbody2D body, Vector2? position)
    {
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        if (position.HasValue)
        {
            // 显式迁移同时对齐子物体探针。启用插值时只写 body.position 会留下旧 Transform。
            Vector3 pose = body.transform.position;
            pose.x = position.Value.x;
            pose.y = position.Value.y;
            body.transform.position = pose;
            body.position = position.Value;
        }
        // 刷新后的几何立即用于水/支撑重采样，不等待旧 Transform 同步。
        Physics2D.SyncTransforms();
    }
}
