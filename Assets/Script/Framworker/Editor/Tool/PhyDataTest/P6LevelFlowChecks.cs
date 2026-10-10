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

/// <summary>真实开始场景和 Addressables 的 P6/P7 Play Mode 衔接检查；不保存场景，关卡进度按原始字节恢复。</summary>
public static class P6LevelFlowChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly List<string> passed = new List<string>();
    private static readonly List<GameObject> fixtures = new List<GameObject>();
    private static string progressPath;
    private static byte[] previousProgress;
    private static bool hadProgress;
    private static bool previousOptionsEnabled;
    private static EnterPlayModeOptions previousOptions;
    private static SimulationMode2D previousSimulation;
    private static float previousTimeScale;
    private static string[] sceneOrder;
    private static Player body, oldSpirit;
    private static PlayerLifecycleRules oldRules;
    private static PlayerEffectLedger effects;
    private static string expectedLoadError;
    private static bool receivedExpectedLoadError, removeIncomingSetup;
    private static int bodyId, spiritId;
    private static GameplayPanel panel;
    private static GameMenuPanel menu;
    private static float expectedCognition, formCost;
    private static int stage;
    private static double deadline, cooldownEnds;
    private static Exception failure;
    private static string runtimeError;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("P6 关卡流程检查要求新开的 batch editor。");
        progressPath = Path.Combine(Application.persistentDataPath, "LevelProgress.txt");
        hadProgress = File.Exists(progressPath);
        previousProgress = hadProgress ? File.ReadAllBytes(progressPath) : null;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        previousSimulation = Physics2D.simulationMode;
        previousTimeScale = Time.timeScale;
        passed.Clear(); stage = 0; failure = null; runtimeError = null; panel = null; menu = null;
        expectedLoadError = null; receivedExpectedLoadError = false;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 180;
        Application.logMessageReceived += RecordRuntimeError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    private static void Advance()
    {
        if (stage == 9)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("P6/P7 真实关卡流程超时，当前步骤=" + stage);
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (!Application.isPlaying) return;
            switch (stage)
            {
                case 0:
                    if (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) return;
                    sceneOrder = DataAndInitMgr.Instance.LoadLevelOrder().scenes;
                    Check(sceneOrder != null && sceneOrder.Length >= 2, "真实顺序文件提供至少教学和非教学两关");
                    Time.timeScale = 0f;
                    DataAndInitMgr.Instance.SaveLevelIndex(0);
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 1;
                    break;
                case 1:
                    if (!Ready(sceneOrder[0])) return;
                    body = Actor("body"); bodyId = body.GetInstanceID();
                    Check(SceneLevelSetup.Current.Teaching && LevelStageMgr.Instance.Teaching, "第一关真实场景配置为教学");
                    Check(body.Identity == PlayerIdentity.Body && Actor("spirit") == null && Players().Length == 1,
                        "教学仅创建一个实体，没有精神体");
                    Check(InputControlMgr.Instance.Player == body && body.Permissions.motionUpdates,
                        "教学唯一输入目标是已恢复运动许可的实体");
                    Check(Rules() != null && Rules().Stage == PlayerGameplayStage.Tutorial
                        && LevelStageMgr.Instance.Stage == PlayerGameplayStage.Tutorial
                        && !LevelStageMgr.Instance.HasExploration && LevelStageMgr.Instance.CognitionRemaining == 0f
                        && !Rules().CognitionExhaustionRequested,
                        "教学死亡规则已组装但不参与探索认知计时或耗尽请求");
                    CheckCamera(body);
                    Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0, "教学安置完成才保存当前序号");
                    effects = (PlayerEffectLedger)typeof(SceneChangeMgr).GetField("effects", Hidden).GetValue(SceneChangeMgr.Instance);
                    Check(effects.GrantEffect(PlayerEffectKind.MovementMultiplier, .9f, PlayerEffectScope.Permanent),
                        "布置此前已经通关确认的永久效果");
                    effects.CommitLevel();
                    AddPendingEffects();
                    StartFailedNextLevel(true);
                    stage = 20;
                    break;
                case 20:
                    if (!FailedLoadFinished()) return;
                    CheckPendingEffects("下一关缺失配置后");
                    Check(SceneLevelSetup.Current == null && Actor("spirit") == null && !InputControlMgr.Instance.InputEnabled,
                        "配置缺失真实安置失败，不开放输入或创建精神体");
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 21;
                    break;
                case 21:
                    if (!Ready(sceneOrder[0])) return;
                    CheckPendingEffects("普通进入原关成功后");
                    StartFailedNextLevel(false);
                    stage = 22;
                    break;
                case 22:
                    if (!FailedLoadFinished()) return;
                    CheckPendingEffects("下一关无法创建精神体后");
                    Check(SceneLevelSetup.Current != null && Actor("spirit") == null && !InputControlMgr.Instance.InputEnabled,
                        "无效精神体配置真实安置失败，不开放输入");
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 23;
                    break;
                case 23:
                    if (!Ready(sceneOrder[0])) return;
                    SceneChangeMgr.Instance.EnterNextLevel();
                    CheckPendingEffects("被返回菜单作废前");
                    int cancelledSerial = (int)typeof(SceneChangeMgr).GetField("loadSerial", Hidden).GetValue(SceneChangeMgr.Instance);
                    Check(SceneChangeMgr.Instance.ReturnToMenu(), "真实返回菜单作废尚在加载的跨关请求");
                    Check(effects.ConfirmedPermanentEffects.Count == 1 && effects.CurrentLevelEffects.Count == 0,
                        "返回菜单只撤销本局新增，不把待确认永久效果提升为已确认");
                    // 作废的回调即使迟到并报告成功，也不能确认效果、写进度或重新开放输入。
                    typeof(SceneChangeMgr).GetMethod("FinishEnter", Hidden).Invoke(SceneChangeMgr.Instance,
                        new object[] { 1, cancelledSerial, true, true });
                    Check(effects.ConfirmedPermanentEffects.Count == 1 && DataAndInitMgr.Instance.LoadLevelIndex() == 0
                        && InputControlMgr.Instance.Player == null && !InputControlMgr.Instance.InputEnabled,
                        "旧请求的迟到成功回调不结算、不推进进度或交回控制");
                    stage = 24;
                    break;
                case 24:
                    if (SceneManager.GetActiveScene().name != "BeginScenes" || Players().Length != 0) return;
                    Check(effects.ConfirmedPermanentEffects.Count == 1 && DataAndInitMgr.Instance.LoadLevelIndex() == 0,
                        "作废请求及菜单实际加载结束仍不提前确认或推进进度");
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 25;
                    break;
                case 25:
                    if (!Ready(sceneOrder[0])) return;
                    body = Actor("body"); bodyId = body.GetInstanceID();
                    AddPendingEffects();
                    SceneChangeMgr.Instance.EnterNextLevel();
                    CheckPendingEffects("有效跨关开始加载后");
                    stage = 2;
                    break;
                case 2:
                    if (!Ready(sceneOrder[1])) return;
                    oldSpirit = Actor("spirit"); spiritId = oldSpirit.GetInstanceID();
                    oldRules = Rules();
                    CheckNonTeaching(oldSpirit);
                    expectedCognition = SceneLevelSetup.Current.InitialCognition;
                    formCost = SceneLevelSetup.Current.FormChangeCost;
                    Check(DataAndInitMgr.Instance.LoadLevelIndex() == 1, "跨关成功保存第二关序号");
                    Check(effects.CurrentLevelEffects.Count == 0 && effects.ConfirmedPermanentEffects.Count == 2,
                        "下一关安置成功才确认新增永久效果并清本局项");
                    Near(effects.MovementMultiplier, 1.08f, "成功跨关不重复叠加永久倍率");
                    Near(effects.JumpMultiplier, 1f, "成功跨关清除本局起跳效果");
                    Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed,
                        body.GetComponent<CharacterPhysics>().speed * 1.08f, "本体消费安置成功后的永久倍率");
                    Near(oldSpirit.GetComponent<CharacterPhysics>().EffectiveMovementSpeed,
                        oldSpirit.GetComponent<CharacterPhysics>().speed * 1.08f, "新精神体消费同一确认记录");
                    UIMgr.Instance.GetOnePanel<GameplayPanel>(value => panel = value);
                    stage = 3;
                    break;
                case 3:
                    if (panel == null || !panel.gameObject.activeInHierarchy) return;
                    Check(panel.transform.parent == UIMgr.Instance.GetUILayer(E_UILayer.Bottom), "游戏显示预设进入 Bottom 层");
                    Button resetButton = panel.transform.Find("ActionArea/ResetTrialButton")?.GetComponent<Button>();
                    Button applyButton = panel.transform.Find("ActionArea/EnvironmentEditArea/ApplyEnvironmentButton")?.GetComponent<Button>();
                    Check(resetButton != null && applyButton != null,
                        "游戏显示预设包含正式试验重置与环境应用按钮");
                    Check(panel.transform.Find("ActionArea/EnvironmentEditArea/EnvironmentValueInput")?.GetComponent<InputField>() != null
                        && panel.transform.Find("ActionArea/EnvironmentEditArea")?.GetComponent<EnvironmentEditView>() != null,
                        "环境输入与编辑显示位于实际操作区预设");
                    Check(panel.transform.Find("MapArea").gameObject.activeSelf == SceneChangeMgr.Instance.CanViewMap,
                        "实际地图区域按本关已启用地图显示，暂停不冒充未接地图");
                    foreach (string reserved in new[] { "ActionArea", "MessageArea" })
                        Check(panel.transform.Find(reserved) != null && !panel.transform.Find(reserved).gameObject.activeSelf,
                            "实际预设安排并隐藏预留节点 " + reserved);
                    GameMenuInputChecks.Capture("gameplay.png");
                    BaseGround editedGround = CheckEnvironmentEditUI();
                    // 测试布置：避开出生点附近的占用，检验实际场景模式入口；不写场景文件。
                    oldSpirit.GetComponent<CharacterPhysics>().RefreshMotionState(new Vector2(5000f, 5000f));
                    Check(SceneChangeMgr.Instance.SetSimulation(true), "场景模式入口成功进入模拟");
                    Check(LevelStageMgr.Instance.IsSimulation && !oldSpirit._ActionData.isFlying,
                        "场景入口同步提交模拟和行走");
                    expectedCognition = Mathf.Max(0f, expectedCognition - formCost);
                    Near(LevelStageMgr.Instance.CognitionRemaining, expectedCognition,
                        "进入模拟仅扣一次场景配置的转换费用");
                    Check(InputControlMgr.Instance.Player == oldSpirit && oldSpirit.GetInstanceID() == spiritId,
                        "模式切换保持精神体实例及唯一控制对象");
                    Check(!WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)),
                        "模拟成功后恢复实体和平台世界运行许可");
                    Check(oldSpirit.FormCooldownRemaining > 0f
                        && oldSpirit.FormCooldownRemaining <= SceneLevelSetup.Current.ManualFormCooldown,
                        "当前受控精神体的资源面板出口立即显示真实转换 CD");
                    CheckTrialResetUI(editedGround);
                    Object.Destroy(editedGround.gameObject);
                    cooldownEnds = EditorApplication.timeSinceStartup + SceneLevelSetup.Current.ManualFormCooldown + .1;
                    stage = 4;
                    break;
                case 4:
                    if (EditorApplication.timeSinceStartup < cooldownEnds) return;
                    Near(oldSpirit.FormCooldownRemaining, 0f, "TimeScale 为零时转换 CD 仍归零");
                    typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
                    Check(panel.FindUIObj<Text>("StageText").text == "探索 · 模拟"
                        && panel.FindUIObj<Text>("CognitionText").text == $"认知  {Mathf.Floor(expectedCognition):0}"
                        && panel.FindUIObj<Text>("FormCooldownText").text == "转换 CD  0.0 秒",
                        "游戏显示消费当前阶段、认知和实际转换 CD");
                    Check(SceneChangeMgr.Instance.SetSimulation(false), "场景模式入口成功返回漫游");
                    Check(!LevelStageMgr.Instance.IsSimulation && oldSpirit._ActionData.isFlying,
                        "冷却结束后返回漫游飞行");
                    expectedCognition = Mathf.Max(0f, expectedCognition - formCost);
                    Near(LevelStageMgr.Instance.CognitionRemaining, expectedCognition,
                        "返回漫游再扣一次费用，沿用本关同一认知余额");
                    Check(WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)),
                        "返回漫游同步暂停世界运动");
                    cooldownEnds = EditorApplication.timeSinceStartup + SceneLevelSetup.Current.ManualFormCooldown + .1;
                    stage = 7;
                    break;
                case 7:
                    if (EditorApplication.timeSinceStartup < cooldownEnds) return;
                    // 试验起点已由正式按钮验过；键位回归仍使用原隔离位置，免受旧场景出生点占用干扰。
                    oldSpirit.GetComponent<CharacterPhysics>().RefreshMotionState(new Vector2(5000f, 5000f));
                    SpiritFormInputChecks.Run(passed, body, oldSpirit, oldRules);
                    SceneChangeMgr.Instance.EnterSavedLevel();
                    stage = 5;
                    break;
                case 5:
                    if (!Ready(sceneOrder[1])) return;
                    Player replacement = Actor("spirit");
                    Check(oldSpirit == null && replacement != null && replacement.GetInstanceID() != spiritId,
                        "重进非教学关销毁上一只精神体并创建新实例");
                    Check(Rules() != oldRules, "重进关卡重新组装唯一规则对象");
                    Check(Rules().Cognition != oldRules.Cognition, "重进关卡重新创建本关共享认知记录");
                    CheckNonTeaching(replacement);
                    Check(SceneChangeMgr.Instance.ToggleGameMenu(), "重进关卡后菜单可再次打开");
                    UIMgr.Instance.GetOnePanel<GameMenuPanel>(value => menu = value);
                    stage = 8;
                    break;
                case 8:
                    if (menu == null || !menu.gameObject.activeInHierarchy) return;
                    Check(menu.transform.parent == UIMgr.Instance.GetUILayer(E_UILayer.Top)
                        && menu.GetComponentsInChildren<Button>(true).Length == 1,
                        "真实游戏菜单位于 Top 层且只有一个按钮");
                    Check(Object.FindObjectsOfType<GameMenuPanel>(true).Length == 1,
                        "首次加载中快速开关后缓存复用同一个菜单实例");
                    GameMenuInputChecks.Capture("game-menu.png");
                    menu.FindUIObj<Button>("ReturnToMenuButton").onClick.Invoke();
                    Check(!SceneChangeMgr.Instance.GameMenuOpen && !menu.gameObject.activeSelf
                        && !panel.gameObject.activeSelf && Time.timeScale == 0f,
                        "真实返回按钮关闭菜单与显示面板并恢复打开前时间");
                    Check(Actor("body") == null && Actor("spirit") == null && Rules() == null,
                        "返回菜单立即清除角色与本关规则引用");
                    Check(InputControlMgr.Instance.Player == null && !LevelStageMgr.Instance.HasExploration,
                        "菜单清除唯一控制目标及探索事实");
                    Check(!WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)),
                        "返回菜单释放两类世界暂停开关");
                    stage = 6;
                    break;
                case 6:
                    if (SceneManager.GetActiveScene().name != "BeginScenes" || Players().Length != 0) return;
                    Check(body == null && Players().Length == 0, "菜单加载完成实体和精神体均已销毁");
                    Check(SceneLevelSetup.Current == null, "菜单没有上一关遗留配置");
                    Check(DataAndInitMgr.Instance.LoadLevelIndex() == 1, "返回菜单保留关卡进度");
                    Check(!SceneChangeMgr.Instance.ToggleGameMenu(), "开始场景不能打开关内菜单");
                    stage = 9;
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception error)
        {
            failure = error;
            Debug.LogException(error);
            stage = 9;
            EditorApplication.isPlaying = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }
    }

    private static bool Ready(string scene)
    {
        return SceneManager.GetActiveScene().name == scene && SceneLevelSetup.Current != null
            && !(bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance)
            && InputControlMgr.Instance.Player != null;
    }

    private static void AddPendingEffects()
    {
        Check(effects.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f, PlayerEffectScope.Permanent)
            && effects.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.3f), "布置本局待确认永久效果和本局起跳效果");
    }

    private static void CheckPendingEffects(string context)
    {
        Check(effects.ConfirmedPermanentEffects.Count == 1 && effects.CurrentLevelEffects.Count == 2,
            context + "永久效果仍待确认，本局效果未提前清除");
        Near(effects.MovementMultiplier, 1.08f, context + "当前永久倍率未重施或撤销");
        Near(effects.JumpMultiplier, 1.3f, context + "本局起跳效果仍保留");
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0, context + "进度仍停在原关");
    }

    private static void StartFailedNextLevel(bool missingSetup)
    {
        removeIncomingSetup = missingSetup;
        receivedExpectedLoadError = false;
        expectedLoadError = missingSetup ? "当前场景没有关卡配置。"
            : "精神体预设、出生点或上涨水配置无效，不能开始探索。";
        SceneManager.sceneLoaded += BreakIncomingSetup;
        SceneChangeMgr.Instance.EnterNextLevel();
        CheckPendingEffects("失败用例开始加载时");
    }

    private static void BreakIncomingSetup(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != sceneOrder[1]) return;
        SceneManager.sceneLoaded -= BreakIncomingSetup;
        SceneLevelSetup incoming = SceneLevelSetup.Current;
        // 只破坏本次播放中的新关配置，不修改保存场景或预设。
        if (removeIncomingSetup) Object.DestroyImmediate(incoming);
        else typeof(SceneLevelSetup).GetField("spiritPrefab", Hidden).SetValue(incoming, null);
    }

    private static bool FailedLoadFinished()
    {
        if (SceneManager.GetActiveScene().name != sceneOrder[1]
            || (bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance)) return false;
        Check(receivedExpectedLoadError, "真实加载完成并报告预期的安置失败原因");
        expectedLoadError = null;
        return true;
    }

    private static BaseGround CheckEnvironmentEditUI()
    {
        float previous = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
            Transform editorArea = panel.transform.Find("ActionArea/EnvironmentEditArea");
            Check(panel.transform.Find("ActionArea").gameObject.activeSelf && editorArea.gameObject.activeSelf
                && !panel.transform.Find("ActionArea/ResetTrialButton").gameObject.activeSelf,
                "漫游实际界面开放环境编辑且隐藏试验重置");

            GameObject item = new GameObject("P6 flow temporary editable ground") { hideFlags = HideFlags.DontSave };
            fixtures.Add(item);
            item.transform.position = new Vector2(7500f, 7500f);
            item.AddComponent<BoxCollider2D>().size = new Vector2(4f, 2f);
            BaseGround ground = item.AddComponent<BaseGround>();
            Physics2D.SyncTransforms();
            CharacterPhysics physics = oldSpirit.GetComponent<CharacterPhysics>();
            Vector2 spiritPosition = physics.DebugActualPosition, spiritVelocity = physics.GetPlanarVelocity();
            float balance = oldRules.Cognition.Remaining;
            EnvironmentEditView view = editorArea.GetComponent<EnvironmentEditView>();
            typeof(EnvironmentEditView).GetMethod("SelectAt", Hidden).Invoke(view, new object[] { (Vector2)item.transform.position });
            InputField value = editorArea.Find("EnvironmentValueInput").GetComponent<InputField>();
            value.text = "0.5";
            Button apply = editorArea.Find("ApplyEnvironmentButton").GetComponent<Button>();
            Check(value.interactable && apply.interactable, "选中本关实际地面后可输入并提交有限倍率");
            apply.onClick.Invoke();
            Near(ground.SlowingEffect, .5f, "真实环境应用按钮改值交给原环境来源");
            Check(physics.DebugActualPosition == spiritPosition && physics.GetPlanarVelocity() == spiritVelocity
                && oldSpirit._ActionData.isFlying, "漫游编辑不改精神体运动或形态");
            Near(oldRules.Cognition.Remaining, balance, "当前环境改值不另扣转换或模拟认知");
            GameMenuInputChecks.Capture("environment-edit.png");
            return ground;
        }
        finally { Time.timeScale = previous; }
    }

    private static void CheckTrialResetUI(BaseGround editedGround)
    {
        float previous = Time.timeScale;
        InputControlMgr input = InputControlMgr.Instance;
        SceneLevelSetup setup = SceneLevelSetup.Current;
        bool hadMap = setup.MapEnabled;
        LevelMapRecord previousMap = oldRules.MapRecord;
        try
        {
            // 本夹具验地图前的固定起点支路；实际地图中心传送由 P8/P9 夹具另验，不依赖用户是否已铺格。
            typeof(SceneLevelSetup).GetField("mapEnabled", Hidden).SetValue(setup, false);
            Check(oldRules.BindExplorationMap(setup) && oldRules.MapRecord == null, "试验按钮夹具临时使用无地图固定起点支路");
            Time.timeScale = 1f;
            typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
            Button reset = panel.transform.Find("ActionArea/ResetTrialButton").GetComponent<Button>();
            Check(panel.transform.Find("ActionArea").gameObject.activeSelf && reset.gameObject.activeSelf && reset.interactable
                && !panel.transform.Find("ActionArea/EnvironmentEditArea").gameObject.activeSelf,
                "模拟实际界面开放试验重置并关闭环境编辑");
            float balance = oldRules.Cognition.Remaining;
            float nextChange = (float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(oldRules);
            bool inputWasEnabled = input.InputEnabled;
            input.PlayerInputData.jumpPressed = input.PlayerInputData.interactTapped = true;
            reset.onClick.Invoke();
            CharacterPhysics physics = oldSpirit.GetComponent<CharacterPhysics>();
            Check((physics.DebugActualPosition - SceneLevelSetup.Current.TrialPoint).magnitude <= .001f,
                "真实试验按钮将当前精神体安置到本关固定试验起点");
            Check(oldSpirit.GetInstanceID() == spiritId && input.Player == oldSpirit && oldRules.IsSimulation
                && !oldSpirit._ActionData.isFlying, "真实试验重置保持同一模拟精神体与唯一输入目标");
            Near(oldRules.Cognition.Remaining, balance, "真实试验按钮保留本关认知余额");
            Near((float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(oldRules), nextChange,
                "真实试验按钮保留转换冷却截止时间");
            Check(input.InputEnabled == inputWasEnabled && !input.PlayerInputData.jumpPressed
                && !input.PlayerInputData.jumpRelease && !input.PlayerInputData.interactTapped
                && !input.PlayerInputData.interactLongPressed, "试验刷新恢复原输入开关且不补发旧跳跃或交互点按");
            Check(!WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)) && physics.IsMotionParticipating,
                "试验刷新恢复当前模拟的角色与世界参与许可");
            Near(editedGround.SlowingEffect, .5f, "真实试验轻刷新保留已经提交的环境改值");
            Near(LevelStageMgr.Instance.CognitionRemaining, expectedCognition,
                "真实认知显示出口继续代理同一规则余额");
            GameMenuInputChecks.Capture("trial-reset.png");
        }
        finally
        {
            typeof(SceneLevelSetup).GetField("mapEnabled", Hidden).SetValue(setup, hadMap);
            typeof(PlayerLifecycleRules).GetField("<MapRecord>k__BackingField", Hidden).SetValue(oldRules, previousMap);
            input.PlayerInputData.jumpPressed = input.PlayerInputData.interactTapped = false;
            Time.timeScale = previous;
            typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
            Check(!panel.transform.Find("ActionArea").gameObject.activeSelf, "TimeScale 暂停再次隐藏操作区");
        }
    }

    private static void CheckNonTeaching(Player spirit)
    {
        Check(!SceneLevelSetup.Current.Teaching && !LevelStageMgr.Instance.Teaching,
            "真实非教学场景使用精神体组装");
        Check(Actor("body") == body && body.GetInstanceID() == bodyId && Players().Length == 2,
            "非教学关复用跨关实体，场景总共仅两个角色");
        Check(spirit != null && spirit.Identity == PlayerIdentity.Spirit && spirit._ActionData.isFlying,
            "P7 非教学初始化精神体为真实飞行形态");
        Check(LevelStageMgr.Instance.HasExploration && !LevelStageMgr.Instance.IsSimulation && Rules() != null,
            "精神体飞行与唯一探索漫游规则同时就绪");
        Near(LevelStageMgr.Instance.CognitionRemaining, SceneLevelSetup.Current.InitialCognition,
            "非教学开局认知等于当前场景初始配置，首次召唤不扣费");
        Near(LevelStageMgr.Instance.CognitionRemaining, Rules().Cognition.Remaining,
            "阶段只读出口代理唯一规则持有的认知余额");
        Rigidbody2D waiting = body.GetComponent<Rigidbody2D>();
        Check(InputControlMgr.Instance.Player == spirit && body.Permissions.motionUpdates
            && !body.Permissions.movement && body._ActionData.HoldIdle
            && waiting.simulated && waiting.bodyType == RigidbodyType2D.Kinematic
            && body.gameObject.layer == Player.PlayerLayer && spirit.gameObject.layer == Player.PlayerLayer
            && Physics2D.GetIgnoreLayerCollision(Player.PlayerLayer, Player.PlayerLayer)
            && !body.TryRequestDeath(PlayerDeathCause.Abyss),
            "控制转交精神体，留下的实体锁定实心碰撞、不与精神体互撞且不发死亡");
        CheckCamera(spirit);
        Check(WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)),
            "非教学开局暂停箱子与平台世界运动");
    }

    private static void CheckCamera(Player target)
    {
        InputControlMgr input = InputControlMgr.Instance;
        MainCamera camera = (MainCamera)typeof(InputControlMgr).GetField("mainCamera", Hidden).GetValue(input);
        Check(camera != null, "真实场景相机登记到唯一输入管理器");
        typeof(InputControlMgr).GetMethod("Update", Hidden).Invoke(input, null);
        Vector3 destination = (Vector3)typeof(MainCamera).GetField("toPoint", Hidden).GetValue(camera);
        Check(destination == new Vector3(target.transform.position.x, target.transform.position.y, -10f),
            "相机目的地消费当前唯一控制角色坐标");
    }

    private static Player Actor(string name) => (Player)typeof(SceneChangeMgr).GetField(name, Hidden).GetValue(SceneChangeMgr.Instance);
    private static PlayerLifecycleRules Rules() => (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
    private static Player[] Players() => Object.FindObjectsOfType<Player>(true);
    private static bool WorldPaused(Type type) => (bool)type.GetField("worldPaused", StaticHidden).GetValue(null);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed.Add("PASS " + name);
    }

    private static void Near(float actual, float expected, string name)
    {
        Check(Mathf.Abs(actual - expected) <= .0001f, name + " [actual=" + actual + ", expected=" + expected + "]");
    }

    private static void RecordRuntimeError(string message, string trace, LogType type)
    {
        if (type == LogType.Error && expectedLoadError != null && message == expectedLoadError)
        {
            receivedExpectedLoadError = true;
            return;
        }
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError ??= "真实启动或关卡流程报错：" + message + "\n" + trace;
    }

    private static void Finish()
    {
        EditorApplication.update -= Advance;
        Application.logMessageReceived -= RecordRuntimeError;
        SceneManager.sceneLoaded -= BreakIncomingSetup;
        if (failure == null && runtimeError != null) failure = new InvalidOperationException(runtimeError);
        foreach (GameObject item in fixtures) if (item != null) Object.DestroyImmediate(item);
        fixtures.Clear();
        try
        {
            if (hadProgress) File.WriteAllBytes(progressPath, previousProgress);
            else if (File.Exists(progressPath)) File.Delete(progressPath);
            Check(hadProgress ? File.Exists(progressPath)
                && Convert.ToBase64String(File.ReadAllBytes(progressPath)) == Convert.ToBase64String(previousProgress)
                : !File.Exists(progressPath), "关卡进度按原始字节或原本不存在的状态恢复");
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        Time.timeScale = previousTimeScale;
        Physics2D.simulationMode = previousSimulation;
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        Directory.CreateDirectory(".utmp/GameUIValidation");
        File.WriteAllLines(".utmp/GameUIValidation/level-flow-checks.txt", passed);
        Debug.Log("P6/P7 level flow Play Mode: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
