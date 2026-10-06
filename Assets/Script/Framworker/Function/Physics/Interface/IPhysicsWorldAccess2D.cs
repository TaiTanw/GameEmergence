using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 当前 Unity 2D 后端的查询、碰撞对设置与位移提交入口。命中、碰撞体和刚体仍是 Unity 类型；
/// 替换其它物理系统时需在此边界转换数据，不能把本接口当作跨引擎数据契约。
/// </summary>
public interface IPhysicsWorldAccess2D
{
    Bounds ReadAabb(BoxCollider2D collider);
    Vector2 ReadBodyPosition(Rigidbody2D body);
    RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction,
        float distance, ContactFilter2D filter);
    int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction,
        float distance, ContactFilter2D filter, List<RaycastHit2D> results);
    int OverlapBox(Vector2 center, Vector2 size, float angle, ContactFilter2D filter,
        List<Collider2D> results);
    int OverlapCollider(Collider2D collider, ContactFilter2D filter, List<Collider2D> results);
    int GetLayerCollisionMask(int layer);
    bool GetIgnoreCollision(Collider2D first, Collider2D second);
    void SetIgnoreCollision(Collider2D first, Collider2D second, bool ignore);
    void SubmitPosition(Rigidbody2D body, Vector2 target);
}
