using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using PhyData;

/// <summary>
/// 相位 4 的全体实体接触协调器，读取相位 3 上报的预测 AABB 与同帧运动快照。
/// 先累加预测重叠的纠偏，再调用实体求环境可行请求；环境裁剪产生残余接触时最多回算两轮。
/// 最终请求确定后清理受阻速度、登记可行的有向接触力，并撤销退出的旧关系。
/// 本类不移动刚体；接触力由实体在下一帧积分，移动请求由各实体在相位 5 提交。
/// </summary>
public class PhysicsSolverMgr : BaseMgr<PhysicsSolverMgr>
{
    /// <summary>
    /// 本帧预测世界快照。相位 3 由实体上报，相位 4 消费后清空。
    /// </summary>
    readonly List<PhysicalBoundingBox> projectingArray = new List<PhysicalBoundingBox>();
    internal IContactSolveStrategy ContactStrategy { get; set; } = new CurrentAabbContactSolver();
    readonly List<EntityOffsetDecision> pendingOffsets = new List<EntityOffsetDecision>();

    // 接触力是有方向的：A 推 B 与 B 推 A 是两条不同的数据链路。
    // previous 用于在本帧扫描结束后找出退出的链路，并让对应动态力进入 fadeAway。
    HashSet<ContactForceLink> previousContactForceLinks = new HashSet<ContactForceLink>();//旧接触
    HashSet<ContactForceLink> currentContactForceLinks = new HashSet<ContactForceLink>(); // 本帧有向接触关系（每帧清空）

    // 同一实体同一挤出方向一帧只衰减一次；多个接触请求取最小保留率（最强阻挡）。
    readonly Dictionary<ContactAttenuationKey, float> pendingAttenuations = new Dictionary<ContactAttenuationKey, float>(); // 新接触速度衰减请求（每帧清空）
    readonly List<ContactForceDecision> pendingContactForces = new List<ContactForceDecision>(); // 本帧有向力参数，等待统一写入
    readonly List<DirectedContactPlan> pendingContactPlans = new List<DirectedContactPlan>(); // 当前策略的当帧有向力决定
    readonly List<HorizontalBlockDecision> pendingHorizontalBlocks = new List<HorizontalBlockDecision>(); // 沿水平接触传来的阻挡方向，等待写回
    readonly List<ContactForceLink> pendingExitedContactLinks = new List<ContactForceLink>(); // 本帧退出的旧有向关系

    public void Init()
    {
        // 第 4 相位：速度预测之后、位移应用之前
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(ContactForSolution, 4);
    }
    /// <summary>
    /// 添加投射（由实体 PositionPrediction 在相位 3 末调用）
    /// </summary>
    /// <param name="box"></param>
    public void AddPhyBoX(PhysicalBoundingBox box)
    {
        projectingArray.Add(box);
    }

    /// <summary>
    /// 相位 4 总入口，建议先沿调用顺序阅读，再展开各私有步骤。
    /// ① 预测 AABB 重叠按最小深度轴分配接触偏置；② 求环境请求并有限次回算剩余水平重叠；
    /// ③ 按最终请求形成接触效果决定；④ 清理受阻速度、刷新接触力、衰减新关系并处理退出。
    /// 相位 3 快照保持不变，当前请求可被重写；结束时清空上报列表，等待下一物理帧。
    /// </summary>
    void ContactForSolution()
    {
        currentContactForceLinks.Clear();
        pendingAttenuations.Clear();
        pendingContactForces.Clear();
        pendingHorizontalBlocks.Clear();
        pendingExitedContactLinks.Clear();
        pendingOffsets.Clear();
        //求解位移偏置量
        ContactStrategy.PlanInitialOffsets(projectingArray, pendingOffsets);
        //应用至实体位移偏置数据
        ApplyPendingOffsets();
        // F_6.2_7-3：先求环境可行位移，再把静态裁剪反馈给实体接触对。
        ResolveEnvironmentRequests();
        ResolveResidualHorizontalOverlaps();
        // 残余回算已结束。此时才把末箱的静墙方向传给上游箱子和玩家，供随后的施力登记读取。
        ContactStrategy.PlanPropagatedHorizontalBlocks(projectingArray, pendingHorizontalBlocks);
        ApplyPropagatedHorizontalBlocks();
        ApplyContactEffectsAfterFinalRequests();

        // 本帧预测快照用完即清；下一帧由各实体重新上报。
        projectingArray.Clear();
    }

