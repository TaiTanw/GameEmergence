using UnityEngine;
using UnityEngine.UI;

/// <summary>环境与背包的制作布局及动态行创建工具；静态布局仅由编辑器制作入口补建。</summary>
public static class EnvironmentEditLayout
{
    public static readonly Color Ink = new Color(.18f, .14f, .20f);
    public static readonly Color Muted = new Color(.34f, .27f, .29f);
    public static readonly Color Card = Color.white;
    public static readonly Color Normal = new Color(.85f, .78f, .66f);
    public static readonly Color Highlight = new Color(.94f, .85f, .68f);
    public static readonly Color Inactive = new Color(.58f, .55f, .50f);

#if UNITY_EDITOR
    public static void Ensure(Transform area, Transform panel)
    {
        // 制作入口只在编辑状态补建；播放时完整使用保存的预设。
        if (Application.isPlaying) return;
        // 首次迁移旧数字面板才清理旧入口；已有槽面板保留背景和手工装饰。
        if (area.Find("EnvironmentEditorContent") == null)
        {
            foreach (Transform child in area)
                if (child.name != "EnvironmentSelectionHint")
                {
                    foreach (Selectable control in child.GetComponentsInChildren<Selectable>(true)) control.interactable = false;
                    child.gameObject.SetActive(false);
                }
            Image oldBackground = area.GetComponent<Image>();
            // 安全：没有旧背景组件时只创建槽面板。
            if (oldBackground != null) { oldBackground.color = Color.clear; oldBackground.raycastTarget = false; }
            RectTransform areaRect = (RectTransform)area;
            areaRect.anchorMin = areaRect.anchorMax = areaRect.pivot = new Vector2(.5f, 0f);
            areaRect.anchoredPosition = new Vector2(0f, -((RectTransform)area.parent).anchoredPosition.y);
            areaRect.sizeDelta = new Vector2(720f, 430f);
            BuildEditor(area);
        }
        Transform editor = area.Find("EnvironmentEditorContent");
        // 固定特性只读行独立于能力槽，较早生成的面板也在同一入口补齐。
        if (editor.Find("EnvironmentFixedFeatureText") == null)
            Label(editor, "EnvironmentFixedFeatureText", "", new Vector2(20, -150), new Vector2(680, 26), 18, Ink);
        if (panel.Find("CognitionBackpackArea") == null) BuildBackpack(panel);
        Transform bag = panel.Find("CognitionBackpackArea");
        // 已生成侧栏同样补齐容量说明，避免较早制作的预设漏掉预览占用口径。
        if (bag.Find("BackpackCapacityText") == null)
            Label(bag, "BackpackCapacityText", "", new Vector2(18, -114), new Vector2(324, 40), 15, Muted);
        Text mode = bag.Find("BackpackModeText").GetComponent<Text>();
        if (bag.Find("BackpackExplanationBox") == null)
        {
            RectTransform label = mode.rectTransform;
            RectTransform explanation = Region(bag, "BackpackExplanationBox", label.anchorMin, label.anchoredPosition, label.sizeDelta);
            explanation.anchorMax = label.anchorMax; explanation.pivot = label.pivot;
            Background(explanation, Normal, false); explanation.SetSiblingIndex(mode.transform.GetSiblingIndex());
            ApplyPaper(explanation.GetComponent<Image>(), GameUIAssets.Shared.note);
        }
        // 旧预设的背包拆点入口移除；功能只通过环境数值行暴露。
        Transform oldSplit = bag.Find("SplitBackpackCalculationButton");
        if (oldSplit != null)
        {
            oldSplit.gameObject.SetActive(false); oldSplit.name = "RetiredBackpackSplitButton";
            if (Application.isPlaying) Object.Destroy(oldSplit.gameObject); else Object.DestroyImmediate(oldSplit.gameObject);
        }
        EnsureAbilityScrollbar(area.Find("EnvironmentEditorContent/EnvironmentAbilityScroll").GetComponent<ScrollRect>());
        if (panel.Find("OpenCognitionBackpackButton") == null)
            Button(panel, "OpenCognitionBackpackButton", "认知背包", new Vector2(1f, 1f),
                new Vector2(-32f, -182f), new Vector2(360f, 48f), 24);
        // 已保存的纸样、纯色底与手工位置直接使用；只为缺少的标题补底图。
        TitlePaper(editor.Find("EnvironmentSelectionTitleText"));
        TitlePaper(bag.Find("BackpackTitleText"));
    }
#endif

