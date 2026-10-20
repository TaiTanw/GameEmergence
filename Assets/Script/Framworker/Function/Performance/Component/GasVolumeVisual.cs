using UnityEngine;

/// <summary>气体精灵的动态雾形，只读触发形态；不修改碰撞、环境性质或危险判定。</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class GasVolumeVisual : MonoBehaviour
{
    [SerializeField] SpriteRenderer bodySprite;
    [SerializeField] BoxCollider2D range;
    [SerializeField] SpriteRenderer fogSprite;
    [Header("雾面外观")]
    [SerializeField, Range(0f, 2f), Tooltip("叠乘原精灵透明度；0 隐藏雾面，1 为默认浓度。")]
    float density = 1f;
    [SerializeField, Range(.05f, 1f), Tooltip("雾边向外扩散的距离，占原最短半边的比例；原作用范围始终包在雾内。")]
    float outerPadding = .4f;
    [SerializeField, Range(.02f, .5f), Tooltip("外扩雾边的渐隐宽度；实际作用范围内保持完整覆盖。")]
    float edgeSoftness = .22f;
    [SerializeField, Range(0f, .4f), Tooltip("轮廓起伏强度，占最短半边的比例。")]
    float edgeRoughness = .24f;
    [SerializeField, Min(.05f), Tooltip("每世界单位的雾团密度；越大越细碎，缩放物体不会拉长噪点。")]
    float noiseScale = 1.15f;
    [SerializeField, Range(0f, 3f), Tooltip("流动与透明度变化的速度倍率；0 保留静态雾形。")]
    float flowSpeed = .65f;
    [SerializeField, Tooltip("同位置的气体可填写不同数值，错开内部雾形。")]
    float phaseOffset;

    static readonly int Enabled = Shader.PropertyToID("_GasEnabled");
    static readonly int Rect = Shader.PropertyToID("_GasRect");
    static readonly int Scale = Shader.PropertyToID("_GasScale");
    static readonly int Appearance = Shader.PropertyToID("_GasAppearance");
    static readonly int Clock = Shader.PropertyToID("_GasTime");
    static readonly int Phase = Shader.PropertyToID("_GasPhase");
    static readonly int Padding = Shader.PropertyToID("_GasPadding");
    MaterialPropertyBlock properties;
    bool showing, originalForceRenderingOff;
    float visualTime, spatialPhase;
    string lastError;

    public bool ValidateConfiguration(out string error)
    {
        // 安全：只使用制作时明确关联的同物体精灵和触发框，不搜索或替换引用。
        error = null;
        if (bodySprite == null || range == null || fogSprite == null || bodySprite.gameObject != gameObject
            || range.gameObject != gameObject || fogSprite.transform.parent != transform)
            error = "原精灵／碰撞框须在根物体，雾面须明确关联到直接子物体。";
        else if (bodySprite.sprite == null || bodySprite.sharedMaterial == null || fogSprite.sprite == null
            || fogSprite.sharedMaterial == null || fogSprite.sharedMaterial.shader.name != "GameEmergence/GasVolume"
            || fogSprite.drawMode != SpriteDrawMode.Sliced)
            error = "请保留根方块精灵，以及子雾面的 Sliced 精灵和 GasVolume 材质。";
        else if (fogSprite.transform.localScale != Vector3.one || fogSprite.transform.localRotation != Quaternion.identity)
            error = "雾面子节点须保持缩放1、旋转0；请调整根物体。";
        else if (!Finite(density) || density < 0f || density > 2f
            || !Finite(outerPadding) || outerPadding < .05f || outerPadding > 1f
            || !Finite(edgeSoftness) || edgeSoftness < .02f || edgeSoftness > .5f
            || !Finite(edgeRoughness) || edgeRoughness < 0f || edgeRoughness > .4f
            || !Finite(noiseScale) || noiseScale < .05f || !Finite(flowSpeed) || flowSpeed < 0f || flowSpeed > 3f
            || !Finite(phaseOffset))
            error = "雾面参数包含非法数值，请按 Inspector 范围设置。";
        else
        {
            Bounds bounds = bodySprite.localBounds;
            Vector3 scale = transform.lossyScale;
            if (!Positive(bounds.extents.x) || !Positive(bounds.extents.y)
                || !Positive(Mathf.Abs(scale.x)) || !Positive(Mathf.Abs(scale.y))
                || !Finite(bounds.center.x) || !Finite(bounds.center.y))
                error = "精灵范围与世界缩放必须有限，宽高不能为零。";
            else if (Vector2.Distance(bounds.size, range.size) > .001f || Vector2.Distance(bounds.center, range.offset) > .001f)
                error = "根精灵的尺寸与中心须和 BoxCollider2D 的尺寸／偏移一致，保证判定框被可见雾包住。";
        }
        return error == null;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Positive(float value) => Finite(value) && value > 0f;

    void OnEnable()
    {
        spatialPhase = transform.position.x * .731f + transform.position.y * 1.371f;
        RefreshVisual(0f);
    }

    // 迷雾是环境装饰，探索时仍缓慢流动；ESC 的原时间暂停自然冻结本时钟。
    void LateUpdate() => RefreshVisual(Application.isPlaying ? Time.deltaTime : 0f);
    void OnDisable() => SetShowing(false);

    void RefreshVisual(float delta)
    {
        if (!ValidateConfiguration(out string error))
        {
            SetShowing(false);
            // 编辑器由 Inspector 显示错误；运行时同一配置错误只报一次。
            if (Application.isPlaying && error != lastError) Debug.LogError(name + " 气体表现：" + error, this);
            lastError = error;
            return;
        }
        lastError = null;
        // 实心形态直接显示原图，不保留雾洞；不回写原精灵颜色或共享材质。
        bool fog = range.enabled && range.isTrigger && bodySprite.enabled;
        SetShowing(fog);
        if (!fog) return;
        visualTime += delta * flowSpeed;
        Bounds bounds = bodySprite.localBounds;
        Vector3 scale = transform.lossyScale;
        Vector2 worldScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        float margin = Mathf.Min(bounds.extents.x * worldScale.x, bounds.extents.y * worldScale.y) * outerPadding;
        // 只扩大独立雾面；根精灵、碰撞框及其制作尺寸保持原值，不积累缩放。
        fogSprite.transform.localPosition = bounds.center;
        fogSprite.size = new Vector2(bounds.size.x + margin * 2f / worldScale.x, bounds.size.y + margin * 2f / worldScale.y);
        fogSprite.color = bodySprite.color;
        fogSprite.sortingLayerID = bodySprite.sortingLayerID;
        fogSprite.sortingOrder = bodySprite.sortingOrder;
        properties ??= new MaterialPropertyBlock();
        fogSprite.GetPropertyBlock(properties);
        properties.SetFloat(Enabled, 1f);
        properties.SetVector(Rect, new Vector4(0f, 0f, bounds.extents.x, bounds.extents.y));
        properties.SetVector(Scale, new Vector4(worldScale.x, worldScale.y, 0f, 0f));
        properties.SetFloat(Padding, margin);
        properties.SetVector(Appearance, new Vector4(density, edgeSoftness, edgeRoughness, noiseScale));
        properties.SetFloat(Clock, visualTime);
        properties.SetFloat(Phase, spatialPhase + phaseOffset);
        fogSprite.SetPropertyBlock(properties);
    }

    void SetShowing(bool value)
    {
        // 显示切换只接管原精灵的渲染开关，外部启停、颜色与属性块继续由原组件持有。
        if (value && !showing && bodySprite != null)
        {
            originalForceRenderingOff = bodySprite.forceRenderingOff;
            bodySprite.forceRenderingOff = true;
        }
        else if (!value && showing && bodySprite != null)
            bodySprite.forceRenderingOff = originalForceRenderingOff;
        showing = value;
        if (fogSprite != null) fogSprite.enabled = value;
    }
}
