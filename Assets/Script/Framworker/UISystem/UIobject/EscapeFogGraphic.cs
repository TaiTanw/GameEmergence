using UnityEngine;
using UnityEngine.UI;

/// <summary>能力结果页的局部雾面；只传递显示进度，共用材质，不持有结算数据。</summary>
[AddComponentMenu("UI/Escape Fog")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EscapeFogGraphic : MaskableGraphic
{
    float reveal, flowTime;

    public void SetPresentation(float progress, float time)
    {
        reveal = Mathf.Clamp01(progress);
        flowTime = time;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        // 尚未开始显现时不提交雾面。
        if (reveal <= 0f) return;
        Rect rect = GetPixelAdjustedRect();
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        for (int i = 0; i < 4; i++)
        {
            float x = i == 1 || i == 2 ? 1f : 0f;
            float y = i >= 2 ? 1f : 0f;
            vertex.position = new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));
            vertex.uv0 = new Vector4(x, y, reveal, flowTime);
            helper.AddVert(vertex);
        }
        helper.AddTriangle(0, 1, 2);
        helper.AddTriangle(2, 3, 0);
    }
}
