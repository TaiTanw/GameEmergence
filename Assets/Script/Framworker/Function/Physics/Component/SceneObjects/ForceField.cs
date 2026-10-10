using PhyData;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 实体进入力场区域时，用 add / maxSpeed 给出动态力初值；后续每帧沿用这组参数。
/// 职能：具体区域算法提供者；区域重叠由实体发现，本类只提供参数。
/// </summary>
public class ForceField : BasicPhysicalObject, IDynamicEnvironmentForce, IMovementSpeedModifier
{
    [SerializeField] protected float speedChangeNum; // 主动速度加性修饰，旧序列化名称保留。
    public float MovementSpeedOffset => TryGetMovementSpeedOffset(out float value) ? value : 0f;
    protected override void AddLegacyEnvironmentParameters(List<EnvironmentParameter> parameters) =>
        AddLegacyEnvironmentParameter(parameters, new MovementSpeedOffsetParameter(speedChangeNum));

    [SerializeField] protected float maxSpeed;
    [SerializeField] protected float add;

    public ForceData CreateEnvironmentForce(IForceAction receiver)
    {
        ForceData force = new ForceData();
        force.Init(add, 0, maxSpeed);
        return force;
    }

    public void ForceCalculation(IForceAction receiver)
    {
        // 固定施力参数沿用进入时的值；不在每帧重新初始化实体的累计速度。
    }
}
