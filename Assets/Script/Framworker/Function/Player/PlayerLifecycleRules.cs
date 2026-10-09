using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 职能：角色阶段、漫游／模拟和死亡的唯一规则入口；场景宿主只组装和调用。
/// 形态执行成功才改变模式和世界许可，模式切换保留现场，不走全量角色刷新。
/// </summary>
public sealed class PlayerLifecycleRules : IPlayerDeathReceiver, IPlayerFormRules
{
    private readonly Player body;
    private readonly Player spirit;
    private readonly Dictionary<Player, uint> handledCycles = new Dictionary<Player, uint>();
    private IPlayerRunRefreshRules refreshRules;
    private float manualCooldown;
    private float nextManualChange;
    private readonly float formCost;
    public LevelCognition Cognition { get; }
    public PlayerGameplayStage Stage { get; private set; }
    public bool IsSimulation { get; private set; }
    /// <summary>只读剩余手动转换冷却；沿用规则的非缩放时间，不另建计时器。</summary>
    public float ManualFormCooldownRemaining => Mathf.Max(0f, nextManualChange - Time.unscaledTime);
    /// <summary>P8 后续规则接入这一已扣费结果；初始化、拒绝和相同形态请求不发出，也不重复结算。</summary>
    public event Action<PlayerFormChangeReason> FormCommitted;

    public PlayerLifecycleRules(Player body, Player spirit, IPlayerRunRefreshRules refreshRules = null,
        LevelCognition cognition = null, float formCost = 1f)
    {
        // 组装约束：错误身份不能用名字或当前控制权补成正确角色。
        if (body != null && body.Identity != PlayerIdentity.Body) throw new ArgumentException("本体引用必须是 Body。");
        if (spirit != null && spirit.Identity != PlayerIdentity.Spirit) throw new ArgumentException("精神体引用必须是 Spirit。");
        this.body = body;
        this.spirit = spirit;
        this.refreshRules = refreshRules;
        Cognition = cognition ?? new LevelCognition(100f);
        this.formCost = float.IsNaN(formCost) || float.IsInfinity(formCost) ? 0f : Mathf.Max(0f, formCost);
    }

    public void ConfigureStage(PlayerGameplayStage stage, bool simulation = false)
    {
        Stage = stage;
        IsSimulation = stage == PlayerGameplayStage.Exploration && simulation;
    }

    public void BindRefreshRules(IPlayerRunRefreshRules rules) => refreshRules = rules;

    /// <summary>人工阅读入口：P6 组装同一精神体后，从这里开始漫游；初始化不收取转换费用。</summary>
    public bool BeginExploration(float formCooldown = 0.5f)
    {
        // 安全：教学或失败的角色组装不能进入精神体模式。
        if (spirit == null || spirit.Identity != PlayerIdentity.Spirit) return false;
        manualCooldown = Mathf.Max(0f, formCooldown);
        nextManualChange = 0f;
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Simulation);
        spirit.BindSpiritRules(this);
        if (!spirit.TryCommitSpiritForm(true)) return false;
        body?.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        ConfigureStage(PlayerGameplayStage.Exploration);
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Roaming);
        SetWorldPaused(true);
        return true;
    }

    /// <summary>
    /// 人工阅读链路：模式请求 → 执行层一次占用检查 → 形态／权限／世界同时提交。
    /// 失败保留原模式；死亡和区域强制跳过手动冷却，成功均从本关认知余额扣费一次。
    /// </summary>
    public bool TryChangeForm(Player subject, bool flying, PlayerFormChangeReason reason)
    {
        // 安全：只有本关探索中的精神体有这条入口。
        if (subject == null || subject != spirit || Stage != PlayerGameplayStage.Exploration) return false;
        // 实际逻辑：相同模式不再执行碰撞校验、冷却或重复结算。
        if (IsSimulation == !flying && (subject.ResolveFourWayAbility() == FourWayMode.Flying) == flying)
            return true;
        if (reason == PlayerFormChangeReason.Manual && Time.unscaledTime < nextManualChange)
        {
            Debug.Log("模式切换被拒绝：手动切换尚在冷却中。", subject);
            return false;
        }
        // 实际逻辑：失败不先恢复世界，也不提前撤销权限，保证重叠拒绝没有旁路效果。
        if (!subject.TryCommitSpiritForm(flying)) return false;
        IsSimulation = !flying;
        subject.ApplyCapabilities(flying ? PlayerCapabilityPermissions.Roaming : PlayerCapabilityPermissions.Simulation);
        SetWorldPaused(flying);
        // 成功转换统一更新下一次手动冷却；强制本身仍不受此时间限制。
        nextManualChange = Time.unscaledTime + manualCooldown;
        Cognition.PayFormCost(formCost);
        FormCommitted?.Invoke(reason);
        return true;
    }

    public static void SetWorldPaused(bool paused)
    {
        BasicEntity.SetWorldPaused(paused);
        Taijie.SetWorldPaused(paused);
    }

    public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request)
    {
        Player subject = request.subject;
        // 安全：不存在的角色或没有周期身份的请求不执行结果。
        if (subject == null || request.cycle == 0) return PlayerDeathStatus.Rejected;
        // 去重：已经完成的旧周期不因重放或阶段切换再次扣费/刷新。
        if (handledCycles.TryGetValue(subject, out uint handled) && handled >= request.cycle)
            return PlayerDeathStatus.Handled;
        if (subject.PendingDeath?.cycle != request.cycle) return PlayerDeathStatus.Rejected;
        // 实际逻辑：成功转换的结算可能推进下一阶段，不能再用结算后的阶段重新解释这次死亡。
        PlayerGameplayStage requestedStage = Stage;
        bool completed;
        if (requestedStage == PlayerGameplayStage.Exploration)
        {
            // 探索规则：只处理本关精神体，同一入口提交飞行、漫游和现场暂停。
            if (subject != spirit) return PlayerDeathStatus.Rejected;
            completed = subject.RequestSpiritForm(true, PlayerFormChangeReason.Death);
        }
        else if (requestedStage == PlayerGameplayStage.Escape)
        {
            if (subject != body) return PlayerDeathStatus.Rejected;
            completed = refreshRules != null && refreshRules.TryRestartExploration(subject);
        }
        else
            // 教学关死亡已定为重开当前关，不走逃生重刷新。此处尚未执行。
            return PlayerDeathStatus.Pending;
        if (!completed) return PlayerDeathStatus.Pending;
        handledCycles[subject] = request.cycle;
        if (requestedStage == PlayerGameplayStage.Exploration) IsSimulation = false;
        if (requestedStage == PlayerGameplayStage.Escape) ConfigureStage(PlayerGameplayStage.Exploration);
        return PlayerDeathStatus.Handled;
    }
}
