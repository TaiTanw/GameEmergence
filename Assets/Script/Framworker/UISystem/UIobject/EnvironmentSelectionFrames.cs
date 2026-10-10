using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>可编辑来源的屏幕矩形提示；只绘制碰撞范围，不参与点选、物理或参数修改。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EnvironmentSelectionFrames : MaskableGraphic
{
    IReadOnlyList<BasicPhysicalObject> sources;
    BasicPhysicalObject selected;
    Camera worldCamera;
    Color availableColor, selectedColor;
    float availableWidth, selectedWidth;

    internal void Display(IReadOnlyList<BasicPhysicalObject> sources, BasicPhysicalObject selected, Camera worldCamera,
        Color availableColor, Color selectedColor, float availableWidth, float selectedWidth)
    {
        this.sources = sources; this.selected = selected; this.worldCamera = worldCamera;
        this.availableColor = availableColor; this.selectedColor = selectedColor;
        this.availableWidth = availableWidth; this.selectedWidth = selectedWidth;
        SetVerticesDirty();
    }

    internal static bool TryGetBounds(BasicPhysicalObject source, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        // 范围：与点选一样只收宿主同物体的活动碰撞框，多框合为一份外包矩形。
        foreach (Collider2D collider in source.GetComponents<Collider2D>())
        {
            if (!collider.isActiveAndEnabled) continue;
            Bounds current = collider.bounds;
            // 暂停：simulated=false 的矩形不在物理查询中，显示直接读其碰撞框配置与变换。
            if (collider is BoxCollider2D box && box.attachedRigidbody != null && !box.attachedRigidbody.simulated)
            {
                current = new Bounds(box.transform.TransformPoint(box.offset), Vector3.zero);
                for (int i = 0; i < 4; i++)
                    current.Encapsulate(box.transform.TransformPoint(box.offset + new Vector2(
                        (i & 1) == 0 ? -box.size.x * .5f : box.size.x * .5f,
                        (i & 2) == 0 ? -box.size.y * .5f : box.size.y * .5f)));
            }
            if (current.size.x <= 0f || current.size.y <= 0f) continue;
            if (found) bounds.Encapsulate(current);
            else { bounds = current; found = true; }
        }
        return found;
    }

    internal static bool ContainsPausedBox(BasicPhysicalObject source, Vector2 point)
    {
        foreach (BoxCollider2D box in source.GetComponents<BoxCollider2D>())
        {
            if (!box.isActiveAndEnabled || box.attachedRigidbody == null || box.attachedRigidbody.simulated) continue;
            Vector2 local = (Vector2)box.transform.InverseTransformPoint(new Vector3(point.x, point.y, box.transform.position.z)) - box.offset;
            // 点选：按真实旋转矩形核对，不以外包矩形的空角代替命中，不改变模拟许可。
            if (Mathf.Abs(local.x) <= box.size.x * .5f && Mathf.Abs(local.y) <= box.size.y * .5f) return true;
        }
        return false;
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        // 安全：镜头或 Canvas 交接期间不拿旧投影绘制。
        if (sources == null || worldCamera == null || canvas == null) return;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        foreach (BasicPhysicalObject source in sources)
        {
            // 生命周期：扫描间隔内停用、销毁或移出本关的来源立即不画。
            if (!EnvironmentEditView.EditableSource(source) || !TryGetBounds(source, out Bounds bounds)) continue;
            if (!TryProject(bounds, uiCamera, out Vector2 min, out Vector2 max)) continue;
            bool highlighted = source == selected;
            Color tint = highlighted ? selectedColor : availableColor;
            float width = Mathf.Max(1f, highlighted ? selectedWidth : availableWidth) / Mathf.Max(.001f, canvas.scaleFactor);
            width = Mathf.Min(width, Mathf.Min(max.x - min.x, max.y - min.y) * .5f);
            // 表现：四条内边保留透明中心，不填充、不遮住物体。
            Quad(vertices, min, new Vector2(max.x, min.y + width), tint);
            Quad(vertices, new Vector2(min.x, max.y - width), max, tint);
            Quad(vertices, new Vector2(min.x, min.y + width), new Vector2(min.x + width, max.y - width), tint);
            Quad(vertices, new Vector2(max.x - width, min.y + width), new Vector2(max.x, max.y - width), tint);
        }
    }

    bool TryProject(Bounds bounds, Camera uiCamera, out Vector2 min, out Vector2 max)
    {
        Vector2 screenMin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 screenMax = new Vector2(float.MinValue, float.MinValue);
        min = max = default;
        for (int i = 0; i < 4; i++)
        {
            Vector3 point = worldCamera.WorldToScreenPoint(new Vector3(
                (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y, bounds.center.z));
            // 可见性：镜头背面与完全离屏的物体保留配置，不产生屏幕框线。
            if (point.z <= 0f) return false;
            screenMin = Vector2.Min(screenMin, point); screenMax = Vector2.Max(screenMax, point);
        }
        if (!worldCamera.pixelRect.Overlaps(Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y))) return false;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenMin, uiCamera, out min)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenMax, uiCamera, out max);
    }

    static void Quad(VertexHelper vertices, Vector2 min, Vector2 max, Color tint)
    {
        int first = vertices.currentVertCount;
        vertices.AddVert(new Vector3(min.x, min.y), tint, Vector2.zero);
        vertices.AddVert(new Vector3(min.x, max.y), tint, Vector2.zero);
        vertices.AddVert(new Vector3(max.x, max.y), tint, Vector2.zero);
        vertices.AddVert(new Vector3(max.x, min.y), tint, Vector2.zero);
        vertices.AddTriangle(first, first + 1, first + 2);
        vertices.AddTriangle(first, first + 2, first + 3);
    }
}
