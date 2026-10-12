using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景切换管理器
/// </summary>
public partial class SceneChangeMgr : BaseMgr<SceneChangeMgr>, IPlayerRunRefreshRules, IPlayerTutorialRefreshRules
{
    const string MenuSceneName = "BeginScenes";
    Player body;
    Player spirit;
    PlayerLifecycleRules levelRules;
    readonly PlayerEffectLedger effects = new PlayerEffectLedger();
    bool refreshing;
    public bool IsFailureRecovering => refreshing;
    bool loading;
    int loadSerial;
    bool enterFromMenu;
    public bool GameMenuOpen { get; private set; }
    float timeScaleBeforeMenu;
    string gameplayMessage;
    float messageUntil;
    public string GameplayMessage => Time.unscaledTime < messageUntil ? gameplayMessage : null;

    void ShowGameplayMessage(string message)
    {
        gameplayMessage = message;
        messageUntil = Time.unscaledTime + 4f;
    }

    /// <summary>人工阅读：固定 ESC → 本关菜单 → 原世界暂停入口；关闭后按当前模式恢复，不重置现场。</summary>
    public bool ToggleGameMenu()
    {
        if (GameMenuOpen) { CloseGameMenu(); return true; }
        // 安全：菜单和关卡加载期间没有可暂停的游戏，不显示关内菜单。
        if (loading || refreshing || body == null || SceneLevelSetup.Current == null
            || (InputControlMgr.Instance.Player == null && !EscapeSelectionPending && !escapeHandoff))
            return false;
        GameMenuOpen = true;
        timeScaleBeforeMenu = Time.timeScale;
        InputControlMgr.Instance.SetMenuInputPaused(true);
        Time.timeScale = 0f;
        PlayerLifecycleRules.SetWorldPaused(true);
        UIMgr.Instance.ShowOneUI<GameMenuPanel>(E_UILayer.Top);
        return true;
    }

    void CloseGameMenu()
    {
        // 安全：未打开时不恢复一个并未由菜单保存的时间与输入状态。
        if (!GameMenuOpen) return;
        GameMenuOpen = false;
        UIMgr.Instance.HideOneUI<GameMenuPanel>();
        Time.timeScale = loading || refreshing ? 0f : timeScaleBeforeMenu;
        // 实际逻辑：漫游继续暂停世界；教学和模拟恢复，平台不会追赶菜单停留时间。
        PlayerLifecycleRules.SetWorldPaused(loading || refreshing || (levelRules != null &&
            ((levelRules.Stage == PlayerGameplayStage.Exploration && !levelRules.IsSimulation)
                || levelRules.EscapeSelectionPending)));
        InputControlMgr.Instance.SetMenuInputPaused(false);
    }

    /// <summary>场景模式请求与区域规则调用同一入口；当前控制对象不变，失败留在原模式。</summary>
    public bool SetSimulation(bool simulation, PlayerFormChangeReason reason = PlayerFormChangeReason.Manual)
    {
        if (loading || refreshing || levelRules == null || spirit == null) return false;
        return spirit.RequestSpiritForm(!simulation, reason);
    }

    public bool CanResetTrial => !loading && !refreshing && !GameMenuOpen && Time.timeScale > 0f
        && levelRules != null && levelRules.Stage == PlayerGameplayStage.Exploration
        && levelRules.IsSimulation && !levelRules.EscapeSelectionPending
        && spirit != null && InputControlMgr.Instance.Player == spirit;

    public bool CanStartEscape => !loading && !refreshing && !GameMenuOpen && Time.timeScale > 0f
        && levelRules != null && levelRules.Stage == PlayerGameplayStage.Exploration
        && !levelRules.EscapeSelectionPending && body != null && spirit != null;
    public bool EscapeSelectionPending => levelRules != null && levelRules.EscapeSelectionPending && !escapeHandoff;

