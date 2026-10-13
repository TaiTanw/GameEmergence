#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>真实菜单点击、首帧过场和显示设置验收；进度与偏好均隔离／恢复。</summary>
public static class MenuDisplayChecks
{
    const string Output = ".utmp/MenuDisplayValidation";
    static readonly List<string> passed = new List<string>();
    static int stage, oldWidth, oldWindow;
    static bool hadWidth, hadWindow, oldOptionsEnabled, sawPanelFade, sawMenuDarkening, sawGameBrightening;
    static EnterPlayModeOptions oldOptions;
    static SimulationMode2D oldSimulation;
    static string oldProgressDirectory, runtimeError;
    static Exception failure;
    static double deadline;
    static BeginPanel begin;

    public static void RunForBatch()
    {
        MenuDisplayPrefabUpdate.Apply();
        Directory.CreateDirectory(Output);
        hadWidth = PlayerPrefs.HasKey("Display.Width"); oldWidth = PlayerPrefs.GetInt("Display.Width");
        hadWindow = PlayerPrefs.HasKey("Display.Windowed"); oldWindow = PlayerPrefs.GetInt("Display.Windowed");
        oldProgressDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        DataAndInitMgr.debugPlayerProgressDirectory = Path.GetFullPath(Output + "/Save-" + Guid.NewGuid().ToString("N"));
        oldOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled; oldOptions = EditorSettings.enterPlayModeOptions;
        oldSimulation = Physics2D.simulationMode;
        EditorSettings.enterPlayModeOptionsEnabled = true; EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        passed.Clear(); failure = null; runtimeError = null; stage = 0;
        sawPanelFade = sawMenuDarkening = sawGameBrightening = false; begin = null;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity");
        deadline = EditorApplication.timeSinceStartup + 120;
        Application.logMessageReceived += RecordError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    static void Advance()
    {
        if (stage == 99)
        { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(); return; }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("菜单验收超时，步骤 " + stage);
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (!Application.isPlaying) return;
            begin = Object.FindObjectsOfType<BeginPanel>(true).FirstOrDefault();
            if (begin != null && begin.gameObject.activeInHierarchy)
            {
                float alpha = begin.GetComponent<CanvasGroup>().alpha;
                if (alpha > .05f && alpha < .95f) sawPanelFade = true;
            }
            switch (stage)
            {
                case 0:
                    if (begin == null || !begin.IsFullyShown || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) return;
                    Check(sawPanelFade && begin.GetComponent<CanvasGroup>().interactable, "开始面板经过可见淡入，完成后开放点击");
                    CheckAspect();
                    begin.FindUIObj<Button>("t2").onClick.Invoke(); stage = 1; break;
                case 1:
                    SetPanel settings = Object.FindObjectOfType<SetPanel>();
                    if (settings == null || begin.gameObject.activeInHierarchy) return;
                    Check(begin.GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true).targetTexture == null, "开始面板淡出后停播并释放视频缓冲");
                    Check(settings.FindUIObj<Button>("di").gameObject.activeSelf, "设置页提供音乐、改键、显示三个独立入口");
                    settings.FindUIObj<Button>("di").onClick.Invoke();
                    Check(!settings.FindUIObj<Button>("mu").gameObject.activeSelf && settings.transform.Find("SettingsContent/DisplaySettingsArea").gameObject.activeSelf,
                        "进入显示设置时隐藏入口，只显示当前子页");
                    settings.FindUIObj<Button>("DisplayMode").onClick.Invoke();
                    settings.FindUIObj<Button>("DisplayResolution").onClick.Invoke();
                    Check(settings.FindUIObj<Button>("DisplayApply").GetComponentInChildren<Text>().text == "应用", "更改选择后等待应用，不立即切换桌面");
                    settings.FindUIObj<Button>("DisplayApply").onClick.Invoke();
                    Check(PlayerPrefs.GetInt("Display.Windowed") == (DisplaySettings.Instance.Windowed ? 1 : 0)
                        && PlayerPrefs.GetInt("Display.Width") == DisplaySettings.Instance.Resolutions[DisplaySettings.Instance.ResolutionIndex].x,
                        "应用后保存分辨率和窗口偏好");
                    Check(settings.FindUIObj<Button>("DisplayApply").GetComponentInChildren<Text>().text == "已应用", "应用后显示明确状态");
                    Capture("display-settings.png");
                    settings.FindUIObj<Button>("hui").onClick.Invoke();
                    Check(settings.FindUIObj<Button>("di").gameObject.activeSelf, "外层返回先关闭显示子页");
                    settings.FindUIObj<Button>("hui").onClick.Invoke(); stage = 2; break;
                case 2:
                    if (begin == null || !begin.IsFullyShown) return;
                    DataAndInitMgr.Instance.SaveLevelIndex(0);
                    begin.FindUIObj<Button>("t1").onClick.Invoke();
                    Check(SceneChangeMgr.Instance.IsLoading && begin.gameObject.activeInHierarchy && begin.GetComponent<CanvasGroup>().alpha == 1f,
                        "首次点击受理后保留完整开始画面，等待渐黑");
                    int serial = LoadSerial(); begin.FindUIObj<Button>("t1").onClick.Invoke();
                    Check(LoadSerial() == serial && !begin.GetComponent<CanvasGroup>().interactable, "重复点击不启动第二轮加载，菜单按钮锁定");
                    stage = 3; break;
                case 3:
                    LoadingPanel mask = Object.FindObjectOfType<LoadingPanel>();
                    bool oldScene = SceneManager.GetActiveScene().name == "BeginScenes";
                    if (mask != null && mask.TransitionWeight > .15f && mask.TransitionWeight < .85f)
                    {
                        if (oldScene)
                        {
                            if (!sawMenuDarkening) { Check(begin.gameObject.activeInHierarchy, "渐黑期间开始封面保留在遮罩下"); Capture("menu-darkening.png"); }
                            sawMenuDarkening = true;
                        }
                        else
                        {
                            if (!sawGameBrightening) { Check(Object.FindObjectOfType<GameplayPanel>() != null, "新场景淡入前游戏 UI 已异步加载完成"); Capture("game-brightening.png"); }
                            sawGameBrightening = true;
                        }
                    }
                    if (oldScene || SceneChangeMgr.Instance.IsLoading) return;
                    Check(sawMenuDarkening && sawGameBrightening && (begin == null || !begin.gameObject.activeInHierarchy) && InputControlMgr.Instance.InputEnabled,
                        "渐黑、异步场景和 UI 加载、渐亮依次完成后交还输入");
                    Check(SceneChangeMgr.Instance.ReturnToMenu(), "完成进关后可以返回主菜单"); stage = 4; break;
                case 4:
                    if (SceneManager.GetActiveScene().name != "BeginScenes" || SceneChangeMgr.Instance.IsLoading || begin == null || !begin.IsFullyShown) return;
                    Check(Object.FindObjectOfType<LoadingPanel>() == null && begin.GetComponent<CanvasGroup>().interactable,
                        "返回菜单保留黑幕至封面就绪，渐亮后恢复按钮");
                    Capture("menu-restored.png"); stage = 99; EditorApplication.isPlaying = false; break;
            }
        }
        catch (Exception error)
        { failure = error; Debug.LogException(error); stage = 99; EditorApplication.isPlaying = false; }
    }

    static void CheckAspect()
    {
        foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1080), new Vector2Int(1024, 768) })
        {
            Rect viewport = DisplaySettings.Viewport(size.x, size.y);
            Check(Mathf.Abs(viewport.width * size.x / (viewport.height * size.y) - 16f / 9f) < .001f
                && Mathf.Abs(viewport.center.x - .5f) < .001f && Mathf.Abs(viewport.center.y - .5f) < .001f,
                size + " 的世界和 UI 可见区域居中保持 16:9");
        }
        Check(DisplaySettings.Instance.Resolutions.All(size => size.x * 9 == size.y * 16), "全部分辨率选项都是整数 16:9");
        Camera ui = Object.FindObjectsOfType<Camera>().First(camera => camera.name.StartsWith("UICamera"));
        Check(ui.rect == DisplaySettings.Viewport(Screen.width, Screen.height), $"实际 {Screen.width}×{Screen.height} UI 镜头使用固定比例视口");
        Canvas.ForceUpdateCanvases();
        Rect canvas = ((RectTransform)Object.FindObjectOfType<Canvas>().transform).rect;
        Check(Mathf.Abs(canvas.width / canvas.height - 16f / 9f) < .001f && Mathf.Abs(canvas.width - 1920f) < 1f,
            "实际画布维持 1920×1080 预设布局，黑边不挤压 UI");
    }
    static int LoadSerial() => (int)typeof(SceneChangeMgr).GetField("loadSerial", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(SceneChangeMgr.Instance);
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); passed.Add("PASS " + message); }
    static void RecordError(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError ??= message + "\n" + trace; }
    static void Capture(string name)
    {
        Camera[] cameras = Object.FindObjectsOfType<Camera>().Where(camera => camera.enabled).OrderBy(camera => camera.depth).ToArray();
        RenderTexture[] previous = cameras.Select(camera => camera.targetTexture).ToArray();
        RenderTexture active = RenderTexture.active;
        int height = Mathf.RoundToInt(960f * Screen.height / Screen.width);
        var render = new RenderTexture(960, height, 24); var texture = new Texture2D(960, height, TextureFormat.RGB24, false);
        try
        {
            Canvas.ForceUpdateCanvases();
            foreach (Camera camera in cameras) { camera.targetTexture = render; camera.Render(); }
            RenderTexture.active = render; texture.ReadPixels(new Rect(0, 0, 960, height), 0, 0); texture.Apply();
            if (name == "menu-restored.png")
            {
                Rect viewport = DisplaySettings.Viewport(Screen.width, Screen.height);
                if (viewport.width < .99f || viewport.height < .99f)
                    Check(texture.GetPixel(1, 1).grayscale < .01f && texture.GetPixel(958, height - 2).grayscale < .01f
                        && texture.GetPixel(480, height / 2).grayscale > .03f,
                        "实际非 16:9 输出四周黑边正确，中心保留封面画面");
            }
            File.WriteAllBytes(Output + "/" + name, texture.EncodeToPNG());
        }
        finally
        {
            for (int index = 0; index < cameras.Length; index++) cameras[index].targetTexture = previous[index];
            RenderTexture.active = active; Object.DestroyImmediate(texture); Object.DestroyImmediate(render);
        }
    }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        if (hadWidth) PlayerPrefs.SetInt("Display.Width", oldWidth); else PlayerPrefs.DeleteKey("Display.Width");
        if (hadWindow) PlayerPrefs.SetInt("Display.Windowed", oldWindow); else PlayerPrefs.DeleteKey("Display.Windowed");
        PlayerPrefs.Save(); DataAndInitMgr.debugPlayerProgressDirectory = oldProgressDirectory;
        Physics2D.simulationMode = oldSimulation; Time.timeScale = 1f;
        EditorSettings.enterPlayModeOptions = oldOptions; EditorSettings.enterPlayModeOptionsEnabled = oldOptionsEnabled;
        File.WriteAllLines(Output + "/checks.txt", passed);
        Debug.Log("Menu display checks: " + passed.Count + "; success=" + (failure == null && runtimeError == null));
        EditorApplication.Exit(failure == null && runtimeError == null ? 0 : 1);
    }
}
#endif
