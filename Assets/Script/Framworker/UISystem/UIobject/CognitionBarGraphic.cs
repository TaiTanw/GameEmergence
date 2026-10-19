using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>认知条的局部几何表现：固定星形轮廓、可耗尽白底和独立回车刻度；不持有计时或认知余额。</summary>
[AddComponentMenu("UI/Cognition Bar Graphic")]
public sealed class CognitionBarGraphic : MaskableGraphic
{
    [SerializeField] Color outlineColor = new Color(.91f, .73f, .31f);
    [SerializeField] Color cognitionColor = Color.white;
    [SerializeField] Color confirmationColor = Color.black;
    [SerializeField, Min(.5f)] float outlineThickness = 2f;
    [SerializeField, Min(8f)] float tickSpacing = 36f;
    [SerializeField, Range(0f, 1f)] float cognitionFraction = 1f;
    [SerializeField, Range(0f, 1f)] float confirmationRemaining = 1f;
    [SerializeField] bool showConfirmation = true;
    [SerializeField, Tooltip("仅剩余白色认知区域产生局部柔光；颜色、强度和半径在专用材质调整。")]
    bool glowEnabled = true;
    // 与局部柔光 Shader 的支持范围一致；最大外扩6像素，保留背包按钮间距。
    const float GlowPadding = 6f;
    readonly List<float> knots = new List<float>(64);
    Vector2 center;
    float halfWidth, starCenter, starJoin, starHeight, ribbonHeight, diamondWidth, diamondHeight;

    public float CognitionFraction => cognitionFraction;
    public float ConfirmationFraction => showConfirmation ? cognitionFraction * confirmationRemaining : 0f;

    public void SetDisplay(float cognition, float confirmation, bool visible)
    {
        cognition = Mathf.Clamp01(cognition); confirmation = Mathf.Clamp01(confirmation);
        // 数据不变时沿用原网格，静止漫游不会每帧重建。
        if (cognitionFraction == cognition && confirmationRemaining == confirmation && showConfirmation == visible) return;
        cognitionFraction = cognition; confirmationRemaining = confirmation; showConfirmation = visible;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        // 安全：布局尚无有效面积时不生成退化网格。
        if (rect.width <= 0f || rect.height <= 0f) return;
        center = rect.center; halfWidth = rect.width * .5f; starHeight = rect.height * .5f;
        ribbonHeight = starHeight * .34f;
        float outward = Mathf.Min(rect.height * 1.25f, halfWidth * .25f);
        float inward = outward * .45f;
        starCenter = halfWidth - outward; starJoin = starCenter - inward;
        diamondWidth = Mathf.Min(rect.height * .45f, starJoin * .5f);
        diamondHeight = starHeight * .5f;
        float fillEdge = halfWidth * cognitionFraction;
        float holeWidth = Mathf.Max(0f, diamondWidth - outlineThickness);
        float holeHeight = Mathf.Max(0f, diamondHeight - outlineThickness);
        // 安全：普通 UI 材质或不支持的 Shader 不接收柔光专用顶点。
        bool canGlow = material != null && material.shader.isSupported && material.HasProperty("_GlowRadius");
        // 柔光先绘制在白底下方；只改变有效长度，颜色和亮度不乘剩余比例。
        if (glowEnabled && canGlow && cognitionFraction > 0f)
        {
            float x = fillEdge + GlowPadding, y = starHeight + GlowPadding;
            Quad(mesh, new Vector2(-x, -y), new Vector2(-x, y), new Vector2(x, y), new Vector2(x, -y), Color.white, true);
        }
        knots.Clear();
        // 星角曲线、白底裁切边和菱形孔的折点共用同一组横坐标，避免窄缝与描边残影。
        for (int i = 0; i <= 10; i++)
        {
            float x = Mathf.Lerp(starJoin, starCenter, i / 10f);
            knots.Add(x); knots.Add(-x);
            x = Mathf.Lerp(starCenter, halfWidth, i / 10f);
            knots.Add(x); knots.Add(-x);
        }
        knots.Add(-fillEdge); knots.Add(fillEdge); knots.Add(0f);
        knots.Add(-holeWidth); knots.Add(holeWidth);
        float outlineCrossing = diamondWidth * (1f - ribbonHeight / diamondHeight);
        knots.Add(-outlineCrossing); knots.Add(outlineCrossing);
        // 菱形孔高于白底时补交点，白底在孔内真正透明。
        if (holeHeight > ribbonHeight)
        {
            float crossing = holeWidth * (1f - ribbonHeight / holeHeight);
            knots.Add(-crossing); knots.Add(crossing);
        }
        knots.Sort();
        for (int i = 1; i < knots.Count; i++)
        {
            float a = knots[i - 1], b = knots[i];
            if (b - a < .001f) continue;
            float topA = Profile(a), topB = Profile(b);
            // 只裁切白色填充；整条外轮廓始终按完整长度绘制。
            if (a >= -fillEdge && b <= fillEdge)
            {
                float holeA = Hole(a, holeWidth, holeHeight, topA), holeB = Hole(b, holeWidth, holeHeight, topB);
                Quad(mesh, new Vector2(a, holeA), new Vector2(a, topA), new Vector2(b, topB), new Vector2(b, holeB), cognitionColor);
                Quad(mesh, new Vector2(a, -topA), new Vector2(a, -holeA), new Vector2(b, -holeB), new Vector2(b, -topB), cognitionColor);
            }
        }
        for (int i = 1; i < knots.Count; i++)
        {
            float a = knots[i - 1], b = knots[i];
            if (b - a < .001f) continue;
            // 菱形处让外框接到菱形边缘，不用横向描边穿过空心孔。
            if (Mathf.Abs((a + b) * .5f) < outlineCrossing) continue;
            Stroke(mesh, new Vector2(a, Profile(a)), new Vector2(b, Profile(b)), outlineThickness, outlineColor);
            Stroke(mesh, new Vector2(a, -Profile(a)), new Vector2(b, -Profile(b)), outlineThickness, outlineColor);
        }
        // 黑线和刻度只画仍存活的部分；消失处没有另一层轨道或描边。
        float lineEdge = Mathf.Min(halfWidth * ConfirmationFraction, starCenter);
        DrawInk(mesh, -lineEdge, -diamondWidth, -1f, 1f);
        DrawInk(mesh, diamondWidth, lineEdge, -1f, 1f);
        int count = Mathf.FloorToInt(lineEdge / tickSpacing);
        for (int i = -count; i <= count; i++)
        {
            float x = i * tickSpacing;
            if (Mathf.Abs(x) < diamondWidth + 2f) continue;
            float h = ribbonHeight * (i % 5 == 0 ? .6f : .3f);
            DrawInk(mesh, Mathf.Max(-lineEdge, x - 1f), Mathf.Min(lineEdge, x + 1f), -h, h);
        }
        // 中央菱形为固定空心装饰，内部不铺背景色，耗尽后仍能透出场景。
        Vector2 left = new Vector2(-diamondWidth, 0f), top = new Vector2(0f, diamondHeight);
        Vector2 right = new Vector2(diamondWidth, 0f), bottom = new Vector2(0f, -diamondHeight);
        Stroke(mesh, left, top, outlineThickness * 2f, outlineColor);
        Stroke(mesh, top, right, outlineThickness * 2f, outlineColor);
        Stroke(mesh, right, bottom, outlineThickness * 2f, outlineColor);
        Stroke(mesh, bottom, left, outlineThickness * 2f, outlineColor);
    }