    void ApplyPendingOffsets()
    {
        foreach (EntityOffsetDecision decision in pendingOffsets)
            decision.entity.AccumulateDisplacementOffset(decision.offset);
    }

    void ApplyPropagatedHorizontalBlocks()
    {
        foreach (HorizontalBlockDecision decision in pendingHorizontalBlocks)
            if (decision.entity != null)
                decision.entity.AdoptPropagatedHorizontalBlock(decision.direction);
    }

    /// <summary>
    /// 最终请求确定后，先收集可行有向力、新接触衰减及退出关系，再按原顺序写入实体效果。
    /// 相位 4 刷新效果参数；下一帧相位 3 才将接触力积分为运动。
    /// 不再改写本帧请求位移，也不提交刚体移动。
    /// </summary>
    void ApplyContactEffectsAfterFinalRequests()
    {
        CollectFeasibleContactForces();
        CollectExitedContactLinks();
        foreach (PhysicalBoundingBox prediction in projectingArray)
            if (prediction.myPhyBox != null)
                prediction.myPhyBox.ClearTransientSpeedForFinalBlock();
        // F_6.2_7-4：只有几何与环境都允许的接触，才登记下一帧的有向力。
        ApplyPendingContactForces();
        //调用实体方法衰减要求的被动速度
        ApplyPendingAttenuations();
        ReconcileContactForceLinks();
    }

    /// <summary>
    /// 逐实体查询“基础预测＋接触偏置”的环境约束，再合成为本轮请求位移。
    /// 首轮及每轮接触反馈结束后复用这个入口；只更新结果，不改变 Unity 世界位置。
    /// </summary>
    void ResolveEnvironmentRequests()
    {
        foreach (PhysicalBoundingBox prediction in projectingArray)
            if (prediction.myPhyBox != null)
            {
                prediction.myPhyBox.ProbeCandidateEnvironment(prediction);
                prediction.myPhyBox.FinalizeRequestedDisplacement();
            }
    }

    /// <summary>
    /// F_6.2_7-3：环境裁剪可能破坏首轮接触分离，用本轮请求终点再检查水平残余重叠。
    /// 朝挤出方向被静态障碍挡住的一侧不分担纠偏，其余实体按 EnvImpact 分担剩余量。
    /// 一轮中累加偏置，轮末统一重查环境；无新纠偏就停止，轮数由当前策略限制。
    /// 只补水平接触反馈，不保证任意多箱链在固定轮数内完全收敛。
    /// </summary>
    void ResolveResidualHorizontalOverlaps()
    {
        for (int pass = 0; pass < ContactStrategy.MaxFeedbackPasses; pass++)
        {
            pendingOffsets.Clear();
            ContactStrategy.PlanResidualOffsets(projectingArray, pendingOffsets);
            if (pendingOffsets.Count == 0) return;
            ApplyPendingOffsets();
            ResolveEnvironmentRequests();
        }
    }

    /// <summary>
    /// 收集可行有向力
    /// 最终请求确定后请策略判断有向接触是否可行，管理器只登记来源关系和当帧效果决定。
    /// </summary>
    void CollectFeasibleContactForces()
    {
        pendingContactPlans.Clear();
        ContactStrategy.PlanContactEffects(projectingArray, pendingContactPlans);
        foreach (DirectedContactPlan plan in pendingContactPlans)
        {
            ContactForceLink link = new ContactForceLink(plan.source, plan.receiver);
            currentContactForceLinks.Add(link);
            pendingContactForces.Add(new ContactForceDecision(link, plan.parameters));
            // 只在有向关系首次成立时登记衰减；持续接触保持原累计速度。
            if (!previousContactForceLinks.Contains(link))
                QueueContactAttenuation(plan.source, plan.sourceSeparateSign, plan.retainRatio);
        }
    }

    /// <summary>统一应用本帧已确定的有向力参数，来源键仍为施力实体。</summary>
    void ApplyPendingContactForces()
    {
        foreach (ContactForceDecision decision in pendingContactForces)
            decision.link.receiver.SetOrUpdateDynamicForce(decision.link.source, decision.parameters);
    }

