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
    private PlayerEffectLedger effectLedger;
    private readonly float simulationCognitionPerSecond;
    private PlayerDeathRequest? animationDeath;
    private PlayerGameplayStage deathStage;
    private uint deathPlayback;
    private bool released;
    public bool DeathPending => animationDeath.HasValue;
    public LevelCognition Cognition { get; }
    public PlayerGameplayStage Stage { get; private set; }
    public bool IsSimulation { get; private set; }
    public LevelMapRecord MapRecord { get; private set; }
    public CognitionBackpack CognitionBackpack { get; private set; }
    public EnvironmentEditDraft ActiveEnvironmentEdit { get; private set; }

    /// <summary>场景组装的一处绑定覆盖三类费用；尚未建立的地图和背包在创建时继续绑定。</summary>
    public void BindEffectLedger(PlayerEffectLedger ledger)
    {
        effectLedger = ledger;
        CognitionBackpack?.BindEffectLedger(ledger);
        MapRecord?.BindEffectLedger(ledger);
    }

    /// <summary>关卡创建新探索时装载背包；失败重开重新组装，模式切换与轻刷新不重建。</summary>
    public bool BindCognitionBackpack(SceneLevelSetup setup)
    {
        if (setup == null || setup.Teaching) return false;
        try
        {
            CognitionBackpack = setup.CreateCognitionBackpack();
            CognitionBackpack.BindEffectLedger(effectLedger);
            return true;
        }
        catch (ArgumentException error) { Debug.LogError(error.Message, setup); return false; }
    }

    public EnvironmentEditDraft BeginEnvironmentEdit(BasicPhysicalObject source)
    {
        // 安全：同关、唯一来源与漫游资格在规则层检查，界面的高光不授予权限。
        if (!CanEditEnvironment || CognitionBackpack == null || source == null || !source.AllowsEnvironmentEditing
            || source.gameObject.scene != spirit.gameObject.scene) return null;
        // 操作归属：有待确认的草稿时，先结束它，再打开其它环境。
        if (ActiveEnvironmentEdit != null && !ActiveEnvironmentEdit.IsCompleted && ActiveEnvironmentEdit.HasChanges)
            return ActiveEnvironmentEdit.Source == source ? ActiveEnvironmentEdit : null;
        return ActiveEnvironmentEdit = new EnvironmentEditDraft(source, CognitionBackpack);
    }

    public void CancelEnvironmentEdit() => ActiveEnvironmentEdit = null;

    public bool ConfirmEnvironmentEdit(EnvironmentEditDraft draft, out string error)
    {
        error = null;
        if (!CanEditEnvironment || draft == null || draft != ActiveEnvironmentEdit
            || draft.Source == null || draft.Source.gameObject.scene != spirit.gameObject.scene)
        { error = "当前不能确认环境修改。"; return false; }
        if (!draft.TryCommit(Cognition, out error)) return false;
        ActiveEnvironmentEdit = null;
        // 全部内容及几何提交后才请求耗尽；不会在扣费一半时交出控制目标。
        RequestCognitionExhaustion();
        return true;
    }

    /// <summary>本关性质配置提供手工格子；教学与未启用地图的旧关卡不建立记录。</summary>
    public bool BindExplorationMap(SceneLevelSetup setup)
    {
        MapRecord = null;
        if (setup == null || setup.Teaching || !setup.MapEnabled) return true;
        MapRecord = new LevelMapRecord(setup.MapCells, Cognition, setup.gameObject.scene);
        MapRecord.BindEffectLedger(effectLedger);
        if (MapRecord.IsValid) return true;
        Debug.LogError(MapRecord.ConfigurationError, setup);
        return false;
    }

    /// <summary>现有场景 Update 顺带采样碰撞框中心，不加入物理触发器或第二条移动管线。</summary>
    public void AdvanceExplorationMap()
    {
        if (EscapeSelectionPending || MapRecord == null) return;
        if (Stage == PlayerGameplayStage.Exploration)
        {
            if (spirit == null || !spirit.Permissions.motionUpdates) return;
            if (MapRecord.SampleNormalMovement(spirit.BodyBounds.center)) RequestCognitionExhaustion();
            return;
        }
        // 逃生只同步实体所在格，不点亮、不扣认知。
        if (Stage == PlayerGameplayStage.Escape && body != null && body.Permissions.motionUpdates)
            MapRecord.OnRelocated(body.BodyBounds.center);
    }

    public void NotifyMapRelocated()
    {
        if (spirit != null) MapRecord?.OnRelocated(spirit.BodyBounds.center);
    }

    /// <summary>在任何世界与资源刷新前决定落点；地图启用后绝不以旧试验起点兜底。</summary>
    public bool TryGetTrialResetPoint(Vector2 fallback, out Vector2 point, out string error)
    {
        point = fallback;
        error = null;
        if (MapRecord == null)
        {
            if (Finite(point.x) && Finite(point.y)) return true;
            error = "关卡试验起点配置无效。";
            return false;
        }
        if (spirit == null) { error = "本关精神体未就绪。"; return false; }
        return MapRecord.TryGetResetCenter(spirit.BodyBounds.center, out point, out error);
    }
    public bool EscapeSelectionPending { get; private set; }
    public bool CanEditEnvironment => Stage == PlayerGameplayStage.Exploration && !IsSimulation && !EscapeSelectionPending
        && !CognitionExhaustionRequested && spirit != null && spirit.ResolveFourWayAbility() == FourWayMode.Flying;

    /// <summary>只在漫游修改本关环境来源；不向角色效果容器写值，后续正常采样读取。</summary>
    public bool TryEditEnvironmentParameter(BasicPhysicalObject source, int index, float value)
    {
        // 安全：来源必须属于本关，不能编辑其它已加载场景或已停用的物体。
        if (source == null || spirit == null || source.gameObject.scene != spirit.gameObject.scene) return false;
        if (!CanEditEnvironment || !source.AllowsEnvironmentEditing) return false;
        return source.TrySetEnvironmentParameter(index, value);
    }
    /// <summary>只读剩余手动转换冷却；沿用规则的非缩放时间，不另建计时器。</summary>
    public float ManualFormCooldownRemaining => Mathf.Max(0f, nextManualChange - Time.unscaledTime);
    /// <summary>P8 后续规则接入这一已扣费结果；初始化、拒绝和相同形态请求不发出，也不重复结算。</summary>
    public event Action<PlayerFormChangeReason> FormCommitted;
    /// <summary>本关只锁存一次的耗尽请求；P8.2 尚未消费时阶段仍为探索，不能把请求当逃生完成。</summary>
    public bool CognitionExhaustionRequested { get; private set; }
    public event Action CognitionExhaustionRequestedOnce;

    public PlayerLifecycleRules(Player body, Player spirit, IPlayerRunRefreshRules refreshRules = null,
        LevelCognition cognition = null, float formCost = 1f, float simulationCognitionPerSecond = 1f)
    {
        // 组装约束：错误身份不能用名字或当前控制权补成正确角色。
        if (body != null && body.Identity != PlayerIdentity.Body) throw new ArgumentException("本体引用必须是 Body。");
        if (spirit != null && spirit.Identity != PlayerIdentity.Spirit) throw new ArgumentException("精神体引用必须是 Spirit。");
        this.body = body;
        this.spirit = spirit;
        this.refreshRules = refreshRules;
        Cognition = cognition ?? new LevelCognition(100f);
        this.formCost = float.IsNaN(formCost) || float.IsInfinity(formCost) ? 0f : Mathf.Max(0f, formCost);
        this.simulationCognitionPerSecond = float.IsNaN(simulationCognitionPerSecond)
            || float.IsInfinity(simulationCognitionPerSecond) ? 0f : Mathf.Max(0f, simulationCognitionPerSecond);
    }

    public void ConfigureStage(PlayerGameplayStage stage, bool simulation = false)
    {
        Stage = stage;
        IsSimulation = stage == PlayerGameplayStage.Exploration && simulation;
        if (stage != PlayerGameplayStage.Exploration) EscapeSelectionPending = false;
    }

    /// <summary>手动与耗尽开始同一逃生结算；选择结束前停止探索操作和计时，不另加玩法阶段。</summary>
    public bool BeginEscapeSelection()
    {
        if (Stage != PlayerGameplayStage.Exploration || EscapeSelectionPending || spirit == null || body == null)
            return false;
        if (ActiveEnvironmentEdit != null && !ActiveEnvironmentEdit.IsCompleted && ActiveEnvironmentEdit.HasChanges)
            return false;
        CancelEnvironmentEdit();
        EscapeSelectionPending = true;
        spirit.PrepareRuntimeRefresh();
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        SetWorldPaused(true);
        return true;
    }

    /// <summary>整关轻刷新只恢复运行状态，不动环境编辑、地图、认知或道具效果。</summary>
    public static void RestoreWorldDefaults(UnityEngine.SceneManagement.Scene scene)
    {
        // 复位顺序：目标位置和驱动先回默认，再恢复箱子，最后清压感接触；不保存整世界快照。
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Taijie target in root.GetComponentsInChildren<Taijie>(true)) target.RestoreMechanismDefaults();
            foreach (MechanismCart cart in root.GetComponentsInChildren<MechanismCart>(true)) cart.RestoreDefault();
        }
        LevelBoxContext.RestoreDefaults(scene);
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MechanismPressurePlate plate in root.GetComponentsInChildren<MechanismPressurePlate>(true)) plate.RestoreDefault();
        Physics2D.SyncTransforms();
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
        body?.ApplyCapabilities(PlayerCapabilityPermissions.Waiting);
        body?.SnapSpawnToGround();
        ConfigureStage(PlayerGameplayStage.Exploration);
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Roaming);
        SetWorldPaused(true);
        // 实际逻辑：初始余额为零也须等探索的角色和权限就绪，再留下耗尽请求。
        RequestCognitionExhaustion();
        return true;
    }

    /// <summary>场景宿主每次 Update 只传一次游戏时间；漫游、教学和逃生不收取模拟费用。</summary>
    public void AdvanceSimulationTime(float elapsedGameTime)
    {
        // 阶段约束：只有探索／模拟按时间扣认知，输入有无操作不影响计费。
        if (Stage != PlayerGameplayStage.Exploration || !IsSimulation || EscapeSelectionPending) return;
        Cognition.PaySimulationTime(elapsedGameTime, simulationCognitionPerSecond);
        RequestCognitionExhaustion();
    }

    void RequestCognitionExhaustion()
    {
        // 去重：转换与模拟计时汇入同一请求，消费者缺席时保持事实，不重复发出或伪造阶段切换。
        if (Stage != PlayerGameplayStage.Exploration || !Cognition.IsExhausted || CognitionExhaustionRequested) return;
        CognitionExhaustionRequested = true;
        CognitionExhaustionRequestedOnce?.Invoke();
    }

    /// <summary>
    /// 人工阅读链路：模式请求 → 执行层一次占用检查 → 形态／权限／世界同时提交。
    /// 失败保留原模式；死亡和区域强制跳过手动冷却，成功均从本关认知余额扣费一次。
    /// </summary>
    public bool TryChangeForm(Player subject, bool flying, PlayerFormChangeReason reason)
    {
        // 安全：只有本关探索中的精神体有这条入口。
        if (subject == null || subject != spirit || Stage != PlayerGameplayStage.Exploration || EscapeSelectionPending) return false;
        if (reason == PlayerFormChangeReason.Manual && ActiveEnvironmentEdit != null
            && !ActiveEnvironmentEdit.IsCompleted && ActiveEnvironmentEdit.HasChanges)
        {
            Debug.Log("模式切换被拒绝：请先确认或取消环境修改。", subject);
            return false;
        }
        // 实际逻辑：相同模式不再执行碰撞校验、冷却或重复结算。
        if (IsSimulation == !flying && (subject.ResolveFourWayAbility() == FourWayMode.Flying) == flying)
            return true;
        if (reason == PlayerFormChangeReason.Manual && Time.unscaledTime < nextManualChange)
        {
            Debug.Log("模式切换被拒绝：手动切换尚在冷却中。", subject);
            return false;
        }
        // 实际逻辑：失败不先恢复世界，也不提前撤销权限，保证重叠拒绝没有旁路效果。
        float effectiveFormCost = effectLedger?.GetUpgradedValue(PlayerUpgradeKind.FormCost, formCost) ?? formCost;
        if (!subject.TryCommitSpiritForm(flying)) return false;
        CancelEnvironmentEdit();
        IsSimulation = !flying;
        subject.ApplyCapabilities(flying ? PlayerCapabilityPermissions.Roaming : PlayerCapabilityPermissions.Simulation);
        SetWorldPaused(flying);
        // 成功转换统一更新下一次手动冷却；强制本身仍不受此时间限制。
        nextManualChange = Time.unscaledTime + manualCooldown;
        Cognition.PayFormCost(effectiveFormCost);
        RequestCognitionExhaustion();
        FormCommitted?.Invoke(reason);
        return true;
    }

    /// <summary>试验轻刷新：先恢复全关箱子，再安置同一精神体；不改变形态、认知或转换冷却。</summary>
    public bool TryResetTrial(Vector2 point)
    {
        // 安全：无目标或无效坐标须在任何资源／世界变更前拒绝。
        if (spirit == null || !Finite(point.x) || !Finite(point.y)) return false;
        // 阶段资格：只有探索模拟可以重置，漫游编辑不借此恢复行走。
        if (Stage != PlayerGameplayStage.Exploration || !IsSimulation || EscapeSelectionPending) return false;
        PlayerCapabilityPermissions permissions = spirit.Permissions;
        spirit.PrepareRuntimeRefresh();
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        SetWorldPaused(true);
        RestoreWorldDefaults(spirit.gameObject.scene);
        // 箱子暂停时不参加查询；先恢复参与并同步，再给角色采样，期间不推进模拟。
        SetWorldPaused(false);
        Physics2D.SyncTransforms();
        LevelBoxContext.Resample(spirit.gameObject.scene);
        spirit.ApplyCapabilities(permissions);
        spirit.RefreshRuntime(point, alignBodyCenter: MapRecord != null);
        NotifyMapRelocated();
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>地图只传送同一精神体；保留资源、世界现场、模式及认知，不走试验轻刷新。</summary>
    public bool TryTeleportMap(Vector2 center)
    {
        if (spirit == null || !Finite(center.x) || !Finite(center.y)
            || Stage != PlayerGameplayStage.Exploration || EscapeSelectionPending) return false;
        spirit.TeleportRuntime(center);
        NotifyMapRelocated();
        return true;
    }

    /// <summary>宿主全黑下成功重建探索后，给旧规则的死亡周期留完成记录，供旧请求重放去重。</summary>
    internal void CompleteEscapeRecovery(PlayerDeathRequest request)
    {
        if (request.subject != body || request.cycle == 0 || Stage != PlayerGameplayStage.Escape) return;
        handledCycles[body] = request.cycle;
        CancelDeathWait(body);
        ConfigureStage(PlayerGameplayStage.Exploration);
    }

    /// <summary>显式重置／禁用作废该角色旧等待；恢复权限和世界仍由原刷新入口提交。</summary>
    internal void CancelDeathWait(Player subject)
    {
        if (!animationDeath.HasValue || animationDeath.Value.subject != subject) return;
        subject?.Presentation?.CancelDeathPlayback();
        animationDeath = null;
        deathPlayback = 0;
    }

    internal void Release()
    {
        if (animationDeath.HasValue) CancelDeathWait(animationDeath.Value.subject);
        released = true;
    }

    public static void SetWorldPaused(bool paused)
    {
        BasicEntity.SetWorldPaused(paused);
        Taijie.SetWorldPaused(paused);
        MechanismPressurePlate.SetWorldPaused(paused);
    }

    public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request)
    {
        Player subject = request.subject;
        // 安全：不存在的角色或没有周期身份的请求不执行结果。
        if (subject == null || request.cycle == 0) return PlayerDeathStatus.Rejected;
        // 去重：已经完成的旧周期不因重放或阶段切换再次扣费/刷新。
        if (handledCycles.TryGetValue(subject, out uint handled) && handled >= request.cycle)
            return PlayerDeathStatus.Handled;
        if (released || subject.PendingDeath?.cycle != request.cycle) return PlayerDeathStatus.Rejected;
        // 模拟遇险是退出试验：立即沿原转换返回漫游，不建立死亡动画等待。
        if (Stage == PlayerGameplayStage.Exploration)
        {
            if (subject != spirit || EscapeSelectionPending) return PlayerDeathStatus.Rejected;
            if (!subject.RequestSpiritForm(true, PlayerFormChangeReason.Death)) return PlayerDeathStatus.Pending;
            handledCycles[subject] = request.cycle;
            return PlayerDeathStatus.Handled;
        }
        if (subject != body) return PlayerDeathStatus.Rejected;
        // 实体死亡关闭操作，继续被动运动和世界更新，结果等待实际单次动画结束。
        if (!animationDeath.HasValue)
        {
            animationDeath = request;
            deathStage = Stage;
            subject.BeginDeathMotion();
            if (subject.Presentation == null)
            { Debug.LogError("死亡角色缺少共用动画表现组件。", subject); return PlayerDeathStatus.Rejected; }
            if (!subject.Presentation.TryBeginDeathPlayback(out deathPlayback)) return PlayerDeathStatus.Rejected;
        }
        if (animationDeath.Value.subject != subject || animationDeath.Value.cycle != request.cycle
            || Stage != deathStage || subject.Presentation == null) return PlayerDeathStatus.Rejected;
        PresentationLayer.DeathPlaybackStatus playback = subject.Presentation.GetDeathPlaybackStatus(deathPlayback);
        if (playback == PresentationLayer.DeathPlaybackStatus.Invalid) return PlayerDeathStatus.Rejected;
        if (playback != PresentationLayer.DeathPlaybackStatus.Completed) return PlayerDeathStatus.Pending;
        PlayerGameplayStage requestedStage = Stage;
        bool completed;
        if (requestedStage == PlayerGameplayStage.Escape)
        {
            completed = refreshRules != null && refreshRules.TryRestartExploration(subject);
        }
        else
        {
            completed = refreshRules is IPlayerTutorialRefreshRules tutorial && tutorial.TryRestartTeaching(subject);
        }
        if (!completed) return PlayerDeathStatus.Pending;
        handledCycles[subject] = request.cycle;
        CancelDeathWait(subject);
        if (requestedStage == PlayerGameplayStage.Escape) ConfigureStage(PlayerGameplayStage.Exploration);
        return PlayerDeathStatus.Handled;
    }
}