    static void ApplyPaper(Image image, Sprite sprite)
    {
        // 安全：装饰没有图像组件时不影响按钮和槽的原交互。
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 4f;
        image.color = Color.white;
        if (sprite == GameUIAssets.Shared.note) ApplyLabelShadow(image);
    }

    public static void ApplyLabelShadow(Image image)
    {
        // 小纸标签只在右侧和下沿投淡影；复用组件，重新显示不会叠加阴影。
        Shadow shadow = image.GetComponent<Shadow>();
        if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .2f);
        shadow.effectDistance = new Vector2(2f, -3f);
        shadow.useGraphicAlpha = true;
    }

    static void TitlePaper(Transform title)
    {
        RectTransform label = (RectTransform)title;
        Transform previous = title.parent.Find(title.name + "Paper");
        // 已制作标题的贴图与大小由预设保存，运行时不再覆盖。
        if (previous != null) return;
        RectTransform paper = Region(title.parent, title.name + "Paper", label.anchorMin, label.anchoredPosition, label.sizeDelta);
        Background(paper, Normal, false);
        paper.anchorMin = label.anchorMin; paper.anchorMax = label.anchorMax; paper.pivot = label.pivot;
        paper.anchoredPosition = label.anchoredPosition + new Vector2(-8, 4);
        paper.sizeDelta = label.sizeDelta + new Vector2(16, 8);
        paper.SetAsFirstSibling();
        ApplyPaper(paper.GetComponent<Image>(), GameUIAssets.Shared.note);
        paper.GetComponent<Image>().raycastTarget = false;
    }

    static void BuildEditor(Transform area)
    {
        RectTransform hint = Region(area, "EnvironmentSelectionHint", new Vector2(.5f, 0), Vector2.zero, new Vector2(720, 54));
        Background(hint, Card, false);
        ApplyPaper(hint.GetComponent<Image>(), GameUIAssets.Shared.note);
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
        Scroll(card, "EnvironmentAbilityScroll", new Vector2(20, -184), new Vector2(680, 120));
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
        Text mode = Label(bag, "BackpackModeText", "只读查看", new Vector2(18, -50), new Vector2(324, 60), 16, Ink);
        mode.horizontalOverflow = HorizontalWrapMode.Wrap;
        Label(bag, "BackpackCapacityText", "", new Vector2(18, -114), new Vector2(324, 40), 15, Muted);
        Scroll(bag, "CognitionBackpackScroll", new Vector2(18, -160), new Vector2(324, 212));
        Label(bag, "BackpackSelectionText", "", new Vector2(18, -378), new Vector2(324, 28), 17, Ink);
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
        image.color = color; image.raycastTarget = blocksInput;
        if (color == Card) { image.sprite = GameUIAssets.Shared.environmentPanel; image.type = Image.Type.Sliced; }
        return image;
    }

    public static Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        RectTransform rect = Region(parent, name, new Vector2(0, 1), position, size);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = GameUIAssets.Shared.font;
        text.text = value; text.fontSize = fontSize; text.color = color; text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }

    public static Button Button(Transform parent, string name, string caption, Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
    {
        RectTransform rect = Region(parent, name, anchor, position, size);
        Image image = Background(rect, Normal, true);
        image.sprite = GameUIAssets.Shared.note; image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 4f;
        ApplyLabelShadow(image);
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(.8f, 1f, 1f); colors.pressedColor = new Color(.55f, .85f, .88f);
        colors.disabledColor = new Color(.82f, .82f, .82f); button.colors = colors;
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

    static void EnsureAbilityScrollbar(ScrollRect scroll)
    {
        if (scroll.verticalScrollbar != null) return;
        RectTransform track = Region(scroll.transform, "VerticalScrollbar", new Vector2(1, 1), Vector2.zero, new Vector2(16, 0));
        track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
        Background(track, Normal, true);
        RectTransform handle = Region(track, "Handle", Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(handle); Image graphic = Background(handle, new Color(.38f, .66f, .72f), true);
        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle; bar.targetGraphic = graphic; bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        // 固定预留拖动条宽度，数字行不会延伸到把手下面。
        scroll.viewport.offsetMax = new Vector2(-18, 0);
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
