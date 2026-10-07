using PhyData;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysicalBox : BasicEntity, IPlayerInteractable
{
    [SerializeField, Tooltip("带电方块：拒绝交互，玩家碰到任意表面即请求电击死亡；推动规则同普通箱")]
    private bool electrified;
    public bool IsElectrified => electrified;

    /// <summary>最近一次相位 1 的箱底几何支撑事实。关系规则须另查参与资格，并在双方本步采样后消费。</summary>
    public bool IsSupported => nowGemetry != null && nowGemetry.isGrounded;

    /// <summary>箱子对玩家交互的处理：外部箱接受未选中者的点按选中；收纳与拉出尚不接受。</summary>
    public PlayerInteractOutcome Interact(in PlayerInteractRequest request)
    {
        // 实际逻辑：带电方块拒绝一切交互。
        if (electrified) return PlayerInteractOutcome.Rejected;
        // 实际逻辑：只有参与运动且有箱底支撑的外部箱可被新选中；已选中者的请求不在此处重复选中。
        if (request.kind == PlayerInteractKind.Tap && !request.requesterSelectsThis && IsMotionParticipating
            && IsSupported)
            return PlayerInteractOutcome.Selected;
        return PlayerInteractOutcome.Rejected;
    }

    protected override void Awake()
    {
        base.Awake();
        // 当前推箱解算只处理平移。冻结物理根节点旋转；表现子节点仍可独立旋转。
        rb.freezeRotation = true;
        rb.angularVelocity = 0f;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        LevelBoxContext.Register(this);
    }

    protected override void OnDisable()
    {
        LevelBoxContext.Unregister(this);
        base.OnDisable();
    }

    protected override void GeometricQuery()
    {
        // 接触查询在命中选择前排除 Trigger；箱体沿自身朝向探测，不在这里解析墙滑能力。
        float a = Vector2.SignedAngle(Vector2.up, transform.up);
        ContactFilter2D contactFilter = new ContactFilter2D { useTriggers = false };
        contactFilter.SetLayerMask(cPhysics.groundLayer);
        RaycastHit2D hit = FindGroundHit(groundV.position, cPhysics.boxCastH, a,
            -transform.up, contactFilter);
        // 顶头与脚下同属接触查询，沿用同一层掩码和 Trigger 过滤。
        RaycastHit2D topHit = PhysicsWorld.BoxCast(upV.position, cPhysics.boxCastH, a,
            transform.up, 0f, contactFilter);
        nowGemetry.istop = topHit.collider != null;
        //缓存真实命中法线；斜坡位移与着地判定必须使用表面信息
        nowGemetry.groundNormal = hit.normal;
        bool onGroundNow = false;
        //默认的空中阻力系数
        self_resistanceCoefficient = 1;
        // 几何着地：法线足够朝上才算踩到支撑，与表面有没有环境能力无关。
        if (hit.collider != null && Vector2.Dot(hit.normal, Vector2.up) > 0.7f)
        {
            onGroundNow = true;
            //在地面，则自身阻力增大
            self_resistanceCoefficient = 20;
        }

        // 几何只保留 Collider；脚下能力由相位 2 经 groundCollider 解析。
        nowGemetry.isGrounded = onGroundNow;
        nowGemetry.groundCollider = onGroundNow ? hit.collider : null;
        // 左右墙只记几何接触。箱体没有贴墙下滑，不把侧面来源送进持续效果登记。
        contactFilter.SetLayerMask(cPhysics.wallLayer);
        RaycastHit2D hit1 = PhysicsWorld.BoxCast(leftV.position, cPhysics.boxCastV, a,
            -transform.right, 0f, contactFilter);
        RaycastHit2D hit2 = PhysicsWorld.BoxCast(rightV.position, cPhysics.boxCastV, a,
            transform.right, 0f, contactFilter);
        nowGemetry.onLeftWall = false;
        nowGemetry.leftWallCollider = null;
        //夹角需小于25度，内积近似0.9
        if (hit1.collider != null && Vector2.Dot(hit1.normal, Vector2.right) > 0.9f)
        {
            nowGemetry.onLeftWall = true;
            nowGemetry.leftWallCollider = hit1.collider;
        }
        nowGemetry.onRightWall = false;
        nowGemetry.rightWallCollider = null;
        if (hit2.collider != null && Vector2.Dot(hit2.normal, Vector2.left) > 0.9f)
        {
            nowGemetry.onRightWall = true;
            nowGemetry.rightWallCollider = hit2.collider;
        }

    }

}
