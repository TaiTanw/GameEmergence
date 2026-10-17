#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>真实非教学关的探索、购买、逃生和失败回归；临时配置只存在于 Play Mode，不保存场景。</summary>
public static class P8P9LevelFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();
    static readonly List<GameObject> fixtures = new List<GameObject>();
    static string progressDirectory, previousProgressDirectory;
    static bool previousOptionsEnabled;
    static EnterPlayModeOptions previousOptions;
    static SimulationMode2D previousSimulation;
    static float previousTimeScale;
    static int stage, bodyId, spiritId, levelSceneHandle, teachingSetupId;
    static double deadline;
    static string[] sceneOrder;
    static Exception failure;
    static string runtimeError;
    static Player body, spirit;
    static PlayerLifecycleRules explorationRules, escapeRules, teachingRules;
    static PlayerEffectLedger ledger;
    static PlayerUpgradeCatalog fixtureCatalog;
    static SceneLevelSetup setup;
    static GameplayPanel panel;
    static BaseGround ground;
    static PhysicalBox box, storedBox;
    static WaterRegion water;
    static MapCell firstCell, secondCell;
    static SceneProp prop, handoffProp;
    static Vector3 propHome;
    static Vector2 boxHome, storedHome;
    static float groundDefault, waterDefault, waterBottom;
    static PlayerDeathRequest escapeDeath, teachingDeath;
    static Action failureChecks;
    static bool sawDarkening, sawBlack, sawBrightening;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("P8/P9 关卡流程检查要求新开的 batch editor。");
        previousProgressDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        progressDirectory = Path.GetFullPath(Path.Combine(".utmp/P89Validation", "Save-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(progressDirectory);
        DataAndInitMgr.debugPlayerProgressDirectory = progressDirectory;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        previousSimulation = Physics2D.simulationMode;
        previousTimeScale = Time.timeScale;
        passed.Clear(); fixtures.Clear(); stage = 0; failure = null; runtimeError = null; panel = null;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 180;
        Application.logMessageReceived += RecordRuntimeError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    static void Advance()
    {
        if (stage == 99)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("P8/P9 真实关卡流程超时，当前步骤=" + stage);
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (!Application.isPlaying) return;
            switch (stage)
            {
                case 0:
                    if (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) return;
                    sceneOrder = DataAndInitMgr.Instance.LoadLevelOrder().scenes;
                    Check(sceneOrder != null && sceneOrder.Length >= 2, "实际顺序提供教学和非教学关");
                    Time.timeScale = 0f;
                    Check(DataAndInitMgr.Instance.TryAddEndingContribution(new Vector2(10, 20), out _)
                        && DataAndInitMgr.Instance.SavePlayerProgress(1, new PlayerEffectLedger()), "P16 隔离进度准备非零已确认向量");
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 1;
                    break;
                case 1:
                    if (!Ready(sceneOrder[1])) return;
                    body = Actor("body"); spirit = Actor("spirit");
                    bodyId = body.GetInstanceID(); spiritId = spirit.GetInstanceID();
                    setup = SceneLevelSetup.Current;
                    levelSceneHandle = setup.gameObject.scene.handle;
                    explorationRules = Rules(); ledger = Effects();
                    Check(!setup.Teaching && explorationRules.Stage == PlayerGameplayStage.Exploration
                        && !explorationRules.IsSimulation && InputControlMgr.Instance.Player == spirit,
                        "真实非教学关从同一规则的精神体漫游开始");
                    UIMgr.Instance.GetOnePanel<GameplayPanel>(value => panel = value);
                    stage = 2;
                    break;
                case 2:
                    if (panel == null || !panel.gameObject.activeInHierarchy) return;
                    InRunningTime(() =>
                    {
                        Check(EnvironmentClickPlayModeChecks.Run(panel, spirit) > 0,
                            "实际保存关卡的地面屏幕点选与应用通过");
                        ConfigureFixtures();
                        CheckExplorationAndPurchase();
                    });
                    stage = 3;
                    break;
                case 3:
                    if (SceneChangeMgr.Instance.IsEscapeHandoff || spirit != null) return;
                    Check(Players().Length == 1, "逃生精神体实际销毁，仅留下长期实体");
                    InRunningTime(() => { CheckPurchaseHandoff(); CheckRisingWaterMenuAndFailure(); });
                    stage = 4;
                    break;
                case 4:
                    if (!FinishFailureTransition()) return;
                    InRunningTime(CheckZeroBudget);
                    stage = 10;
                    break;
                case 10:
                    if (SceneChangeMgr.Instance.IsEscapeHandoff || Actor("spirit") != null) return;
                    InRunningTime(CheckZeroBudgetHandoff);
                    InRunningTime(CheckEscapeCrush);
                    stage = 8;
                    break;
                case 8:
                    if (!FinishFailureTransition()) return;
                    Check(Rules().Stage == PlayerGameplayStage.Exploration && Actor("spirit") != null,
                        "负面逃生死亡后正常返回探索");
                    Time.timeScale = 1f;
                    Check(SceneChangeMgr.Instance.StartEscape()
                        && SceneChangeMgr.Instance.PurchaseEscapeAbility(OfferIndex(PlayerUpgradeKind.MapCost))
                        && SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities(),
                        "退出回归受理第三次精神体消散与实体交接");
                    stage = 11;
                    break;
                case 11:
                    if (SceneChangeMgr.Instance.IsEscapeHandoff || Actor("spirit") != null) return;
                    Check(body.TryRequestDeath(PlayerDeathCause.Abyss) && body.PendingDeath.HasValue
                        && !SceneChangeMgr.Instance.IsFailureRecovering, "第三次死亡先播放实体动画");
                    stage = 12;
                    break;
                case 12:
                    if (!SceneChangeMgr.Instance.IsFailureRecovering) return;
                    Check(SceneChangeMgr.Instance.ReturnToMenu(), "死亡动画结束、渐暗尚未完成时可以显式退出本关");
                    stage = 9;
                    break;
                case 9:
                    if (SceneManager.GetActiveScene().name != "BeginScenes" || Players().Length != 0 || SceneChangeMgr.Instance.IsLoading) return;
                    Check(!SceneChangeMgr.Instance.IsFailureRecovering && Time.timeScale == 1f
                        && !WorldPaused(typeof(BasicEntity)), "失败演出退出清理恢复时间与世界许可");
                    UIMgr.Instance.GetOnePanel<FailureRecoveryPanel>(value =>
                        Check(!value.gameObject.activeSelf, "失败演出退出撤销独立遮罩"));
                    DataAndInitMgr.Instance.SaveLevelIndex(0);
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 5;
                    break;
                case 5:
                    if (!Ready(sceneOrder[0])) return;
                    Check(SceneLevelSetup.Current.Teaching && Actor("body").GetInstanceID() != bodyId
                        && Actor("spirit") == null && Players().Length == 1,
                        "失败演出退出后重进教学创建唯一实体，不残留旧精神体");
                    body = Actor("body"); bodyId = body.GetInstanceID();
                    teachingRules = Rules(); teachingSetupId = SceneLevelSetup.Current.GetInstanceID();
                    Check(teachingRules.Stage == PlayerGameplayStage.Tutorial && !LevelStageMgr.Instance.HasExploration,
                        "教学组装独立死亡规则，未建立探索阶段");
                    Check(DataAndInitMgr.Instance.TryAddEndingContribution(new Vector2(7, 8), out _), "P16 教学死亡前准备本关贡献");
                    Check(body.TryRequestDeath(PlayerDeathCause.Abyss) && body.PendingDeath.HasValue
                        && SceneLevelSetup.Current.GetInstanceID() == teachingSetupId,
                        "教学死亡先播放实体动画，完成前不提前重载");
                    teachingDeath = body.PendingDeath.Value;
                    Check(!body.TryRequestDeath(PlayerDeathCause.OxygenDepleted), "教学同死亡周期不开始第二次重载");
                    stage = 6;
                    break;
                case 6:
                    if (!Ready(sceneOrder[0]) || SceneLevelSetup.Current.GetInstanceID() == teachingSetupId) return;
                    Check(Actor("body").GetInstanceID() == bodyId && Actor("spirit") == null && Players().Length == 1
                        && LevelStageMgr.Instance.LevelIndex == 0 && DataAndInitMgr.Instance.LoadLevelIndex() == 0,
                        "教学死亡实际重载同关，同实体和关序保持且无精神体");
                    Check(Rules() != teachingRules && Rules().Stage == PlayerGameplayStage.Tutorial
                        && !body.PendingDeath.HasValue && body.DeathStatus == PlayerDeathStatus.Handled,
                        "教学就绪后显式恢复死亡周期并组装新规则");
                    Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero
                        && DataAndInitMgr.Instance.ConfirmedEndingVector == new Vector3(10, 20, 0),
                        "P16 实际教学死亡重开不确认本关贡献");
                    Check(Rules().ReceiveDeath(teachingDeath) == PlayerDeathStatus.Rejected,
                        "已重开教学拒绝旧死亡请求，不再次加载");
                    Check(InputControlMgr.Instance.Player == body && body.Permissions.motionUpdates,
                        "教学重开交回唯一实体控制与运动许可");
                    Check(SceneChangeMgr.Instance.ReturnToMenu(), "流程完成从现有关卡退出到主菜单");
                    stage = 7;
                    break;
                case 7:
                    if (SceneManager.GetActiveScene().name != "BeginScenes" || Players().Length != 0 || SceneChangeMgr.Instance.IsLoading) return;
                    Check(Actor("body") == null && Actor("spirit") == null && Rules() == null
                        && InputControlMgr.Instance.Player == null && SceneLevelSetup.Current == null,
                        "退出释放角色、规则、控制与旧场景配置");
                    Check(!WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)),
                        "退出不留下世界暂停许可");
                    stage = 99;
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception error)
        {
            failure = error; Debug.LogException(error);
            stage = 99; EditorApplication.isPlaying = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }
    }

    static void ConfigureFixtures()
    {
        const float floor = 8000f;
        GameObject floorObject = NewObject("P89 temporary editable floor", new Vector2(8020f, floor - .5f));
        var physics = spirit.GetComponent<CharacterPhysics>();
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(physics);
        floorObject.layer = FirstLayer(config.groundLayer);
        floorObject.AddComponent<BoxCollider2D>().size = new Vector2(100f, 1f);
        ground = floorObject.AddComponent<BaseGround>();
        groundDefault = ground.SlowingEffect;
        Physics2D.SyncTransforms();

        Vector2 bodyPoint = RootAtCenter(body.GetComponent<CharacterPhysics>(), new Vector2(8000f,
            floor + body.GetComponent<CharacterPhysics>().BodyBounds.extents.y + .005f));
        Vector2 spiritPoint = RootAtCenter(physics, new Vector2(8010f, floor + physics.BodyBounds.extents.y + .005f));
        SetField(setup, "bodySpawn", NewObject("P89 body point", bodyPoint).transform);
        SetField(setup, "spiritSpawn", NewObject("P89 spirit point", spiritPoint).transform);
        SetField(setup, "trialStart", NewObject("P89 trial point", spiritPoint + Vector2.right * 2f).transform);
        body.RefreshRuntime(bodyPoint); spirit.RefreshRuntime(spiritPoint);

        box = BoxOnFloor(8020f, floor); storedBox = BoxOnFloor(8040f, floor);
        boxHome = box.DebugActualPosition; storedHome = storedBox.DebugActualPosition;
        GameObject waterObject = NewObject("P89 temporary rising water", new Vector2(8020f, floor - 5f));
        BoxCollider2D waterCollider = waterObject.AddComponent<BoxCollider2D>();
        waterCollider.size = new Vector2(100f, 4f); waterCollider.isTrigger = true;
        water = waterObject.AddComponent<WaterRegion>();
        Physics2D.SyncTransforms();
        SetField(setup, "risingWater", water); SetField(setup, "waterRiseSpeed", 1f);
        SetField(setup, "risingWaterMaximum", NewObject("P89 maximum water surface", new Vector2(8020f, floor + 30f)).transform);
        Check(setup.PrepareRisingWater(out _), "本关上涨水从实际制作矩形及最高点准备默认范围");
        waterDefault = water.SurfaceY; waterBottom = waterCollider.bounds.min.y;
        fixtureCatalog = ScriptableObject.CreateInstance<PlayerUpgradeCatalog>();
        foreach (PlayerUpgradeDefinition definition in fixtureCatalog.Definitions)
            definition.Enabled = definition.Kind == PlayerUpgradeKind.MapCost || definition.Kind == PlayerUpgradeKind.FormCost;
        fixtureCatalog.Get(PlayerUpgradeKind.MapCost).Label = "联测步伐强化";
        fixtureCatalog.Get(PlayerUpgradeKind.FormCost).Label = "联测高价选项";
        fixtureCatalog.Get(PlayerUpgradeKind.FormCost).Cost = 200f;
        Check(ledger.Configure(fixtureCatalog), "联测使用独立升级配置并按能力标识核对随机顺序");
        Resources.Load<PlayerAbilityPool>(PlayerAbilityPool.ResourceName).Entries = new List<PlayerAbilityOffer> {
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 1.2f),
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, false, .8f) };
        prop = Prop(new Vector2(7990f, floor + 1f));
        SetField(prop, "endingContribution", new Vector2(2, -3));
        propHome = prop.transform.position;

        firstCell = NewObject("P89 first map cell", new Vector2(8010f, floor + 3f)).AddComponent<MapCell>();
        secondCell = NewObject("P89 second map cell", new Vector2(8030f, floor + 3f)).AddComponent<MapCell>();
        foreach (MapCell cell in new[] { firstCell, secondCell })
        {
            SetField(cell, "width", 20f); SetField(cell, "height", 12f); SetField(cell, "cognitionCost", 2f);
        }
        var fixtureCells = new List<MapCell>();
        // 关卡制作可继续增加格子；临时联测仍遵守本关所有格子均须引用的规则。
        foreach (GameObject root in setup.gameObject.scene.GetRootGameObjects())
            fixtureCells.AddRange(root.GetComponentsInChildren<MapCell>(true));
        SetField(setup, "mapEnabled", true); SetField(setup, "mapCells", fixtureCells.ToArray());
        Check(explorationRules.BindExplorationMap(setup) && explorationRules.MapRecord.IsValid,
            "本关临时格子由性质配置关联并建立唯一记录");
        explorationRules.NotifyMapRelocated();
        RefreshPanel();
        MapView mapView = panel.transform.Find("MapArea").GetComponent<MapView>();
        typeof(MapView).GetMethod("Update", Hidden).Invoke(mapView, null);
        Check(((System.Collections.IDictionary)typeof(MapView).GetField("tiles", Hidden).GetValue(mapView)).Count == 0,
            "尚未探索时地图不创建未知格子的图形或点击热区");

        Check(ledger.RestoreConfirmed(new[] { 0, 0, 0, 0, 1, 0 }, new PlayerEffectRecord[0]),
            "夹具模拟此前确认的入包降费");
        ledger.CommitLevel();
        Check(ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.1f)
            && ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.3f),
            "夹具模拟本关两项临时运动效果");
        Check(ledger.CaptureConfirmedUpgradeLevels()[4] == 1 && ledger.CurrentLevelEffects.Count == 2,
            "此前降费已确认，本关运动两份未跨关");
    }

    static void CheckExplorationAndPurchase()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        Check(host.EditEnvironmentParameter(ground, 0, .5f), "真实漫游入口提交地面改值");
        Near(ground.SlowingEffect, .5f, "原环境来源持有提交值");
        MoveCenter(spirit.GetComponent<CharacterPhysics>(), prop.transform.position);
        TapInteract(spirit);
        Check(Used(prop) && !prop.gameObject.activeSelf, "漫游实际近处交互领取普通道具并登记已用");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == new Vector3(2, -3, 0), "P16 实际漫游道具领取登记本关二维向量");
        prop.transform.position += Vector3.right * 5f;
        CharacterPhysics physics = spirit.GetComponent<CharacterPhysics>();
        // 格外移动保留上一格；先进入另一格，再返回起始格，才能经正常移动点亮两格。
        MoveCenter(physics, new Vector2(8030f, 8003f)); explorationRules.AdvanceExplorationMap();
        MapView reveal = panel.transform.Find("MapArea").GetComponent<MapView>();
        typeof(MapView).GetMethod("Update", Hidden).Invoke(reveal, null);
        var revealedTiles = (System.Collections.IDictionary)typeof(MapView).GetField("tiles", Hidden).GetValue(reveal);
        Check(revealedTiles.Count == 1 && revealedTiles.Contains(secondCell) && !revealedTiles.Contains(firstCell),
            "只显示新点亮的单格，未点亮相邻格不提供标记和热区");
        RectTransform single = ((Image)revealedTiles[secondCell]).rectTransform;
        Near(single.anchoredPosition, Vector2.zero, "只有一格时按已亮范围居中，未知格不影响布局");
        MoveCenter(physics, new Vector2(8010f, 8003f)); explorationRules.AdvanceExplorationMap();
        MoveCenter(physics, new Vector2(8030f, 8003f)); explorationRules.AdvanceExplorationMap();
        Check(explorationRules.MapRecord.IsLit(firstCell) && explorationRules.MapRecord.IsLit(secondCell),
            "碰撞框位置经实际地图采样入口点亮两格");
        CheckBlockedFormNotice(host, physics);
        Check(host.SetSimulation(true), "实际场景入口从漫游进入模拟");
        RefreshPanel();
        Check(!panel.GetComponentInChildren<EnvironmentSelectionFrames>(true).gameObject.activeSelf,
            "真实进入模拟后隐藏漫游对象框线");
        explorationRules.AdvanceSimulationTime(.375f);
        RefreshPanel();
        Check(panel.transform.Find("CognitionArea").gameObject.activeInHierarchy
            && panel.FindUIObj<Text>("CognitionText").text == $"{Mathf.Floor(explorationRules.Cognition.Remaining):0} / {SceneLevelSetup.Current.InitialCognition:0.##}",
            "非整数认知面板向下取整显示，规则余额继续保留小数");
        box.RefreshMotionState(boxHome + Vector2.right * 8f);
        storedBox.RefreshMotionState(storedHome + Vector2.right * 12f);
        Check(storedBox.Interact(new PlayerInteractRequest(spirit, PlayerInteractKind.LongPress, true))
            == PlayerInteractOutcome.Stored, "试验现场包含移动箱子和收纳箱子");
        float balance = explorationRules.Cognition.Remaining;
        Check(host.ResetTrial(), "现有试验入口完成当前格中心轻刷新");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == new Vector3(2, -3, 0), "P16 试验轻刷新保留本关向量");
        Near(physics.BodyBounds.center, secondCell.Center, "地图重置把碰撞框中心安置于当前格中心");
        Check(explorationRules.MapRecord.CurrentCell == secondCell, "地图重置后当前格保持同一目标");
        Near(box.DebugActualPosition, boxHome, "当前格试验重置恢复远处箱子默认");
        Near(storedBox.DebugActualPosition, storedHome, "当前格试验重置恢复另一处收纳箱默认");
        Check(!storedBox.IsStored && spirit.GetInstanceID() == spiritId && explorationRules.IsSimulation,
            "轻刷新退出收纳且保留同一模拟精神体");
        Check(!spirit._ActionData.isFlying && !WorldPaused(typeof(BasicEntity)) && host.CanResetTrial,
            "试验重置不切回漫游，继续模拟行走与世界运行");
        Near(ground.SlowingEffect, .5f, "试验轻刷新保留已编辑环境");
        Near(explorationRules.Cognition.Remaining, balance, "试验轻刷新保留转换与格子结算后的认知");
        Check(explorationRules.MapRecord.IsLit(firstCell) && explorationRules.MapRecord.IsLit(secondCell)
            && ledger.CurrentLevelEffects.Count == 2, "试验轻刷新保留地图及本关新增效果");
        Check(!prop.gameObject.activeSelf && prop.transform.position != propHome,
            "轻刷新保留道具领取与当前摆放");
        CheckMapUI();
        balance = explorationRules.Cognition.Remaining;

        handoffProp = Prop(physics.BodyBounds.center);
        typeof(SceneProp).GetMethod("OnTriggerEnter2D", Hidden).Invoke(handoffProp,
            new object[] { spirit.GetComponent<Collider2D>() });
        TapInteract(spirit);
        Check(!Used(handoffProp), "模拟精神体已在道具范围内但不能领取");

        box.RefreshMotionState(boxHome + Vector2.right * 9f);
        RefreshPanel();
        Check(panel.transform.Find("StartEscapeButton") == null
            && panel.transform.Find("EndExplorationNote").gameObject.activeInHierarchy && host.CanStartEscape,
            "真实探索界面提供不可点击便签并移除开始逃生按钮");
        P11UIInputChecks.EndExploration(panel, passed);
        Check(host.EscapeSelectionPending && explorationRules.EscapeSelectionPending
            && InputControlMgr.Instance.Player == null && !spirit.Permissions.motionUpdates
            && !body.Permissions.motionUpdates && WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)),
            "实际回车长按进入唯一准备入口，暂停角色与世界并撤销探索控制");
        Near(box.DebugActualPosition, boxHome + Vector2.right * 9f, "进入购买准备保持箱子现场，不提前复位");
        Check(!water.RisingAllowed, "等待购买时水不取得上涨许可");
        int movementIndex = OfferIndex(PlayerUpgradeKind.MapCost);
        int expensiveIndex = OfferIndex(PlayerUpgradeKind.FormCost);
        float upgradeCost = host.EscapeOffers[movementIndex].Cost;
        Check(!host.StartEscape() && !host.PurchaseEscapeAbility(-1) && !host.PurchaseEscapeAbility(expensiveIndex),
            "连续开始、非法索引与买不起选项均拒绝");
        Near(explorationRules.Cognition.Remaining, balance, "拒绝操作不产生部分扣费");
        Check(host.ToggleGameMenu() && host.GameMenuOpen && Time.timeScale == 0f
            && !host.PurchaseEscapeAbility(movementIndex), "选择期间仍可暂停菜单，暂停时不可提交购买");
        Check(host.ToggleGameMenu() && !host.GameMenuOpen && Time.timeScale == 1f
            && host.EscapeSelectionPending && WorldPaused(typeof(BasicEntity)) && InputControlMgr.Instance.Player == null,
            "选择期间关闭菜单保留结算冻结与空控制目标");
        RefreshPanel();
        Check(panel.FindUIObj<Text>("CognitionText").text == $"{Mathf.Floor(explorationRules.Cognition.Remaining):0} / {SceneLevelSetup.Current.InitialCognition:0.##}",
            "购买等待时无受控角色也沿用认知向下取整显示");
        typeof(EscapeChoiceView).GetMethod("Refresh", Hidden).Invoke(panel.GetComponentInChildren<EscapeChoiceView>(), null);
        Check(panel.transform.Find("EscapeChoiceArea/Choices/EscapeBudgetText").GetComponent<Text>().text
            == $"剩余认知 {explorationRules.Cognition.Remaining:0.##}",
            "逃生准备按共享认知显示当前实际余额");
        Button purchaseButton = panel.transform.Find("EscapeChoiceArea/Choices/CostPage/Content/MapCostRow/MapCostRowPurchaseButton")?.GetComponent<Button>();
        Button expensiveButton = panel.transform.Find("EscapeChoiceArea/Choices/CostPage/Content/FormCostRow/FormCostRowPurchaseButton")?.GetComponent<Button>();
        Check(purchaseButton != null && purchaseButton.gameObject.activeInHierarchy && purchaseButton.interactable
            && expensiveButton != null && !expensiveButton.interactable,
            "本次固定候选按同一预算显示可购与不可支付选项");
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            GameMenuInputChecks.Capture("p89-escape-choice.png");
        purchaseButton.onClick.Invoke();
        Check(host.EscapeSelectionPending && !host.IsEscapeHandoff && Actor("spirit") == spirit
            && InputControlMgr.Instance.Player == null && ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 1,
            "实际能力按钮提交一次等级购买并留在准备面板");
        Near(explorationRules.Cognition.Remaining, balance - upgradeCost,
            "购买按配置价格仅扣一次共享认知");
        Button nextButton = panel.transform.Find("EscapeChoiceArea/Choices/NextEscapeButton")?.GetComponent<Button>();
        Check(nextButton != null && nextButton.interactable, "能力购买后独立下一步仍可使用");
        nextButton.onClick.Invoke();
        Check(host.EscapeAutomaticReady && host.BeginEscapeFromAbilities(), "第二页确认后开始原消散交接");
        Check(!host.EscapeSelectionPending && host.IsEscapeHandoff, "下一步最终确认后才开始精神体消散");
        RefreshPanel();
        Check(!panel.transform.Find("CognitionArea").gameObject.activeInHierarchy,
            "确认交接即隐藏顶部认知条和白条");
        Check(!host.PurchaseEscapeAbility(0) && !host.StartEscape(), "已完成购买与交接不重复执行");
        Near(box.DebugActualPosition, boxHome + Vector2.right * 9f, "消散完成前仍保持购买现场");
        Check(!water.RisingAllowed && WorldPaused(typeof(BasicEntity)), "消散及镜头交接前不开放水与世界");
    }

    static void CheckBlockedFormNotice(SceneChangeMgr host, CharacterPhysics physics)
    {
        Check(!host.CanResetTrial && !host.ResetTrial(), "漫游仍不能借用试验重置");
        float balance = explorationRules.Cognition.Remaining;
        float cooldown = explorationRules.ManualFormCooldownRemaining;
        GameObject blocker = NewObject("Form restore notice blocker", physics.BodyBounds.center);
        blocker.layer = ground.gameObject.layer;
        SceneManager.MoveGameObjectToScene(blocker, setup.gameObject.scene);
        blocker.AddComponent<BoxCollider2D>().size = new Vector2(8f, 8f);
        Physics2D.SyncTransforms();
        try
        {
            Check(!host.SetSimulation(true) && !explorationRules.IsSimulation && spirit._ActionData.isFlying,
                "物体内部拒绝恢复行走并保持原漫游形态");
            Near(explorationRules.Cognition.Remaining, balance, "占用拒绝不扣认知");
            Near(explorationRules.ManualFormCooldownRemaining, cooldown, "占用拒绝不新建转换冷却");
            RefreshPanel();
            RectTransform popup = (RectTransform)panel.transform.Find("MessageArea");
            Check(popup.gameObject.activeSelf && panel.FindUIObj<Text>("RefreshMessageText").text == "无法在物体内部转换"
                && popup.anchorMin.y == 0f && popup.anchorMax.y == 0f,
                "真实底部提示框显示物体内部转换拒绝原因");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                GameMenuInputChecks.Capture("p11-form-blocked-notice.png");
        }
        finally { Object.DestroyImmediate(blocker); Physics2D.SyncTransforms(); }
    }

    static void CheckPurchaseHandoff()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        RefreshPanel();
        Check(!panel.transform.Find("CognitionArea").gameObject.activeInHierarchy,
            "正式逃生持续隐藏顶部认知条");
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            GameMenuInputChecks.Capture("p11-escape-hidden-cognition.png");
        Check(Rules().Stage == PlayerGameplayStage.Escape && !host.EscapeSelectionPending
            && Actor("spirit") == null && Actor("body") == body && InputControlMgr.Instance.Player == body,
            "购买完成只交给同一实体，退出精神体与选择暂停");
        Check(water.RisingAllowed && !WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)),
            "逃生交接恢复世界并开放本关水上涨");
        Near(box.DebugActualPosition, boxHome, "未保留现场的消散完成后才复位全关箱子");
        Near(ground.SlowingEffect, .5f, "开始逃生保留本关环境改值");
        Check(ledger.CurrentLevelEffects.Count == 2 && ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 1
            && ledger.CaptureConfirmedUpgradeLevels()[4] == 1, "购买等级加入本关待确认记录，未重复确认此前降费等级");
        Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed,
            (float)ledger.ResolveMotionAttribute(PlayerAbilityKind.MovementSpeed, body.GetComponent<CharacterPhysics>().speed),
            "实体实际有效速度消费共用购买效果");
        escapeRules = Rules();
        TapInteract(body);
        Check(!Used(handoffProp) && handoffProp.gameObject.activeSelf,
            "真实购买交接后远处本体交互不能继承精神体道具范围");
        Vector2 bodyReturn = body.GetComponent<CharacterPhysics>().DebugActualPosition;
        MoveCenter(body.GetComponent<CharacterPhysics>(), handoffProp.transform.position);
        TapInteract(body);
        Check(Used(handoffProp) && !handoffProp.gameObject.activeSelf,
            "交接后本体实际靠近可交互领取，无需沿用进入缓存");
        body.RefreshRuntime(bodyReturn);
    }

    static void CheckRisingWaterMenuAndFailure()
    {
        // P15 使用实际预设参与现有逃生／菜单／失败流程，不保存这些关卡夹具。
        GameObject cartObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "MechanismCart.prefab"),
            new Vector3(8050f, 8000.775f, 0f), Quaternion.identity);
        SceneManager.MoveGameObjectToScene(cartObject, setup.gameObject.scene); fixtures.Add(cartObject);
        MechanismCart cart = cartObject.GetComponent<MechanismCart>();
        Vector2 cartHome = cart.DebugActualPosition;
        GameObject plateObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "PressurePlate.prefab"),
            new Vector3(8058f, 8000.125f, 0f), Quaternion.identity);
        SceneManager.MoveGameObjectToScene(plateObject, setup.gameObject.scene); fixtures.Add(plateObject);
        MechanismPressurePlate plate = plateObject.GetComponent<MechanismPressurePlate>();
        Taijie door = (Taijie)plate.TargetComponent;
        Vector3 doorHome = door.transform.position;
        PhysicalBox pressureBox = BoxOnFloor(8058f, 8000f);
        Vector2 pressureHome = pressureBox.DebugActualPosition;
        Physics2D.SyncTransforms();
        MoveCenter(body.GetComponent<CharacterPhysics>(), cart.BodyBounds.center);
        SceneProp cartProp = Prop(cart.BodyBounds.center);
        Check(MechanismCart.FindInteractTarget(body) == cart,
            $"P15 实际小车可被发现 [ready={cart.IsMotionReady}, participating={cart.IsMotionParticipating}, player={body.Permissions.movement}/{body.Permissions.motionUpdates}, distance={cart.GetComponent<Collider2D>().Distance(body.GetComponent<Collider2D>()).distance}]");
        TapInteract(body);
        Check(cart.IsOn && !Used(cartProp), $"P15 真实受控实体点按开启小车，同次不领取重叠道具 [on={cart.IsOn}, prop={Used(cartProp)}]");
        TapInteract(body); Check(!cart.IsOn && !Used(cartProp), "P15 再次点按关闭，仍独占该次交互");
        TapInteract(body);
        MethodInfo fixedTick = typeof(MonoPublicMgr).GetMethod("FixedUpdate", Hidden);
        for (int i = 0; i < 4; i++) fixedTick.Invoke(MonoPublicMgr.Instance, null);
        Check(plate.IsPressed && plate.OccupantCount == 1, "P15 正式逃生的实际调度采集箱子顶部接触并启动门");
        typeof(Taijie).GetField("nowtime", Hidden).SetValue(door, AudioSettings.dspTime - .75d);
        typeof(Taijie).GetMethod("FixFun", Hidden).Invoke(door, null);
        Check(door.transform.position.y > doorHome.y + 1f && cart.DebugActualPosition.x > cartHome.x,
            "P15 逃生运行小车和关联实心门");
        Vector2 pausedCart = cart.DebugActualPosition;
        Vector3 pausedDoor = door.transform.position;
        float before = water.SurfaceY;
        for (int i = 0; i < 5; i++) typeof(WaterRegion).GetMethod("AdvanceWaterLevel", Hidden).Invoke(water, null);
        Check(water.SurfaceY > before, "实际水相位按逃生上涨许可推进水面");
        Near(water.GetComponent<BoxCollider2D>().bounds.min.y, waterBottom, "实际上涨固定制作水底");
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && SceneChangeMgr.Instance.GameMenuOpen
            && Time.timeScale == 0f && WorldPaused(typeof(BasicEntity)), "逃生菜单复用现有世界暂停");
        fixedTick.Invoke(MonoPublicMgr.Instance, null);
        Near(cart.DebugActualPosition, pausedCart, "P15 菜单暂停小车");
        Near((Vector2)door.transform.position, pausedDoor, "P15 菜单暂停实心门");
        Check(plate.IsPressed && cart.IsOn && door.Delta == Vector2.zero, "P15 暂停保留压感／开关，清本步携带贡献");
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && !SceneChangeMgr.Instance.GameMenuOpen
            && Time.timeScale == 1f && !WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie))
            && InputControlMgr.Instance.Player == body && InputControlMgr.Instance.InputEnabled,
            "关闭逃生菜单恢复实体与世界，没有误按漫游保持暂停");
        box.RefreshMotionState(boxHome + Vector2.right * 15f);
        storedBox.RefreshMotionState(storedHome + Vector2.right * 18f);
        int previousSpiritId = spiritId;
        var observer = new DeathObserver(escapeRules);
        body.BindDeathReceiver(observer);
        GameObject electricObject = NewObject("P15 escape fixed contact", body.GetComponent<CharacterPhysics>().BodyBounds.center);
        electricObject.AddComponent<BoxCollider2D>().isTrigger = true;
        electricObject.AddComponent<BaseGround>(); electricObject.AddComponent<FixedContactHazard>();
        Physics2D.SyncTransforms();
        typeof(Player).GetMethod("UpdateBoxRelations", Hidden).Invoke(body, null);
        Check(observer.Count == 1 && observer.Last.cause == PlayerDeathCause.ContactHazard,
            "P15 真实固定危险触发器经统一死亡请求触发完整失败重开");
        escapeDeath = observer.Last;
        Check(!SceneChangeMgr.Instance.IsFailureRecovering && escapeRules.DeathPending && body.PendingDeath.HasValue
            && body.DeathStatus == PlayerDeathStatus.Pending && Rules() == escapeRules && Actor("spirit") == null,
            "接触即死先等待实体死亡动画，不提前渐暗或重建探索");
        Near(box.DebugActualPosition, boxHome + Vector2.right * 15f, "死亡动画开始保持箱子现场");
        Check(!InputControlMgr.Instance.InputEnabled && body.Permissions.motionUpdates
            && !body.Permissions.movement && !body.Permissions.resourceUpdates, "死亡动画停操作与资源，保留被动运动");
        Check(escapeRules.ReceiveDeath(escapeDeath) == PlayerDeathStatus.Pending
            && !SceneChangeMgr.Instance.IsFailureRecovering, "同死亡周期动画期间重放不提前启动恢复");
        sawDarkening = sawBlack = sawBrightening = false;
        failureChecks = () =>
        {
        Player replacement = Actor("spirit");
        Check(replacement != null && replacement.GetInstanceID() != previousSpiritId && replacement._ActionData.isFlying
            && Actor("body") == body && body.GetInstanceID() == bodyId && InputControlMgr.Instance.Player == replacement,
            "失败保留长期实体并创建新的受控飞行精神体");
        Check(SceneLevelSetup.Current == setup && setup.gameObject.scene.handle == levelSceneHandle
            && LevelStageMgr.Instance.LevelIndex == 1 && DataAndInitMgr.Instance.LoadLevelIndex() == 1,
            "逃生失败没有重载场景或改变当前关序号");
        Check(Rules() != escapeRules && Rules().Stage == PlayerGameplayStage.Exploration && !Rules().IsSimulation,
            "失败重新组装唯一探索漫游规则");
        Near(ground.SlowingEffect, groundDefault, "重刷新由原环境来源恢复制作默认");
        Near(box.DebugActualPosition, boxHome, "重刷新恢复全关第一个箱子");
        Near(storedBox.DebugActualPosition, storedHome, "重刷新恢复全关另一个箱子");
        Near(cart.DebugActualPosition, cartHome, "P15 失败恢复小车默认位置");
        Check(!cart.IsOn && cart.GetPlanarVelocity() == Vector2.zero && !plate.IsPressed,
            "P15 失败恢复小车默认开关／速度并清压感状态");
        Near((Vector2)door.transform.position, doorHome, "P15 失败恢复门默认位置");
        Near(pressureBox.DebugActualPosition, pressureHome, "P15 失败恢复压住机关的箱子默认位置");
        Check(door.Delta == Vector2.zero, "P15 失败不留下旧平台位移");
        Near(water.SurfaceY, waterDefault, "重刷新立即恢复本关默认水面");
        Check(!water.RisingAllowed && WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)),
            "新漫游停止水上涨并保持世界暂停");
        float occupied = 0f;
        foreach (MapCell cell in Rules().MapRecord.Cells) occupied += Rules().MapRecord.ReservedCognition(cell);
        Near(Rules().Cognition.Remaining + occupied, setup.InitialCognition, "失败认知重建后只发生新探索正常出生格占用");
        Check(!escapeRules.MapRecord.IsLit(firstCell) && !escapeRules.MapRecord.IsLit(secondCell)
            && !Rules().MapRecord.IsLit(secondCell), "旧格子记录清空，新规则不继承远处探索点亮");
        Check(ledger.CurrentLevelEffects.Count == 0 && ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 0
            && ledger.CaptureConfirmedUpgradeLevels()[4] == 1,
            "失败撤销本关购买和运动效果，保留此前确认降费");
        Check(prop.gameObject.activeSelf && prop.transform.position == propHome
            && !(bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop),
            "重刷新由道具原拥有者恢复默认位置与未领取状态");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero
            && DataAndInitMgr.Instance.ConfirmedEndingVector == new Vector3(10, 20, 0),
            "P16 实际逃生失败清本关向量并保留已确认向量");
        Near(ledger.MovementMultiplier, 1f, "失败撤销所有本局水平速度效果");
        Near(ledger.JumpMultiplier, 1f, "失败恢复基础起跳倍率，永久降费仍保留");
        Near(replacement.GetComponent<CharacterPhysics>().EffectiveJumpSpeed,
            replacement.GetComponent<CharacterPhysics>().upSpeed,
            "新精神体起跳恢复基础值，不恢复永久运动");
        int replacementId = replacement.GetInstanceID();
        Check(escapeRules.ReceiveDeath(escapeDeath) == PlayerDeathStatus.Handled
            && Rules().ReceiveDeath(escapeDeath) == PlayerDeathStatus.Rejected
            && Actor("spirit").GetInstanceID() == replacementId,
            "同死亡周期重放不会再次刷新或重建精神体");
        };
    }

    static void CheckEscapeCrush()
    {
        Vector2 center = body.GetComponent<CharacterPhysics>().BodyBounds.center;
        GameObject doorObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "MechanismDoor.prefab"),
            center + Vector2.left * 1.77f, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(doorObject, setup.gameObject.scene); fixtures.Add(doorObject);
        Taijie door = doorObject.GetComponent<Taijie>();
        Vector2 doorHome = door.transform.position;
        typeof(Taijie).GetField("mechanismOffset", Hidden).SetValue(door, new Vector2(1.55f, 0f));
        GameObject wall = NewObject("P15 escape crush wall", center + Vector2.right * 5.53f);
        wall.layer = LayerMask.NameToLayer("Ground");
        wall.AddComponent<BoxCollider2D>().size = new Vector2(10f, 8f); wall.AddComponent<Wall>();
        var observer = new DeathObserver(Rules()); body.BindDeathReceiver(observer);
        door.TrySetMechanismActive(true);
        typeof(Taijie).GetField("nowtime", Hidden).SetValue(door, AudioSettings.dspTime - 2d);
        // 使用真实调度：相位 0 推门，唯一相位 4 模拟，相位 5 残留夹击死亡。
        typeof(MonoPublicMgr).GetMethod("FixedUpdate", Hidden).Invoke(MonoPublicMgr.Instance, null);
        Check(observer.Count == 1 && observer.Last.cause == PlayerDeathCause.Crushed,
            $"P15 正式逃生门推向固定墙夹住实体，经模拟后死亡入口完整重开 [count={observer.Count}, cause={observer.Last.cause}, door={body.GetComponent<Collider2D>().Distance(door.GetComponent<Collider2D>()).distance}, wall={body.GetComponent<Collider2D>().Distance(wall.GetComponent<Collider2D>()).distance}]");
        Check(!SceneChangeMgr.Instance.IsFailureRecovering && Rules().DeathPending
            && Rules().Stage == PlayerGameplayStage.Escape, "真实夹击也先等待实体动画，不立即复位门");
        sawDarkening = sawBlack = sawBrightening = false;
        failureChecks = () =>
        {
        Near((Vector2)door.transform.position, doorHome, "P15 夹击失败恢复机关门默认位置");
        Check(!(bool)typeof(Taijie).GetField("easedMotion", Hidden).GetValue(door)
            && door.Delta == Vector2.zero && WorldPaused(typeof(BasicEntity)),
            "P15 夹击复位清门命令与携带贡献，返回漫游暂停");
        };
    }

    static bool FinishFailureTransition()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        if (!host.IsFailureRecovering && Rules().Stage == PlayerGameplayStage.Escape)
        {
            if (!body.PendingDeath.HasValue || !Rules().DeathPending || InputControlMgr.Instance.InputEnabled)
                throw new InvalidOperationException("实体动画等待丢失死亡请求或提前开放输入。");
            return false;
        }
        if (host.IsFailureRecovering)
        {
            if (InputControlMgr.Instance.InputEnabled || host.CanTeleportMap || host.CanResetTrial)
                throw new InvalidOperationException("失败演出提前放开操作、地图传送或重置。");
            UIMgr.Instance.GetOnePanel<FailureRecoveryPanel>(value =>
            {
                if (!value.gameObject.activeInHierarchy) return;
                if (!value.GetComponent<Image>().raycastTarget)
                    throw new InvalidOperationException("失败遮罩未阻挡界面点击。");
                if (Rules().Stage == PlayerGameplayStage.Escape)
                {
                    if (value.Weight > 0f && value.Weight < 1f) sawDarkening = true;
                    if (value.Weight == 1f) sawBlack = true;
                }
                else if (!sawBrightening && value.Weight > 0f && value.Weight < 1f)
                {
                    sawBrightening = true;
                    Near(Rules().Cognition.Remaining, setup.InitialCognition, "全黑复位后渐亮期间认知保持初始容量");
                    Check(!Rules().MapRecord.IsLit(firstCell) && !Rules().MapRecord.IsLit(secondCell),
                        "渐亮期间新探索尚未正常采样点亮出生格");
                    Check(Time.timeScale == 0f && !InputControlMgr.Instance.InputEnabled,
                        "渐亮期间世界和输入仍暂停");
                }
            });
            return false;
        }
        Check(sawDarkening && sawBlack && sawBrightening, "真实帧序观察到渐暗、全黑后复位、渐亮三个阶段");
        Check(InputControlMgr.Instance.InputEnabled && WorldPaused(typeof(BasicEntity))
            && Rules().Stage == PlayerGameplayStage.Exploration, "渐亮结束才恢复漫游输入，世界仍暂停");
        RefreshPanel();
        Check(panel.transform.Find("CognitionArea").gameObject.activeInHierarchy,
            "逃生失败恢复探索时顶部认知条重新显示");
        Time.timeScale = 0f; // 夹具继续手动推进物理，不让自动帧移动验证位置。
        failureChecks(); failureChecks = null;
        return true;
    }

    static void CheckZeroBudget()
    {
        Player replacement = Actor("spirit");
        PlayerLifecycleRules rules = Rules();
        MoveCenter(replacement.GetComponent<CharacterPhysics>(), new Vector2(8030f, 8003f));
        rules.NotifyMapRelocated();
        Check(SceneChangeMgr.Instance.SetSimulation(true), "新探索可再进入模拟以验证认知耗尽");
        rules.AdvanceSimulationTime(1000000f);
        Check(rules.Cognition.IsExhausted && rules.CognitionExhaustionRequested
            && rules.Stage == PlayerGameplayStage.Exploration, "耗尽先留下唯一请求，不伪造已完成逃生");
        typeof(SceneChangeMgr).GetMethod("AdvanceLevelCognition", Hidden).Invoke(SceneChangeMgr.Instance, null);
        Check(SceneChangeMgr.Instance.EscapeSelectionPending && !SceneChangeMgr.Instance.EscapeAutomaticReady
            && !SceneChangeMgr.Instance.IsEscapeHandoff && Actor("spirit") == replacement && InputControlMgr.Instance.Player == null,
            "耗尽先打开一级购买页，不提前消散");
        Near(rules.Cognition.Remaining, 0f, "耗尽余额保持为零");
        Check(SceneChangeMgr.Instance.NextEscapeStep() && ledger.NegativeAbility.HasValue && !ledger.PositiveAbility.HasValue,
            "零余额在第二页只获得一个本场负面");
        Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed, body.GetComponent<CharacterPhysics>().speed * .8f, "本场负面进入实际角色消费者");
        Check(SceneChangeMgr.Instance.BeginEscapeFromAbilities() && !SceneChangeMgr.Instance.NextEscapeStep()
            && !SceneChangeMgr.Instance.PurchaseEscapeAbility(0), "二级确认后不重复抽取或付款");
    }

    static void CheckZeroBudgetHandoff()
    {
        Check(Rules().Stage == PlayerGameplayStage.Escape && InputControlMgr.Instance.Player == body
            && water.RisingAllowed && !WorldPaused(typeof(BasicEntity)), "零预算消散及镜头交接完成才开放实体、水与世界");
        Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed,
            body.GetComponent<CharacterPhysics>().speed * .8f, "零预算实体交接后消费一次负面倍率");
    }

    static void CheckMapUI()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        RefreshPanel();
        Transform map = panel.transform.Find("MapArea");
        // 用户关卡原本已启用地图时，面板不会再次 OnEnable；临时换记录后先执行地图自己的刷新。
        typeof(MapView).GetMethod("Update", Hidden).Invoke(map.GetComponent<MapView>(), null);
        Transform tileObject = map.Find("MapCellsArea/MapCell_" + firstCell.GetInstanceID());
        Check(tileObject != null, "地图夹具换记录后更新为新记录的格子按钮");
        Button tile = tileObject.GetComponent<Button>();
        Check(map.gameObject.activeInHierarchy && tile.interactable, "真实地图显示手工格子并接可操作按钮");
        float before = explorationRules.Cognition.Remaining;
        box.RefreshMotionState(boxHome + Vector2.right * 7f);
        var state = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(spirit);
        state.stamina = 17f; state.oxygen = 29f;
        tile.onClick.Invoke();
        Near(spirit.GetComponent<CharacterPhysics>().BodyBounds.center, firstCell.Center, "地图按钮把碰撞框中心传送至格中心");
        Check(explorationRules.MapRecord.CurrentCell == firstCell, "地图传送后当前格为所点目标");
        Near(box.DebugActualPosition, boxHome + Vector2.right * 7f, "模拟地图传送保留移动箱子现场，不执行轻刷新");
        Near(state.stamina, 17f, "地图传送不补满体力");
        Near(state.oxygen, 29f, "地图传送不补满氧气");
        Check(explorationRules.IsSimulation && !WorldPaused(typeof(BasicEntity)), "模拟传送保留行走与世界运行");
        Near(explorationRules.Cognition.Remaining, before, "地图传送不额外收转换或格子费用");
        Transform cancel = map.Find("CancelMapCellButton");
        Check(cancel == null || !cancel.gameObject.activeSelf, "地图不开放主动取消记录／释放认知按钮");
        Check(map.Find("MapHintText").GetComponent<Text>().text.Contains("点击已点亮格子传送")
            && !map.Find("MapHintText").GetComponent<Text>().text.Contains("取消"), "真实面板明确说明传送与独立重置");
        SetField(explorationRules, "nextManualChange", 0f); // 夹具在同帧核对两种模式，不等待手动切换冷却。
        Check(host.SetSimulation(false), "地图联验可先切回漫游");
        float roamBefore = explorationRules.Cognition.Remaining;
        Check(host.TryTeleport(secondCell), "漫游也可点击已点亮格传送");
        Near(spirit.GetComponent<CharacterPhysics>().BodyBounds.center, secondCell.Center, "漫游传送仍按碰撞框中心安置");
        Check(spirit._ActionData.isFlying && !explorationRules.IsSimulation && WorldPaused(typeof(BasicEntity)),
            "漫游传送保留飞行形态与世界暂停");
        Near(box.DebugActualPosition, boxHome + Vector2.right * 7f, "漫游传送也保留箱子现场");
        Near(state.stamina, 17f, "漫游传送仍不补资源");
        Near(explorationRules.Cognition.Remaining, roamBefore, "漫游传送不增加认知消耗");
        SetField(explorationRules, "nextManualChange", 0f);
        Check(host.SetSimulation(true) && host.TryTeleport(firstCell), "返回模拟后地图仍只传送");
        Check(host.ResetTrial(), "独立试验重置按钮仍可恢复现场");
        Near(box.DebugActualPosition, boxHome, "只有独立试验重置才恢复箱子默认");
        Near(state.stamina, state.staminaMax, "独立试验重置保留原有资源恢复职责");
        Vector2 validPoint = spirit.GetComponent<CharacterPhysics>().DebugActualPosition;
        box.RefreshMotionState(boxHome + Vector2.right * 6f);
        MoveCenter(spirit.GetComponent<CharacterPhysics>(), new Vector2(7900f, 8003f));
        GameObject resetBlocker = NewObject("Trial reset center blocker", firstCell.Center);
        resetBlocker.layer = ground.gameObject.layer;
        SceneManager.MoveGameObjectToScene(resetBlocker, setup.gameObject.scene);
        resetBlocker.AddComponent<BoxCollider2D>().size = new Vector2(8f, 8f);
        Physics2D.SyncTransforms();
        try
        {
            Check(host.ResetTrial(), "人工中心即使被物体占用，重置也不进行卡住校验");
            Check(explorationRules.IsSimulation && !spirit._ActionData.isFlying,
                "占用中心重置仍保持实验行走，不借用形态切换");
        }
        finally { Object.DestroyImmediate(resetBlocker); Physics2D.SyncTransforms(); }
        Near(spirit.GetComponent<CharacterPhysics>().BodyBounds.center, firstCell.Center, "格外重置使用保留格中心");
        Near(box.DebugActualPosition, boxHome, "格外重置同样完成整关箱子轻刷新");
        // 只有尚未建立当前格才拒绝；临时换入未采样记录，保留原记录及其认知占用。
        LevelMapRecord previousMap = explorationRules.MapRecord;
        SetField(explorationRules, "<MapRecord>k__BackingField",
            new LevelMapRecord(setup.MapCells, explorationRules.Cognition, setup.gameObject.scene));
        try
        {
            MoveCenter(spirit.GetComponent<CharacterPhysics>(), new Vector2(7900f, 8003f));
            box.RefreshMotionState(boxHome + Vector2.right * 6f);
            Check(!host.ResetTrial(), "还没有当前格时独立重置在刷新前拒绝");
            Near(box.DebugActualPosition, boxHome + Vector2.right * 6f, "无合法当前格时不先恢复箱子");
            RefreshPanel();
            Check(panel.transform.Find("MessageArea").gameObject.activeSelf
                && panel.FindUIObj<Text>("RefreshMessageText").text.Contains("尚未进入任何地图格子"),
                "没有当前格的拒绝以实际界面提示原因");
        }
        finally { SetField(explorationRules, "<MapRecord>k__BackingField", previousMap); }
        spirit.GetComponent<CharacterPhysics>().RefreshMotionState(validPoint); explorationRules.NotifyMapRelocated();
        Check(host.ResetTrial(), "回到合法当前格后可继续完整重置");
        RefreshPanel();
        Check(!panel.transform.Find("MessageArea").gameObject.activeSelf, "成功重置清掉旧配置提示");
        typeof(MapView).GetMethod("Update", Hidden).Invoke(map.GetComponent<MapView>(), null);
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            GameMenuInputChecks.Capture("p89-map-simulation.png");
    }

    sealed class DeathObserver : IPlayerDeathReceiver
    {
        readonly IPlayerDeathReceiver next;
        public int Count { get; private set; }
        public PlayerDeathRequest Last { get; private set; }
        public DeathObserver(IPlayerDeathReceiver next) => this.next = next;
        public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request)
        { Count++; Last = request; return next.ReceiveDeath(request); }
    }

    static bool Ready(string scene) => SceneManager.GetActiveScene().name == scene && SceneLevelSetup.Current != null
        && !(bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance)
        && InputControlMgr.Instance.Player != null;
    static void InRunningTime(Action action)
    {
        float previous = Time.timeScale;
        try { Time.timeScale = 1f; action(); }
        finally
        {
            // 新失败演出已接管时间时，夹具不能把宿主的暂停覆盖回运行。
            if (!SceneChangeMgr.Instance.IsFailureRecovering) Time.timeScale = previous;
        }
    }
    static void RefreshPanel() => typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
    static GameObject NewObject(string name, Vector2 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position; fixtures.Add(item); return item;
    }
    static SceneProp Prop(Vector2 center)
    {
        GameObject item = NewObject("P89 actual trigger prop", center);
        BoxCollider2D range = item.AddComponent<BoxCollider2D>();
        range.isTrigger = true; range.size = Vector2.one * 2f;
        SceneProp result = item.AddComponent<SceneProp>();
        SetField(result, "clearsLevel", false);
        return result;
    }
    static bool Used(SceneProp item) => (bool)typeof(SceneProp).GetField("used", Hidden).GetValue(item);
    static void TapInteract(Player player)
    {
        var input = (PlayerInputData)typeof(Player).GetField("inputData", Hidden).GetValue(player);
        Check(input != null && InputControlMgr.Instance.Player == player, "交互由当前角色已绑定的输入消费");
        input.interactTapped = true;
        player.TickLogic(0f);
        input.interactTapped = false;
    }
    static PhysicalBox BoxOnFloor(float x, float floor)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/box.prefab");
        if (asset == null) throw new InvalidOperationException("缺少实际箱子夹具预设。");
        BoxCollider2D collider = asset.GetComponent<BoxCollider2D>();
        Vector3 scale = asset.transform.lossyScale;
        float y = floor + Mathf.Abs(collider.size.y * scale.y) * .5f - collider.offset.y * scale.y + .005f;
        GameObject item = Object.Instantiate(asset, new Vector2(x, y), Quaternion.identity);
        item.hideFlags = HideFlags.DontSave; fixtures.Add(item); Physics2D.SyncTransforms();
        return item.GetComponent<PhysicalBox>();
    }
    static Vector2 RootAtCenter(BasicEntity entity, Vector2 center) =>
        entity.DebugActualPosition + center - (Vector2)entity.BodyBounds.center;
    static void MoveCenter(BasicEntity entity, Vector2 center)
    { entity.RefreshMotionState(RootAtCenter(entity, center)); Physics2D.SyncTransforms(); }
    static int FirstLayer(LayerMask mask)
    {
        for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
        throw new InvalidOperationException("角色配置缺少地面查询层。");
    }
    static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    static Player Actor(string name) => (Player)typeof(SceneChangeMgr).GetField(name, Hidden).GetValue(SceneChangeMgr.Instance);
    static PlayerLifecycleRules Rules() => (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
    static PlayerEffectLedger Effects() => (PlayerEffectLedger)typeof(SceneChangeMgr).GetField("effects", Hidden).GetValue(SceneChangeMgr.Instance);
    static int OfferIndex(PlayerUpgradeKind kind)
    {
        for (int i = 0; i < SceneChangeMgr.Instance.EscapeOffers.Count; i++)
            if (SceneChangeMgr.Instance.EscapeOffers[i].Kind == kind) return i;
        throw new InvalidOperationException("联测候选缺少能力：" + kind);
    }
    static Player[] Players() => Object.FindObjectsOfType<Player>(true);
    static bool WorldPaused(Type type) => (bool)type.GetField("worldPaused", StaticHidden).GetValue(null);
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) <= .002f, message + $" [{actual} / {expected}]");
    static void Near(Vector2 actual, Vector2 expected, string message) => Check((actual - expected).magnitude <= .002f, message);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P8/P9 FLOW FAILED: " + message);
        passed.Add("PASS " + message);
    }
    static void RecordRuntimeError(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError ??= "真实关卡流程报错：" + message + "\n" + trace;
    }
    static void Finish()
    {
        EditorApplication.update -= Advance;
        Application.logMessageReceived -= RecordRuntimeError;
        if (failure == null && runtimeError != null) failure = new InvalidOperationException(runtimeError);
        foreach (GameObject item in fixtures) if (item != null) Object.DestroyImmediate(item);
        fixtures.Clear();
        if (fixtureCatalog != null) Object.DestroyImmediate(fixtureCatalog);
        try
        {
            DataAndInitMgr.Instance.ClearLevelProgress();
            if (Directory.Exists(progressDirectory)) Directory.Delete(progressDirectory);
            Check(!Directory.Exists(progressDirectory), "测试仅清理独占目录的新旧进度，未改玩家实际存档");
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        finally { DataAndInitMgr.debugPlayerProgressDirectory = previousProgressDirectory; }
        Time.timeScale = previousTimeScale; Physics2D.simulationMode = previousSimulation;
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        Directory.CreateDirectory(".utmp/P89Validation");
        File.WriteAllLines(".utmp/P89Validation/level-flow-checks.txt", passed);
        Debug.Log("P8/P9 level flow Play Mode: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
