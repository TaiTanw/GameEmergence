using UnityEngine;

/// <summary>只在转场期间处理游戏镜头，角色前景和 UI 继续沿各自镜头绘制。</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public sealed class TransitionPixelation : MonoBehaviour
{
    const string ShaderPath = "NecessaryAssets/TransitionPixelation";
    Material material;
    bool materialFailed;
    float weight, maxBlockPixels;

    void Awake() { enabled = false; }

    internal void SetWeight(float value, float pixels)
    {
        weight = value;
        maxBlockPixels = pixels;
        // 转场结束直接关闭图像效果，正常游戏不增加全屏后处理。
        enabled = weight > 0f && EnsureMaterial();
    }

    bool EnsureMaterial()
    {
        if (material != null) return true;
        if (materialFailed) return false;
        Shader shader = Resources.Load<Shader>(ShaderPath);
        // 安全：构建也从 Resources 保留并加载 Shader，失效时明确拒绝此效果。
        if (shader == null || !shader.isSupported)
        {
            materialFailed = true;
            Debug.LogError(name + "：转场像素化 Shader 缺失或不受当前设备支持。", this);
            return false;
        }
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // 安全：组件销毁或材质失效时仍完成本次相机输出。
        if (material == null) { Graphics.Blit(source, destination); return; }
        // 参数按 1080 高度定义，窗口缩放后仍保持相同视觉大小的正方形像素块。
        float maximum = Mathf.Max(1f, maxBlockPixels * source.height / 1080f);
        material.SetFloat("_BlockPixels", Mathf.Lerp(1f, maximum, weight));
        Graphics.Blit(source, destination, material);
    }

    void OnDestroy()
    {
        if (material == null) return;
        // 运行时延后销毁；编辑器临时验证对象立即释放其独立材质。
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
    }
}
