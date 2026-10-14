#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>真实最后通关、专属角色、菜单重播和进度清除。只改独立存档与临时构建入口。</summary>
public static class EndingFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Output = ".utmp/P16EndingValidation";
    static readonly List<string> passed = new List<string>();
    static readonly Queue<string> expectedErrors = new Queue<string>();
    static string directory, previousDirectory, runtimeError;
    static LevelOrderFile previousOrder;
    static EditorBuildSettingsScene[] previousScenes;
    static bool previousOptionsEnabled, finished;
    static EnterPlayModeOptions previousOptions;
    static Exception failure;
    static double deadline;
    static IEnumerator checks;
    static bool replaceHost;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求独立 batch editor。");
        PlayerProgressChecks.Run();
        passed.Clear(); expectedErrors.Clear(); failure = null; runtimeError = null; finished = replaceHost = false;
        Directory.CreateDirectory(Output);
        previousDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        previousOrder = DataAndInitMgr.debugLevelOrder;
        previousScenes = EditorBuildSettings.scenes;
        directory = Path.GetFullPath(Path.Combine(Output, "Save-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory); DataAndInitMgr.debugPlayerProgressDirectory = directory;
        var scenes = new List<EditorBuildSettingsScene>(previousScenes);
        scenes.RemoveAll(item => item.path == EndingAssets.ScenePath);
        scenes.Add(new EditorBuildSettingsScene(EndingAssets.ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
        SetOrder();
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled; previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true; EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 240; checks = Run();
        Application.logMessageReceived += RecordError; SceneManager.sceneLoaded += ReplaceHost;
        EditorApplication.update += Advance; EditorApplication.isPlaying = true;
    }
    static void Advance()
    {
        if (finished) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); return; }
        if (!Application.isPlaying) return;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("P16 真实流程联验超时。");
            if (checks.MoveNext()) return;
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        finished = true; EditorApplication.isPlaying = false;
    }
    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        SceneChangeMgr.Instance.EnterSavedLevel();
        while (!OrdinaryReady())
        { TransitionPixelationChecks.AssertState(false, "菜单进普通关不启用像素化"); yield return null; }
        Player body = Body(); SceneProp prop = Prop(body);
        DataAndInitMgr.debugLevelOrder.endings = new EndingSceneEntry[0];
        Check(!SceneChangeMgr.Instance.TryClearLevel(body, source: prop), "未配置结局时最后关明确拒绝");
        SetOrder(); DataAndInitMgr.debugLevelOrder.endings[0].x = new EndingAxisRange { lowerKind = EndingBoundKind.Closed, lower = 100 };
        Check(!SceneChangeMgr.Instance.TryClearLevel(body, source: prop) && !body.IsLevelClearing, "零命中不领取、不冻结玩家");
        SetOrder(); DataAndInitMgr.debugLevelOrder.endings = new[] { Entry("EndingExample"), Entry("TeskScenes2") };
        Check(!SceneChangeMgr.Instance.TryClearLevel(body, source: prop), "多命中明确拒绝而不按配置顺序选取");
        SetOrder("MissingP16Ending");
        Check(!SceneChangeMgr.Instance.TryClearLevel(body, source: prop) && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero,
            "不可加载的结局在领取前拒绝，不修改贡献");
        SetOrder();
        var ledger = (PlayerEffectLedger)typeof(SceneChangeMgr).GetField("effects", Hidden).GetValue(SceneChangeMgr.Instance);
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f, PlayerEffectScope.Permanent);
        SceneProp.OfferInteract(body);
        Check(body.IsLevelClearing && !Loading(), "最后关沿真实通关前段受理");
        bool sawBlackPreparation = false, sawOldActor = false, capturedDarkening = false, capturedBlack = false;
        while (!EndingReady())
        {
            LoadingPanel cover = Object.FindObjectOfType<LoadingPanel>();
            if (cover != null) TransitionPixelationChecks.AssertState(cover.TransitionWeight > 0f,
                "关卡入结局的旧角色和新场景沿世界镜头一起像素化");
            if (Loading() && body != null && cover != null && cover.TransitionWeight < 1f)
            {
                Require(!body.GetComponentInChildren<SpriteRenderer>().forceRenderingOff, "最后关渐暗覆盖旧角色前不得隐藏");
                Require(!Array.Exists(Object.FindObjectsOfType<Camera>(), c => c.enabled && c.name == "PlayerPortraitCamera"),
                    "最后关不用遮罩上方的普通跨关角色镜头"); sawOldActor = true;
                if (!capturedDarkening && cover.TransitionWeight > .3f && cover.TransitionWeight < .7f)
                { Capture("ending-darkening.png"); capturedDarkening = true; }
            }
            var host = Object.FindObjectOfType<EndingSceneController>();
            if (host != null && host.Context != null && !host.Context.IsRevealed)
            {
                Require(!SceneChangeMgr.Instance.IsEnding && !InputControlMgr.Instance.InputEnabled
                    && !Object.FindObjectOfType<EndingActor>().GetComponent<Rigidbody2D>().simulated, "准备及揭幕期间不开演、不开角色运动");
                sawBlackPreparation = true;
                if (!capturedBlack && cover != null && cover.TransitionWeight == 1f)
                {
                    Require(body == null && Object.FindObjectsOfType<Player>().Length == 0, "全黑准备时旧角色已删除");
                    Capture("ending-full-black.png"); capturedBlack = true;
                }
            }
            yield return null;
        }
        Check(sawOldActor && sawBlackPreparation, "旧角色全黑交接，新角色等揭完开演");
        TransitionPixelationChecks.AssertState(false, "结局开演前关闭像素化");
        Check(true, "P19 关卡入结局覆盖角色，全黑删除旧角色，揭幕后恢复清晰");
        var ending = Object.FindObjectOfType<EndingSceneController>(); var context = ending.Context;
        var actor = context.ControlledActor; var rigidbody = actor.GetComponent<Rigidbody2D>();
        Check(Body() == null && Object.FindObjectsOfType<Player>().Length == 0 && SceneLevelSetup.Current == null
            && !LevelStageMgr.Instance.IsRunning && LevelStageMgr.Instance.Stage == PlayerGameplayStage.Inactive,
            "结局移除普通玩家和普通阶段，专属角色无死亡／资源入口");
        Check(context.Data.Vector == new Vector3(2, -3, 0) && !context.Data.IsReplay && DataAndInitMgr.Instance.SavedEndingScene == "EndingExample"
            && DataAndInitMgr.Instance.ConfirmedEndingVector == context.Data.Vector && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero,
            "黑幕内整体确认最终贡献、能力和保存的结局目标");
        Check(ledger.CaptureConfirmedEffects().Length == 1 && ledger.CurrentLevelEffects.Count == 0,
            "最后关未确认永久效果也在结局就绪时一起确认");
        Check(actor.GetComponent<Player>() == null && actor.GetComponent<CharacterPhysics>() == null
            && actor.GetComponentInChildren<PresentationLayer>() == null && !actor.ControlEnabled, "专属角色复用形象和动画，脚本先锁输入");
        Check(Object.FindObjectOfType<LoadingPanel>() == null && Time.timeScale == 1 && InputControlMgr.Instance.InputEnabled,
            "开演通知前遮罩已退出，输入服务和演出时间已就绪");
        string path = Path.Combine(directory, "PlayerProgress.json"), saved = File.ReadAllText(path);
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && context.IsPaused && Time.timeScale == 0f
            && actor.Animator.speed == 0 && !rigidbody.simulated, "ESC 菜单暂停角色、Animator 与演出时钟");
        Vector2 pausePosition = rigidbody.position;
        double until = EditorApplication.timeSinceStartup + .5;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Check(!actor.ControlEnabled && rigidbody.position == pausePosition && context.DeltaTime == 0,
            "暂停不会提前结束两秒剧情等待或移动角色");
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && !context.IsPaused && actor.Animator.speed == 1
            && rigidbody.simulated, "继续恢复演出状态及角色模拟");
        while (!actor.ControlEnabled || !actor.IsGrounded) yield return null;
        var inactiveRoot = new GameObject("P16 inactive character fixture"); inactiveRoot.SetActive(false);
        var npc = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EndingAssets.ActorPath), inactiveRoot.transform).GetComponent<EndingActor>();
        object[] config = { null };
        Check(context.RegisterActor(npc) && (bool)typeof(EndingActor).GetMethod("ValidateConfiguration", Hidden).Invoke(npc, config),
            "预摆未启用角色在 Awake 前也能登记并检查配置");
        inactiveRoot.SetActive(true);
        Check(npc.GetComponent<Rigidbody2D>().simulated, "动态角色启用后自动接上已揭幕的演出运动");
        npc.gameObject.SetActive(false); npc.gameObject.SetActive(true);
        Check(npc.GetComponent<Rigidbody2D>().simulated, "角色关闭再启用不留下运动锁");
        Object.Destroy(inactiveRoot);
        float startX = rigidbody.position.x;
        until = EditorApplication.timeSinceStartup + .3;
        while (EditorApplication.timeSinceStartup < until)
        { InputControlMgr.Instance.PlayerInputData.moveInput = 1; yield return null; }
        Check(rigidbody.position.x > startX + .1f, "复用真实输入控制器的水平数据驱动独立行走");
        InputControlMgr.Instance.PlayerInputData.moveInput = 0; InputControlMgr.Instance.PlayerInputData.jumpPressed = true;
        yield return null; yield return null;
        while (rigidbody.velocity.y <= .1f || actor.IsGrounded) yield return null;
        Check(!InputControlMgr.Instance.PlayerInputData.jumpPressed && !actor.IsGrounded, "跳跃点按仅消费一次并实际离地");
        Vector2 flyingVelocity = rigidbody.velocity;
        SceneChangeMgr.Instance.ToggleGameMenu(); yield return null;
        Check(rigidbody.velocity == flyingVelocity, "暂停保留空中速度");
        SceneChangeMgr.Instance.ToggleGameMenu();
        while (!actor.IsGrounded) yield return null;
        actor.SetControlEnabled(false); startX = rigidbody.position.x;
        until = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < until)
        { InputControlMgr.Instance.PlayerInputData.moveInput = 1; InputControlMgr.Instance.PlayerInputData.jumpPressed = true; yield return null; }
        Check(Mathf.Abs(rigidbody.position.x - startX) < .01f && rigidbody.velocity.y <= .1f,
            "场景脚本关闭操作后忽略行走和跳跃");
        InputControlMgr.Instance.PlayerInputData.moveInput = 0; actor.SetControlEnabled(true); yield return null; yield return null;
        Check(rigidbody.velocity.y <= .1f, "重新开操作不补发关闭期间的跳跃");
        actor.SetAutomaticMotion(false); actor.SetAutomaticAnimation(false); actor.Animator.Play("Finale");
        yield return null; yield return null;
        Check(!rigidbody.simulated && actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Finale") && Object.FindObjectOfType<FailureRecoveryPanel>() == null,
            "死亡形象的专属演出状态不触发普通失败，脚本可接管运动及动画");
        Capture("ending-example.png");
        Check(SceneChangeMgr.Instance.ReturnToMenu(), "结局允许返回菜单"); while (!MenuReady()) yield return null;
        Check(!context.IsActive && File.ReadAllText(path) == saved, "退出失效旧通信并保留结局存档");
        var oldCamera = Object.FindObjectOfType<MainCamera>(); Object.Destroy(oldCamera.gameObject); yield return null;
        SceneChangeMgr.Instance.EnterSavedLevel();
        while (!EndingReady())
        { TransitionPixelationChecks.AssertState(false, "菜单直接重播结局保持纯黑幕"); yield return null; }
        var replay = Object.FindObjectOfType<EndingSceneController>().Context;
        Check(replay.Data.IsReplay && replay.Data.SceneName == "EndingExample" && replay.Data.Vector == new Vector3(2, -3, 0)
            && File.ReadAllText(path) == saved && Object.FindObjectsOfType<Player>().Length == 0,
            "菜单开始直接重播已存结局，不重复累计或再次写盘");
        Check(Object.FindObjectOfType<MainCamera>() != null, "冷启动直接重播无需普通玩家也建立游戏镜头");
        SceneChangeMgr.Instance.ReturnToMenu(); while (!MenuReady()) yield return null;
        File.WriteAllText(path, saved.Replace("EndingExample", "MissingP16Ending"));
        SceneChangeMgr.Instance.EnterSavedLevel(); yield return null;
        Check(!Loading() && SceneManager.GetActiveScene().name == "BeginScenes" && SceneChangeMgr.Instance.MenuMessage.Contains("不能加载")
            && File.ReadAllText(path).Contains("MissingP16Ending"), "失效保存目标留在菜单提示，原档保持可修复");
        File.WriteAllText(path, saved); DataAndInitMgr.Instance.LoadPlayerProgress(ledger, out _);
        UIMgr.Instance.SceneUIShow(E_UI_Process.SetPanel);
        SetPanel settings;
        while ((settings = Object.FindObjectOfType<SetPanel>()) == null) yield return null;
        yield return null;
        var clear = Find<Button>(settings, "ClearGameProgress"); var caption = clear.GetComponentInChildren<Text>();
        Check(caption.text == "清空游戏进度" && caption.color.r > caption.color.g && caption.color.r > caption.color.b,
            "设置预设有红色清空游戏进度入口");
        Capture("settings-clear-entry.png");
        clear.onClick.Invoke(); yield return null;
        Check(settings.transform.Find("SettingsContent/ClearProgressConfirmation").gameObject.activeSelf
            && File.ReadAllText(path) == saved, "点击入口先显示确认，不提前清除");
        Capture("settings-clear-confirm.png");
        Find<Button>(settings, "ClearProgressCancel").onClick.Invoke();
        Check(File.ReadAllText(path) == saved && clear.gameObject.activeSelf, "取消保留进度并恢复设置入口");
        clear.onClick.Invoke();
        expectedErrors.Enqueue("清除玩家进度失败：");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Find<Button>(settings, "ClearProgressConfirm").onClick.Invoke();
        Check(expectedErrors.Count == 0 && File.ReadAllText(path) == saved && DataAndInitMgr.Instance.SavedEndingScene == "EndingExample"
            && Find<Text>(settings, "ClearProgressMessage").text.Contains("失败"), "清除失败提示失败并保留磁盘、内存进度");
        Find<Button>(settings, "ClearProgressCancel").onClick.Invoke();
        var preferences = PreferenceFiles();
        clear.onClick.Invoke(); Find<Button>(settings, "ClearProgressConfirm").onClick.Invoke();
        Check(!File.Exists(path) && DataAndInitMgr.Instance.SavedEndingScene == null
            && DataAndInitMgr.Instance.ConfirmedEndingVector == Vector3.zero && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero,
            "确认清除进度、保存结局、累计向量");
        Check(PreferenceFiles() == preferences, "清进度保留音量、按键及显示配置文件");
        Check(ledger.CaptureConfirmedEffects().Length == 0, "清进度同时清除内存已确认能力记录");
        UIMgr.Instance.HideOneUI<SetPanel>(); UIMgr.Instance.SceneUIShow(E_UI_Process.BeginPanel);
        SceneChangeMgr.Instance.EnterSavedLevel(); while (!OrdinaryReady()) yield return null;
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0 && LevelStageMgr.Instance.IsRunning, "清除后开始重新进入第一普通关");

        // 准备取消：旧通信不可关闭新加载；嵌套准备的 finally 必须执行。
        replaceHost = true; EndingPreparationProbe.Throw = false; EndingPreparationProbe.Disposed = false;
        SceneProp.OfferInteract(CreatePropForBody());
        EndingPreparationProbe probe;
        while ((probe = Object.FindObjectOfType<EndingPreparationProbe>()) == null || probe.Context == null) yield return null;
        var obsolete = probe.Context;
        Check(Loading() && !obsolete.IsRevealed, "长准备夹具确实停留黑幕内");
        SceneChangeMgr.Instance.ReturnToMenu();
        Check(!obsolete.IsActive && EndingPreparationProbe.Disposed, "取消准备立即作废通信并释放准备协程");
        while (!MenuReady()) yield return null;
        replaceHost = false; SceneChangeMgr.Instance.EnterSavedLevel(); obsolete.ReturnToMenu();
        Check(Loading() && !obsolete.SetControlledActor(null), "旧结局回调不能取消新加载或接管输入");
        while (!OrdinaryReady()) yield return null;

        // 准备及写盘失败均保留以前普通进度。
        string beforeFailure = File.ReadAllText(path);
        replaceHost = true; EndingPreparationProbe.Throw = true;
        expectedErrors.Enqueue("结局准备失败：P16 expected preparation failure");
        SceneProp.OfferInteract(CreatePropForBody());
        while (!Loading()) yield return null; while (!MenuReady()) yield return null;
        Check(expectedErrors.Count == 0 && File.ReadAllText(path) == beforeFailure && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero,
            "场景准备失败保留旧存档，丢弃本次贡献并返回菜单提示");
        replaceHost = false; SceneChangeMgr.Instance.EnterSavedLevel(); while (!OrdinaryReady()) yield return null;
        beforeFailure = File.ReadAllText(path); Directory.CreateDirectory(path + ".tmp");
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.4f, PlayerEffectScope.Permanent);
        expectedErrors.Enqueue("保存玩家进度失败："); expectedErrors.Enqueue("结局进度保存失败，已保留此前进度。");
        SceneProp.OfferInteract(CreatePropForBody());
        while (!Loading()) yield return null; while (!MenuReady()) yield return null;
        Directory.Delete(path + ".tmp");
        Check(expectedErrors.Count == 0 && File.ReadAllText(path) == beforeFailure && DataAndInitMgr.Instance.SavedEndingScene == null
            && DataAndInitMgr.Instance.ConfirmedEndingVector == Vector3.zero && ledger.CaptureConfirmedEffects().Length == 0,
            "结局保存失败不改旧目的地、已确认贡献或能力，不揭幕开演");
    }
    // 返回实际玩家，顺便安置可交互道具。
    static Player CreatePropForBody() { Player actor = Body(); Prop(actor); return actor; }
    static SceneProp Prop(Player actor)
    {
        var obj = new GameObject("P16 validation clear prop"); obj.transform.position = actor.GetComponent<Collider2D>().bounds.center;
        var collider = obj.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = Vector2.one * 4;
        var prop = obj.AddComponent<SceneProp>(); typeof(SceneProp).GetField("endingContribution", Hidden).SetValue(prop, new Vector2(2, -3));
        Physics2D.SyncTransforms(); return prop;
    }
    static EndingSceneEntry Entry(string scene) => new EndingSceneEntry { scene = scene };
    static void SetOrder(string scene = "EndingExample") => DataAndInitMgr.debugLevelOrder = new LevelOrderFile
        { scenes = new[] { "TeskScenes1" }, endings = new[] { Entry(scene) } };
    static Player Body() => (Player)typeof(SceneChangeMgr).GetField("body", Hidden).GetValue(SceneChangeMgr.Instance);
    static bool Loading() => SceneChangeMgr.Instance.IsLoading;
    static bool OrdinaryReady() => !Loading() && SceneLevelSetup.Current != null && Body() != null;
    static bool EndingReady() => !Loading() && SceneChangeMgr.Instance.IsEnding;
    static bool MenuReady()
    {
        // 等待准备失败返回期间，仍在结局加载的帧应继续像素化；菜单出现后才核对清理。
        if (SceneManager.GetActiveScene().name == "BeginScenes")
            TransitionPixelationChecks.AssertState(false, "返回菜单或取消结局加载后清除像素化");
        return !Loading() && SceneManager.GetActiveScene().name == "BeginScenes"
            && Object.FindObjectOfType<BeginPanel>() != null && Object.FindObjectOfType<BeginPanel>().IsFullyShown;
    }
    static T Find<T>(Component root, string name) where T : Component
        => Array.Find(root.GetComponentsInChildren<T>(true), item => item.name == name);
    static string PreferenceFiles()
    {
        var files = new List<string>();
        foreach (string name in new[] { "MusicInfo.bitto", "InputBindingOverrides.json" })
        { string path = Path.Combine(Application.persistentDataPath, name); files.Add(name + ":" + (File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : "missing")); }
        files.Add("display:" + DisplaySettings.Instance.ResolutionIndex + ":" + DisplaySettings.Instance.Windowed);
        return string.Join("\n", files);
    }
    static void Capture(string name)
    {
        typeof(P66TransitionChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "P16-" + name });
        File.Copy(Path.Combine(".utmp/P66Validation", "P16-" + name), Path.Combine(Output, name), true);
    }
    static void ReplaceHost(Scene scene, LoadSceneMode mode)
    {
        if (!replaceHost || scene.name != "EndingExample") return;
        var host = Object.FindObjectOfType<EndingSceneController>(); GameObject root = host.gameObject;
        Object.DestroyImmediate(host); root.AddComponent<EndingPreparationProbe>();
    }
    static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    static void Check(bool value, string label)
    { Require(value, label); passed.Add("PASS " + label); File.WriteAllLines(Path.Combine(Output, "flow-progress.txt"), passed); }
    static void RecordError(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (type == LogType.Error && expectedErrors.Count > 0 && message.StartsWith(expectedErrors.Peek(), StringComparison.Ordinal))
        { expectedErrors.Dequeue(); return; }
        runtimeError ??= message + "\n" + trace;
    }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError; SceneManager.sceneLoaded -= ReplaceHost;
        DataAndInitMgr.debugPlayerProgressDirectory = previousDirectory; DataAndInitMgr.debugLevelOrder = previousOrder;
        EditorBuildSettings.scenes = previousScenes;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled; EditorSettings.enterPlayModeOptions = previousOptions;
        passed.Add("RESULT: " + (failure == null ? "PASS" : "FAIL " + failure.Message));
        File.WriteAllLines(Path.Combine(Output, "flow-checks.txt"), passed);
        Debug.Log("P16 ending flow: " + passed.Count + "; success=" + (failure == null)); EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
