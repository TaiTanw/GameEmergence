using UnityEngine;

/// <summary>
/// 职能：表现层的单目标交互提示绘制；只按调用方给的世界矩形、文字与颜色显示，不读也不写玩法状态。
/// 具体：首次显示时在场景根创建一个边框（LineRenderer 闭环）和一行文字（TextMesh，位于矩形上方），之后复用；
/// 目标的状态色（如收纳变暗）由目标自己处理，这里只叠加边框，不改目标渲染器。其它可交互物件可复用同一绘制。
/// </summary>
public sealed class InteractionHintView
{
    private const float LineWidth = .05f;
    private const float LabelLift = .15f;
    private const int SortingOrder = 100;
    private GameObject root;
    private LineRenderer outline;
    private TextMesh label;

    public void Show(Bounds bounds, string text, Color color)
    {
        if (root == null) Build();
        root.SetActive(true);
        Vector3 min = bounds.min, max = bounds.max;
        outline.SetPosition(0, new Vector3(min.x, min.y, 0f));
        outline.SetPosition(1, new Vector3(min.x, max.y, 0f));
        outline.SetPosition(2, new Vector3(max.x, max.y, 0f));
        outline.SetPosition(3, new Vector3(max.x, min.y, 0f));
        outline.startColor = outline.endColor = color;
        label.text = text;
        label.color = color;
        label.transform.position = new Vector3(bounds.center.x, max.y + LabelLift, 0f);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
        root = null;
    }

    private void Build()
    {
        root = new GameObject("interaction hint");
        outline = root.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.positionCount = 4;
        outline.startWidth = outline.endWidth = LineWidth;
        outline.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        outline.sortingOrder = SortingOrder;

        var text = new GameObject("label");
        text.transform.SetParent(root.transform, false);
        label = text.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.font = font;
        label.fontSize = 48;
        label.characterSize = .06f;
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = font.material;
        renderer.sortingOrder = SortingOrder;
    }
}