    /// <summary>手动和耗尽共用一次探索结束；选择时暂停，低预算直接接受本局负面并开始逃生。</summary>
    public bool StartEscape()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (!CanStartEscape || setup == null || !ValidEscapeSetup(setup)) return false;
        // 配置错误在购买／暂停前指出；不以占位延时跳过消散。
        if (!ValidateEscapeHandoff(setup)) return false;
        if (!levelRules.BeginEscapeSelection()) return false;
        InputControlMgr.Instance.BindPlayer(null);
        bool affordable = false;
        foreach (EscapeAbilityOffer offer in setup.EscapeAbilities)
            affordable |= levelRules.Cognition.Remaining >= offer.Cost;
        if (!affordable)
        {
            if (!CompleteEscapePreparation()) return false;
            effects.GrantEffect(PlayerEffectKind.MovementMultiplier, setup.LowCognitionMovementMultiplier);
        }
        return true;
    }

    public bool PurchaseEscapeAbility(int index)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        // 选择只提交一次；无效或买不起的选项不先扣余额或给效果。
        if (loading || refreshing || GameMenuOpen || Time.timeScale <= 0f || !EscapeSelectionPending || setup == null
            || index < 0 || index >= setup.EscapeAbilities.Count) return false;
        EscapeAbilityOffer offer = setup.EscapeAbilities[index];
        if (offer == null || !offer.IsValid || levelRules.Cognition.Remaining < offer.Cost) return false;
        if (!CompleteEscapePreparation()) return false;
        if (!effects.GrantEffect(offer.Kind, offer.Multiplier)) return false;
        levelRules.Cognition.TryPayAbilityCost(offer.Cost);
        return true;
    }

    static bool ValidEscapeSetup(SceneLevelSetup setup)
    {
        if (setup.EscapeAbilities == null || setup.EscapeAbilities.Count == 0
            || float.IsNaN(setup.LowCognitionMovementMultiplier) || float.IsInfinity(setup.LowCognitionMovementMultiplier)
            || setup.LowCognitionMovementMultiplier <= 0f || setup.LowCognitionMovementMultiplier > 1f) return false;
        foreach (EscapeAbilityOffer offer in setup.EscapeAbilities)
            if (offer == null || !offer.IsValid) return false;
        if (!setup.ValidateRisingWaterConfiguration(out string waterError))
        { Debug.LogError(waterError, setup); return false; }
        return true;
    }

    /// <summary>受理一次异步失败演出，返回 false 保持死亡待处理；全黑后才重建同关探索。</summary>
    public bool TryRestartExploration(Player deadPlayer)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (loading || refreshing || setup == null || setup.Teaching || deadPlayer != body
            || deadPlayer == null || !deadPlayer.PendingDeath.HasValue
            || levelRules == null || levelRules.Stage != PlayerGameplayStage.Escape || !CanSpawnSpirit(setup)) return false;
        refreshing = true;
        int serial = ++loadSerial;
        PlayerDeathRequest request = deadPlayer.PendingDeath.Value;
        PlayerLifecycleRules previousRules = levelRules;
        InputControlMgr.Instance.InputOpenOrClose(false);
        InputControlMgr.Instance.BindPlayer(null);
        PlayerLifecycleRules.SetWorldPaused(true);
        Time.timeScale = 0f;
        UIMgr.Instance.ShowOneUI<FailureRecoveryPanel>(E_UILayer.Top, panel =>
        {
            // 退出／换关已经作废本轮时，迟到的面板回调不能再重置场景。
            if (serial != loadSerial) return;
            string error = null;
            if (panel == null || !panel.ValidateSettings(out error))
            {
                Debug.LogError(panel == null ? "逃生失败面板缺失。" : error);
                ReturnToMenu();
                return;
            }
            MonoPublicMgr.Instance.StartCoroutine(RecoverEscape(panel, setup, previousRules, request, serial));
        });
        return false;
    }

    IEnumerator RecoverEscape(FailureRecoveryPanel panel, SceneLevelSetup setup,
        PlayerLifecycleRules previousRules, PlayerDeathRequest request, int serial)
    {
        yield return FadeFailure(panel, 0f, 1f, serial);
        if (serial != loadSerial) yield break;
        // 留一帧真正的全黑，之后才提交世界／认知／角色的重置。
        yield return null;
        if (serial != loadSerial) yield break;
        bool restored = false;
        try
        {
            restored = RestoreExplorationAfterFailure(setup);
        }
        catch (System.Exception error) { Debug.LogException(error); }
        if (!restored)
        {
            Debug.LogError("逃生失败复位未完成，返回菜单。", setup);
            ReturnToMenu();
            yield break;
        }
        previousRules.CompleteEscapeRecovery(request);
        InputControlMgr.Instance.SnapCameraToPlayer();
        yield return FadeFailure(panel, 1f, 0f, serial);
        if (serial != loadSerial) yield break;
        UIMgr.Instance.HideOneUI<FailureRecoveryPanel>();
        refreshing = false;
        Time.timeScale = GameMenuOpen ? 0f : 1f;
        InputControlMgr.Instance.InputOpenOrClose(true);
    }

    IEnumerator FadeFailure(FailureRecoveryPanel panel, float from, float to, int serial)
    {
        float duration = to > from ? panel.DarkenSeconds : panel.BrightenSeconds;
        float elapsed = 0f;
        panel.SetWeight(from);
        while (elapsed < duration)
        {
            if (serial != loadSerial) yield break;
            elapsed += Time.unscaledDeltaTime;
            panel.SetWeight(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        if (serial == loadSerial) panel.SetWeight(to);
    }

    bool RestoreExplorationAfterFailure(SceneLevelSetup setup)
    {
        InputControlMgr.Instance.BindPlayer(null);
        body.PrepareRuntimeRefresh();
        body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        PlayerLifecycleRules.SetWorldPaused(true);
        setup.RisingWater?.RestoreRunDefaults();
        foreach (GameObject root in setup.gameObject.scene.GetRootGameObjects())
            foreach (BasicPhysicalObject source in root.GetComponentsInChildren<BasicPhysicalObject>(true))
                source.RestoreEnvironmentDefaults();
        PlayerLifecycleRules.RestoreWorldDefaults(setup.gameObject.scene);
        foreach (GameObject root in setup.gameObject.scene.GetRootGameObjects())
            foreach (SceneProp prop in root.GetComponentsInChildren<SceneProp>(true)) prop.RestoreDefault();
        levelRules.MapRecord?.Reset();
        effects.ResetCurrentLevel();
        body.RefreshRuntime(setup.BodyPoint, true);
        PlaceDepth(body, setup.BodyPose);
        ReleaseLevelRules();
        return SpawnSpirit(setup);
    }

    public bool TryRestartTeaching(Player deadPlayer)
    {
        if (loading || refreshing || deadPlayer != body || SceneLevelSetup.Current == null || !SceneLevelSetup.Current.Teaching)
            return false;
        effects.ResetCurrentLevel();
        EnterLevel(LevelStageMgr.Instance.LevelIndex, DataAndInitMgr.Instance.LoadLevelOrder());
        // 异步重开尚未安置完成时不报告死亡已处理；新关组装会显式恢复死亡周期。
        return false;
    }

    public bool CanEditEnvironment => !loading && !refreshing && !GameMenuOpen && Time.timeScale > 0f
        && levelRules != null && levelRules.CanEditEnvironment && InputControlMgr.Instance.Player == spirit;

    public CognitionBackpack CurrentCognitionBackpack => levelRules?.CognitionBackpack;

    public EnvironmentEditDraft BeginEnvironmentEdit(BasicPhysicalObject source) =>
        CanEditEnvironment ? levelRules.BeginEnvironmentEdit(source) : null;

    public void CancelEnvironmentEdit() => levelRules?.CancelEnvironmentEdit();

    public bool ConfirmEnvironmentEdit(EnvironmentEditDraft draft, out string error)
    {
        error = null;
        if (!CanEditEnvironment) { error = "当前不能确认环境修改。"; return false; }
        return levelRules.ConfirmEnvironmentEdit(draft, out error);
    }

    public int GetPushableEnvironmentOverlapCount(BasicPhysicalObject source) =>
        CanEditEnvironment && source != null ? LevelBoxContext.CountPushableOverlaps(source) : 0;

    public bool EditEnvironmentParameter(BasicPhysicalObject source, int index, float value)
    {
        if (!CanEditEnvironment) return false;
        return levelRules.TryEditEnvironmentParameter(source, index, value);
    }

    /// <summary>界面试验重置入口；地图前使用固定起点，后续地图目标仍交给同一规则安置。</summary>
    public bool ResetTrial()
    {
        // 安全：加载、菜单暂停或控制交接时不消费重置请求。
        if (!CanResetTrial || SceneLevelSetup.Current == null) return false;
        Vector2 point = SceneLevelSetup.Current.TrialPoint;
        // 地图启用后只校验实际格子目标；未启用时才校验试验起点。
        if (!levelRules.TryGetTrialResetPoint(point, out point, out string error))
        {
            Debug.LogWarning(error);
            ShowGameplayMessage(error);
            return false;
        }
        gameplayMessage = null;
        InputControlMgr input = InputControlMgr.Instance;
        bool enabled = input.InputEnabled;
        if (enabled) input.InputOpenOrClose(false);
        try { return levelRules.TryResetTrial(point); }
        finally { if (enabled) input.InputOpenOrClose(true); }
    }

    void ReleaseLevelRules()
    {
        CancelEscapeHandoff();
        levelRules?.Release();
        // 生命周期：卸关或返回菜单时移除本关计时，下一关只登记一次。
        if (levelRules != null && !MonoPublicMgr.IsQuitting)
            MonoPublicMgr.Instance.RemoveUpdatEvent(AdvanceLevelCognition);
        LevelStageMgr.Instance.BindRules(null);
        levelRules = null;
        gameplayMessage = null;
        PlayerLifecycleRules.SetWorldPaused(false);
    }

    void AdvanceLevelCognition()
    {
        // 暂停约束：加载与关内菜单不产生模拟费用；其余情况用随 TimeScale 推进的游戏时间。
        if (loading || GameMenuOpen || levelRules == null || Time.timeScale <= 0f) return;
        levelRules.AdvanceSimulationTime(Time.deltaTime);
        levelRules.AdvanceExplorationMap();
        if (levelRules.CognitionExhaustionRequested && !levelRules.EscapeSelectionPending)
        {
            // 耗尽优先结束探索；未确认的预览取消，已确认配置和背包仍保留。
            levelRules.CancelEnvironmentEdit();
            StartEscape();
        }
    }

    /// <summary>按已保存的关序号进入。没有存档时是第一关。</summary>
    public void EnterSavedLevel()
    {
        LevelOrderFile order = DataAndInitMgr.Instance.LoadLevelOrder();
        int index = DataAndInitMgr.Instance.LoadLevelIndex();
        // 安全：进度超出当前顺序时回到第一关。
        if (order.scenes == null || index < 0 || index >= order.scenes.Length) index = 0;
        EnterLevel(index, order);
    }

    /// <summary>进入顺序中的下一关。最后一关不再往后。</summary>
    public void EnterNextLevel()
    {
        LevelOrderFile order = DataAndInitMgr.Instance.LoadLevelOrder();
        EnterLevel(DataAndInitMgr.Instance.LoadLevelIndex() + 1, order, true);
    }

    /// <summary>还在关卡里就结束本局并载回开始场景。人已经在菜单时返回 false，让界面自己退一层。</summary>
    public bool ReturnToMenu()
    {
        CloseGameMenu();
        UIMgr.Instance.HideOneUI<GameplayPanel>();
        int serial = ++loadSerial;
        loading = false;
        refreshing = false;
        CancelEscapeHandoff();
        UIMgr.Instance.HideOneUI<FailureRecoveryPanel>();
        InputControlMgr.Instance.EndTransitionView(true);
        body?.GetComponentInChildren<PresentationLayer>()?.SetTransitionPlayback(false);
        Time.timeScale = 1f;
        ReleaseLevelRules();
        effects.ResetCurrentLevel();
        InputControlMgr.Instance.BindPlayer(null);
        InputControlMgr.Instance.InputOpenOrClose(false);
        // 实际逻辑：不在开始场景就结束本局。实体跨场景，必须显式销毁。
        bool inLevel = SceneManager.GetActiveScene().name != MenuSceneName;
        DestroyActor(ref spirit);
        DestroyActor(ref body);
        if (!inLevel)
        {
            UIMgr.Instance.HideOneUI<LoadingPanel>();
            return false;
        }
        UIMgr.Instance.HideOneUI<ChangePanel>();
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top, panel =>
        {
            if (serial == loadSerial) panel.SetTransitionWeight(1f);
        });
        LoadSceneAsync(MenuSceneName, () =>
        {
            // 实际逻辑：加载被新的进入或返回作废时，不能关掉新的加载页。
            if (serial != loadSerial) return;
            UIMgr.Instance.HideOneUI<LoadingPanel>();
        });
        return true;
    }

    void EnterLevel(int index, LevelOrderFile order, bool confirmPrevious = false)
    {
        // 安全：没有顺序时不编造关卡。
        if (order.scenes == null || order.scenes.Length == 0)
        {
            Debug.LogError("关卡顺序还没生成，不能进入场景。");
            return;
        }
        // 安全：上一轮场景还没安置完时不开始下一次。
        if (loading)
        {
            Debug.Log("场景正在加载。");
            return;
        }
        if (index < 0 || index >= order.scenes.Length)
        {
            Debug.Log("已经是顺序中的最后一关。");
            return;
        }
        // 安全：失效的顺序文件不能关闭输入后再把加载流程卡住。
        if (string.IsNullOrWhiteSpace(order.scenes[index])
            || !Application.CanStreamedLevelBeLoaded(order.scenes[index]))
        {
            Debug.LogError($"关卡不能加载：{order.scenes[index]}，请重新生成关卡顺序并核对构建设置。");
            return;
        }
        refreshing = false;
        CancelEscapeHandoff();
        UIMgr.Instance.HideOneUI<FailureRecoveryPanel>();
        loading = true;
        // 换关被受理时立即作废旧死亡，不能等淡出结束后再阻止迟到扣费。
        levelRules?.Release();
        CloseGameMenu();
        UIMgr.Instance.HideOneUI<GameplayPanel>();
        int serial = ++loadSerial;
        enterFromMenu = SceneManager.GetActiveScene().name == MenuSceneName;
        // 实际逻辑：从渐暗开始暂停世界；镜头和实体动画独立使用真实时间。
        Time.timeScale = 0f;
        // 实际逻辑：主菜单只做黑屏淡入淡出。关与关之间才把镜头缓动到实体。
        InputControlMgr.Instance.InputOpenOrClose(false);
        if (!enterFromMenu && body != null)
        {
            body.GetComponentInChildren<PresentationLayer>()?.SetTransitionPlayback(true);
            spirit?.GetComponentInChildren<PresentationLayer>()?.SetTransitionHidden(true);
            InputControlMgr.Instance.BeginTransitionView(body.transform, false);
        }
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top, panel =>
        {
            if (serial != loadSerial || panel == null) return;
            MonoPublicMgr.Instance.StartCoroutine(DarkenThenLoad(panel, index, order, confirmPrevious, serial));
        });
    }

    IEnumerator DarkenThenLoad(LoadingPanel panel, int index, LevelOrderFile order, bool confirmPrevious, int serial)
    {
        yield return FadeTransition(panel, 0f, 1f, serial);
        if (serial != loadSerial) yield break;
        ReleaseLevelRules();
        InputControlMgr.Instance.BindPlayer(null);
        if (body != null) body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        if (spirit != null) spirit.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        DestroyActor(ref spirit);
        // 实际逻辑：全暗后才卸载旧关，新关物体在安置前保持暂停。
        LoadSceneAsync(order.scenes[index], () =>
        {
            if (serial != loadSerial) return;
            PlaceBody(index, serial, placed => FinishEnter(index, serial, placed, confirmPrevious));
        });
    }

    IEnumerator FadeTransition(LoadingPanel panel, float from, float to, int serial)
    {
        float duration = panel != null ? (to > from ? panel.FadeOutDuration : panel.FadeInDuration) : .7f;
        float elapsed = 0f;
        panel?.SetTransitionWeight(from);
        while (elapsed < duration)
        {
            // 实际逻辑：作废的协程只退出，清理由取消入口处理，不能关掉新一轮镜头。
            if (serial != loadSerial) yield break;
            elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
            panel?.SetTransitionWeight(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        if (serial == loadSerial) panel?.SetTransitionWeight(to);
    }

    void FinishEnter(int index, int serial, bool placed, bool confirmPrevious)
    {
        // 实际逻辑：返回或新的进入已经作废这一轮时，不再写进度、也不再打开输入。
        if (serial != loadSerial)
        {
            // 安全：这次加载已被返回菜单作废时恢复时间；新的一次进入仍由它自己暂停。
            if (!loading) Time.timeScale = 1f;
            return;
        }
        if (!placed)
        {
            // 安全：新关已经换上，安置失败不能停在没有操作的场景里。上一关进度和效果确认都未写入。
            Time.timeScale = 1f;
            loading = false;
            InputControlMgr.Instance.EndTransitionView();
            Debug.Log("关卡安置失败，返回菜单。");
            ReturnToMenu();
            return;
        }
        // 实际逻辑：全黑缓冲也隐藏前景实体，避免新出生点的下落穿过遮罩；精神体等演出结束再显示。
        body?.GetComponentInChildren<PresentationLayer>()?.SetTransitionHidden(true);
        if (!enterFromMenu && body != null)
        {
            spirit?.GetComponentInChildren<PresentationLayer>()?.SetTransitionHidden(true);
            InputControlMgr.Instance.BeginTransitionView(body.transform, true);
        }
        else InputControlMgr.Instance.SnapCameraToPlayer();
        if (confirmPrevious) effects.CommitLevel();
        DataAndInitMgr.Instance.SaveLevelIndex(index);
        // 实际逻辑：角色和规则安置成功就恢复物理，输入仍关闭；探索的世界许可继续由阶段规则决定。
        Time.timeScale = 1f;
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top, panel =>
        {
            if (serial != loadSerial) return;
            MonoPublicMgr.Instance.StartCoroutine(BrightenThenPlay(panel, serial));
        });
    }

    IEnumerator BrightenThenPlay(LoadingPanel panel, int serial)
    {
        float settleDuration = panel != null ? panel.PhysicsSettleDuration : .25f;
        float elapsed = 0f;
        // 实际逻辑：用有限的全黑时间容纳出生点小幅下落，不额外模拟物理，也不无限等待落地。
        panel?.SetTransitionWeight(1f);
        while (elapsed < settleDuration)
        {
            if (serial != loadSerial) yield break;
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (serial != loadSerial) yield break;
        // 实际逻辑：缓冲结束后按实际位置重新对齐镜头，再显露实体和背景。
        if (!enterFromMenu && body != null) InputControlMgr.Instance.BeginTransitionView(body.transform, true);
        else InputControlMgr.Instance.SnapCameraToPlayer();
        body?.GetComponentInChildren<PresentationLayer>()?.SetTransitionHidden(false);
        yield return FadeTransition(panel, 1f, 0f, serial);
        if (serial != loadSerial) yield break;
        InputControlMgr.Instance.EndTransitionView();
        body?.GetComponentInChildren<PresentationLayer>()?.SetTransitionPlayback(false);
        spirit?.GetComponentInChildren<PresentationLayer>()?.SetTransitionHidden(false);
        loading = false;
        UIMgr.Instance.HideOneUI<LoadingPanel>();
        InputControlMgr.Instance.InputOpenOrClose(true);
        UIMgr.Instance.ShowOneUI<GameplayPanel>(E_UILayer.Bottom);
    }

    /// <summary>人工阅读入口：场景配置 → 留下的实体 → 本关精神体 → 唯一输入目标；成功后才确认效果并保存进度。</summary>
    void PlaceBody(int index, int serial, UnityAction<bool> done)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        // 安全：场景没摆关卡配置时不创建或搬走实体，也不记进度。
        if (setup == null)
        {
            Debug.LogError("当前场景没有关卡配置。");
            done?.Invoke(false);
            return;
        }
        LevelStageMgr.Instance.Note(index, setup.Teaching);
        EnsureBody(serial, ok =>
        {
            if (serial != loadSerial) return;
            // 安全：实体没就绪时不安置，也不把控制权交给精神体。
            if (!ok || body == null)
            {
                done?.Invoke(false);
                return;
            }
            // 实际逻辑：先绑输入再刷新，状态机才能在 Start 之前拿到操作数据。
            InputControlMgr.Instance.BindPlayer(body);
            body.BindEffectLedger(effects);
            body.RefreshRuntime(setup.BodyPoint);
            PlaceDepth(body, setup.BodyPose);
            if (setup.Teaching)
            {
                levelRules = new PlayerLifecycleRules(body, null, this, new LevelCognition(0f));
                levelRules.ConfigureStage(PlayerGameplayStage.Tutorial);
                LevelStageMgr.Instance.BindRules(levelRules);
                body.BindDeathReceiver(levelRules);
                body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
                done?.Invoke(true);
                return;
            }
            // 实际逻辑：非教学关实体停在出生点，控制交给初始飞行的本关精神体。
            body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
            done?.Invoke(SpawnSpirit(setup));
        });
    }

    bool SpawnSpirit(SceneLevelSetup setup)
    {
        if (!CanSpawnSpirit(setup))
        {
            Debug.LogError("精神体预设、出生点或上涨水配置无效，不能开始探索。", setup);
            return false;
        }
        // 实际逻辑：上一只还在时先去掉，这一关再新建。
        DestroyActor(ref spirit);
        // 安全：没指定预设时不造精神体，实体保持待机。
        if (setup.SpiritPrefab == null)
        {
            Debug.LogError("非教学关没有指定精神体预设。", setup);
            return false;
        }
        GameObject created = Object.Instantiate(setup.SpiritPrefab, setup.SpiritPose, Quaternion.identity);
        spirit = created.GetComponent<Player>();
        // 安全：预设身份不对时不把实体的控制权交出去。
        if (spirit == null || spirit.Identity != PlayerIdentity.Spirit)
        {
            Debug.LogError("精神体预设的身份不是精神体。", created);
            if (created != null) Object.Destroy(created);
            spirit = null;
            return false;
        }
        InputControlMgr.Instance.BindPlayer(spirit);
        spirit.BindEffectLedger(effects);
        spirit.RefreshRuntime(setup.SpiritPoint);
        PlaceDepth(spirit, setup.SpiritPose);
        levelRules = new PlayerLifecycleRules(body, spirit, this,
            cognition: new LevelCognition(setup.InitialCognition), formCost: setup.FormChangeCost,
            simulationCognitionPerSecond: setup.SimulationCognitionPerSecond);
        if (!levelRules.BindCognitionBackpack(setup))
        {
            DestroyActor(ref spirit);
            ReleaseLevelRules();
            return false;
        }
        if (!levelRules.BeginExploration(setup.ManualFormCooldown))
        {
            Debug.LogError("精神体飞行初始化失败。", spirit);
            DestroyActor(ref spirit);
            ReleaseLevelRules();
            return false;
        }
        LevelStageMgr.Instance.BindRules(levelRules);
        levelRules.BindExplorationMap(setup);
        if (!setup.PrepareRisingWater(out string waterError))
        { Debug.LogError(waterError, setup); return false; }
        MonoPublicMgr.Instance.AddUpdatEvent(AdvanceLevelCognition);
        return true;
    }

    static bool CanSpawnSpirit(SceneLevelSetup setup)
    {
        if (setup == null || setup.SpiritPrefab == null) return false;
        if (!setup.ValidateRisingWaterConfiguration(out string waterError))
        { Debug.LogError(waterError, setup); return false; }
        Player configured = setup.SpiritPrefab.GetComponent<Player>();
        Vector2 spiritPoint = setup.SpiritPoint, bodyPoint = setup.BodyPoint;
        return configured != null && configured.Identity == PlayerIdentity.Spirit
            && Finite(spiritPoint) && Finite(bodyPoint)
            && Finite(setup.SpiritPose.z) && Finite(setup.BodyPose.z);
    }

    static bool Finite(Vector2 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
        && !float.IsNaN(point.y) && !float.IsInfinity(point.y);

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>出生标记的深度只写角色根节点，平面坐标仍走原来的刷新。</summary>
    static void PlaceDepth(Player actor, Vector3 pose)
    {
        if (actor == null || !Finite(pose.z)) return;
        Vector3 position = actor.transform.position;
        position.z = pose.z;
        actor.transform.position = position;
    }

    static void DestroyActor(ref Player actor)
    {
        if (actor != null) Object.Destroy(actor.gameObject);
        actor = null;
    }

    void EnsureBody(int serial, UnityAction<bool> ready)
    {
        if (serial != loadSerial) return;
        if (body != null)
        {
            // 安全：已经留下的对象必须是实体，不能把精神体当成跨关角色。
            if (body.Identity != PlayerIdentity.Body)
            {
                Debug.LogError("已有角色的身份不是实体。", body);
                ready?.Invoke(false);
                return;
            }
            ready?.Invoke(true);
            return;
        }
        string model = InputControlMgr.Instance.PlayerModleName;
        // 安全：没有实体预设名时不能凭空造玩家。
        if (string.IsNullOrEmpty(model))
        {
            Debug.LogError("没有配置实体预设。");
            ready?.Invoke(false);
            return;
        }
        AddresableMge.Instance.LoadAssetAsyncI<GameObject>(model, handle =>
        {
            if (serial != loadSerial) return;
            // 安全：地址资源加载失败时保留菜单退出入口，不实例化空资源或写进度。
            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                || handle.Result == null)
            {
                Debug.LogError($"实体预设加载失败：{model}。");
                ready?.Invoke(false);
                return;
            }
            if (body == null)
            {
                GameObject created = Object.Instantiate(handle.Result);
                Object.DontDestroyOnLoad(created);
                Player player = created.GetComponent<Player>();
                // 安全：没有玩家组件，或身份不是实体时，不留下这个对象。
                if (player == null || player.Identity != PlayerIdentity.Body)
                {
                    Debug.LogError(player == null ? "实体预设上没有玩家组件。" : "实体预设的身份不是实体。", created);
                    Object.Destroy(created);
                    ready?.Invoke(false);
                    return;
                }
                body = player;
            }
            ready?.Invoke(true);
        });
    }

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
    /// <summary>
    /// 异步场景切换
    /// </summary>
    /// <param name="sceneName">场景名</param>
    /// <param name="callBack">回调</param>
    /// <param name="percentage">任务权重占比</param>
    public void LoadSceneAsync(string sceneName,UnityAction callBack,float percentage=1f)
    {
        AsyncOperation tion = SceneManager.LoadSceneAsync(sceneName);
        MonoPublicMgr.Instance.StartCoroutine(Load(tion,callBack,percentage));
    }

    IEnumerator Load(AsyncOperation ao,UnityAction action,float percentage)
    {
        while (!ao.isDone)
        {
            //事件分发，用于外部获取加载进度
            EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene,ao.progress* percentage);
            yield return 0;
        }

        EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene,1 * percentage);
        action?.Invoke();
    }
    /// <summary>
    /// 加载到游戏场景以及资源加载1
    /// </summary>
    /// <param name="name">场景名</param>
    /// <param name="resAndNum">加载资源名称和缓存池大小</param>

    /// <summary>旧入口会按名字预热缓存池再实例化玩家。新关卡流程不再调用；台阶物件留到逻辑收尾再清。</summary>
    public void LoadResToChangeSence(string name,Dictionary<string,int> resAndNum=null)
    {
        int task = 0;

        GameObject tobj = null;
        void ToStart()
        {
            if (task == 2)
            {

                GameObject created = GameObject.Instantiate(tobj);
                // 现有单角色场景由加载组装显式选目标，预设体 Awake 不再抢控制。
                InputControlMgr.Instance.BindPlayer(created.GetComponent<Player>());
                
                
                UIMgr.Instance.HideOneUI<LoadingPanel>();
                EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene, 0.1f);
            }
        }
        UIMgr.Instance.ShowOneUI<LoadingPanel>();

        LoadSceneAsync(name, () =>
        {
            //print("场景切换成功");
            //台阶对象预热

            if (resAndNum != null)
            {
                foreach (var e in resAndNum)
                {
                    PoolMgr.Instance.Preload(e.Key, e.Value);
                }

            }

            task += 1;
            ToStart();
        }, 0.4f);


        //玩家加载
        AddresableMge.Instance.LoadAssetAsyncI<GameObject>(InputControlMgr.Instance.PlayerModleName, (obj) =>
        {
            tobj = obj.Result;

            task += 1;
            EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene, 0.4f);
            ToStart();
        });
    }
}
