using UnityEngine;
using UnityEngine.UI;

/// <summary>环境编辑和侧栏背包共用的布局；旧预设在运行时补齐，编辑器生成也走同一份布局。</summary>
public static class EnvironmentEditLayout
{
    public static readonly Color Ink = new Color(.88f, .94f, .96f);
    public static readonly Color Muted = new Color(.58f, .69f, .75f);
    public static readonly Color Card = new Color(.055f, .09f, .12f, .96f);
    public static readonly Color Normal = new Color(.10f, .17f, .21f);
    public static readonly Color Highlight = new Color(.17f, .38f, .40f);
    public static readonly Color Inactive = new Color(.085f, .10f, .12f);

    public static void Ensure(Transform area, Transform panel)
    {
        // 旧数字输入只隐藏；已保存预设立即使用槽编辑，不要求先运行编辑器生成器。
        foreach (Transform child in area)
            if (child.name != "EnvironmentEditorContent" && child.name != "EnvironmentSelectionHint")
            {
                foreach (Selectable control in child.GetComponentsInChildren<Selectable>(true)) control.interactable = false;
                child.gameObject.SetActive(false);
            }
        Image oldBackground = area.GetComponent<Image>();
        if (oldBackground != null) { oldBackground.color = Color.clear; oldBackground.raycastTarget = false; }
        RectTransform areaRect = (RectTransform)area;
        areaRect.anchorMin = areaRect.anchorMax = areaRect.pivot = new Vector2(.5f, 0f);
        areaRect.anchoredPosition = new Vector2(0f, 80f); areaRect.sizeDelta = new Vector2(720f, 430f);
        // 同名节点只构造一次，面板停用再启用不会复制背包或事件控件。
        if (area.Find("EnvironmentEditorContent") == null) BuildEditor(area);
        if (panel.Find("CognitionBackpackArea") == null) BuildBackpack(panel);
        Transform bag = panel.Find("CognitionBackpackArea");
        // 已生成侧栏同样补齐容量说明，避免较早制作的预设漏掉预览占用口径。
        if (bag.Find("BackpackCapacityText") == null)
            Label(bag, "BackpackCapacityText", "", new Vector2(18, -84), new Vector2(324, 40), 15, Muted);
        RectTransform bagScroll = (RectTransform)bag.Find("CognitionBackpackScroll");
        bagScroll.anchoredPosition = new Vector2(18, -132); bagScroll.sizeDelta = new Vector2(324, 190);
        if (panel.Find("OpenCognitionBackpackButton") == null)
            Button(panel, "OpenCognitionBackpackButton", "认知背包", new Vector2(1f, 1f),
                new Vector2(-32f, -182f), new Vector2(360f, 48f), 24);
    }

    static void BuildEditor(Transform area)
    {
        RectTransform hint = Region(area, "EnvironmentSelectionHint", new Vector2(.5f, 0), Vector2.zero, new Vector2(720, 54));
        Background(hint, Card, false);
        Text prompt = Label(hint, "Label", "点击蓝框环境查看性质", Vector2.zero, new Vector2(720, 54), 21, Muted);
        Stretch(prompt.rectTransform); prompt.alignment = TextAnchor.MiddleCenter;
        RectTransform card = Region(area, "EnvironmentEditorContent", new Vector2(0, 1), Vector2.zero, new Vector2(720, 430));
        Background(card, Card, true);
        Label(card, "EnvironmentSelectionTitleText", "环境性质", new Vector2(20, -14), new Vector2(590, 34), 24, Ink);
        Button(card, "CloseEnvironmentButton", "关闭", new Vector2(0, 1), new Vector2(628, -14), new Vector2(72, 34), 19);
        Button(card, "ViewColliderListButton", "碰撞器列表", new Vector2(0, 1), new Vector2(20, -56), new Vector2(180, 34), 20);
        Button(card, "ViewTriggerListButton", "触发器列表", new Vector2(0, 1), new Vector2(210, -56), new Vector2(180, 34), 20);
        Label(card, "EnvironmentListStateText", "", new Vector2(406, -56), new Vector2(294, 34), 18, Muted);
        Button(card, "EnvironmentGeometrySlot", "触发器性质 · 独立槽", new Vector2(0, 1), new Vector2(20, -100), new Vector2(610, 46), 21);
        Button(card, "ExtractTriggerNatureButton", "抽取", new Vector2(0, 1), new Vector2(638, -100), new Vector2(62, 46), 18);
        Scroll(card, "EnvironmentAbilityScroll", new Vector2(20, -156), new Vector2(680, 148));
        Label(card, "EnvironmentOccupancyText", "", new Vector2(20, -314), new Vector2(680, 23), 17, Muted);
        Label(card, "EnvironmentPreviewCostText", "", new Vector2(20, -342), new Vector2(680, 25), 19, Ink);
        Text message = Label(card, "EnvironmentDraftHintText", "", new Vector2(20, -374), new Vector2(470, 42), 17, Muted);
        message.horizontalOverflow = HorizontalWrapMode.Wrap;
        Button(card, "ConfirmEnvironmentButton", "确认", new Vector2(0, 1), new Vector2(506, -374), new Vector2(92, 42), 22);
        Button(card, "CancelEnvironmentButton", "取消", new Vector2(0, 1), new Vector2(610, -374), new Vector2(90, 42), 22);
        card.gameObject.SetActive(false);
    }

