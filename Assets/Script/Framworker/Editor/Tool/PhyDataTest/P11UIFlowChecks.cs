#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>实际菜单、视频、设置、存档入口和教学显示；使用独立进度，不保存场景夹具。</summary>
public static class P11UIFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();
    static int stage;
    static double deadline;
    static Exception failure;
    static bool optionsEnabled;
    static EnterPlayModeOptions options;
    static SimulationMode2D simulation;
    static string oldProgress, progress;
    static BeginPanel begin;
    static SetPanel settings;
    static MusicSetPanel music;
    static InputChangePanel keys;
    static GameplayPanel gameplay;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求新开的 batch editor");
        P11UIPrefabUpgrade.Apply();
        oldProgress = DataAndInitMgr.debugPlayerProgressDirectory;
        progress = Path.GetFullPath(".utmp/P11Validation/progress-" + Guid.NewGuid().ToString("N"));
        DataAndInitMgr.debugPlayerProgressDirectory = progress;
        optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled; options = EditorSettings.enterPlayModeOptions;
        simulation = Physics2D.simulationMode;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        passed.Clear(); stage = 0; failure = null;
        deadline = EditorApplication.timeSinceStartup + 150;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        Application.logMessageReceived += RecordError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    static void Advance()
    {
        if (stage == 99) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); return; }
        try
        {
            if (failure != null) throw failure;
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("P11 超时，步骤=" + stage);
            if (!Application.isPlaying || Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) return;
            switch (stage)
            {
                case 0:
                    UIMgr.Instance.GetOnePanel<BeginPanel>(panel => begin = panel);
                    if (begin == null) return;
                    VideoPlayer video = begin.GetComponentInChildren<VideoPlayer>(true);
                    if (!video.isPrepared || !video.isPlaying || video.frame < 1) return;
                    Check(begin.GetComponentsInChildren<Button>().Length == 3, "开始界面只有开始、设置、退出三项入口");
                    Check(video.isLooping && video.audioOutputMode == VideoAudioOutputMode.None && video.targetTexture != null,
                        "封面实际解码播放且设置循环、无音轨、无深度画面缓冲");
                    Check(video.targetTexture.depth == 0 && !video.targetTexture.useMipMap, "视频缓冲没有深度和 Mipmap");
                    RenderTexture target = video.targetTexture; begin.ShowMe();
                    Check(video.targetTexture == target, "重复显示开始面板不重复分配视频缓冲");
                    Capture("p11-begin.png");
                    begin.FindUIObj<Button>("t2").onClick.Invoke(); stage = 1; break;
                case 1:
                    UIMgr.Instance.GetOnePanel<SetPanel>(panel => settings = panel);
                    if (settings == null || !settings.isActiveAndEnabled) return;
                    Check(!begin.isActiveAndEnabled && begin.GetComponentInChildren<VideoPlayer>(true).targetTexture == null,
                        "离开开始界面即停止播放并释放缓冲");
                    Check(settings.FindUIObj<Button>("mu").gameObject.activeInHierarchy && settings.FindUIObj<Button>("ke").gameObject.activeInHierarchy,
                        "设置外层显示居中音乐和改键入口");
                    Capture("p11-settings.png");
                    settings.FindUIObj<Button>("mu").onClick.Invoke(); stage = 2; break;
                case 2:
                    UIMgr.Instance.GetOnePanel<MusicSetPanel>(panel => music = panel);
                    if (music == null || !music.gameObject.activeInHierarchy || music.transform.parent.name != "SettingsContent") return;
                    Check(!settings.FindUIObj<Button>("mu").gameObject.activeSelf && !settings.FindUIObj<Button>("ke").gameObject.activeSelf,
                        "打开音乐子页后两个入口消失，只留下当前列");
                    Check(settings.GetComponentsInChildren<Button>().Length == 1, "音乐子页只有外层一颗返回按钮");
                    Slider volume = music.FindUIObj<Slider>("s1"); float oldVolume = volume.value;
                    volume.value = .33f; Check(Mathf.Approximately(MusicMgr.Instance.BkVolume, .33f), "音乐滑条仍接现有音量消费者");
                    volume.value = oldVolume;
                    Capture("p11-music.png"); settings.FindUIObj<Button>("hui").onClick.Invoke();
                    Check(settings.FindUIObj<Button>("mu").gameObject.activeSelf && !music.gameObject.activeInHierarchy, "外层返回隐藏音乐页并恢复两入口");
                    settings.FindUIObj<Button>("ke").onClick.Invoke(); stage = 3; break;
                case 3:
                    UIMgr.Instance.GetOnePanel<InputChangePanel>(panel => keys = panel);
                    if (keys == null || !keys.gameObject.activeInHierarchy || keys.transform.Find("rebindScroll") == null) return;
                    Check(settings.GetComponentsInChildren<Button>().Length == 9, "改键页八行按键与外层唯一返回同时显示");
                    Check(keys.transform.Find("out").gameObject.activeSelf == false && keys.transform.Find("rebindScroll/viewport/rebindContent").childCount == 8,
                        "改键子页自己的返回隐藏，八行仍在可滚动列中");
                    InputActionMap map = (InputActionMap)typeof(InputControlMgr).GetField("player1", Hidden).GetValue(InputControlMgr.Instance);
                    InputAction form = map["FormSwitch"]; InputBinding original = form.bindings[0];
                    try
                    {
                        form.ApplyBindingOverride(0, map["Jump"].bindings[0].effectivePath);
                        settings.FindUIObj<Button>("hui").onClick.Invoke();
                        Check(keys.gameObject.activeInHierarchy && !settings.FindUIObj<Button>("mu").gameObject.activeSelf && !keys.CanLeave(),
                            "存在重复键时外层返回留在改键页并显示冲突");
                        form.ApplyBindingOverride(0, "<Keyboard>/enter");
                        Check(!keys.CanLeave() && keys.FindUIObj<Text>("conflictT").text.Contains("回车"), "改键页明确提示回车专用键冲突");
                    }
                    finally { if (original.overridePath == null) form.RemoveBindingOverride(0); else form.ApplyBindingOverride(0, original); }
                    keys.UpdateUIvalue(); Capture("p11-keys.png");
                    settings.FindUIObj<Button>("hui").onClick.Invoke();
                    Check(settings.FindUIObj<Button>("ke").gameObject.activeSelf && !keys.gameObject.activeInHierarchy, "修正冲突后返回设置入口");
                    settings.FindUIObj<Button>("mu").onClick.Invoke(); stage = 4; break;
                case 4:
                    if (!music.gameObject.activeInHierarchy) return;
                    Check(music.transform.parent.name == "SettingsContent" && settings.GetComponentsInChildren<Button>().Length == 1,
                        "重新打开已缓存的子页仍归外层显示并保持唯一返回");
                    settings.FindUIObj<Button>("hui").onClick.Invoke(); settings.FindUIObj<Button>("hui").onClick.Invoke(); stage = 5; break;
                case 5:
                    if (!begin.gameObject.activeInHierarchy) return;
                    DataAndInitMgr.Instance.SaveLevelIndex(0);
                    begin.FindUIObj<Button>("t1").onClick.Invoke();
                    Check(!begin.gameObject.activeInHierarchy && begin.GetComponentInChildren<VideoPlayer>(true).targetTexture == null,
                        "开始游戏直接关闭封面并进入保存关序，没有 ChangePanel");
                    stage = 6; break;
                case 6:
                    UIMgr.Instance.GetOnePanel<GameplayPanel>(panel => gameplay = panel);
                    if (gameplay == null || !gameplay.gameObject.activeInHierarchy || InputControlMgr.Instance.Player == null || Time.timeScale <= 0f) return;
                    Check(SceneLevelSetup.Current.Teaching && LevelStageMgr.Instance.LevelIndex == 0, "开始入口实际进入已保存教学关");
                    Check(!gameplay.transform.Find("CognitionArea").gameObject.activeSelf && gameplay.transform.Find("EndExplorationNote") == null,
                        "教学不显示认知条、长按白条和结束探索便签");
                    Check(!gameplay.transform.Find("StaminaBar").gameObject.activeSelf && !gameplay.transform.Find("OxygenBar").gameObject.activeSelf,
                        "角色初始满资源时两条竖条均隐藏");
                    Check(gameplay.transform.Find("ResourceArea") == null && gameplay.transform.Find("StageArea") == null && gameplay.transform.Find("MenuHintArea") == null
                        && gameplay.transform.Find("StartEscapeButton") == null, "游戏界面旧数字、阶段文字、ESC 常驻提示与逃生按钮均移除");
                    CheckResources();
                    foreach (Text text in gameplay.GetComponentsInChildren<Text>(true)) Check(text.font == GameUIAssets.Shared.font, "FZYTK 游戏文字：" + text.name);
                    Capture("p11-tutorial.png");
                    EditorApplication.isPlaying = false; stage = 99; break;
            }
        }
        catch (Exception error) { failure = error; Debug.LogException(error); EditorApplication.isPlaying = false; stage = 99; }
    }

    static void CheckResources()
    {
        RectTransform bar = (RectTransform)gameplay.transform.Find("StaminaBar"); Image fill = gameplay.FindUIObj<Image>("StaminaFill");
        var update = typeof(GameplayPanel).GetMethod("UpdateResource", Hidden);
        object[] values = { bar, fill, 50f, 100f, -1f }; update.Invoke(gameplay, values);
        Check(bar.gameObject.activeSelf && fill.fillAmount == .5f && fill.type == Image.Type.Filled && fill.sprite != null,
            "资源消耗后立即显示竖条并实际具备填充精灵");
        values[2] = 100f; update.Invoke(gameplay, values);
        Check(bar.gameObject.activeSelf, "刚回满保留竖条，避免立即闪退");
        values[4] = Time.unscaledTime - 1.1f; update.Invoke(gameplay, values);
        Check(!bar.gameObject.activeSelf, "回满超过 1 秒后隐藏竖条");
        values[2] = 20f; update.Invoke(gameplay, values); Capture("p11-resource.png");
    }
    static void Capture(string name) => GameMenuInputChecks.Capture(name);
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); passed.Add("PASS " + message); }
    static void RecordError(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure ??= new InvalidOperationException(message + "\n" + stack); }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        DataAndInitMgr.debugPlayerProgressDirectory = oldProgress;
        EditorSettings.enterPlayModeOptions = options; EditorSettings.enterPlayModeOptionsEnabled = optionsEnabled;
        Physics2D.simulationMode = simulation; Time.timeScale = 1f;
        Directory.CreateDirectory(".utmp/P11Validation");
        File.WriteAllLines(".utmp/P11Validation/ui-flow-checks.txt", passed);
        File.AppendAllText(".utmp/P11Validation/ui-flow-checks.txt", "RESULT success=" + (failure == null) + (failure == null ? "" : "\n" + failure));
        Debug.Log("P11 UI flow: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
