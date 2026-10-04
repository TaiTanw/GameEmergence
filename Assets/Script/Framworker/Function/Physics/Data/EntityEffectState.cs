using System.Collections.Generic;
using PhyData;
using UnityEngine;

/// <summary>
/// 实体自身的效果数值状态：保存移速修饰及各类附加速度，并按原时序汇总。
/// 职能：持有实体效果的数值容器；来源接入关系仍由 EntityEnvironmentContext 管理。
/// </summary>
internal sealed class EntityEffectState
{
    private readonly Dictionary<EnvironmentRegistration, float> movementModifiers =
        new Dictionary<EnvironmentRegistration, float>();
    private readonly Dictionary<EnvironmentRegistration, Vector2> stateVelocities =
        new Dictionary<EnvironmentRegistration, Vector2>();
    private readonly List<SpeedStackData> timedSpeeds = new List<SpeedStackData>();
    private readonly HashSet<IDynamicAddForce> dynamicSources = new HashSet<IDynamicAddForce>();
    private readonly Dictionary<IDynamicAddForce, ForceData> dynamicForces =
        new Dictionary<IDynamicAddForce, ForceData>();
    private readonly List<IDynamicAddForce> dynamicRemovalBuffer = new List<IDynamicAddForce>();
    private IForceAction receiver;
    private bool isRecalculate;

#if UNITY_EDITOR
    public int DebugMovementModifierCount => movementModifiers.Count;
    public int DebugStateVelocityCount => stateVelocities.Count;
    public int DebugDynamicForceCount => dynamicForces.Count;
#endif

    public void BindReceiver(IForceAction value) => receiver = value;

    public void RegisterMovementModifier(EnvironmentRegistration source, float offset)
    {
        movementModifiers[source] = offset;
        isRecalculate = true;
    }

    public void RemoveMovementModifier(EnvironmentRegistration source)
    {
        movementModifiers.Remove(source);
        isRecalculate = true;
    }

    public void UpdateMovementModifier(PlayerPhysicsData movement)
    {
        // 数值规则：来源发生登记或撤销时，才更新跨帧保留的倍率。
        if (!isRecalculate) return;

        float sum = 0f;
        foreach (float offset in movementModifiers.Values)
            sum += offset;

        movement.nowPhyNum = Mathf.Clamp(sum, -0.95f, 4f);
        isRecalculate = false;
    }

    public void AddStateVelocity(EnvironmentRegistration source, Vector2 velocity)
    {
        stateVelocities[source] = velocity;
    }

    public void RemoveStateVelocity(EnvironmentRegistration source)
    {
        stateVelocities.Remove(source);
    }

    private void AccumulateStateVelocity(ref Vector2 speed)
    {
        // 持续速度每帧逐来源相加，不受质量影响，也不使用移速修饰的脏标识。
        foreach (Vector2 velocity in stateVelocities.Values)
            speed += velocity;
    }

    public void AddTimedSpeed(float duration, float speed, float envImpact)
    {
        timedSpeeds.Add(new SpeedStackData
        {
            responseTimer1 = Time.time + duration,
            hspeed = speed * envImpact
        });
    }

    private void AccumulateTimedSpeed(ref Vector2 speed)
    {
        // 反向遍历并换位删除已过期项，保持原有时间判断和累加顺序。
        for (int i = timedSpeeds.Count - 1; i >= 0; i--)
        {
            if (timedSpeeds[i].responseTimer1 < Time.time)
            {
                int last = timedSpeeds.Count - 1;
                if (i != last) timedSpeeds[i] = timedSpeeds[last];
                timedSpeeds.RemoveAt(last);
            }
            else speed.x += timedSpeeds[i].hspeed;
        }
    }

    private void AttenuateTimedSpeed(float separateSign, float retainRatio)
    {
        for (int i = 0; i < timedSpeeds.Count; i++)
        {
            SpeedStackData data = timedSpeeds[i];
            if (data.hspeed * separateSign < 0f)
            {
                data.hspeed *= retainRatio;
                timedSpeeds[i] = data;
            }
        }
    }

    public void AttenuateTransientContactSpeed(float separateSign, float retainRatio)
    {
        if (Mathf.Approximately(separateSign, 0f)) return;

        separateSign = separateSign >= 0f ? 1f : -1f;
        retainRatio = Mathf.Clamp01(retainRatio);
        AttenuateTimedSpeed(separateSign, retainRatio);

        // 只有退出后仍在渐隐的动态速度参与本次碰撞消耗。
        foreach (IDynamicAddForce source in dynamicSources)
        {
            ForceData data = dynamicForces[source];
            if (data.type == E_PhyForceType.fadeAway &&
                data.speedStacking * separateSign < 0f)
            {
                data.speedStacking *= retainRatio;
                dynamicForces[source] = data;
            }
        }
    }

    public void ClearTransientSpeedAtWall()
    {
        timedSpeeds.Clear();
        // 静墙清空累计速度，不撤销动态来源及其控制状态。
        foreach (IDynamicAddForce source in dynamicSources)
        {
            ForceData data = dynamicForces[source];
            data.speedStacking = 0;
            dynamicForces[source] = data;
        }
    }

