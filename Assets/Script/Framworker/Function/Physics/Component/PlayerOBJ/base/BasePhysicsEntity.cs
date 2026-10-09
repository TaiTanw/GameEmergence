using Cysharp.Threading.Tasks.Triggers;
using PhyData;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// 角色物理基类
/// </summary>
public abstract class BasePhysicsEntity : BasicEntity
{
    protected override bool HasHorizontalDrive => true;
    #region 运行时数据
    //只读数据包装
    ReadOnly_GeometryPhysicsData readOnly_GeometryPhysicsData;
    //自身特殊物理职能数据
    protected PhysicalFunctionData playphyFunData;
    public ReadOnly_GeometryPhysicsData ReadOnly_GeometryPhysicsData => readOnly_GeometryPhysicsData;

    ReadOnly_PlayerPhysicsData readOnly_PlayerPhysicsData;
    public ReadOnly_PlayerPhysicsData ReadOnly_PlayersPhysicsData => readOnly_PlayerPhysicsData;
    // 跨帧分别保留尚未穿完的平台；相位 1 维护，脚下查询与相位 4 共用只读视图。
    readonly List<Collider2D> passingOneWays = new List<Collider2D>();
    readonly List<RaycastHit2D> oneWayDropHits = new List<RaycastHit2D>();



    #endregion

