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
    static string progressPath, runtimeError;
    static byte[] previousProgress;
    static bool hadProgress, previousOptionsEnabled, finished;
    static EnterPlayModeOptions previousOptions;
    static Exception failure;
    static double deadline;
    static IEnumerator checks;
    static Animator expectedFailureAnimator;
    static int expectedPlaybackErrors, failureStateHash;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求独立 batch editor。");
        CatgirlLevelClearAssets.ImportAndValidate();
        CatgirlPresentationAnimationChecks.Run();
        ScenePropInteractionChecks.Run();
        passed.Clear(); fixtures.Clear(); runtimeError = null; failure = null; finished = false;
        expectedPlaybackErrors = failureStateHash = 0; expectedFailureAnimator = null;
        progressPath = Path.Combine(Application.persistentDataPath, "LevelProgress.txt");
        hadProgress = File.Exists(progressPath);
        previousProgress = hadProgress ? File.ReadAllBytes(progressPath) : null;
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
        while (!Ready(0)) yield return null;
        Player body = Actor("body");
        var presentation = body.GetComponentInChildren<PresentationLayer>();
        Check(presentation.ValidateLevelClearPlayback(0, out _), "实体编号 0 的前段和循环可播放");
        Check(!presentation.ValidateLevelClearPlayback(99, out _) && !body.IsLevelClearing, "未配置编号明确拒绝且不锁角色");
        Check(!body.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "实体仍不能开启飞行");
        int bodyId = body.GetInstanceID(), setupId = SceneLevelSetup.Current.GetInstanceID();
        SceneProp prop = Prop(body);
        SceneProp.OfferInteract(body);
        Check(!prop.gameObject.activeSelf && body.IsLevelClearing && SceneChangeMgr.Instance.IsLevelClearPending && !Loading(),
            "真实通关点按领取道具并锁定前段，尚未开始加载");
        Check(!SceneChangeMgr.Instance.TryClearLevel(body), "重复通关不重新开始或重复结算");
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
            }
            yield return null;
        }
        yield return null;
        Check(sawLoop, "真实前段完成后进入加载循环");
        Check(Actor("body").GetInstanceID() == bodyId && !body.IsLevelClearing && !SceneChangeMgr.Instance.IsLevelClearPending,
            "下一关保留同一实体并释放通关锁");
        Check(presentation.GetLevelClearPlaybackStatus(playback) == PresentationLayer.LevelClearPlaybackStatus.Invalid,
            "淡入结束后旧播放身份作废");
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 1 && Time.timeScale == 1f, "安置成功后才保存下一关并恢复时间");
        Player spirit = Actor("spirit");
        Check(spirit != null && InputControlMgr.Instance.Player == spirit, "普通关沿现有规则创建并控制精神体");
        Check(!SceneChangeMgr.Instance.TryClearLevel(spirit) && !spirit.IsLevelClearing, "共享脚本中的精神体不能触发通关");
        Check(!spirit.GetComponentInChildren<PresentationLayer>().ValidateLevelClearPlayback(0, out _),
            "精神体控制器不要求实体通关状态");
        Check(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash("Base Layer.On"),
            "下一关待命实体恢复原待机，不残留通关末帧");

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
            Check(body.IsLevelClearing && !prop.gameObject.activeSelf && !Loading(),
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
            Check(body.IsLevelClearing && !prop.gameObject.activeSelf
                && presentation.GetLevelClearPlaybackStatus(playback) == PresentationLayer.LevelClearPlaybackStatus.Invalid,
                "同一道具可重新交互，旧播放身份不能完成新等待");
            while (!Ready(1)) yield return null;
            Check(!body.IsLevelClearing && DataAndInitMgr.Instance.LoadLevelIndex() == 1,
                "打断后重试仍沿真实前段、循环与原换关成功安置下一关");
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
        SceneProp.OfferInteract(body);
        Check(body.IsLevelClearing && !Loading(), "退出回归重新进入前段");
        Check(SceneChangeMgr.Instance.ReturnToMenu(), "前段期间允许结束本局");
        while (SceneManager.GetActiveScene().name != "BeginScenes" || SceneLevelSetup.Current != null) yield return null;
        double until = EditorApplication.timeSinceStartup + 1.2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Check(!SceneChangeMgr.Instance.IsLevelClearPending && Actor("body") == null && InputControlMgr.Instance.Player == null,
            "退出作废旧等待，没有迟到加载或重新开放角色");
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 0, "取消前段不推进进度");
    }

    static SceneProp Prop(Player body)
    {
        var obj = new GameObject("Level clear validation prop"); fixtures.Add(obj);
        obj.transform.position = body.GetComponent<Collider2D>().bounds.center;
        var collider = obj.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = Vector2.one * 4;
        var prop = obj.AddComponent<SceneProp>();
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
        if (hadProgress) File.WriteAllBytes(progressPath, previousProgress);
        else if (File.Exists(progressPath)) File.Delete(progressPath);
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        passed.Add("RESULT: " + (failure == null ? "PASS" : "FAIL " + failure.Message));
        File.WriteAllLines(".utmp/LevelClearValidation/flow-checks.txt", passed);
        Debug.Log("Level clear flow: " + passed.Count + "; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
