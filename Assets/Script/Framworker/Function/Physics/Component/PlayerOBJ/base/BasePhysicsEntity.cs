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
    #region 运行时数据
    //只读数据包装
    ReadOnly_GeometryPhysicsData readOnly_GeometryPhysicsData;
    //自身特殊物理职能数据
    protected PhysicalFunctionData playphyFunData;
    public ReadOnly_GeometryPhysicsData ReadOnly_GeometryPhysicsData => readOnly_GeometryPhysicsData;

    ReadOnly_PlayerPhysicsData readOnly_PlayerPhysicsData;
    public ReadOnly_PlayerPhysicsData ReadOnly_PlayersPhysicsData => readOnly_PlayerPhysicsData;



    #endregion

    protected override void Awake()
    {
        base.Awake();

    }
    protected override void Init()
    {
        playphyFunData = new PhysicalFunctionData();
        nowPhyFun = playphyFunData;
        readOnly_GeometryPhysicsData = new(nowGemetry, playphyFunData);
        readOnly_PlayerPhysicsData = new(playerPhysicsData);
    }
    protected override void GeometricQuery()
    {
        // 接触查询在命中选择前排除 Trigger，避免区域遮住实心表面；不改全局查询开关。
        ContactFilter2D contactFilter = new ContactFilter2D { useTriggers = false };
        contactFilter.SetLayerMask(cPhysics.groundLayer);
        // 地面检测（只做检测，不做响应）
        RaycastHit2D hit = FindGroundHit(groundV.position, cPhysics.boxCastH, 0f,
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
        RaycastHit2D hit1 = PhysicsWorld.BoxCast(leftV.position, cPhysics.boxCastV, 0,
            Vector2.left, 0f, contactFilter);
        RaycastHit2D hit2 = PhysicsWorld.BoxCast(rightV.position, cPhysics.boxCastV, 0,
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
