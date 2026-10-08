using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PhyData
{
    /// <summary>
    /// 限时速度叠加效果
    /// </summary>
	public struct SpeedStackData
    {
        /// <summary>
        /// 响应计时器（控制瞬发速度何时复原（受力时间
        /// </summary>
        public float responseTimer1;

        /// <summary>
        /// 水平速度
        /// </summary>
        public float hspeed;

    }
    /// <summary>
    /// 施力类型
    /// </summary>
    public enum E_PhyForceType
    {
        apply,//施加
        controlRecovery,//控制复原
    }

    /// <summary>
    /// 动态施力参数：不包含实体已累计的速度或当前受力类型。
    /// 环境来源创建参数，实体的 ForceData 保存当前控制参数；真实惯性归刚体。
    /// </summary>
    public readonly struct DynamicForceParameters
    {
        public readonly float Force;
        public readonly float balanceSpeed;
        public readonly float recoverySpeed;

        public DynamicForceParameters(float force, float balanceSpeed, float recoverySpeed)
        {
            Force = force;
            this.balanceSpeed = Mathf.Abs(balanceSpeed);
            this.recoverySpeed = recoverySpeed;
        }
    }

    /// <summary>动态环境参数；每步读取刚体实际速度，不保存另一份累计动量。</summary>
    public struct ForceData
    {
        public DynamicForceParameters parameters;
        public readonly float Force => parameters.Force;
        public readonly float recoverySpeed => parameters.recoverySpeed;
        public readonly float balanceSpeed => parameters.balanceSpeed;
        public E_PhyForceType type;
        public void Init(float force, float recoverySpeed, float balanceSpeed)
        {
            parameters = new DynamicForceParameters(force, balanceSpeed, recoverySpeed);
            type = E_PhyForceType.apply;
        }
    }

    #region 物理实时数据

    /// <summary>
    /// 实时物理移动效应数据
    /// </summary>
    public class PlayerPhysicsData
    {
        /// <summary>
        /// 当前自身水平速度（主动位移
        /// </summary>
        public float horizontalSpeed;
        /// <summary>
        /// 当前自身竖直速度（可主动影响
        /// </summary>
        public float verticalSpeed;
        /// <summary>
        /// 水平物理影响速度（被动位移
        /// </summary>
        public float phyHSpeed;
        /// <summary>
        /// 垂直物理影响速度（被动
        /// </summary>
        public float phyVSpeed;
        /// <summary>
        /// 当前环境物理约束数据,移动速度（粘滞力
        /// </summary>
        public float nowPhyNum;
        /// <summary>真正消费跳跃/墙跳时递增，原生顶起不产生跳跃起手。</summary>
        public int jumpSequence;

    }
    /// <summary>
    /// 相位 1 记下本帧碰到了什么：脚下、左右墙、是否顶头、是否着地。
    /// 例如侧面碰到墙，这里只留 Collider 和 onLeftWall，还不表示能贴墙下滑。
    /// 职能：几何接触事实；能力是否有效在 PhysicalFunctionData，环境效果在 Context。
    /// </summary>
    public class GeometryPhysicsData
    {
        // 真实命中与脚本能力分别保存；无脚本地面/墙也保留 Collider 几何身份。
        public Collider2D groundCollider;
        public Collider2D leftWallCollider;
        public Collider2D rightWallCollider;
        public bool istop;//几何：头顶方向碰到实心碰撞体
        public bool isGrounded;     // 几何：法线判定后的着地，不表示表面能力
        public bool onLeftWall; // 几何：左侧接触，不表示墙滑能力
        public bool onRightWall; // 几何：右侧接触，不表示墙滑能力

        /// <summary>
        /// 贴地法线
        /// </summary>
        public Vector2 groundNormal;

        /// <summary>
        /// 本帧自身碰撞体重叠到的区域 Trigger。例如人碰到水的触发器，这里先记下碰撞体，还不登记减速。
        /// 职能：相位 1 感知缓冲；监测点与嵌入深度不放在本列表。
        /// </summary>
        public readonly List<Collider2D> regionOverlaps = new List<Collider2D>();
    }
    /// <summary>
    /// 角色在相位 2 从左右墙 Collider 解析出的墙滑能力引用。
    /// 例如左侧碰到普通墙时 canLeftWall 为空，几何上的 onLeftWall 仍可为真。
    /// 职能：物理职能结果；逻辑层只读其是否有效，乘数由执行层读取。箱体不使用本类型。
    /// </summary>
    public class PhysicalFunctionData
    {
        public IWallSlideSurface canLeftWall;
        public IWallSlideSurface canRightWall;
    }
    /// <summary>
    /// 速度物理实时数据只读包装
    /// </summary>
    public class ReadOnly_PlayerPhysicsData
    {
        private readonly PlayerPhysicsData _data;

        public ReadOnly_PlayerPhysicsData(PlayerPhysicsData data)
        {
            _data = data;
        }

        public int jumpSequence => _data.jumpSequence;
        public float horizontalSpeed => _data.horizontalSpeed;   //当前自身水平速度(主动

        public float phyHSpeed => _data.phyHSpeed;//被动水平速度
        public float verticalSpeed => _data.verticalSpeed;  //当前自身竖直速度

        public float phyVSpeed=>_data.phyVSpeed;//被动垂直速度


    }

    /// <summary>
    /// 逻辑层只读出口。着地和左右贴墙读几何接触；canLeftWall / canRightWall 读墙滑能力是否仍有效。
    /// 职能：行为层边界。不暴露墙滑乘数，也不把几何接触和能力可用性合成一个含义。
    /// </summary>
    public class ReadOnly_GeometryPhysicsData
    {
        private readonly GeometryPhysicsData _data;
        private readonly PhysicalFunctionData _data2;
        public ReadOnly_GeometryPhysicsData(GeometryPhysicsData data,PhysicalFunctionData data2)
        {
            _data = data;
            _data2 = data2;
        }

        public bool isGrounded => _data.isGrounded; // 几何接触
        public bool onLeftWall => _data.onLeftWall; // 几何接触
        public bool onRightWall => _data.onRightWall; // 几何接触

        public bool canRightWall => EnvironmentCapabilities.IsActive(_data2.canRightWall); // 职能：右侧墙滑能力可用
        public bool canLeftWall => EnvironmentCapabilities.IsActive(_data2.canLeftWall); // 职能：左侧墙滑能力可用

    }


    #endregion

    /// <summary>
    /// 本步原生运动命令与模拟后同一步的刚体位置；供运行时只读比较请求和实际移动。
    /// requestStep/observedStep 是同一固定步；requestedWorldDelta 为自由命令推算，碰撞后的实际位移另外读取。
    /// 实测差值可能包含 Unity 碰撞修正及其它外部位置写入，不宣称是纯引擎修正量。
    /// </summary>
    public readonly struct EntityDisplacementObservation
    {
        public readonly int requestStep;
        public readonly int observedStep;
        public readonly Vector2 requestOrigin;
        public readonly Vector2 requestedWorldDelta;
        public readonly Vector2 actualPosition;

        public Vector2 RequestedTarget => requestOrigin + requestedWorldDelta;
        public Vector2 ActualWorldDelta => actualPosition - requestOrigin;
        public Vector2 DifferenceFromRequest => actualPosition - RequestedTarget;

        public EntityDisplacementObservation(int requestStep, int observedStep,
            Vector2 requestOrigin, Vector2 requestedWorldDelta, Vector2 actualPosition)
        {
            this.requestStep = requestStep;
            this.observedStep = observedStep;
            this.requestOrigin = requestOrigin;
            this.requestedWorldDelta = requestedWorldDelta;
            this.actualPosition = actualPosition;
        }
    }

}
