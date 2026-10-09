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

    [MenuItem("自定义工具/UI/生成游戏面板基础预设")]
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
            Region(root, "MapArea", new Vector2(1, 0), new Vector2(-32, 32), new Vector2(240, 200)).gameObject.SetActive(false);
            Region(root, "ActionArea", new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(640, 80)).gameObject.SetActive(false);
            Region(root, "MessageArea", new Vector2(.5f, 0), new Vector2(0, 140), new Vector2(720, 100)).gameObject.SetActive(false);
            root.gameObject.AddComponent<GameplayPanel>();
            Save(root);
        }
        finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
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