    /// <summary>
    /// 汇总本帧新接触对施力者的瞬时速度衰减请求。
    /// key 使用实体 + 挤出方向，保证同一方向一帧只执行一次。
    /// </summary>
    void QueueContactAttenuation(BasicEntity entity, float separateSign, float retainRatio)
    {
        ContactAttenuationKey key = new ContactAttenuationKey(entity, separateSign);
        retainRatio = Mathf.Clamp01(retainRatio);
        if (pendingAttenuations.TryGetValue(key, out float oldRetainRatio))
            pendingAttenuations[key] = Mathf.Min(oldRetainRatio, retainRatio);
        else
            pendingAttenuations.Add(key, retainRatio);
    }
    /// <summary>
    /// 应用待处理施力物速度衰减
    /// </summary>
    void ApplyPendingAttenuations()
    {
        foreach (KeyValuePair<ContactAttenuationKey, float> pair in pendingAttenuations)
        {
            if (pair.Key.entity != null)
            {
                // 容器在相位 4 被修改；phyHSpeed 会在下一帧相位 3 重算时反映该变化。
                pair.Key.entity.AttenuateTransientContactSpeed(
                    pair.Key.separateSign,
                    pair.Value);
            }
        }
    }

    /// <summary>
    /// 对比上帧与本帧有向关系，先记录退出决定，不在收集时修改实体状态。
    /// </summary>
    void CollectExitedContactLinks()
    {
        foreach (ContactForceLink oldLink in previousContactForceLinks)
        {
            if (!currentContactForceLinks.Contains(oldLink) &&
                oldLink.source != null && oldLink.receiver != null)
                pendingExitedContactLinks.Add(oldLink);
        }
    }

    /// <summary>应用退出决定：来源级清理后转 fadeAway，再交换新旧关系集合。</summary>
    void ReconcileContactForceLinks()
    {
        foreach (ContactForceLink oldLink in pendingExitedContactLinks)
        {
            oldLink.receiver.ClearBlockedContactSpeed(oldLink.source);
            oldLink.receiver.RemoveForce(oldLink.source);
        }
        HashSet<ContactForceLink> temp = previousContactForceLinks;
        previousContactForceLinks = currentContactForceLinks;
        currentContactForceLinks = temp;
        currentContactForceLinks.Clear();
    }

    /// <summary>
    /// 本帧有向接触的待应用决定：保存来源关系与力参数。
    /// 它只属于相位 4 工作区；接收者的累计速度仍由 EntityEffectState 持有。
    /// </summary>
    readonly struct ContactForceDecision
    {
        public readonly ContactForceLink link;
        public readonly DynamicForceParameters parameters;

        public ContactForceDecision(ContactForceLink link, DynamicForceParameters parameters)
        {
            this.link = link;
            this.parameters = parameters;
        }
    }

    struct ContactForceLink : IEquatable<ContactForceLink>
    {
        public readonly BasicEntity source;
        public readonly BasicEntity receiver;
        readonly int sourceId;
        readonly int receiverId;

        public ContactForceLink(BasicEntity source, BasicEntity receiver)
        {
            this.source = source;
            this.receiver = receiver;
            sourceId = source != null ? source.GetInstanceID() : 0;
            receiverId = receiver != null ? receiver.GetInstanceID() : 0;
        }

        public bool Equals(ContactForceLink other)
        {
            return sourceId == other.sourceId && receiverId == other.receiverId;
        }

        public override bool Equals(object obj)
        {
            return obj is ContactForceLink && Equals((ContactForceLink)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (sourceId * 397) ^ receiverId;
            }
        }
    }

    struct ContactAttenuationKey : IEquatable<ContactAttenuationKey>
    {
        public readonly BasicEntity entity;
        public readonly float separateSign;
        readonly int entityId;
        readonly int direction;

        public ContactAttenuationKey(BasicEntity entity, float separateSign)
        {
            this.entity = entity;
            direction = separateSign >= 0f ? 1 : -1;
            this.separateSign = direction;
            entityId = entity != null ? entity.GetInstanceID() : 0;
        }

        public bool Equals(ContactAttenuationKey other)
        {
            return entityId == other.entityId && direction == other.direction;
        }

        public override bool Equals(object obj)
        {
            return obj is ContactAttenuationKey && Equals((ContactAttenuationKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (entityId * 397) ^ direction;
            }
        }
    }
}
