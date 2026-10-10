#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>生成关内 UI 基础预设；仅写这两个预设及其 UI 地址，运行时不依赖此工具。</summary>
public static class GameUIPrefabBuilder
{
    static readonly Color Ink = new Color(.88f, .94f, .96f);
    static readonly Color Muted = new Color(.58f, .69f, .75f);
    static readonly Color Card = new Color(.055f, .09f, .12f, .92f);

    [MenuItem("自定义工具/开发工具/UI/生成游戏面板基础预设")]
    public static void Build()
    {
        BuildGameplay();
        BuildMenu();
        AssetDatabase.SaveAssets();
        Debug.Log("Game UI prefabs saved: GameplayPanel, GameMenuPanel");
    }

    public static void RunForBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void BuildGameplayForBatch()
    {
        try { BuildGameplay(); AssetDatabase.SaveAssets(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void BuildGameplay()
    {
        RectTransform root = Root("GameplayPanel");
        try
        {
            RectTransform resources = Region(root, "ResourceArea", new Vector2(0, 1), new Vector2(32, -32), new Vector2(304, 188));
            Background(resources, Card, false);
            Label(resources, "CognitionText", "认知  100", new Vector2(20, -16), new Vector2(264, 40), 30, Ink);
            Label(resources, "StaminaText", "体力  100 / 100", new Vector2(20, -64), new Vector2(264, 30), 23, Muted);
            Label(resources, "OxygenText", "氧气  100 / 100", new Vector2(20, -98), new Vector2(264, 30), 23, Muted);
            Label(resources, "FormCooldownText", "转换 CD  0.0 秒", new Vector2(20, -138), new Vector2(264, 30), 23, Ink);

            RectTransform stage = Region(root, "StageArea", new Vector2(.5f, 1), new Vector2(0, -32), new Vector2(260, 48));
            Background(stage, Card, false);
            Text stageText = Label(stage, "StageText", "探索 · 漫游", new Vector2(0, 0), new Vector2(260, 48), 24, Ink);
            Stretch(stageText.rectTransform); stageText.alignment = TextAnchor.MiddleCenter;
            RectTransform hint = Region(root, "MenuHintArea", Vector2.one, new Vector2(-32, -32), new Vector2(240, 48));
            Text hintText = Label(hint, "MenuHintText", "ESC  游戏菜单", Vector2.zero, new Vector2(240, 48), 23, Muted);
            Stretch(hintText.rectTransform); hintText.alignment = TextAnchor.MiddleRight;

            // 预留容器只安排位置，不把开发占位文字显示给玩家。
            BuildMapArea(root);
            RectTransform actions = Region(root, "ActionArea", new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(720, 176));
            RectTransform resetRect = Region(actions, "ResetTrialButton", new Vector2(.5f, 0), new Vector2(0, 8), new Vector2(260, 64));
            Image resetImage = Background(resetRect, new Color(.13f, .32f, .37f), true);
            Button reset = resetRect.gameObject.AddComponent<Button>();
            reset.targetGraphic = resetImage;
            Text resetLabel = Label(resetRect, "Label", "重置试验", Vector2.zero, new Vector2(260, 64), 27, Ink);
            Stretch(resetLabel.rectTransform); resetLabel.alignment = TextAnchor.MiddleCenter;
            resetRect.anchoredPosition = new Vector2(0, 8);
            RectTransform startRect = Region(actions, "StartEscapeButton", new Vector2(.5f, 0), new Vector2(150, 8), new Vector2(260, 64));
            Image startImage = Background(startRect, new Color(.25f, .3f, .14f), true);
            Button start = startRect.gameObject.AddComponent<Button>(); start.targetGraphic = startImage;
            Text startLabel = Label(startRect, "Label", "开始逃生", Vector2.zero, new Vector2(260, 64), 27, Ink);
            Stretch(startLabel.rectTransform); startLabel.alignment = TextAnchor.MiddleCenter;
            RectTransform environment = NewRect("EnvironmentEditArea", actions);
            Stretch(environment); Background(environment, Card, true);
            Label(environment, "EnvironmentObjectText", "环境参数", new Vector2(20, -12), new Vector2(500, 30), 24, Ink);
            RectTransform nextRect = Region(environment, "NextEnvironmentParameterButton", new Vector2(0, 1), new Vector2(540, -10), new Vector2(160, 34));
            Image nextImage = Background(nextRect, new Color(.13f, .32f, .37f), true);
            Button next = nextRect.gameObject.AddComponent<Button>(); next.targetGraphic = nextImage;
            Text nextLabel = Label(nextRect, "Label", "切换参数", Vector2.zero, new Vector2(160, 34), 20, Ink);
            Stretch(nextLabel.rectTransform); nextLabel.alignment = TextAnchor.MiddleCenter;
            Label(environment, "EnvironmentParameterText", "选择物体后显示可调整参数。", new Vector2(20, -46), new Vector2(680, 30), 21, Muted);
            RectTransform valueRect = Region(environment, "EnvironmentValueInput", new Vector2(0, 1), new Vector2(20, -88), new Vector2(410, 48));
            Image valueImage = Background(valueRect, new Color(.1f, .16f, .2f), true);
            Text valueText = Label(valueRect, "Text (Legacy)", "", new Vector2(12, 0), new Vector2(386, 48), 23, Ink);
            valueText.rectTransform.anchorMin = valueText.rectTransform.anchorMax = new Vector2(0, 1);
            Text placeholder = Label(valueRect, "Placeholder", "输入大于或等于 0 的数", new Vector2(12, 0), new Vector2(386, 48), 20, Muted);
            InputField valueInput = valueRect.gameObject.AddComponent<InputField>();
            valueInput.targetGraphic = valueImage; valueInput.textComponent = valueText; valueInput.placeholder = placeholder;
            valueInput.contentType = InputField.ContentType.Standard; valueInput.lineType = InputField.LineType.SingleLine;
            RectTransform applyRect = Region(environment, "ApplyEnvironmentButton", new Vector2(0, 1), new Vector2(450, -88), new Vector2(250, 48));
            Image applyImage = Background(applyRect, new Color(.13f, .32f, .37f), true);
            Button apply = applyRect.gameObject.AddComponent<Button>(); apply.targetGraphic = applyImage;
            Text applyLabel = Label(applyRect, "Label", "应用", Vector2.zero, new Vector2(250, 48), 25, Ink);
            Stretch(applyLabel.rectTransform); applyLabel.alignment = TextAnchor.MiddleCenter;
            Label(environment, "EnvironmentEditHintText", "点击环境物体，查看并调整参数。", new Vector2(20, -140), new Vector2(680, 28), 19, Muted);
            environment.gameObject.AddComponent<EnvironmentEditView>();
            environment.gameObject.SetActive(false);
            startRect.SetParent(root, false);
            startRect.anchorMin = startRect.anchorMax = startRect.pivot = Vector2.one;
            startRect.anchoredPosition = new Vector2(-32, -100);
            actions.gameObject.SetActive(false);
            RectTransform message = Region(root, "MessageArea", new Vector2(.5f, 0), new Vector2(0, 140), new Vector2(720, 80));
            Background(message, Card, false);
            Text messageText = Label(message, "RefreshMessageText", "", Vector2.zero, new Vector2(680, 64), 24, Ink);
            Stretch(messageText.rectTransform); messageText.alignment = TextAnchor.MiddleCenter;
            messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.gameObject.SetActive(false);
            BuildEscapeChoices(root);
            root.gameObject.AddComponent<GameplayPanel>();
            Save(root);
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
    }

    static void BuildMapArea(RectTransform root)
    {
        RectTransform map = Region(root, "MapArea", new Vector2(1, 0), new Vector2(-32, 32), new Vector2(360, 440));
        Background(map, Card, true);
        Label(map, "MapTitleText", "认知地图", new Vector2(20, -12), new Vector2(320, 36), 24, Ink);
        RectTransform cells = Region(map, "MapCellsArea", new Vector2(0, 1), new Vector2(20, -56), new Vector2(320, 240));
        Background(cells, new Color(.025f, .045f, .065f, .7f), true); cells.gameObject.AddComponent<RectMask2D>();
        Label(map, "MapSelectedText", "选择格子查看记录", new Vector2(20, -304), new Vector2(320, 30), 17, Muted);
        RectTransform cancelRect = Region(map, "CancelMapCellButton", new Vector2(0, 1), new Vector2(20, -344), new Vector2(320, 42));
        Image cancelImage = Background(cancelRect, new Color(.13f, .32f, .37f), true);
        Button cancel = cancelRect.gameObject.AddComponent<Button>(); cancel.targetGraphic = cancelImage;
        Text cancelLabel = Label(cancelRect, "Label", "取消记录", Vector2.zero, new Vector2(320, 42), 22, Ink);
        Stretch(cancelLabel.rectTransform); cancelLabel.alignment = TextAnchor.MiddleCenter;
        Text hint = Label(map, "MapHintText", "点击格子查看记录。", new Vector2(20, -394), new Vector2(320, 34), 16, Muted);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        map.gameObject.AddComponent<MapView>();
        map.gameObject.SetActive(false);
    }

    static void BuildMenu()
    {
        RectTransform root = Root("GameMenuPanel");
        try
        {
            Background(root, new Color(.015f, .025f, .04f, .78f), true);
            RectTransform card = Region(root, "MenuCard", new Vector2(.5f, .5f), Vector2.zero, new Vector2(520, 300));
            Background(card, Card, false);
            Text title = Label(card, "MenuTitleText", "游戏菜单", new Vector2(0, -32), new Vector2(520, 56), 34, Ink);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(.5f, 1);
            title.rectTransform.pivot = new Vector2(.5f, 1); title.alignment = TextAnchor.MiddleCenter;
            RectTransform buttonRect = Region(card, "ReturnToMenuButton", new Vector2(.5f, .5f), new Vector2(0, -8), new Vector2(320, 64));
            Image image = Background(buttonRect, new Color(.13f, .32f, .37f), true);
            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(.7f, .95f, 1f); colors.pressedColor = new Color(.5f, .75f, .8f);
            button.colors = colors;
            Text buttonText = Label(buttonRect, "Label", "返回主菜单", Vector2.zero, new Vector2(320, 64), 27, Ink);
            Stretch(buttonText.rectTransform); buttonText.alignment = TextAnchor.MiddleCenter;
            Text resume = Label(card, "ResumeHintText", "ESC  继续游戏", new Vector2(0, 32), new Vector2(520, 36), 22, Muted);
            resume.rectTransform.anchorMin = resume.rectTransform.anchorMax = new Vector2(.5f, 0);
            resume.rectTransform.pivot = new Vector2(.5f, 0); resume.alignment = TextAnchor.MiddleCenter;
            root.gameObject.AddComponent<GameMenuPanel>();
            Save(root);
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
    }

    static void BuildEscapeChoices(RectTransform root)
    {
        RectTransform overlay = NewRect("EscapeChoiceArea", root);
        Stretch(overlay); Background(overlay, new Color(.015f, .025f, .04f, .82f), true);
        RectTransform card = Region(overlay, "Choices", new Vector2(.5f, .5f), new Vector2(0, -20), new Vector2(680, 400));
        Background(card, Card, true);
        VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24); layout.spacing = 16;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        Text budget = Label(overlay, "EscapeBudgetText", "选择一项能力开始逃生", Vector2.zero, new Vector2(760, 60), 28, Ink);
        budget.rectTransform.anchorMin = budget.rectTransform.anchorMax = new Vector2(.5f, .5f);
        budget.rectTransform.pivot = new Vector2(.5f, .5f); budget.rectTransform.anchoredPosition = new Vector2(0, 240);
        budget.alignment = TextAnchor.MiddleCenter;
        RectTransform template = NewRect("ChoiceTemplate", card);
        Image background = Background(template, new Color(.13f, .32f, .37f), true);
        Button choice = template.gameObject.AddComponent<Button>(); choice.targetGraphic = background;
        LayoutElement size = template.gameObject.AddComponent<LayoutElement>(); size.preferredHeight = 64;
        Text label = Label(template, "Label", "能力", Vector2.zero, new Vector2(632, 64), 25, Ink);
        Stretch(label.rectTransform); label.alignment = TextAnchor.MiddleCenter;
        template.gameObject.SetActive(false);
        overlay.gameObject.AddComponent<EscapeChoiceView>(); overlay.gameObject.SetActive(false);
    }

    static RectTransform Root(string name)
    {
        RectTransform root = NewRect(name, null);
        Stretch(root); return root;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        RectTransform rect = obj.GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        return rect;
    }

    static RectTransform Region(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RectTransform rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Image Background(RectTransform rect, Color color, bool blocksInput)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = blocksInput; return image;
    }

    static Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        RectTransform rect = Region(parent, name, new Vector2(0, 1), position, size);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value; text.fontSize = fontSize; text.color = color;
        text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static void Save(RectTransform root)
    {
        string path = "Assets/MyObjAssets/UIPanel/" + root.name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings.FindGroup("UI");
        if (group == null) throw new InvalidOperationException("UI 地址分组不存在。");
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group);
        entry.address = root.name;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
        EditorUtility.SetDirty(group);
    }
}
#endif
