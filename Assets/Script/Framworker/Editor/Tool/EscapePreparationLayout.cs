#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只制作逃生准备区域；保留游戏面板其它人工布局。</summary>
public static class EscapePreparationLayout
{
    static readonly Color Ink = new Color(.88f, .94f, .96f);
    // 独立制作入口只更新这一块预设；不在运行时构造 UI。
    public static void RunForBatch()
    {
        const string path = "Assets/MyObjAssets/UIPanel/GameplayPanel.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Build(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            foreach (string name in new[] { "player20", "SpiritPlayer" })
            {
                string actorPath = "Assets/MyObjAssets/gameObject/Pf/Player/" + name + ".prefab";
                GameObject actor = PrefabUtility.LoadPrefabContents(actorPath);
                try { PrefabUtility.SaveAsPrefabAsset(actor, actorPath); }
                finally { PrefabUtility.UnloadPrefabContents(actor); }
            }
            Directory.CreateDirectory(".utmp/P20Validation");
            File.WriteAllText(".utmp/P20Validation/prefab.txt", "PASS scoped escape UI authoring");
            Debug.Log("P20 prefab authoring passed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        EditorApplication.Exit(0);
    }
    public static void Build(Transform parent)
    {
        Transform old = parent.Find("EscapeChoiceArea");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        RectTransform overlay = Rect("EscapeChoiceArea", parent);
        Stretch(overlay);
        Background(overlay, new Color(.015f, .025f, .04f, .85f), true);
        RectTransform card = Rect("Choices", overlay);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(920, 820);
        Background(card, new Color(.055f, .09f, .12f, .98f), true);
        Text title = Label(card, "EscapeBudgetText", "逃生准备", 30, 46);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0, 1);
        title.rectTransform.pivot = new Vector2(0, 1);
        title.rectTransform.anchoredPosition = new Vector2(24, -18);
        title.rectTransform.sizeDelta = new Vector2(872, 46);
        RectTransform tabs = Rect("Tabs", card);
        Place(tabs, new Vector2(24, -78), new Vector2(872, 52));
        RectTransform costTab = MakeButton(tabs, "CostTabButton", "1 · 认知降费购买", 52);
        Place(costTab, Vector2.zero, new Vector2(424, 52));
        RectTransform abilityTab = MakeButton(tabs, "AbilityTabButton", "2 · 本场体能效果", 52);
        Place(abilityTab, new Vector2(448, 0), new Vector2(424, 52));
        RectTransform viewport = Rect("CostPage", card);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(20, 106); viewport.offsetMax = new Vector2(-20, -146);
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
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        Label(content, "PurchaseHintText", "三项降费均可多次购买，直到满级；通关后保留，失败或退出撤销本关新购。\n剩余认知资源将用于强化体能，并有概率触发正负面效果；余额越少，负面概率越高。", 20, 94);
        Label(content, "EscapeCostText", "", 20, 76);
        Label(content, "OfferTitle", "购买完成后点下一步 · 降费主要用于后续关卡探索", 22, 34);
        Text locked = Label(content, "LockedPurchaseText", "本次体能结果已确定，购买已关闭。", 20, 32);
        locked.gameObject.SetActive(false);
        string[] rows = { "MapCostRow", "BackpackCostRow", "FormCostRow" };
        string[] titles = { "地图认知降费", "入包认知降费", "转换认知降费" };
        for (int i = 0; i < rows.Length; i++)
        {
            RectTransform row = Rect(rows[i], content);
            Background(row, new Color(.08f, .16f, .20f), false);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 128;
            Place(Label(row, rows[i] + "NameText", titles[i], 23, 34).rectTransform, new Vector2(14, -8), new Vector2(610, 34));
            Place(Label(row, rows[i] + "ValuesText", "", 19, 64).rectTransform, new Vector2(14, -46), new Vector2(610, 64));
            Place(MakeButton(row, rows[i] + "PurchaseButton", "购买一级", 52), new Vector2(640, -12), new Vector2(168, 52));
            Place(Label(row, rows[i] + "StateText", "", 19, 38).rectTransform, new Vector2(640, -72), new Vector2(168, 38));
        }
        RectTransform abilityPage = Rect("AbilityPage", card);
        abilityPage.anchorMin = Vector2.zero; abilityPage.anchorMax = Vector2.one;
        abilityPage.offsetMin = new Vector2(20, 106); abilityPage.offsetMax = new Vector2(-20, -146);
        Background(abilityPage, new Color(.025f, .045f, .065f, .6f), false);
        Place(Label(abilityPage, "AbilityHintText", "正负分别判定，可同时生效；先正面后负面计算，最终体能限制在配置范围内。\n以下效果仅在本关生效。", 21, 70).rectTransform, new Vector2(20, -16), new Vector2(832, 70));
        Place(Label(abilityPage, "EmptyResultText", "当前无任何效果生效", 27, 150).rectTransform, new Vector2(20, -116), new Vector2(832, 150));
        for (int i = 0; i < 2; i++)
        {
            RectTransform row = Rect(i == 0 ? "PositiveResult" : "NegativeResult", abilityPage);
            Place(row, new Vector2(20, -104 - 140 * i), new Vector2(832, 124));
            Background(row, i == 0 ? new Color(.09f, .26f, .23f) : new Color(.30f, .16f, .17f), false);
            Place(Label(row, row.name + "TitleText", i == 0 ? "正面效果 · 生效" : "负面效果 · 生效", 24, 38).rectTransform, new Vector2(18, -10), new Vector2(796, 38));
            Place(Label(row, row.name + "ValueText", "", 26, 52).rectTransform, new Vector2(18, -58), new Vector2(796, 52));
        }
        Place(Label(abilityPage, "FinalValuesText", "", 22, 100).rectTransform, new Vector2(20, -418), new Vector2(832, 100));
        abilityPage.gameObject.SetActive(false);
        RectTransform next = MakeButton(card, "NextEscapeButton", "下一步", 64);
        next.anchorMin = next.anchorMax = next.pivot = new Vector2(.5f, 0);
        next.anchoredPosition = new Vector2(0, 22); next.sizeDelta = new Vector2(640, 64);
        RectTransform begin = MakeButton(card, "BeginEscapeButton", "开始逃生", 64);
        begin.anchorMin = begin.anchorMax = begin.pivot = new Vector2(.5f, 0);
        begin.anchoredPosition = new Vector2(0, 22); begin.sizeDelta = new Vector2(640, 64);
        begin.gameObject.SetActive(false);
        overlay.gameObject.AddComponent<EscapeChoiceView>();
        overlay.gameObject.SetActive(false);
    }
    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size; }
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
        label.font = GameUIAssets.Shared.font;
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
