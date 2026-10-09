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
    private static int bodyId, spiritId;
    private static ChangePanel panel;
    private static Button modeButton;
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
        passed.Clear(); stage = 0; failure = null; runtimeError = null; panel = null;
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
                    Check(Rules() == null && LevelStageMgr.Instance.CognitionRemaining == 0f
                        && !LevelStageMgr.Instance.CognitionExhausted, "教学没有非教学关的认知记录或耗尽事实");
                    CheckCamera(body);
                    Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0, "教学安置完成才保存当前序号");
                    SceneChangeMgr.Instance.EnterNextLevel();
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
                    UIMgr.Instance.ShowOneUI<ChangePanel>(E_UILayer.Middle, value => panel = value);
                    stage = 3;
                    break;
                case 3:
                    if (panel == null || !panel.gameObject.activeInHierarchy) return;
                    modeButton = (Button)typeof(ChangePanel).GetField("simulationButton", Hidden).GetValue(panel);
                    Check(modeButton != null && modeButton.gameObject.activeInHierarchy, "真实换关面板提供模拟模式按钮");
                    Check(modeButton.GetComponentInChildren<Text>().text == "进入模拟", "漫游显示进入模拟提示");
                    // 测试布置：避开关卡出生点附近的占用，检验实际按钮与规则链路；不写场景文件。
                    oldSpirit.GetComponent<CharacterPhysics>().RefreshMotionState(new Vector2(5000f, 5000f));
                    modeButton.onClick.Invoke();
                    Check(LevelStageMgr.Instance.IsSimulation && !oldSpirit._ActionData.isFlying,
                        "点击真实 UI 按钮经场景入口提交模拟和行走");
                    expectedCognition = Mathf.Max(0f, expectedCognition - formCost);
                    Near(LevelStageMgr.Instance.CognitionRemaining, expectedCognition,
                        "真实模式按钮成功进入模拟仅扣一次场景配置的转换费用");
                    Check(InputControlMgr.Instance.Player == oldSpirit && oldSpirit.GetInstanceID() == spiritId,
                        "模式按钮保持精神体实例及唯一控制对象");
                    Check(!WorldPaused(typeof(BasicEntity)) && !WorldPaused(typeof(Taijie)),
                        "模拟成功后恢复实体和平台世界运行许可");
                    Check(oldSpirit.FormCooldownRemaining > 0f
                        && oldSpirit.FormCooldownRemaining <= SceneLevelSetup.Current.ManualFormCooldown,
                        "当前受控精神体的资源面板出口立即显示真实转换 CD");
                    cooldownEnds = EditorApplication.timeSinceStartup + SceneLevelSetup.Current.ManualFormCooldown + .1;
                    stage = 4;
                    break;
                case 4:
                    if (EditorApplication.timeSinceStartup < cooldownEnds) return;
                    Near(oldSpirit.FormCooldownRemaining, 0f, "TimeScale 为零时转换 CD 仍归零");
                    UIMgr.Instance.ShowOneUI<ChangePanel>();
                    Check(modeButton.GetComponentInChildren<Text>().text == "返回漫游", "模拟显示返回漫游提示");
                    modeButton.onClick.Invoke();
                    Check(!LevelStageMgr.Instance.IsSimulation && oldSpirit._ActionData.isFlying,
                        "同一模式按钮在冷却结束后返回漫游飞行");
                    expectedCognition = Mathf.Max(0f, expectedCognition - formCost);
                    Near(LevelStageMgr.Instance.CognitionRemaining, expectedCognition,
                        "真实模式按钮返回漫游再扣一次费用，沿用本关同一认知余额");
                    Check(WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)),
                        "返回漫游同步暂停世界运动");
                    cooldownEnds = EditorApplication.timeSinceStartup + SceneLevelSetup.Current.ManualFormCooldown + .1;
                    stage = 7;
                    break;
                case 7:
                    if (EditorApplication.timeSinceStartup < cooldownEnds) return;
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
                    Check(SceneChangeMgr.Instance.ReturnToMenu(), "真实返回入口开始加载菜单");
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
        Check(InputControlMgr.Instance.Player == spirit && !body.Permissions.motionUpdates
            && !body.GetComponent<Rigidbody2D>().simulated, "控制转交精神体，留下的实体暂停运动");
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
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeError ??= "真实启动或关卡流程报错：" + message + "\n" + trace;
    }

    private static void Finish()
    {
        EditorApplication.update -= Advance;
        Application.logMessageReceived -= RecordRuntimeError;
        if (failure == null && runtimeError != null) failure = new InvalidOperationException(runtimeError);
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
        Directory.CreateDirectory(".utmp/P7Validation");
        File.WriteAllLines(".utmp/P7Validation/level-flow-checks.txt", passed);
        Debug.Log("P6/P7 level flow Play Mode: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