    protected override void Awake()
    {
        base.Awake();

    }
    protected override void Init()
    {
        playphyFunData = new PhysicalFunctionData();
        readOnly_GeometryPhysicsData = new(nowGemetry, playphyFunData);
        readOnly_PlayerPhysicsData = new(playerPhysicsData);
    }
    protected override void GeometricQuery()
    {
        if (UsesCollisionFreeMovement) { ClearEnvironmentFacts(); return; }
        // 角色顶头由本步原生接触补充，先清掉已经离开的旧接触。
        nowGemetry.istop = false;
        // 接触查询在命中选择前排除 Trigger，避免区域遮住实心表面；不改全局查询开关。
        ContactFilter2D contactFilter = new ContactFilter2D { useTriggers = false };
        contactFilter.SetLayerMask(cPhysics.groundLayer);
        // 先维护下平台的临时碰撞对，再形成本帧支撑事实。
        ReleaseOneWayDropsIfClear();
        oneWayDropColliders = passingOneWays;
        PrepareOneWayDrops(contactFilter);
        RaycastHit2D hit = FindGroundHit(ProbePosition(groundV), cPhysics.boxCastH, 0f,
            Vector2.down, contactFilter);
        //缓存贴地法线
        nowGemetry.groundNormal = hit.normal;
        bool onGroundNow = false;
        //默认的空中阻力系数
        self_resistanceCoefficient = 1;
        //一定角度内正对碰撞才算着地
        if (hit.collider != null && Vector2.Dot(hit.normal, Vector2.up) > 0.7f)
        {
            onGroundNow = true;
            //在地面，则自身阻力增大
            self_resistanceCoefficient = 20;
        }
        // 几何只保留 Collider；脚下能力由相位 2 经 groundCollider 解析。
        nowGemetry.isGrounded = onGroundNow;
        nowGemetry.groundCollider = onGroundNow ? hit.collider : null;
        //检测左右靠墙
        contactFilter.SetLayerMask(cPhysics.wallLayer);
        RaycastHit2D hit1 = PhysicsWorld.BoxCast(ProbePosition(leftV), cPhysics.boxCastV, 0,
            Vector2.left, 0f, contactFilter);
        RaycastHit2D hit2 = PhysicsWorld.BoxCast(ProbePosition(rightV), cPhysics.boxCastV, 0,
            Vector2.right, 0f, contactFilter);
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

    /// <summary>
    /// 这一帧是否要放开查询命中的单向顶面。默认不放开，由具体角色提供意图。
    /// 职能：行为意图进入相位 1；不在这里改速度。
    /// </summary>
    protected virtual bool WantsDropThroughCurrentOneWay(Collider2D support, Vector2 normal) => false;

    void PrepareOneWayDrops(ContactFilter2D filter)
    {
        // 安全：没有实体碰撞框时不能查询下落路径。
        if (boxCollider == null || !boxCollider.enabled) return;
        Vector2 probeSize = cPhysics.boxCastH;
        probeSize.x = Mathf.Max(probeSize.x, PhysicsWorld.ReadAabb(boxCollider).size.x);
        // 实际逻辑：覆盖下一相位可能产生的向下位移，避免先碰撞、下一帧才放开。
        // 这里只估查询范围，不写速度；真正的着地查询仍使用 GroundProbeDistance。
        float dt = Time.fixedDeltaTime;
        float fallingSpeed = GetPlanarVelocity().y + gravity * dt;
        float distance = GroundProbeDistance + Mathf.Max(0f, -fallingSpeed * dt);
        oneWayDropHits.Clear();
        PhysicsWorld.BoxCast(ProbePosition(groundV), probeSize, 0f, Vector2.down, distance, filter, oneWayDropHits);
        foreach (RaycastHit2D hit in oneWayDropHits)
        {
            // 安全：自身与无效命中不是环境支撑。
            if (hit.collider == null || hit.collider.attachedRigidbody == rb) continue;
            if (WantsDropThroughCurrentOneWay(hit.collider, hit.normal))
                BeginOneWayDrop(hit.collider);
        }
    }

    void BeginOneWayDrop(Collider2D platform)
    {
        // 实际逻辑：新支撑另建忽略对，旧平台只有自己清离后才恢复。
        if (passingOneWays.Contains(platform)) return;
        passingOneWays.Add(platform);
        // 安全：没有自身碰撞体时不能建立忽略对。
        if (boxCollider == null || platform == null) return;
        PhysicsWorld.SetIgnoreCollision(boxCollider, platform, true);
    }

    void ReleaseOneWayDropsIfClear()
    {
        for (int i = passingOneWays.Count - 1; i >= 0; i--)
        {
            Collider2D platform = passingOneWays[i];
            // 安全：销毁或禁用的碰撞体不再保留关系。
            bool active = platform != null && platform.enabled && platform.gameObject.activeInHierarchy;
            // 实际逻辑：每块平台单独等身体清离，松键不提前恢复。
            if (active && boxCollider != null && boxCollider.enabled && !HasClearedOneWay(platform)) continue;
            if (active && boxCollider != null && boxCollider.enabled)
                PhysicsWorld.SetIgnoreCollision(boxCollider, platform, false);
            passingOneWays.RemoveAt(i);
        }
    }

    bool HasClearedOneWay(Collider2D platform)
    {
        Bounds body = PhysicsWorld.ReadAabb(boxCollider);
        Bounds surface = platform.bounds;
        // 实际逻辑：下方、侧方完全分离，或跳回顶面探测范围以上，都已离开这块平台。
        // 顶面保留脚下探测的间隙，避免刚开始落下时尚未进入平台就恢复碰撞。
        return body.max.y < surface.min.y || body.max.x < surface.min.x ||
            body.min.x > surface.max.x || body.min.y > surface.max.y + GroundProbeDistance;
    }

    protected void EndOneWayDrops()
    {
        foreach (Collider2D platform in passingOneWays)
            if (platform != null && boxCollider != null)
                PhysicsWorld.SetIgnoreCollision(boxCollider, platform, false);
        passingOneWays.Clear();
        oneWayDropColliders = null;
    }

    protected override void OnDisable()
    {
        EndOneWayDrops();
        base.OnDisable();
    }

    protected override void OnMotionRefresh(bool relocated)
    {
        // 保持位置的轻刷新仍须保留尚未穿完的平台忽略对；迁移位置才撤销旧对。
        if (relocated) EndOneWayDrops();
    }

    protected override void PhyFunUpdate()
    {
        base.PhyFunUpdate();

        playphyFunData.canLeftWall = EnvironmentCapabilities.Find<IWallSlideSurface>(nowGemetry.leftWallCollider);
        playphyFunData.canRightWall = EnvironmentCapabilities.Find<IWallSlideSurface>(nowGemetry.rightWallCollider);
    }
    protected override void HActiveSpeedOperation()
    {
        //处理基础移动（赋值操作，最先）//计算主动位移
        playerPhysicsData.horizontalSpeed = HorizontalSpeedCalculation() * (1 + playerPhysicsData.nowPhyNum);
        //统一事件触发（控制时序在物理检测和赋值操作之后
        PhyEventUpdate();
    }
    protected override void VActiveSpeedOperation()
    {
        VerticalTransmission();
    }
    
    /// <summary>
    /// 最先执行的水平速度赋值操作
    /// </summary>
    /// <param name="horizontalSpeed"></param>
    protected abstract float HorizontalSpeedCalculation();

    /// <summary>
    /// 统一消费物理事件更新逻辑
    /// </summary>
    protected abstract void PhyEventUpdate();
    
    /// <summary>
    /// 处理特殊行为的状态性垂直变速（例如贴墙下滑，攀岩
    /// </summary>
    /// <param name="v"></param>
    protected abstract void VerticalTransmission();


}
