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

public static class EndingMotionChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Output = ".utmp/V22Validation";
    static readonly List<string> results = new List<string>();
    static string previousDirectory, error;
    static LevelOrderFile previousOrder;
    static bool optionsEnabled, finished;
    static EnterPlayModeOptions options;
    static int targetRate;
    static double deadline;
    static IEnumerator checks;
    static Exception failure;
    static EndingMotionProbe probe;
    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求独立 batch editor。");
        Directory.CreateDirectory(Output); results.Clear(); error = null; failure = null; finished = false;
        previousDirectory = DataAndInitMgr.debugPlayerProgressDirectory; previousOrder = DataAndInitMgr.debugLevelOrder;
        DataAndInitMgr.debugPlayerProgressDirectory = Path.GetFullPath(Path.Combine(Output, "Save-" + Guid.NewGuid().ToString("N")));
        DataAndInitMgr.debugLevelOrder = new LevelOrderFile { scenes = new[] { "TeskScenes1" }, endings = new[] { new EndingSceneEntry { scene = "EndingExample" } } };
        optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled; options = EditorSettings.enterPlayModeOptions;
        targetRate = Application.targetFrameRate;
        EditorSettings.enterPlayModeOptionsEnabled = true; EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        Application.targetFrameRate = 120; MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity");
        checks = Run(); deadline = EditorApplication.timeSinceStartup + 120;
        Application.logMessageReceived += RecordError; EditorApplication.update += Advance; EditorApplication.isPlaying = true;
    }
    static void Advance()
    {
        if (finished) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); return; }
        if (!Application.isPlaying) return;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("V22 连续运动检查超时。");
            if (checks.MoveNext()) return;
        }
        catch (Exception exception) { failure = exception; Debug.LogException(exception); }
        finished = true; if (probe != null) probe.Flush(); EditorApplication.isPlaying = false;
    }
    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        SceneChangeMgr.Instance.EnterSavedLevel();
        while (SceneChangeMgr.Instance.IsLoading || SceneLevelSetup.Current == null) yield return null;
        var body = (Player)typeof(SceneChangeMgr).GetField("body", Hidden).GetValue(SceneChangeMgr.Instance);
        var item = new GameObject("V22 final prop"); item.transform.position = body.GetComponent<Collider2D>().bounds.center;
        var collider = item.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = Vector2.one * 4;
        item.AddComponent<SceneProp>(); Physics2D.SyncTransforms(); SceneProp.OfferInteract(body);
        while (!SceneChangeMgr.Instance.IsEnding || SceneChangeMgr.Instance.IsLoading) yield return null;
        var actor = Object.FindObjectOfType<EndingSceneController>().Context.ControlledActor;
        probe = actor.gameObject.AddComponent<EndingMotionProbe>(); probe.Actor = actor; probe.Output = Path.Combine(Output, "motion.csv");
        while (!probe.Complete) yield return null;
        foreach (int phase in new[] { 1, 3 })
        {
            results.Add($"phase={phase}; fixed={probe.Steps[phase]}; badFixed={probe.BadSteps[phase]}; render={probe.Frames[phase]}; backward={probe.BackwardFrames[phase]}; maxFrame={probe.MaxFrame[phase]:F6}; cameraError={probe.MaxCameraError[phase]:F6}; displaySpeedError={probe.MaxSpeedError[phase]:F6}");
            File.WriteAllLines(Path.Combine(Output, "motion-results.txt"), results);
        }
        Check(probe.Steps[1] > 50 && probe.Steps[3] > 50, "开放操作后首段与等待后均采样超过 50 个真实物理步");
        Check(probe.BadSteps[1] == 0 && probe.BadSteps[3] == 0, "首段与等待后恒定输入均每步走足预定距离");
        Check(probe.Frames[1] > 50 && probe.Frames[3] > 50, "首段与等待后均采样真实渲染帧");
        Check(probe.BackwardFrames[1] == 0 && probe.BackwardFrames[3] == 0, "连续运动显示位置不逆向回跳");
        Check(probe.MaxCameraError[1] < .001f && probe.MaxCameraError[3] < .001f, "镜头采样本帧显示位置，无跟随错帧");
        Check(probe.MaxSpeedError[1] < .001f && probe.MaxSpeedError[3] < .001f, "首次及等待后显示位移连续，均按帧时间推进");
        var rigidbody = actor.GetComponent<Rigidbody2D>();
        Vector2 before = rigidbody.position;
        actor.SetAutomaticMotion(true); actor.SetAutomaticMotion(true);
        Check((rigidbody.position - before).sqrMagnitude < .00000001f, "重复开启自动运动不把插值显示位置回写实际刚体");
    }
    static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); results.Add("PASS " + label); }
    static void RecordError(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error ??= message + "\n" + trace; }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        DataAndInitMgr.debugPlayerProgressDirectory = previousDirectory; DataAndInitMgr.debugLevelOrder = previousOrder;
        EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled; EditorSettings.enterPlayModeOptions = options;
        Application.targetFrameRate = targetRate;
        results.Add("RESULT: " + (failure == null ? "PASS" : "FAIL " + failure.Message));
        File.WriteAllLines(Path.Combine(Output, "motion-results.txt"), results);
        Debug.Log("V22 motion checks: success=" + (failure == null)); EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
