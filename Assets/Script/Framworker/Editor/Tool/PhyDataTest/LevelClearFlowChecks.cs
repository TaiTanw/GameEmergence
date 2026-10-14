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
using Object = UnityEngine.Object;

/// <summary>真实角色、道具、Animator 与跨关加载联验；不保存夹具，完成后还原进度与编辑器配置。</summary>
public static class LevelClearFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();
    static readonly List<Object> fixtures = new List<Object>();
    static string progressDirectory, previousProgressDirectory, runtimeError;
    static bool previousOptionsEnabled, finished;
    static EnterPlayModeOptions previousOptions;
    static Exception failure;
    static double deadline;
    static IEnumerator checks;
    static Animator expectedFailureAnimator;
    static int expectedPlaybackErrors, failureStateHash;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求独立 batch editor。");
        CatgirlLevelClearAssets.Validate();
        CatgirlPresentationAnimationChecks.Run();
        ScenePropInteractionChecks.Run();
        passed.Clear(); fixtures.Clear(); runtimeError = null; failure = null; finished = false;
        expectedPlaybackErrors = failureStateHash = 0; expectedFailureAnimator = null;
        previousProgressDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        progressDirectory = Path.GetFullPath(Path.Combine(".utmp/LevelClearValidation", "Save-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(progressDirectory);
        DataAndInitMgr.debugPlayerProgressDirectory = progressDirectory;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 150;
        checks = Run();
        Application.logMessageReceived += RecordError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    static void Advance()
    {
        if (finished) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); return; }
        if (!Application.isPlaying) return;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("通关动画与换关联验超时。");
            if (checks.MoveNext()) return;
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        finished = true;
        EditorApplication.isPlaying = false;
    }

    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        Check(DataAndInitMgr.Instance.LoadLevelOrder().scenes.Length >= 2, "实际顺序包含两关");
        Enter(0);
        bool sawMenuBuffer = false;
        while (!Ready(0))
        {
            LoadingPanel cover = Object.FindObjectOfType<LoadingPanel>();
            if (Loading() && Time.timeScale > 0f && Actor("body") != null && cover != null && cover.TransitionWeight == 1f)
            {
                Require(Actor("body").GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                    "菜单首次进关的全黑缓冲继续隐藏角色");
                sawMenuBuffer = true;
            }
            yield return null;
        }
        Check(sawMenuBuffer, "菜单首次进关保留全黑角色遮挡");
        Player body = Actor("body");
        var presentation = body.GetComponentInChildren<PresentationLayer>();
        Check(presentation.ValidateLevelClearPlayback(0, out _), "实体编号 0 的前段和循环可播放");
        Check(!presentation.ValidateLevelClearPlayback(99, out _) && !body.IsLevelClearing, "未配置编号明确拒绝且不锁角色");
        Check(!body.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "实体仍不能开启飞行");
        int bodyId = body.GetInstanceID(), setupId = SceneLevelSetup.Current.GetInstanceID();
        SceneProp prop = Prop(body);
        SceneProp otherProp = Prop(body);
        otherProp.transform.position += Vector3.right * .5f;
        Check(SceneProp.FindInteractTarget(body) == prop, "多个通关范围重叠时选中最近道具");
        body.TickLogic(0f);
        Check(body._ActionData.InteractProp == prop, "教学实体接入玩家只读道具提示目标");
        typeof(PresentationLayer).GetMethod("UpdateInteractHint", Hidden).Invoke(presentation, null);
        var hintView = (InteractionHintView)typeof(PresentationLayer).GetField("interactHintView", Hidden).GetValue(presentation);
        var hintRoot = (GameObject)typeof(InteractionHintView).GetField("root", Hidden).GetValue(hintView);
        Check(hintRoot.activeSelf && hintRoot.GetComponentInChildren<TextMesh>().text == "点按交互",
            "实际共用提示框显示通关道具交互文字");
        SceneProp.OfferInteract(body);
        Check(prop.gameObject.activeSelf && (bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop)
            && body.IsLevelClearing && SceneChangeMgr.Instance.IsLevelClearPending && !Loading(),
            "真实通关点按保留道具并锁定前段，尚未开始加载");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == new Vector3(2, -3, 0)
            && DataAndInitMgr.Instance.ConfirmedEndingVector == Vector3.zero,
            "通关受理只登记本关二维贡献，尚未确认");
        Check(body._ActionData.InteractProp == null && !InputControlMgr.Instance.InputEnabled,
            "通关受理立即清掉交互提示并关闭玩家输入");
        SceneProp.OfferInteract(body);
        Check(!(bool)typeof(SceneProp).GetField("used", Hidden).GetValue(otherProp)
            && SceneProp.FindInteractTarget(body) == null, "玩家通关锁阻止其它入口和重复点按");
        Check(!SceneChangeMgr.Instance.TryClearLevel(body), "重复通关不重新开始或重复结算");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == new Vector3(2, -3, 0), "重复通关不重复登记贡献");
        Check(!body.TryRequestDeath(PlayerDeathCause.Abyss) && !body.TryRequestDeath(PlayerDeathCause.OxygenDepleted)
            && !body.TryRequestDeath(PlayerDeathCause.Electrocuted), "通关期间所有危险反馈都被屏蔽");
        PlayerResourceFacts resources = body.ResourceFacts;
        body.TickLogic(50f);
        Check(Mathf.Approximately(body.ResourceFacts.stamina, resources.stamina)
            && Mathf.Approximately(body.ResourceFacts.oxygen, resources.oxygen), "通关期间不更新角色资源");
        Check(!body.Permissions.motionUpdates && !body.Permissions.movement && !body.Permissions.resourceUpdates,
            "通关角色运动、操作和资源全部锁定");
        var animator = body.GetComponentInChildren<Animator>();
        uint playback = (uint)typeof(PresentationLayer).GetField("levelClearPlaybackSequence", Hidden).GetValue(presentation);
        Check(!presentation.TryBeginLevelClearLoop(playback), "前段未完成不能提前循环");
        // 同死亡等待一样，用真实时间推进；暂停不能让前段和加载循环停住。
        Time.timeScale = 0f;
        bool sawLoop = false;
        bool sawVisibleBuffer = false;
        while (!Ready(1))
        {
            var status = presentation.GetLevelClearPlaybackStatus(playback);
            if (status == PresentationLayer.LevelClearPlaybackStatus.Playing)
                Require(!Loading() && SceneLevelSetup.Current.GetInstanceID() == setupId, "前段完整播放前不能换关");
            if (Loading() && status == PresentationLayer.LevelClearPlaybackStatus.Looping)
            {
                sawLoop = true;
                Require(body.IsLevelClearing, "整个加载保持终结动作锁定");
                Require(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash("Base Layer.LevelClearLoop_0"),
                    "加载实际采样循环状态");
                Require(!body.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                    "普通跨关的加载角色逐帧保持可见，不能隐藏后重现");
                if (Time.timeScale > 0f)
                {
                    if (!sawVisibleBuffer && Object.FindObjectOfType<LoadingPanel>().TransitionWeight == 1f)
                    {
                        typeof(P66TransitionChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                            .Invoke(null, new object[] { "V20-ordinary-black.png" });
                        typeof(P66TransitionChecks).GetMethod("CheckCoverPixels", BindingFlags.Static | BindingFlags.NonPublic)
                            .Invoke(null, new object[] { Object.FindObjectOfType<LoadingPanel>(), passed });
                        sawVisibleBuffer = true;
                    }
                    Camera portrait = Array.Find(Object.FindObjectsOfType<Camera>(), c => c.enabled && c.name == "PlayerPortraitCamera");
                    Require(portrait != null, "新关安置及缓冲仍使用角色前景镜头");
                    Vector3 viewport = portrait.WorldToViewportPoint(body.transform.position);
                    Require(Mathf.Abs(viewport.x - .5f) < .002f && Mathf.Abs(viewport.y - .5f) < .002f,
                        "普通跨关安置及揭幕期间角色根位置保持画面中心");
                }
            }
            yield return null;
        }
        yield return null;
        Check(sawLoop, "真实前段完成后进入加载循环");
        Check(sawVisibleBuffer, "真实普通跨关在全黑物理缓冲中持续显示居中的加载角色");
        Check(Actor("body").GetInstanceID() == bodyId && !body.IsLevelClearing && !SceneChangeMgr.Instance.IsLevelClearPending,
            "下一关保留同一实体并释放通关锁");
        Check(presentation.GetLevelClearPlaybackStatus(playback) == PresentationLayer.LevelClearPlaybackStatus.Invalid,
            "淡入结束后旧播放身份作废");
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 1 && Time.timeScale == 1f, "安置成功后才保存下一关并恢复时间");
        Check(DataAndInitMgr.Instance.ConfirmedEndingVector == new Vector3(2, -3, 0)
            && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero,
            "实际下一关安置成功才整体确认向量");
        Player spirit = Actor("spirit");
        Check(spirit != null && InputControlMgr.Instance.Player == spirit, "普通关沿现有规则创建并控制精神体");
        Check(!SceneChangeMgr.Instance.TryClearLevel(spirit) && !spirit.IsLevelClearing, "共享脚本中的精神体不能触发通关");
        Check(!spirit.GetComponentInChildren<PresentationLayer>().ValidateLevelClearPlayback(0, out _),
            "精神体控制器不要求实体通关状态");
        Check(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash("Base Layer.On"),
            "下一关待命实体恢复原待机，不残留通关末帧");

        GameObject lightPropObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ScenePropLightPrefabBuilder.PrefabPath));
        fixtures.Add(lightPropObject);
        lightPropObject.transform.position = spirit.GetComponent<Collider2D>().bounds.center;
        SceneProp escapeProp = lightPropObject.GetComponent<SceneProp>();
        ScenePropLightEffect lightEffect = lightPropObject.GetComponent<ScenePropLightEffect>();
        ParticleSystem lightPoints = lightPropObject.GetComponentInChildren<ParticleSystem>();
        yield return null;
        Check(lightEffect.enabled && lightPoints.isPlaying, "探索实际启用预设光效组件及悬浮粒子");
        var lightProperties = new MaterialPropertyBlock();
        lightPropObject.GetComponent<SpriteRenderer>().GetPropertyBlock(lightProperties);
        Check(lightProperties.GetFloat("_Brightness") > 1f, "探索阶段实际提高道具本体亮度");
        Check(SceneProp.FindInteractTarget(spirit) == null, "普通关探索精神体靠近通关道具也没有交互目标");
        Check(SceneChangeMgr.Instance.StartEscape() && (SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities()),
            "逃生提示联验沿真实准备及最终确认入口交接");
        while (SceneChangeMgr.Instance.IsEscapeHandoff) yield return null;
        Check(LevelStageMgr.Instance.Stage == PlayerGameplayStage.Escape && InputControlMgr.Instance.Player == body,
            "最终确认后由逃生实体取得控制");
        yield return null;
        Check(!lightEffect.enabled && !lightPoints.isPlaying && lightPoints.particleCount == 0,
            "逃生交接自动关闭光效并清除探索光点");
        lightPropObject.GetComponent<SpriteRenderer>().GetPropertyBlock(lightProperties);
        Check(lightProperties.GetFloat("_Brightness") == 0f, "逃生阶段恢复原道具亮度属性");
        escapeProp.transform.position = body.GetComponent<Collider2D>().bounds.center;
        body.TickLogic(0f);
        Check(body._ActionData.InteractProp == escapeProp, "逃生阶段靠近道具输出共用交互框目标");

        foreach (bool failLoop in new[] { false, true })
        {
            Enter(0);
            while (!Ready(0)) yield return null;
            body = Actor("body"); presentation = body.GetComponentInChildren<PresentationLayer>();
            animator = body.GetComponentInChildren<Animator>();
            RuntimeAnimatorController originalController = animator.runtimeAnimatorController;
            AnimatorOverrideController overrides = null;
            if (failLoop)
            {
                overrides = new AnimatorOverrideController(originalController); fixtures.Add(overrides);
                animator.runtimeAnimatorController = overrides;
                animator.Update(0f);
            }
            // 验证恢复原许可而非强行写回默认 Walking；第二种故障的输入原本已关闭。
            var before = new PlayerCapabilityPermissions(true, true, false, true, true, false);
            body.ApplyCapabilities(before);
            InputControlMgr.Instance.InputOpenOrClose(!failLoop);
            prop = Prop(body); SceneProp.OfferInteract(body);
            Vector3 confirmedBeforeFailure = DataAndInitMgr.Instance.ConfirmedEndingVector;
            Check(body.IsLevelClearing && prop.gameObject.activeSelf
                && (bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop) && !Loading(),
                (failLoop ? "循环失效" : "前段打断") + "回归从真实道具受理，播放开始前配置有效");
            playback = (uint)typeof(PresentationLayer).GetField("levelClearPlaybackSequence", Hidden).GetValue(presentation);
            Vector3 takenPosition = prop.transform.position + Vector3.right * .1f;
            prop.transform.position = takenPosition;
            expectedFailureAnimator = animator; expectedPlaybackErrors = 1;
            if (failLoop)
            {
                // 受理后仅换坏循环，前段仍能实际完成；错误在 TryBeginLevelClearLoop 的实际采样中发生。
                AnimationClip badLoop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/MyObjAssets/gameObject/Animation/LevelClearLoop_0.anim"));
                fixtures.Add(badLoop);
                var settings = AnimationUtility.GetAnimationClipSettings(badLoop);
                settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(badLoop, settings);
                overrides["LevelClearLoop_0"] = badLoop;
            }
            else
            {
                Check(SceneChangeMgr.Instance.ToggleGameMenu(), "前段期间打开菜单暂停操作");
                animator.Play("Base Layer.On", 0, 0f); animator.Update(0f);
            }
            while (SceneChangeMgr.Instance.IsLevelClearPending) yield return null;
            Check(expectedPlaybackErrors == 0 && failureStateHash == Animator.StringToHash(
                failLoop ? "Base Layer.LevelClearLoop_0" : "Base Layer.On"),
                failLoop ? "前段完成后的循环实际采样失败被检出" : "前段实际状态被打断并检出");
            Check(!body.IsLevelClearing && !Loading() && DataAndInitMgr.Instance.LoadLevelIndex() == 0,
                "播放失败释放角色与受理锁，不开始加载或推进进度");
            Check(body.Permissions.Equals(before) && !InputControlMgr.Instance.InputEnabled,
                "回滚精确恢复原许可，仍尊重原输入关闭或当前菜单暂停");
            if (!failLoop)
            {
                Check(SceneChangeMgr.Instance.ToggleGameMenu() && InputControlMgr.Instance.InputEnabled,
                    "菜单继续后恢复打断前的输入，不留下延迟输入锁");
            }
            Check(prop.gameObject.activeSelf && !(bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop)
                && prop.transform.position == takenPosition, "失败只归还触发道具，保留其当前位置");
            Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero
                && DataAndInitMgr.Instance.ConfirmedEndingVector == confirmedBeforeFailure,
                "通关前段或循环失败精确撤销本次贡献");
            var fsm = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(body);
            PlayerResourceFacts previousResources = body.ResourceFacts;
            fsm.stamina = fsm.oxygen = 50f;
            InputControlMgr.Instance.PlayerInputData.moveInput = .25f;
            body.TickLogic(.1f);
            Check(body._ActionData.onMove > 0f && !Mathf.Approximately(fsm.stamina, 50f)
                && !Mathf.Approximately(fsm.oxygen, 50f), "打断后运动指令及体力／氧气更新均重新受理");
            InputControlMgr.Instance.PlayerInputData.moveInput = 0f; body.TickLogic(0f);
            fsm.stamina = previousResources.stamina; fsm.oxygen = previousResources.oxygen;
            var receiver = (IPlayerDeathReceiver)typeof(Player).GetField("deathReceiver", Hidden).GetValue(body);
            body.BindDeathReceiver(null);
            Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "打断后死亡入口重新受理");
            body.ResetDeathCycle(); body.BindDeathReceiver(receiver);
            animator.runtimeAnimatorController = originalController;
            InputControlMgr.Instance.InputOpenOrClose(true);
            double quietUntil = EditorApplication.timeSinceStartup + .8;
            while (EditorApplication.timeSinceStartup < quietUntil) yield return null;
            Check(!Loading() && !SceneChangeMgr.Instance.IsLevelClearPending && DataAndInitMgr.Instance.LoadLevelIndex() == 0,
                "超过原动画时长也无迟到加载");
            SceneProp.OfferInteract(body);
            Check(body.IsLevelClearing && prop.gameObject.activeSelf
                && (bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop)
                && presentation.GetLevelClearPlaybackStatus(playback) == PresentationLayer.LevelClearPlaybackStatus.Invalid,
                "同一道具可重新交互，旧播放身份不能完成新等待");
            while (!Ready(1)) yield return null;
            Check(!body.IsLevelClearing && DataAndInitMgr.Instance.LoadLevelIndex() == 1,
                "打断后重试仍沿真实前段、循环与原换关成功安置下一关");
            Check(DataAndInitMgr.Instance.ConfirmedEndingVector == confirmedBeforeFailure + new Vector3(2, -3, 0)
                && DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero, "归还后的重试只确认一次贡献");
        }

        Enter(0);
        while (!Ready(0)) yield return null;
        body = Actor("body"); presentation = body.GetComponentInChildren<PresentationLayer>();
        prop = Prop(body); SceneProp.OfferInteract(body);
        Check(body.IsLevelClearing && SceneChangeMgr.Instance.IsLevelClearPending, "普通刷新回归建立通关等待");
        body.RefreshRuntime();
        body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        InputControlMgr.Instance.InputOpenOrClose(true);
        yield return null;
        Check(!body.IsLevelClearing && !SceneChangeMgr.Instance.IsLevelClearPending && !Loading(),
            "普通刷新作废等待并允许本关重新交互，不留下受理锁");
        Check(prop.gameObject.activeSelf, "普通刷新取消未完成通关时归还原道具");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero, "普通刷新打断通关同步撤销贡献");
        SceneProp.OfferInteract(body);
        Check(body.IsLevelClearing && !Loading(), "退出回归重新进入前段");
        Check(SceneChangeMgr.Instance.ReturnToMenu(), "前段期间允许结束本局");
        while (SceneManager.GetActiveScene().name != "BeginScenes" || SceneLevelSetup.Current != null) yield return null;
        double until = EditorApplication.timeSinceStartup + 1.2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Check(!SceneChangeMgr.Instance.IsLevelClearPending && Actor("body") == null && InputControlMgr.Instance.Player == null,
            "退出作废旧等待，没有迟到加载或重新开放角色");
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0, "取消前段不推进进度");
        Check(DataAndInitMgr.Instance.CurrentLevelEndingVector == Vector3.zero, "返回菜单清本关贡献且旧等待不能恢复");

        while (Loading()) yield return null;
        Enter(0);
        while (!Ready(0)) yield return null;
        // 真实跨关返回教学：让新出生点在全黑下小幅下落，检查移动期间也保持居中。
        Enter(0);
        while (Time.timeScale == 0f || Actor("body") == null) yield return null;
        body = Actor("body");
        Check(Loading() && Object.FindObjectOfType<LoadingPanel>().TransitionWeight == 1f,
            "教学跨关在全黑内恢复物理，尚未开放操作");
        body.RefreshRuntime(new Vector2(9000f, 9000f));
        Bounds bounds = body.GetComponent<CharacterPhysics>().BodyBounds;
        var floor = new GameObject("V20 temporary spawn floor"); fixtures.Add(floor);
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.transform.position = new Vector3(bounds.center.x, bounds.min.y - .2f, 0f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(6f, .1f);
        Physics2D.SyncTransforms();
        body.RefreshRuntime(new Vector2(9000f, 9000f));
        float startY = body.GetComponent<Rigidbody2D>().position.y;
        bool fell = false;
        yield return null;
        while (Loading())
        {
            Require(!body.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                "跨关出生下落期间实体持续可见");
            Camera portrait = Array.Find(Object.FindObjectsOfType<Camera>(), c => c.enabled && c.name == "PlayerPortraitCamera");
            Require(portrait != null, "跨关出生下落保持角色前景镜头");
            Vector3 viewport = portrait.WorldToViewportPoint(body.transform.position);
            Require(Mathf.Abs(viewport.x - .5f) < .002f && Mathf.Abs(viewport.y - .5f) < .002f,
                "本帧实际下落位置在前景镜头中心");
            fell |= body.GetComponent<Rigidbody2D>().position.y < startY - .05f;
            yield return null;
        }
        Check(fell && body.NowPhyData.isGrounded, "新出生点实际下落并落稳，全程角色可见且居中");
        Object.Destroy(floor);
        Enter(0);
        while (Time.timeScale == 0f) yield return null;
        Check(Loading() && SceneChangeMgr.Instance.ReturnToMenu(), "全黑物理缓冲期间可返回菜单");
        while (Loading()) yield return null;
        Check(Actor("body") == null && !Object.FindObjectOfType<LoadingPanel>()
            && !Array.Exists(Object.FindObjectsOfType<Camera>(), c => c.enabled && c.name == "PlayerPortraitCamera"),
            "缓冲期间退出作废迟到揭幕，角色与前景镜头完整清理");
    }

    static SceneProp Prop(Player body)
    {
        var obj = new GameObject("Level clear validation prop"); fixtures.Add(obj);
        obj.transform.position = body.GetComponent<Collider2D>().bounds.center;
        var collider = obj.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = Vector2.one * 4;
        var prop = obj.AddComponent<SceneProp>();
        typeof(SceneProp).GetField("endingContribution", Hidden).SetValue(prop, new Vector2(2, -3));
        Physics2D.SyncTransforms();
        return prop;
    }
    static void Enter(int index) { DataAndInitMgr.Instance.SaveLevelIndex(index); SceneChangeMgr.Instance.EnterSavedLevel(); }
    static Player Actor(string field) => (Player)typeof(SceneChangeMgr).GetField(field, Hidden).GetValue(SceneChangeMgr.Instance);
    static bool Loading() => (bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance);
    static bool Ready(int index) => !Loading() && SceneLevelSetup.Current != null && InputControlMgr.Instance.Player != null
        && DataAndInitMgr.Instance.LoadLevelIndex() == index;
    static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
    static void Check(bool condition, string label)
    {
        Require(condition, label); passed.Add("PASS " + label);
        Directory.CreateDirectory(".utmp/LevelClearValidation");
        File.WriteAllLines(".utmp/LevelClearValidation/flow-progress.txt", passed);
    }
    static void RecordError(string message, string trace, LogType type)
    {
        if (type == LogType.Error && expectedPlaybackErrors > 0
            && message.EndsWith("：通关动画配置无效或被打断，本次加载等待作废。", StringComparison.Ordinal))
        {
            --expectedPlaybackErrors;
            failureStateHash = expectedFailureAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            return;
        }
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError ??= message + "\n" + trace;
    }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        foreach (Object item in fixtures) if (item != null) Object.DestroyImmediate(item);
        fixtures.Clear();
        DataAndInitMgr.debugPlayerProgressDirectory = previousProgressDirectory;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        passed.Add("RESULT: " + (failure == null ? "PASS" : "FAIL " + failure.Message));
        File.WriteAllLines(".utmp/LevelClearValidation/flow-checks.txt", passed);
        Debug.Log("Level clear flow: " + passed.Count + "; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
