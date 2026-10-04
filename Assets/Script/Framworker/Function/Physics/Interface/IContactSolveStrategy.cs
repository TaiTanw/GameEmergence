using System.Collections.Generic;
using PhyData;
using UnityEngine;

/// <summary>
/// 相位 4 接触计算的整帧入口；当前实现仍读取预测框中的 BasicEntity，
/// 但不负责调度相位、查询环境、写入实体或提交刚体位置。
/// </summary>
internal interface IContactSolveStrategy
{
    int MaxFeedbackPasses { get; }
    void PlanInitialOffsets(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<EntityOffsetDecision> offsets);
    void PlanResidualOffsets(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<EntityOffsetDecision> offsets);
    void PlanContactEffects(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<DirectedContactPlan> effects);
    void PlanPropagatedHorizontalBlocks(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<HorizontalBlockDecision> blocks);
}

/// <summary>
/// 相位 4 残余回算之后，准备写回某个实体的水平阻挡方向。
/// 只描述「谁要带上哪一个方向」；直接静墙裁剪仍由环境请求写入，这里不覆盖已有方向。
/// 职能：当前接触策略的当帧决定，不持有跨帧速度。
/// </summary>
internal readonly struct HorizontalBlockDecision
{
    internal readonly BasicEntity entity;
    internal readonly float direction;

    internal HorizontalBlockDecision(BasicEntity entity, float direction)
    {
        this.entity = entity;
        this.direction = direction;
    }
}

/// <summary>本帧有向接触的力参数和新接触衰减比例；不保存接收者的累计速度。</summary>
internal readonly struct DirectedContactPlan
{
    internal readonly BasicEntity source;
    internal readonly BasicEntity receiver;
    internal readonly DynamicForceParameters parameters;
    internal readonly float sourceSeparateSign;
    internal readonly float retainRatio;

    internal DirectedContactPlan(BasicEntity source, BasicEntity receiver,
        DynamicForceParameters parameters, float sourceSeparateSign, float retainRatio)
    {
        this.source = source;
        this.receiver = receiver;
        this.parameters = parameters;
        this.sourceSeparateSign = sourceSeparateSign;
        this.retainRatio = retainRatio;
    }
}

/// <summary>相位 4 待应用的实体偏置；由管理器按策略给出的顺序写回实体。</summary>
internal readonly struct EntityOffsetDecision
{
    internal readonly BasicEntity entity;
    internal readonly Vector2 offset;

    internal EntityOffsetDecision(BasicEntity entity, Vector2 offset)
    {
        this.entity = entity;
        this.offset = offset;
    }
}