    float Profile(float x)
    {
        x = Mathf.Abs(x);
        if (x <= starJoin) return ribbonHeight;
        // 内尖较短，外尖较长，向两侧保留四棱星的凹曲线轮廓。
        if (x <= starCenter)
        {
            float t = (x - starJoin) / (starCenter - starJoin);
            return Mathf.Lerp(ribbonHeight, starHeight, t * t * t);
        }
        float outward = 1f - (x - starCenter) / (halfWidth - starCenter);
        return starHeight * outward * outward * outward;
    }

    static float Hole(float x, float width, float height, float outer)
    {
        // 安全：没有可用孔宽时不除以零。
        if (width <= 0f) return 0f;
        return Mathf.Min(outer, height * Mathf.Clamp01(1f - Mathf.Abs(x) / width));
    }

    void DrawInk(VertexHelper mesh, float left, float right, float bottom, float top)
    {
        if (right <= left) return;
        Quad(mesh, new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), confirmationColor);
    }

    void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 delta = b - a;
        // 安全：重复轮廓点不生成零长度描边。
        if (delta.sqrMagnitude < .000001f) return;
        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
        Quad(mesh, a - normal, a + normal, b + normal, b - normal, tint);
    }

    void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint, bool glow = false)
    {
        int start = mesh.currentVertCount;
        Color32 vertexColor = tint * color;
        // UV0自带完整局部坐标及固定形状参数；不同进度共用一份材质，不复制材质或修改全局参数。
        mesh.AddVert((Vector3)(a + center), vertexColor, glow ? new Vector4(a.x, a.y, halfWidth, outlineThickness) : Vector4.zero);
        mesh.AddVert((Vector3)(b + center), vertexColor, glow ? new Vector4(b.x, b.y, halfWidth, outlineThickness) : Vector4.zero);
        mesh.AddVert((Vector3)(c + center), vertexColor, glow ? new Vector4(c.x, c.y, halfWidth, outlineThickness) : Vector4.zero);
        mesh.AddVert((Vector3)(d + center), vertexColor, glow ? new Vector4(d.x, d.y, halfWidth, outlineThickness) : Vector4.zero);
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
    }
}
