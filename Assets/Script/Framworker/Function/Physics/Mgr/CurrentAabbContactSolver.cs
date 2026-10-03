using System.Collections.Generic;
using PhyData;
using UnityEngine;

/// <summary>
/// 当前预测 AABB 接触策略：按最小重叠轴和 EnvImpact 计算双方偏置。
/// 职能仅是产生当帧决定；相位顺序与实体状态仍由 PhysicsSolverMgr 管理。
/// </summary>
internal sealed class CurrentAabbContactSolver : IContactSolveStrategy
{
    private const float ContactSlop = 0.002f;
    private const float MinEnvironmentSum = 0.0001f;
    private const float ClosingSpeedEpsilon = 0.01f;
    private const float ContactForceGain = 10f;

    public int MaxFeedbackPasses => 2;

    public void PlanInitialOffsets(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<EntityOffsetDecision> offsets)
    {
        for (int i = 0; i < predictions.Count; i++)
        {
            PhysicalBoundingBox a = predictions[i];
            for (int j = i + 1; j < predictions.Count; j++)
            {
                PhysicalBoundingBox b = predictions[j];
                if (a.myPhyBox == null || b.myPhyBox == null) continue;

                Vector2 delta = a.point - b.point;
                Vector2 overlap = a.size + b.size -
                    new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                if (overlap.x <= 0f || overlap.y <= 0f) continue;

                bool separateOnX = overlap.x <= overlap.y;
                float rawDepth = separateOnX ? overlap.x : overlap.y;
                float correctionDepth = Mathf.Max(rawDepth - ContactSlop, 0f);
                float envA = Mathf.Max(0f, a.myPhyBox.EnvImpact);
                float envB = Mathf.Max(0f, b.myPhyBox.EnvImpact);
                float envSum = Mathf.Max(envA + envB, MinEnvironmentSum);
                float weightA = envA / envSum;
                float weightB = envB / envSum;
                float sign = separateOnX
                    ? (delta.x >= 0f ? 1f : -1f)
                    : (delta.y >= 0f ? 1f : -1f);
                Vector2 offsetA = separateOnX
                    ? new Vector2(sign * correctionDepth * weightA, 0f)
                    : new Vector2(0f, sign * correctionDepth * weightA);
                Vector2 offsetB = separateOnX
                    ? new Vector2(-sign * correctionDepth * weightB, 0f)
                    : new Vector2(0f, -sign * correctionDepth * weightB);
                offsets.Add(new EntityOffsetDecision(a.myPhyBox, offsetA));
                offsets.Add(new EntityOffsetDecision(b.myPhyBox, offsetB));
            }
        }
    }

    public void PlanResidualOffsets(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<EntityOffsetDecision> offsets)
    {
        for (int i = 0; i < predictions.Count; i++)
        {
            PhysicalBoundingBox a = predictions[i];
            if (a.myPhyBox == null) continue;
            for (int j = i + 1; j < predictions.Count; j++)
            {
                PhysicalBoundingBox b = predictions[j];
                if (b.myPhyBox == null) continue;
                Vector2 centerA = a.point - a.motionFrame.plannedWorldDelta + a.myPhyBox.RequestedWorldDelta;
                Vector2 centerB = b.point - b.motionFrame.plannedWorldDelta + b.myPhyBox.RequestedWorldDelta;
                Vector2 delta = centerA - centerB;
                Vector2 overlap = a.size + b.size - new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                // 只有仍贴合且适合沿 X 分离的接触，才进入水平反馈。
                if (overlap.x <= ContactSlop || overlap.y <= 0f || overlap.x > overlap.y) continue;

                float signA = delta.x >= 0f ? 1f : -1f;
                float weightA = a.myPhyBox.BlockedHorizontalDirection == signA
                    ? 0f : Mathf.Max(0f, a.myPhyBox.EnvImpact);
                float weightB = b.myPhyBox.BlockedHorizontalDirection == -signA
                    ? 0f : Mathf.Max(0f, b.myPhyBox.EnvImpact);
                float sum = weightA + weightB;
                // 双方受阻时保留请求；最终接触检查会排除仍深度嵌入的施力。
                if (sum <= MinEnvironmentSum) continue;
                float correction = overlap.x - ContactSlop;
                offsets.Add(new EntityOffsetDecision(a.myPhyBox,
                    new Vector2(signA * correction * weightA / sum, 0f)));
                offsets.Add(new EntityOffsetDecision(b.myPhyBox,
                    new Vector2(-signA * correction * weightB / sum, 0f)));
            }
        }
    }