    public void AddDynamicForce(IDynamicAddForce source, ForceData force)
    {
        // 再次进入时只恢复施加状态，保留旧参数与已累计速度。
        if (dynamicSources.Contains(source))
        {
            ChangeDynamicType(source, E_PhyForceType.apply);
            return;
        }
        dynamicSources.Add(source);
        dynamicForces[source] = force;
    }

    public void SetOrUpdateDynamicForce(IDynamicAddForce source, DynamicForceParameters parameters)
    {
        if (dynamicForces.TryGetValue(source, out ForceData data))
        {
            data.parameters = parameters;
            data.type = E_PhyForceType.apply;
            dynamicForces[source] = data;
            dynamicSources.Add(source);
            return;
        }

        ForceData newData = new ForceData { parameters = parameters, type = E_PhyForceType.apply };
        dynamicForces[source] = newData;
        dynamicSources.Add(source);
    }

    public void ChangeDynamicForce(IDynamicAddForce source, float force)
    {
        ForceData data = dynamicForces[source];
        data.parameters = new DynamicForceParameters(force, data.balanceSpeed, data.recoverySpeed);
        dynamicForces[source] = data;
    }

    public void ChangeDynamicType(IDynamicAddForce source, E_PhyForceType type)
    {
        ForceData data = dynamicForces[source];
        data.type = type;
        dynamicForces[source] = data;
    }

    public void RemoveDynamicForce(IDynamicAddForce source)
    {
        // 已被积分循环移除时不再切换类型。
        if (!dynamicForces.ContainsKey(source)) return;
        ChangeDynamicType(source, E_PhyForceType.fadeAway);
    }

    /// <summary>
    /// F_6.2_7-4：旧接触退出时，只清除指定来源朝最终静态障碍的累计速度。
    /// 判断该来源自身的 speedStacking，不依赖其它来源合成后的净被动速度；保留其余来源。
    /// 这里只清速度，退出类型仍由随后 RemoveDynamicForce 切换为 fadeAway。
    /// </summary>
    public void ClearBlockedContactSpeed(IDynamicAddForce source, float blockedDirection)
    {
        // 无静态阻挡、来源不存在或累计速度不朝障碍时，不需要清理这条来源。
        if (blockedDirection == 0f || !dynamicForces.TryGetValue(source, out ForceData data) ||
            data.speedStacking * blockedDirection <= 0f) return;
        data.speedStacking = 0f;
        dynamicForces[source] = data;
    }

    public void UpdatePassiveSpeed(PlayerPhysicsData movement, float envImpact, float resistance,
        float blockedHorizontalDirection = 0f)
    {
        Vector2 speed = Vector2.zero;
        AccumulateTimedSpeed(ref speed);
        AccumulateDynamicSpeed(ref speed, envImpact, resistance, blockedHorizontalDirection);
        AccumulateStateVelocity(ref speed);
        movement.phyHSpeed = speed.x;
        movement.phyVSpeed = speed.y;
    }

    private void AccumulateDynamicSpeed(ref Vector2 speed, float envImpact, float resistance,
        float blockedHorizontalDirection)
    {
        dynamicRemovalBuffer.Clear();
        if (dynamicSources.Count == 0) return;

        foreach (IDynamicAddForce source in dynamicSources)
        {
            ForceData data = dynamicForces[source];
            // 渐隐项不再回调来源；其它状态的回调可能改写参数，随后必须重读结构体。
            if (data.type != E_PhyForceType.fadeAway)
            {
                source.ForceCalculation(receiver);
                data = dynamicForces[source];
            }

            switch (data.type)
            {
                case E_PhyForceType.apply:
                    data.FixUpdate(envImpact);
                    // 实际逻辑：这里读到的是上一帧相位 4 的阻挡方向。力与它同号时，本帧不把 apply 加进被动速度。
                    if (blockedHorizontalDirection != 0f && data.Force * blockedHorizontalDirection > 0f)
                        data.speedStacking = 0f;
                    else
                        speed.x += data.speedStacking;
                    dynamicForces[source] = data;
                    break;
                case E_PhyForceType.controlRecovery:
                    speed.x += data.ControlledSpeedRecovery(envImpact);
                    dynamicForces[source] = data;
                    break;
                case E_PhyForceType.balance:
                    break;
                case E_PhyForceType.fadeAway:
                    if (data.speedStacking > 0f)
                    {
                        data.speedStacking -= resistance * envImpact * Time.fixedDeltaTime;
                        data.speedStacking = Mathf.Clamp(data.speedStacking, 0f, data.speedStacking);
                    }
                    else if (data.speedStacking < 0f)
                    {
                        data.speedStacking += resistance * envImpact * Time.fixedDeltaTime;
                        data.speedStacking = Mathf.Clamp(data.speedStacking, data.speedStacking, 0f);
                    }

                    if (Mathf.Abs(data.speedStacking) < 0.2)
                    {
                        dynamicRemovalBuffer.Add(source);
                        dynamicForces.Remove(source);
                    }
                    else
                    {
                        speed.x += data.speedStacking;
                        dynamicForces[source] = data;
                    }
                    break;
            }
        }

        foreach (IDynamicAddForce source in dynamicRemovalBuffer)
            dynamicSources.Remove(source);
        dynamicRemovalBuffer.Clear();
    }
}
