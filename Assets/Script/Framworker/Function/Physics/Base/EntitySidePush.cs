using System.Collections.Generic;
using UnityEngine;

/// <summary>相位 3 留下的本步计划。还没有考虑实体之间。</summary>
readonly struct EntityMotionFact
{
    public readonly BasicEntity Entity;
    public readonly Vector2 PlannedVelocity;

    public EntityMotionFact(BasicEntity entity, Vector2 plannedVelocity)
    {
        Entity = entity;
        PlannedVelocity = plannedVelocity;
    }
}

/// <summary>实体侧面推动之后要写回的速度。不是位置偏置。</summary>
struct EntityPushResult
{
    public Vector2 Velocity;
}

/// <summary>
/// 模拟前写一次水平速度：人顶到箱子侧面时，箱子得到这一步的水平速度，人收到不穿入的距离。
/// 只处理水平分离；站在箱顶不进入。被水平关节锁住的箱子不改写。只写接触到的第一只，长链交给随后的一次模拟。
/// </summary>
static class EntitySidePush
{
    const float Skin = 0.001f;
    const float ClosingEpsilon = 0.01f;
    static readonly List<BasicEntity> steps = new List<BasicEntity>();
    static readonly List<RaycastHit2D> hits = new List<RaycastHit2D>();
    static MonoPublicMgr registeredManager;

    public static void EnsureRegistered()
    {
        MonoPublicMgr manager = MonoPublicMgr.Instance;
        // 实际逻辑：同一调度器只订阅一次；重进播放后跟随新调度器重新订阅。
        if (manager == registeredManager) return;
        // 安全：旧调度器尚未销毁时先移除订阅。
        if (registeredManager != null) registeredManager.RemovePhysicalTimingUpdate(Apply, 4);
        steps.Clear();
        hits.Clear();
        manager.AddPhysicalTimingUpdate(Apply, 4);
        registeredManager = manager;
    }

    public static void Report(BasicEntity entity)
    {
        // 安全：空实体没有本步计划。
        if (entity == null || steps.Contains(entity)) return;
        steps.Add(entity);
    }

    public static void Forget(BasicEntity entity) => steps.Remove(entity);

    static void Apply()
    {
        PushOnce();
        steps.Clear();
    }

    static void PushOnce()
    {
        for (int i = 0; i < steps.Count; i++)
        {
            BasicEntity raw = steps[i];
            // 安全：中途停用的实体不再推动。
            if (raw == null || !raw.HasPendingStep || !raw.DrivesHorizontalStep) continue;
            EntityMotionFact driver = new EntityMotionFact(raw, raw.PlannedStepVelocity);
            float intent = driver.PlannedVelocity.x;
            float dt = Time.fixedDeltaTime;
            // 实际逻辑：用本步计划速度找侧面，不用模拟后的位置。
            if (dt <= 0f || !driver.Entity.TrySweepSideEntity(intent, dt, hits, out BasicEntity other, out float gap))
                continue;
            if (!other.IsMotionParticipating) continue;
            // 实际逻辑：操作已把这一对交给水平关节时，不改写双方速度。
            if (driver.Entity.HoldsHorizontalLock(other)) continue;
            EntityMotionFact receiver = new EntityMotionFact(other, other.PlannedStepVelocity);
            float otherVx = receiver.PlannedVelocity.x;
            bool closing = intent > 0f ? intent > otherVx + ClosingEpsilon : intent < otherVx - ClosingEpsilon;
            if (!closing) continue;
            // 实际逻辑：先留出本步闭合间隙的位移，余量才用于推动；不能隔空把箱子提前加到同速。
            float slack = Mathf.Max(0f, gap - Skin);
            float pushVx = intent - Mathf.Sign(intent) * slack / dt;
            // 实际逻辑：本步追不上已有运动的箱子时，让双方沿原计划接近，不提前减速箱子。
            bool reaches = intent > 0f ? pushVx > otherVx + ClosingEpsilon : pushVx < otherVx - ClosingEpsilon;
            if (!reaches) continue;
            float allowedOther = receiver.Entity.WallLimitedHorizontal(pushVx);
            // 实际逻辑：只把箱子加速到推动方向，不把它反向拉回。
            allowedOther = intent > 0f
                ? Mathf.Clamp(allowedOther, 0f, intent)
                : Mathf.Clamp(allowedOther, intent, 0f);
            EntityPushResult pushed = new EntityPushResult { Velocity = receiver.PlannedVelocity };
            pushed.Velocity.x = allowedOther;
            receiver.Entity.CommitHorizontal(pushed.Velocity.x);
            // 实际逻辑：箱子让出的距离只沿推动方向加。向左推时速度为负，不能再取反把人弹回右边。
            float boxDelta = allowedOther * dt;
            float allowedDriver = intent > 0f
                ? Mathf.Min(intent, (slack + Mathf.Max(0f, boxDelta)) / dt)
                : Mathf.Max(intent, (-slack + Mathf.Min(0f, boxDelta)) / dt);
            allowedDriver = intent > 0f
                ? Mathf.Clamp(allowedDriver, 0f, intent)
                : Mathf.Clamp(allowedDriver, intent, 0f);
            EntityPushResult driverResult = new EntityPushResult { Velocity = driver.PlannedVelocity };
            driverResult.Velocity.x = allowedDriver;
            driver.Entity.CommitHorizontal(driverResult.Velocity.x);
        }
    }
}
