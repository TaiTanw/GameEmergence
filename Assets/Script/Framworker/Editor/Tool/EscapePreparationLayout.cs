#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>只制作逃生准备区域；保留游戏面板其它人工布局。</summary>
public static class EscapePreparationLayout
{
    static readonly Color Ink = new Color(.88f, .94f, .96f);
    public static void Build(Transform parent)
    {
        Transform old = parent.Find("EscapeChoiceArea");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        RectTransform overlay = Rect("EscapeChoiceArea", parent);
        Stretch(overlay);
        Background(overlay, new Color(.015f, .025f, .04f, .85f), true);
        RectTransform card = Rect("Choices", overlay);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(920, 720);
        Background(card, new Color(.055f, .09f, .12f, .98f), true);
        Text title = Label(card, "EscapeBudgetText", "逃生准备", 30, 46);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0, 1);
        title.rectTransform.pivot = new Vector2(0, 1);
        title.rectTransform.anchoredPosition = new Vector2(24, -18);
        title.rectTransform.sizeDelta = new Vector2(872, 46);
        RectTransform viewport = Rect("Viewport", card);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(20, 106); viewport.offsetMax = new Vector2(-20, -76);
        Background(viewport, new Color(.025f, .045f, .065f, .6f), true);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16); layout.spacing = 14;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = card.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        Label(content, "KeepTitle", "现场与认知费用", 23, 34);
        MakeButton(content, "PreserveSceneButton", "保留现场", 64);
        Label(content, "EscapeCostText", "", 20, 112);
        Label(content, "OfferTitle", "本次可选升级 · 可重复购买，完成后点下一步", 23, 40);
        RectTransform template = MakeButton(content, "ChoiceTemplate", "升级", 112);
        template.gameObject.SetActive(false);
        Label(content, "EmptyPoolText", "", 22, 80);
        Text penalty = Label(content, "PenaltyText", "", 26, 330);
        penalty.alignment = TextAnchor.MiddleCenter; penalty.gameObject.SetActive(false);
        RectTransform next = MakeButton(card, "NextEscapeButton", "下一步", 64);
        next.anchorMin = next.anchorMax = next.pivot = new Vector2(.5f, 0);
        next.anchoredPosition = new Vector2(0, 22); next.sizeDelta = new Vector2(640, 64);
        overlay.gameObject.AddComponent<EscapeChoiceView>();
        overlay.gameObject.SetActive(false);
    }
    static RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        obj.transform.SetParent(parent, false); return obj.GetComponent<RectTransform>();
    }
    static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    static Image Background(RectTransform rect, Color color, bool raycast)
    { Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image; }
    static Text Label(Transform parent, string name, string text, int fontSize, float height)
    {
        RectTransform rect = Rect(name, parent);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text; label.fontSize = fontSize; label.color = Ink;
        label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return label;
    }
    static RectTransform MakeButton(Transform parent, string name, string text, float height)
    {
        RectTransform rect = Rect(name, parent);
        Image image = Background(rect, new Color(.13f, .32f, .37f), true);
        rect.gameObject.AddComponent<Button>().targetGraphic = image;
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        Text label = Label(rect, "Label", text, 22, height); Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(18, 8); label.rectTransform.offsetMax = new Vector2(-18, -8);
        return rect;
    }
}
#endif
