using System.Collections.Generic;
using PhyData;
using UnityEngine;

/// <summary>环境来源的参数与限时命令；动量归 Rigidbody2D，不保留实体接触力或速度尾效。</summary>
internal sealed class EntityEffectState
{
    private readonly Dictionary<EnvironmentRegistration, float> movementModifiers = new Dictionary<EnvironmentRegistration, float>();
    private readonly Dictionary<EnvironmentRegistration, Vector2> stateVelocities = new Dictionary<EnvironmentRegistration, Vector2>();
    private readonly List<SpeedStackData> timedSpeeds = new List<SpeedStackData>();
    private readonly Dictionary<IDynamicAddForce, ForceData> dynamicForces = new Dictionary<IDynamicAddForce, ForceData>();
    private readonly List<IDynamicAddForce> forceSources = new List<IDynamicAddForce>();
    private IForceAction receiver;
    private bool isRecalculate;
    public bool HasDynamicForces => dynamicForces.Count > 0;
#if UNITY_EDITOR
    public int DebugMovementModifierCount => movementModifiers.Count;
    public int DebugStateVelocityCount => stateVelocities.Count;
    public int DebugDynamicForceCount => dynamicForces.Count;
#endif
    public void BindReceiver(IForceAction value) => receiver = value;
    public void ResetTransientMotion() { timedSpeeds.Clear(); isRecalculate = true; }
    public void RegisterMovementModifier(EnvironmentRegistration source, float offset) { movementModifiers[source] = offset; isRecalculate = true; }
    public void RemoveMovementModifier(EnvironmentRegistration source) { movementModifiers.Remove(source); isRecalculate = true; }
    public void UpdateMovementModifier(PlayerPhysicsData movement)
    {
        if (!isRecalculate) return;
        float sum = 0f;
        foreach (float offset in movementModifiers.Values) sum += offset;
        movement.nowPhyNum = Mathf.Clamp(sum, -0.95f, 4f);
        isRecalculate = false;
    }
    public void AddStateVelocity(EnvironmentRegistration source, Vector2 velocity) => stateVelocities[source] = velocity;
    public void RemoveStateVelocity(EnvironmentRegistration source) => stateVelocities.Remove(source);
    public void AddTimedSpeed(float duration, float speed, float envImpact) => timedSpeeds.Add(new SpeedStackData
        { responseTimer1 = Time.time + duration, hspeed = speed * envImpact });
    public void UpdatePassiveSpeed(PlayerPhysicsData movement)
    {
        Vector2 speed = Vector2.zero;
        foreach (Vector2 velocity in stateVelocities.Values) speed += velocity;
        for (int i = timedSpeeds.Count - 1; i >= 0; i--)
        {
            if (timedSpeeds[i].responseTimer1 < Time.time) timedSpeeds.RemoveAt(i);
            else speed.x += timedSpeeds[i].hspeed;
        }
        movement.phyHSpeed = speed.x;
        movement.phyVSpeed = speed.y;
    }
    public void AddDynamicForce(IDynamicAddForce source, ForceData force) => dynamicForces[source] = force;
    public void ChangeDynamicForce(IDynamicAddForce source, float force)
    {
        if (!dynamicForces.TryGetValue(source, out ForceData data)) return;
        data.parameters = new DynamicForceParameters(force, data.balanceSpeed, data.recoverySpeed);
        dynamicForces[source] = data;
    }
    public void ChangeDynamicType(IDynamicAddForce source, E_PhyForceType type)
    {
        if (!dynamicForces.TryGetValue(source, out ForceData data)) return;
        data.type = type;
        dynamicForces[source] = data;
    }
    public void RemoveDynamicForce(IDynamicAddForce source) => dynamicForces.Remove(source);

    /// <summary>每个动态来源只改变本步真实速度；碰撞消掉的动量不在另一个容器积累。</summary>
    public float CalculateDynamicVelocityDelta(float actual, float envImpact, float dt)
    {
        float speed = actual;
        forceSources.Clear();
        forceSources.AddRange(dynamicForces.Keys);
        foreach (IDynamicAddForce source in forceSources)
        {
            source.ForceCalculation(receiver);
            if (!dynamicForces.TryGetValue(source, out ForceData data)) continue;
            if (data.type == E_PhyForceType.controlRecovery)
                speed = Mathf.MoveTowards(speed, 0f, Mathf.Max(0f, data.recoverySpeed * envImpact) * dt);
            else if (data.type == E_PhyForceType.apply)
            {
                float change = data.Force * envImpact * dt;
                // 已超过该源限速时不骤然清掉其它原生动量；它仍能沿反方向减速。
                if (change > 0f && speed < data.balanceSpeed) speed = Mathf.Min(data.balanceSpeed, speed + change);
                else if (change < 0f && speed > -data.balanceSpeed) speed = Mathf.Max(-data.balanceSpeed, speed + change);
            }
        }
        return speed - actual;
    }
}
