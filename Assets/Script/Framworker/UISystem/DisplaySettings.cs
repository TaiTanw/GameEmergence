using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>只持有本机显示偏好；世界、UI 和过场镜头共用固定 16:9 可见区域。</summary>
public sealed class DisplaySettings : MonoBehaviour
{
    const string WidthKey = "Display.Width", WindowKey = "Display.Windowed";
    public static DisplaySettings Instance { get; private set; }
    public bool Windowed { get; private set; }
    public int ResolutionIndex { get; private set; }
    public IReadOnlyList<Vector2Int> Resolutions => resolutions;
    readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    Camera blackBackground;
    int screenWidth, screenHeight;

    void Awake()
    {
        Instance = this;
        int width = Display.main.systemWidth, height = Display.main.systemHeight;
        // 安全：无桌面信息的编辑器验收使用项目默认尺寸，不写玩家偏好。
        if (width <= 0 || height <= 0) { width = 1920; height = 1080; }
        foreach (int candidate in new[] { 960, 1280, 1600, 1920 })
            if (candidate <= width && candidate * 9 / 16 <= height)
                resolutions.Add(new Vector2Int(candidate, candidate * 9 / 16));
        // 小屏幕至少提供一项能放进桌面的整数 16:9 尺寸。
        if (resolutions.Count == 0)
        {
            int unit = Mathf.Max(1, Mathf.Min(width / 16, height / 9));
            resolutions.Add(new Vector2Int(unit * 16, unit * 9));
        }
        int savedWidth = PlayerPrefs.GetInt(WidthKey, 1920);
        ResolutionIndex = resolutions.Count - 1;
        for (int index = 0; index < resolutions.Count; index++)
            if (resolutions[index].x == savedWidth) ResolutionIndex = index;
        Windowed = PlayerPrefs.GetInt(WindowKey, 0) != 0;
        var backdrop = new GameObject("DisplayBlackBackground");
        backdrop.transform.SetParent(transform, false);
        blackBackground = backdrop.AddComponent<Camera>();
        blackBackground.clearFlags = CameraClearFlags.SolidColor;
        blackBackground.backgroundColor = Color.black;
        blackBackground.cullingMask = 0;
        blackBackground.depth = -100f;
        blackBackground.useOcclusionCulling = false;
        Camera.onPreCull += FitCamera;
        SceneManager.sceneLoaded += SceneLoaded;
        // 编辑器不改变宿主窗口或桌面分辨率，构建运行时恢复本机选择。
        if (!Application.isEditor) SetScreen();
    }

    public void Apply(int index, bool windowed)
    {
        // 安全：旧设置页或无效索引不产生越界，也不写入错误偏好。
        if (index < 0 || index >= resolutions.Count) return;
        ResolutionIndex = index;
        Windowed = windowed;
        if (!Application.isEditor) SetScreen();
        PlayerPrefs.SetInt(WidthKey, resolutions[index].x);
        PlayerPrefs.SetInt(WindowKey, windowed ? 1 : 0);
        PlayerPrefs.Save();
    }

    void SetScreen()
    {
        Vector2Int size = resolutions[ResolutionIndex];
        Screen.SetResolution(size.x, size.y, Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
    }

    public static Rect Viewport(int width, int height)
    {
        // 安全：窗口切换期间可能短暂报告零尺寸，等有效尺寸后再计算黑边。
        if (width <= 0 || height <= 0) return new Rect(0f, 0f, 1f, 1f);
        float aspect = (float)width / height, target = 16f / 9f;
        if (aspect > target)
        { float span = target / aspect; return new Rect((1f - span) * .5f, 0f, span, 1f); }
        float vertical = aspect / target;
        return new Rect(0f, (1f - vertical) * .5f, 1f, vertical);
    }

    void FitCamera(Camera camera)
    {
        // 离屏纹理保留自身构图，黑边底层始终清理整个窗口。
        if (camera == blackBackground || camera.targetTexture != null || camera.targetDisplay != 0) return;
        camera.rect = Viewport(Screen.width, Screen.height);
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode) => FitVisibleCameras();
    void FitVisibleCameras()
    { foreach (Camera camera in Camera.allCameras) FitCamera(camera); }
    void LateUpdate()
    {
        // 屏幕尺寸变化先更新 UI 摄像机，画布排版和点击映射随后使用同一视口。
        if (screenWidth == Screen.width && screenHeight == Screen.height) return;
        screenWidth = Screen.width; screenHeight = Screen.height;
        FitVisibleCameras();
    }
    void OnDestroy()
    {
        Camera.onPreCull -= FitCamera;
        SceneManager.sceneLoaded -= SceneLoaded;
        if (Instance == this) Instance = null;
    }
}
