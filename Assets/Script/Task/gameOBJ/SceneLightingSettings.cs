using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本场景的受光基线。只控制光照材质，不参与关卡规则或相机后处理。</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class SceneLightingSettings : MonoBehaviour
{
    [Header("全场景光照")]
    [SerializeField, Tooltip("关闭后受光精灵恢复原色；UI、水体和背景使用原材质。")]
    bool lightingEnabled = true;
    [SerializeField, ColorUsage(false), Tooltip("暗处的基础色调。建议蓝灰，避免纯蓝或纯黑。")]
    Color ambientColor = new Color(.72f, .79f, .94f, 1f);
    [SerializeField, Range(0f, 2f), Tooltip("暗处基础照度，保证人物、墙缘和危险面仍可辨。")]
    float ambientIntensity = .9f;
    [SerializeField, Range(0f, 3f), Tooltip("所有受光精灵的局部灯光倍率；单盏灯仍在 Light 组件调颜色、强度与范围。")]
    float localLightIntensity = 1f;

    static readonly int Enabled = Shader.PropertyToID("_SceneLightingEnabled");
    static readonly int Ambient = Shader.PropertyToID("_SceneLightingAmbient");
    static readonly int Local = Shader.PropertyToID("_SceneLightingLocal");
    static SceneLightingSettings current;
    string reportedError;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InitializeGlobals()
    {
        ResetGlobals();
        Shader.SetGlobalFloat("_SceneLightingTransition", 0f);
    }

    static void ResetGlobals()
    {
        current = null;
        Shader.SetGlobalFloat(Enabled, 0f);
        Shader.SetGlobalColor(Ambient, Color.white);
        Shader.SetGlobalFloat(Local, 1f);
    }

    public bool ValidateConfiguration(out string error)
    {
        // 安全：非法参数明确拒绝，不把错误配置改成另一个有效值。
        if (!Finite(ambientIntensity) || ambientIntensity < 0f || ambientIntensity > 2f
            || !Finite(localLightIntensity) || localLightIntensity < 0f || localLightIntensity > 3f)
        { error = "基础照度需在 0–2，局部灯光倍率需在 0–3，且必须为有限数值。"; return false; }
        if (!Finite(ambientColor.r) || !Finite(ambientColor.g) || !Finite(ambientColor.b)
            || ambientColor.r < 0f || ambientColor.r > 1f || ambientColor.g < 0f || ambientColor.g > 1f
            || ambientColor.b < 0f || ambientColor.b > 1f)
        { error = "环境颜色 RGB 必须是 0–1 的有限数值。"; return false; }
        error = null;
        return true;
    }

    public void Apply()
    {
        // 安全：预设资源本身不能接管当前场景；停用组件不再发布设置。
        if (!isActiveAndEnabled || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
        // 编辑器只预览活动场景，避免同时打开的其它场景互相覆盖。
        if (gameObject.scene != SceneManager.GetActiveScene()) return;
        if (current != null && current != this && current.gameObject.scene == gameObject.scene)
        { Report("同一场景只允许一个启用的全场景光照设置。"); return; }
        if (!ValidateConfiguration(out string error))
        {
            Report(error);
            // 当前设置变为非法时退回原色，不能残留上一组灯光参数。
            if (current == this) ResetGlobals();
            return;
        }
        current = this;
        reportedError = null;
        Color color = QualitySettings.activeColorSpace == ColorSpace.Linear ? ambientColor.linear : ambientColor;
        Shader.SetGlobalColor(Ambient, color * ambientIntensity);
        Shader.SetGlobalFloat(Local, localLightIntensity);
        Shader.SetGlobalFloat(Enabled, lightingEnabled ? 1f : 0f);
    }

    void OnEnable()
    {
        SceneManager.activeSceneChanged += ActiveSceneChanged;
        Apply();
    }

    void LateUpdate() => Apply();

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        // 只清理自己发布的状态，旧场景退出不能清除新场景设置。
        if (current == this) ResetGlobals();
    }

    void ActiveSceneChanged(Scene previous, Scene next)
    {
        if (current == this && next != gameObject.scene) ResetGlobals();
        Apply();
    }

    void Report(string error)
    {
        // 同一错误只报一次，编辑时不逐帧刷屏。
        if (reportedError == error) return;
        reportedError = error;
        Debug.LogError(name + " 的场景光照配置无效：" + error, this);
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
