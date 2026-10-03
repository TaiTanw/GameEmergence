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
        balance,//平衡
        controlRecovery,//控制复原
        fadeAway,//消退
    }

    /// <summary>
    /// 动态施力参数：不包含实体已累计的速度或当前受力类型。
    /// 环境来源创建参数，实体的 ForceData 持有参数并独立维护跨帧状态。
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

    /// <summary>
    /// 动态受力数据：施力参数与跨帧状态分开保存。
    /// </summary>
    public struct ForceData 
    {
        /// <summary>施力配置；刷新此值不覆盖下方的跨帧状态。</summary>
        public DynamicForceParameters parameters;
        public readonly float Force => parameters.Force;
        public readonly float recoverySpeed => parameters.recoverySpeed;
        public readonly float balanceSpeed => parameters.balanceSpeed;
        /// <summary>
        /// 此施力所致的速度叠加
        /// </summary>
        public float speedStacking;
        /// <summary>
        /// 当满足当前叠加速度大于平衡速度时
        /// </summary>
        public readonly bool IsBalance => Mathf.Abs(speedStacking) > Mathf.Abs(parameters.balanceSpeed);
        /// <summary>
        /// 施力类型
        /// </summary>
        public E_PhyForceType type;

        public void Init(float Force,float recoverySpeed, float balanceSpeed)
        {
            parameters = new DynamicForceParameters(Force, balanceSpeed, recoverySpeed);
            type = E_PhyForceType.apply;
        }


        /// <summary>
        /// 当类型为施加的物理帧更新逻辑
        /// </summary>
        /// <param name="quality">受力物体受环境影响程度（质量倒数）</param>
        /// <returns>速度叠加</returns>
        public float FixUpdate(float quality)
        {
            speedStacking += parameters.Force * quality * Time.fixedDeltaTime;
            if(IsBalance)
            {
                if (speedStacking < 0)
                {
                    speedStacking=-parameters.balanceSpeed;
                }
                else
                {
                    speedStacking=parameters.balanceSpeed;
                }
            }

            return speedStacking;
        }
        /// <summary>
        /// 受控速度恢复
        /// </summary>
        /// <param name="quality">质量倒数</param>
        /// <returns></returns>
        public float ControlledSpeedRecovery(float quality)
        {
            if (speedStacking > 0.2)
            {
                speedStacking -= parameters.recoverySpeed * quality * Time.fixedDeltaTime;
            }
            else if (speedStacking < -0.2)
            {
                speedStacking += parameters.recoverySpeed * quality * Time.fixedDeltaTime;
            }
            else
            {
                speedStacking = 0;
            }

            return speedStacking;
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
        /// 位移偏置（旧草稿，正式管线改用 EntitySolutionResult.displacementOffset）
        /// </summary>
        //public float displacementBias;
        /// <summary>
        /// 位移嵌入（旧草稿，预测重叠在解算器内用 AABB v4 计算，不写回此字段）
        /// </summary>
        //public float positionalEmbedding;
        /// <summary>
        /// 当前环境物理约束数据,移动速度（粘滞力
        /// </summary>
        public float nowPhyNum;

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
    /// 物理职能基类
    /// </summary>
    public class BasePhyFunData
    {
        /// <summary>
        /// 已废弃：推墙/推箱不再用 Enter 边沿 AddForce（保留字段以免旧序列化/注释对照）
        /// </summary>
        public IForceAction nowForceThing;
        /// <summary>
        /// 已废弃：同上
        /// </summary>
        public IForceAction lastForceThing;
        // 支撑环境关系及采样已归实体 EnvironmentContext，不再保存 BaseGround 类型引用。


    }
    /// <summary>
    /// 角色在相位 2 从左右墙 Collider 解析出的墙滑能力引用。
    /// 例如左侧碰到普通墙时 canLeftWall 为空，几何上的 onLeftWall 仍可为真。
    /// 职能：物理职能结果；逻辑层只读其是否有效，乘数由执行层读取。箱体不使用本类型。
    /// </summary>
    public class PhysicalFunctionData:BasePhyFunData
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
    /// 相位 3 输出的只读运动快照，每帧整体替换；预测和接触解算读取同一份基础运动。
    /// freeVelocity 是速度更新后的世界合速度，motionVelocity/motionDelta 是支撑处理后的自身运动；
    /// 平台携带另存 platformDelta，合成 plannedWorldDelta 供预测使用，不作为自身接触施力速度。
    /// 速度单位为世界单位/秒，位移单位为世界单位。相位 4 的约束结果另存 EntitySolutionResult，
    /// 这份快照不保存接触纠偏、最终请求或 Unity 实际落点，也不持有跨帧累计速度。
    /// </summary>
    public readonly struct EntityMotionFrame
    {
        public readonly Vector2 freeVelocity; // 自由合速度（支撑处理前）
        public readonly Vector2 motionVelocity; // 自身运动速度（支撑处理后）
        public readonly Vector2 motionDelta; // 自身基础位移
        public readonly Vector2 platformDelta; // 支撑平台携带位移（离地为零）
        public readonly Vector2 plannedWorldDelta; // 预测基础位移＝自身＋平台

        /// <summary>保存各运动分量，并将自身位移与平台位移合成为预测基础；不在这里施加碰撞约束。</summary>
        public EntityMotionFrame(Vector2 freeVelocity, Vector2 motionVelocity, Vector2 motionDelta, Vector2 platformDelta)
        {
            this.freeVelocity = freeVelocity;
            this.motionVelocity = motionVelocity;
            this.motionDelta = motionDelta;
            this.platformDelta = platformDelta;
            plannedWorldDelta = motionDelta + platformDelta;
        }
    }

    /// <summary>
    /// 相位 3 上报给 PhysicsSolverMgr 的预测 AABB：用本帧基础位移平移当前碰撞框。
    /// 携带产生该终点的运动快照，使相位 4 的接触读速与预测同帧；实体引用用于写回纠偏。
    /// point 还不包含接触偏置或环境裁剪；回算请求终点时，要减去基础位移再加 RequestedWorldDelta。
    /// </summary>
    public struct PhysicalBoundingBox
    {
        public Vector2 point; // 世界预测中心
        public Vector2 size; // AABB 半宽、半高
        public EntityMotionFrame motionFrame; // 同帧基础运动
        public BasicEntity myPhyBox; // 所属实体
    }

    /// <summary>
    /// 每个实体持有的相位 4 查询工作区，由 ProbeCandidateEnvironment 清空、复用和填充。
    /// 候选终点＝基础预测终点＋实体间偏置；分别保存移动路径、终点重叠、脚下与抬升净空的命中。
    /// 原始法线和距离供环境判定与最终请求裁剪读取；单向组件字典只记录身份，不直接表示可通行。
    /// 接触回算改变偏置后会重查本工作区；它不是帧初着地事实，也不保存最终请求或跨帧状态。
    /// </summary>
    internal sealed class EntityCandidateGeometry
    {
        internal Vector2 candidateCenter; // 世界候选中心
        internal Vector2 candidateDelta; // 当前中心至候选中心的位移
        internal float downwardCastLift; // 向下回扫的起点抬高量
        internal readonly List<RaycastHit2D> pathHits = new List<RaycastHit2D>(); // 候选移动路径命中
        internal readonly List<RaycastHit2D> downwardHits = new List<RaycastHit2D>(); // 候选终点脚下命中
        internal readonly List<RaycastHit2D> liftHits = new List<RaycastHit2D>(); // 额外抬升路径命中
        internal readonly List<RaycastHit2D> allowedDownwardHits = new List<RaycastHit2D>(); // X 受限后的脚下命中
        internal readonly List<Collider2D> endpointOverlaps = new List<Collider2D>(); // 候选终点重叠体
        // 单向碰撞体 → 有效平台效应器
        internal readonly Dictionary<Collider2D, PlatformEffector2D> oneWayEffectors =
            new Dictionary<Collider2D, PlatformEffector2D>();

        internal void RecordOneWayEffector(Collider2D hit)
        {
            if (hit.usedByEffector && hit.TryGetComponent(out PlatformEffector2D effector) &&
                effector.isActiveAndEnabled && effector.useOneWay)
                oneWayEffectors[hit] = effector;
        }
    }

    /// <summary>
    /// 相位 4 写给本实体的解算结果：接触偏置与环境限制在这里合成为 requestedWorldDelta。
    /// 接触回算期间可以重写，最终相位 5 只提交请求位移一次；阻挡方向供接触施力及速度清理读取。
    /// 基础运动仍在 EntityMotionFrame，累计速度仍在 EntityEffectState；请求不等于 Unity 实际落点。
    /// </summary>
    public struct EntitySolutionResult
    {
        public Vector2 displacementOffset; // 实体间纠偏位移
        public Vector2 environmentOffset; // 环境附加修正（当前只用坡面 Y）
        public float allowedHorizontalDelta; // 环境允许的水平位移
        public Vector2 requestedWorldDelta; // 本帧请求位移
        public float blockedHorizontalDirection; // X 阻挡方向：-1 左、1 右、0 无
    }

    /// <summary>
    /// 一次位移请求与下一物理步观测到的刚体位置；供运行时只读比较请求和实际移动。
    /// requestStep 是提交请求的实体物理步，observedStep 是随后采样的实体物理步。
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