    public void PlanContactEffects(IReadOnlyList<PhysicalBoundingBox> predictions,
        List<DirectedContactPlan> effects)
    {
        for (int i = 0; i < predictions.Count; i++)
        {
            PhysicalBoundingBox a = predictions[i];
            if (a.myPhyBox == null) continue;
            for (int j = i + 1; j < predictions.Count; j++)
            {
                PhysicalBoundingBox b = predictions[j];
                if (b.myPhyBox == null) continue;
                Vector2 delta = a.point - b.point;
                Vector2 overlap = a.size + b.size - new Vector2(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                if (overlap.x <= 0f || overlap.y <= 0f) continue;
                bool separateOnX = overlap.x <= overlap.y;
                float envA = Mathf.Max(0f, a.myPhyBox.EnvImpact);
                float envB = Mathf.Max(0f, b.myPhyBox.EnvImpact);
                float envSum = Mathf.Max(envA + envB, MinEnvironmentSum);
#if UNITY_EDITOR
                float rawDepth = separateOnX ? overlap.x : overlap.y;
                float correctionDepth = Mathf.Max(rawDepth - ContactSlop, 0f);
                float weightA = envA / envSum;
                float weightB = envB / envSum;
                float sign = separateOnX ? (delta.x >= 0f ? 1f : -1f) : (delta.y >= 0f ? 1f : -1f);
                Vector2 offsetA = separateOnX ? new Vector2(sign * correctionDepth * weightA, 0f)
                    : new Vector2(0f, sign * correctionDepth * weightA);
                Vector2 offsetB = separateOnX ? new Vector2(-sign * correctionDepth * weightB, 0f)
                    : new Vector2(0f, -sign * correctionDepth * weightB);
#endif

                Vector2 finalA = a.point - a.motionFrame.plannedWorldDelta + a.myPhyBox.RequestedWorldDelta;
                Vector2 finalB = b.point - b.motionFrame.plannedWorldDelta + b.myPhyBox.RequestedWorldDelta;
                Vector2 finalDelta = finalA - finalB;
                Vector2 finalOverlap = a.size + b.size - new Vector2(Mathf.Abs(finalDelta.x), Mathf.Abs(finalDelta.y));
                ContactSpeedResolution speedResolution = default;
                // 未解开的深度嵌入不能继续登记下一帧推力。
                if (separateOnX && finalOverlap.x >= -ContactSlop &&
                    finalOverlap.x <= ContactSlop + 0.001f && finalOverlap.y > 0f)
                    speedResolution = PlanHorizontalContactForce(a, b, delta, envSum, effects);

#if UNITY_EDITOR
                PhysicsEditorObservationBridge.ReportContactPair(
                    a.myPhyBox, b.myPhyBox, a.point, b.point, delta, separateOnX,
                    rawDepth, correctionDepth, speedResolution.closingSpeed,
                    speedResolution.aAppliedForce, speedResolution.bAppliedForce,
                    speedResolution.targetSpeedAToB, speedResolution.targetSpeedBToA,
                    weightA, weightB, offsetA, offsetB);
#endif
            }
        }
    }

    private ContactSpeedResolution PlanHorizontalContactForce(
        PhysicalBoundingBox a, PhysicalBoundingBox b, Vector2 centerDelta,
        float envSum, List<DirectedContactPlan> effects)
    {
        ContactSpeedResolution result = default;
        BasicEntity entityA = a.myPhyBox;
        BasicEntity entityB = b.myPhyBox;
        float velocityA = a.motionFrame.motionVelocity.x;
        float velocityB = b.motionFrame.motionVelocity.x;
        float separateSignA = centerDelta.x >= 0f ? 1f : -1f;
        float separateSignB = -separateSignA;
        result.closingSpeed = -(velocityA - velocityB) * separateSignA;
        // 相对速度未闭合时，贴合或同速同行都不产生新推力。
        if (result.closingSpeed <= ClosingSpeedEpsilon) return result;

        if (velocityA * separateSignA < -ClosingSpeedEpsilon &&
            entityA.BlockedHorizontalDirection != -separateSignA &&
            entityB.BlockedHorizontalDirection != -separateSignA)
        {
            result.targetSpeedAToB = AddDirectedPlan(entityA, entityB, velocityA,
                separateSignA, envSum, effects);
            result.aAppliedForce = true;
        }
        if (velocityB * separateSignB < -ClosingSpeedEpsilon &&
            entityB.BlockedHorizontalDirection != -separateSignB &&
            entityA.BlockedHorizontalDirection != -separateSignB)
        {
            result.targetSpeedBToA = AddDirectedPlan(entityB, entityA, velocityB,
                separateSignB, envSum, effects);
            result.bAppliedForce = true;
        }
        return result;
    }

    private static float AddDirectedPlan(BasicEntity source, BasicEntity receiver,
        float sourceAxisSpeed, float sourceSeparateSign, float envSum,
        List<DirectedContactPlan> effects)
    {
        float receiverWeight = Mathf.Clamp01(Mathf.Max(0f, receiver.EnvImpact) / envSum);
        float targetSpeed = sourceAxisSpeed * receiverWeight;
        effects.Add(new DirectedContactPlan(source, receiver,
            new DynamicForceParameters(targetSpeed * ContactForceGain, Mathf.Abs(targetSpeed), 0f),
            sourceSeparateSign, receiverWeight));
        return targetSpeed;
    }

    private struct ContactSpeedResolution
    {
        internal float closingSpeed;
        internal bool aAppliedForce;
        internal bool bAppliedForce;
        internal float targetSpeedAToB;
        internal float targetSpeedBToA;
    }
}
