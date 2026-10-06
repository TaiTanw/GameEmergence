using System;
using System.Collections.Generic;

/// <summary>
/// 职能：最小阶段死亡消费者，只协调明确角色引用和已接入的系统。
/// 探索死亡请求统一改飞行，逃生请求完整失败刷新；依赖未接入或未提交成功时保持 Pending。
/// </summary>
public sealed class PlayerLifecycleRules : IPlayerDeathReceiver
{
    private readonly Player body;
    private readonly Player spirit;
    private readonly Dictionary<Player, uint> handledCycles = new Dictionary<Player, uint>();
    private IPlayerRunRefreshRules refreshRules;
    public PlayerGameplayStage Stage { get; private set; }
    public bool IsSimulation { get; private set; }

    public PlayerLifecycleRules(Player body, Player spirit, IPlayerRunRefreshRules refreshRules = null)
    {
        // 组装约束：错误身份不能用名字或当前控制权补成正确角色。
        if (body != null && body.Identity != PlayerIdentity.Body) throw new ArgumentException("本体引用必须是 Body。");
        if (spirit != null && spirit.Identity != PlayerIdentity.Spirit) throw new ArgumentException("精神体引用必须是 Spirit。");
        this.body = body;
        this.spirit = spirit;
        this.refreshRules = refreshRules;
    }

    public void ConfigureStage(PlayerGameplayStage stage, bool simulation = false)
    {
        Stage = stage;
        IsSimulation = stage == PlayerGameplayStage.Exploration && simulation;
    }

    public void BindRefreshRules(IPlayerRunRefreshRules rules) => refreshRules = rules;

    public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request)
    {
        Player subject = request.subject;
        // 安全：不存在的角色或没有周期身份的请求不执行结果。
        if (subject == null || request.cycle == 0) return PlayerDeathStatus.Rejected;
        // 去重：已经完成的旧周期不因重放或阶段切换再次扣费/刷新。
        if (handledCycles.TryGetValue(subject, out uint handled) && handled >= request.cycle)
            return PlayerDeathStatus.Handled;
        if (subject.PendingDeath?.cycle != request.cycle) return PlayerDeathStatus.Rejected;
        bool completed;
        if (Stage == PlayerGameplayStage.Exploration)
        {
            // 探索规则：只处理明确的精神体，不改变模拟开关或认知格子。
            if (subject != spirit) return PlayerDeathStatus.Rejected;
            completed = subject.RequestSpiritForm(true, PlayerFormChangeReason.Death);
        }
        else if (Stage == PlayerGameplayStage.Escape)
        {
            if (subject != body) return PlayerDeathStatus.Rejected;
            completed = refreshRules != null && refreshRules.TryRestartExploration(subject);
        }
        else
            // 教学死亡结果尚无已确认规则，不能借探索/逃生分支猜测。
            return PlayerDeathStatus.Pending;
        if (!completed) return PlayerDeathStatus.Pending;
        handledCycles[subject] = request.cycle;
        if (Stage == PlayerGameplayStage.Escape) ConfigureStage(PlayerGameplayStage.Exploration);
        return PlayerDeathStatus.Handled;
    }
}
