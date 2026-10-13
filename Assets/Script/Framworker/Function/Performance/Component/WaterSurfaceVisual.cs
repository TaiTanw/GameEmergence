using UnityEngine;

/// <summary>只读矩形水域的顶点波浪与有限接触波纹；不修改水位、碰撞器或角色运动。</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class WaterSurfaceVisual : MonoBehaviour
{
    const int RippleCapacity = 8;
    const int SegmentLimit = 2048;
    [SerializeField] WaterRegion source;
    [SerializeField] BoxCollider2D range;
    [SerializeField] SpriteRenderer bodySprite;
    [SerializeField] MeshFilter surfaceMesh;
    [SerializeField] MeshRenderer surfaceRenderer;
    [Header("水面波形（世界单位）")]
    [SerializeField, Min(0f)] float waveAmplitude = .045f;
    [SerializeField, Min(.01f)] float waveLength = 1.6f;
    [SerializeField, Min(0f)] float waveSpeed = .65f;
    [SerializeField, Min(.001f), Tooltip("网格最大横向间隔；同时保证最短波长至少 12 段，最多 2048 段。")]
    float segmentSpacing = .08f;
    [Header("局部扰动（最多同时 8 个）")]
    [SerializeField, Min(0f)] float entryAmplitude = .1f;
    [SerializeField, Min(0f)] float strokeAmplitude = .035f;
    [SerializeField, Min(.01f)] float rippleLength = .65f;
    [SerializeField, Min(.01f)] float rippleWidth = .32f;
    [SerializeField, Min(.01f)] float rippleSpeed = 2.2f;
    [SerializeField, Min(.01f)] float rippleDecay = 1.5f;
    [SerializeField, Min(.01f)] float rippleLifetime = 3f;
    [SerializeField, Min(.05f)] float strokeInterval = .22f;
    [SerializeField, Min(.01f), Tooltip("波形叠加的显示位移上限；浅水另限制在高度的 40%，不影响碰撞。")]
    float displacementLimit = .18f;
    [Header("外观（主体颜色读取根 SpriteRenderer）")]
    [SerializeField] Color surfaceColor = new Color(.65f, 1f, 1f, .75f);
    [SerializeField, Min(.001f)] float surfaceLineWidth = .025f;
    [SerializeField, Range(0f, 1f)] float depthDarkening = .25f;

    static readonly int BodyColor = Shader.PropertyToID("_BodyColor");
    static readonly int SurfaceColor = Shader.PropertyToID("_SurfaceColor");
    static readonly int Wave = Shader.PropertyToID("_Wave");
    static readonly int RippleShape = Shader.PropertyToID("_RippleShape");
    static readonly int Ripples = Shader.PropertyToID("_Ripples");
    static readonly int Clock = Shader.PropertyToID("_WaterTime");
    static readonly int Size = Shader.PropertyToID("_WaterSize");
    static readonly int Appearance = Shader.PropertyToID("_Appearance");
    readonly Vector4[] ripples = new Vector4[RippleCapacity];
    int nextRipple;
    float visualTime;
    Mesh ownedMesh;
    Vector3[] meshVertices;
    Vector2[] meshUV;
    int[] meshIndices;
    MaterialPropertyBlock properties;
    Vector2 builtSize, builtOffset;
    Vector3 builtScale;
    int builtSegments;
    bool showing, spriteWasHidden;
    uint levelVersion;
    uint restoreVersion;
    int environmentVersion;
    string lastError;

    public bool ValidateConfiguration(out string error)
    {
        error = null;
        // 安全：关联只接受同宿主与直接子节点，不搜索替代引用。
        if (source == null || range == null || bodySprite == null || surfaceMesh == null || surfaceRenderer == null)
            error = "缺少水宿主、碰撞器、主体精灵或表现网格引用。";
        else if (source.gameObject != gameObject || range.gameObject != gameObject || bodySprite.gameObject != gameObject
            || surfaceMesh.transform.parent != transform || surfaceRenderer.gameObject != surfaceMesh.gameObject)
            error = "水宿主／碰撞器／精灵必须在根节点，网格与渲染器必须在同一直接子节点。";
        else if (surfaceMesh.transform.localPosition != Vector3.zero || surfaceMesh.transform.localRotation != Quaternion.identity
            || surfaceMesh.transform.localScale != Vector3.one)
            error = "表现子节点的位置、旋转、缩放必须保持 0、0、1；请调整根碰撞器尺寸。";
        else if (surfaceRenderer.sharedMaterial == null || surfaceRenderer.sharedMaterial.shader.name != "GameEmergence/WaterSurface")
            error = "表现材质必须使用 GameEmergence/WaterSurface。";
        else if (!Finite(range.size.x) || !Finite(range.size.y) || range.size.x <= 0f || range.size.y <= 0f
            || !Finite(range.offset.x) || !Finite(range.offset.y))
            error = "碰撞器尺寸必须为有限正数，偏移必须为有限数。";
        else if (!Positive(waveLength) || !Positive(segmentSpacing) || !Positive(rippleLength) || !Positive(rippleWidth)
            || !Positive(rippleSpeed) || !Positive(rippleDecay) || !Positive(rippleLifetime) || !Positive(displacementLimit)
            || !Finite(strokeInterval) || strokeInterval < .05f
            || !Positive(surfaceLineWidth) || !NonNegative(waveAmplitude) || !NonNegative(waveSpeed)
            || !NonNegative(entryAmplitude) || !NonNegative(strokeAmplitude) || !Finite(depthDarkening)
            || depthDarkening < 0f || depthDarkening > 1f || !Finite(surfaceColor.r) || !Finite(surfaceColor.g)
            || !Finite(surfaceColor.b) || !Finite(surfaceColor.a) || surfaceColor.a < 0f || surfaceColor.a > 1f)
            error = "波形、扰动与外观参数包含非法数值。";
        if (error != null) return false;
        // 首版只支持无旋转、正缩放的水平矩形，包括父层级。
        for (Transform item = transform; item != null; item = item.parent)
            if (item.localRotation != Quaternion.identity || !Positive(item.localScale.x)
                || !Positive(item.localScale.y) || !Mathf.Approximately(item.localScale.z, 1f)
                || !Finite(item.localPosition.x) || !Finite(item.localPosition.y) || !Finite(item.localPosition.z))
            { error = "水体及父层级须保持无旋转、正 X/Y 缩放、Z 缩放为 1。"; return false; }
        float width = range.size.x * transform.lossyScale.x;
        float height = range.size.y * transform.lossyScale.y;
        if (!Positive(width) || !Positive(height) || width / EffectiveSpacing > SegmentLimit)
        { error = "水体宽度或最短波长超出 2048 段预算；请缩小宽度或增大波长／网格间隔。"; return false; }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Positive(float value) => Finite(value) && value > 0f;
    static bool NonNegative(float value) => Finite(value) && value >= 0f;
    float EffectiveSpacing => Mathf.Min(segmentSpacing, Mathf.Min(waveLength, rippleLength) / 12f);
    int RequiredSegments(float width) => Mathf.Max(2, Mathf.CeilToInt(width / EffectiveSpacing));
    bool IsWaterActive => source != null && source.IsEnvironmentActive && range.enabled && range.isTrigger
        && source.TryGetWaterSwimming(out _);

    void OnEnable() { ClearRipples(); RefreshVisual(0f, false); }
    void LateUpdate() => RefreshVisual(Application.isPlaying ? Time.deltaTime : 0f, BasicEntity.IsWorldPaused);
    void OnDisable() { ClearRipples(); SetShowing(false); }
    void OnDestroy()
    {
        SetShowing(false);
        if (ownedMesh != null)
        {
            if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh);
        }
    }

    public void ClearRipples()
    {
        for (int i = 0; i < RippleCapacity; i++) ripples[i] = new Vector4(0f, -100000f, 0f, 0f);
        nextRipple = 0;
    }

    void RefreshVisual(float delta, bool paused)
    {
        if (!ValidateConfiguration(out string error))
        {
            SetShowing(false);
            // 编辑器 Inspector 显示配置问题；运行时同一错误只记录一次。
            if (Application.isPlaying && error != lastError) Debug.LogError($"{name} 水体表现：{error}", this);
            lastError = error;
            return;
        }
        lastError = null;
        bool active = IsWaterActive && bodySprite.enabled;
        // 来源恢复／性质切换清旧扰动；上涨本身不清除正在传播的波纹。
        if (levelVersion != source.WaterLevelVersion || restoreVersion != source.EnvironmentRestoreVersion
            || environmentVersion != source.EnvironmentParameterVersion || active != showing)
            ClearRipples();
        levelVersion = source.WaterLevelVersion;
        restoreVersion = source.EnvironmentRestoreVersion;
        environmentVersion = source.EnvironmentParameterVersion;
        SetShowing(active);
        if (!active) return;
        if (!paused) visualTime += delta;
        Vector3 scale = transform.lossyScale;
        int segments = RequiredSegments(range.size.x * scale.x);
        if (ownedMesh == null || builtSize != range.size || builtOffset != range.offset || builtScale != scale || builtSegments != segments)
            BuildMesh(scale, segments);
        // 参数变化也要更新裁剪边界，不依赖几何重建。
        ownedMesh.bounds = new Bounds(new Vector3(range.offset.x, range.offset.y, 0f),
            new Vector3(range.size.x, range.size.y + displacementLimit * 2f / scale.y, .1f));
        surfaceRenderer.sortingLayerID = bodySprite.sortingLayerID;
        surfaceRenderer.sortingOrder = bodySprite.sortingOrder;
        properties ??= new MaterialPropertyBlock();
        properties.SetColor(BodyColor, bodySprite.color);
        properties.SetColor(SurfaceColor, surfaceColor);
        properties.SetVector(Wave, new Vector4(waveAmplitude, waveLength, waveSpeed, Mathf.Min(displacementLimit, range.size.y * scale.y * .4f)));
        properties.SetVector(RippleShape, new Vector4(rippleLength, rippleWidth, rippleSpeed, rippleDecay));
        properties.SetVectorArray(Ripples, ripples);
        properties.SetFloat(Clock, visualTime);
        properties.SetVector(Size, new Vector4(range.size.x * scale.x, range.size.y * scale.y, scale.y, rippleLifetime));
        properties.SetVector(Appearance, new Vector4(surfaceLineWidth, depthDarkening, 0f, 0f));
        surfaceRenderer.SetPropertyBlock(properties);
    }

    void SetShowing(bool active)
    {
        // 显示接管只覆盖渲染开关，主体色仍由原形态／主水配置持有。
        if (active && !showing && bodySprite != null)
        { spriteWasHidden = bodySprite.forceRenderingOff; bodySprite.forceRenderingOff = true; }
        if (!active && showing && bodySprite != null) bodySprite.forceRenderingOff = spriteWasHidden;
        showing = active;
        if (surfaceRenderer != null) surfaceRenderer.enabled = active && !spriteWasHidden;
    }

    void BuildMesh(Vector3 scale, int segments)
    {
        if (ownedMesh == null)
        {
            ownedMesh = new Mesh { name = "WaterSurface runtime mesh", hideFlags = HideFlags.DontSave };
            ownedMesh.MarkDynamic();
        }
        int columns = segments + 1;
        bool topologyChanged = meshVertices == null || meshVertices.Length != columns * 3;
        // 上涨只更新高度；段数不变时复用数组及索引，避免持续上涨逐帧分配网格缓冲。
        if (topologyChanged)
        {
            meshVertices = new Vector3[columns * 3];
            meshUV = new Vector2[meshVertices.Length];
            meshIndices = new int[segments * 12];
        }
        var vertices = meshVertices;
        var uv = meshUV;
        var indices = meshIndices;
        float bottom = range.offset.y - range.size.y * .5f, top = bottom + range.size.y;
        float band = Mathf.Min(.4f / scale.y, range.size.y * .5f);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < columns; col++)
            {
                float x = (float)col / segments;
                float y = row == 0 ? bottom : row == 1 ? top - band : top;
                int index = row * columns + col;
                vertices[index] = new Vector3(range.offset.x + (x - .5f) * range.size.x, y, 0f);
                uv[index] = new Vector2(row == 2 ? 1f : 0f, (y - bottom) / range.size.y);
            }
        if (topologyChanged)
        {
            int cursor = 0;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < segments; col++)
                {
                    int a = row * columns + col, c = a + columns;
                    indices[cursor++] = a; indices[cursor++] = c; indices[cursor++] = a + 1;
                    indices[cursor++] = a + 1; indices[cursor++] = c; indices[cursor++] = c + 1;
                }
            ownedMesh.Clear();
        }
        ownedMesh.vertices = vertices; ownedMesh.uv = uv;
        if (topologyChanged) ownedMesh.triangles = indices;
        ownedMesh.bounds = new Bounds(new Vector3(range.offset.x, range.offset.y, 0f),
            new Vector3(range.size.x, range.size.y + displacementLimit * 2f / scale.y, .1f));
        surfaceMesh.sharedMesh = ownedMesh;
        builtSize = range.size; builtOffset = range.offset; builtScale = scale; builtSegments = segments;
    }

    internal bool SampleContact(Bounds previous, Bounds current, Vector2 velocity, bool stroke, float strokeElapsed)
    {
        // 只接受本水域的有效显示，不因其它池的游泳汇总事实发波纹。
        if (!isActiveAndEnabled || !showing || !IsWaterActive) return false;
        Bounds water = range.bounds;
        float y = water.max.y;
        bool entered = previous.min.y > y && current.min.y <= y && velocity.y < -.1f;
        if (entered)
        {
            float fraction = (previous.min.y - y) / (previous.min.y - current.min.y);
            float x = Mathf.Lerp(previous.center.x, current.center.x, fraction);
            if (x >= water.min.x && x <= water.max.x)
            { EmitRipple(x, entryAmplitude * Mathf.Clamp(-velocity.y / 5f, .35f, 1.5f)); return true; }
        }
        // 水面附近主动游动才留下扰动；纯水流、静止与深水动作不触发。
        if (stroke && strokeElapsed >= strokeInterval && current.max.y >= y - .15f && current.min.y < y
            && current.center.x >= water.min.x && current.center.x <= water.max.x)
        { EmitRipple(current.center.x, strokeAmplitude); return true; }
        return false;
    }

    void EmitRipple(float x, float amplitude)
    {
        ripples[nextRipple] = new Vector4(x, visualTime, amplitude, 0f);
        nextRipple = (nextRipple + 1) % RippleCapacity;
    }
}