    static void BuildBackpack(Transform panel)
    {
        // 右侧背包下沿高于原有认知地图；两面板同时出现也不挡地图和顶部按钮。
        RectTransform bag = Region(panel, "CognitionBackpackArea", Vector2.one, new Vector2(-32, -182), new Vector2(360, 414));
        Background(bag, Card, true);
        Label(bag, "BackpackTitleText", "认知背包", new Vector2(18, -12), new Vector2(240, 32), 24, Ink);
        Button(bag, "CloseCognitionBackpackButton", "关闭", new Vector2(0, 1), new Vector2(280, -12), new Vector2(62, 32), 18);
        Text mode = Label(bag, "BackpackModeText", "只读查看", new Vector2(18, -50), new Vector2(324, 32), 17, Muted);
        mode.horizontalOverflow = HorizontalWrapMode.Wrap;
        Label(bag, "BackpackCapacityText", "", new Vector2(18, -84), new Vector2(324, 40), 15, Muted);
        Scroll(bag, "CognitionBackpackScroll", new Vector2(18, -132), new Vector2(324, 190));
        Label(bag, "BackpackSelectionText", "", new Vector2(18, -328), new Vector2(324, 28), 17, Ink);
        Button(bag, "SplitBackpackCalculationButton", "抽出计算点", new Vector2(0, 1), new Vector2(18, -364), new Vector2(324, 36), 21);
        bag.gameObject.SetActive(false);
    }

    public static RectTransform Region(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
        var rect = item.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }

    public static Image Background(RectTransform rect, Color color, bool blocksInput)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = blocksInput; return image;
    }

    public static Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        RectTransform rect = Region(parent, name, new Vector2(0, 1), position, size);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value; text.fontSize = fontSize; text.color = color; text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }

    public static Button Button(Transform parent, string name, string caption, Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
    {
        RectTransform rect = Region(parent, name, anchor, position, size);
        Image image = Background(rect, Normal, true);
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(.8f, 1f, 1f); colors.pressedColor = new Color(.55f, .85f, .88f);
        colors.disabledColor = new Color(.45f, .5f, .55f); button.colors = colors;
        Text label = Label(rect, "Label", caption, Vector2.zero, size, fontSize, Ink);
        Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(10, 0);
        label.rectTransform.offsetMax = new Vector2(-10, 0); label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    public static RectTransform Scroll(Transform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = Region(parent, name, new Vector2(0, 1), position, size);
        Background(rect, new Color(.025f, .045f, .065f, .7f), true);
        RectTransform viewport = Region(rect, "Viewport", new Vector2(0, 1), Vector2.zero, size);
        Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Region(viewport, "Content", new Vector2(0, 1), Vector2.zero, new Vector2(size.x, 0));
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        ScrollRect scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28; return content;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
