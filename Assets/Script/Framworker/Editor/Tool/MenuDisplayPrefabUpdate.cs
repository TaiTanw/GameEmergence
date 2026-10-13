#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>仅增补开始淡化组件和显示设置入口，不重建其它面板。</summary>
public static class MenuDisplayPrefabUpdate
{
    public static void Apply()
    {
        const string beginPath = "Assets/MyObjAssets/UIPanel/BeginPanel.prefab";
        GameObject begin = PrefabUtility.LoadPrefabContents(beginPath);
        try
        {
            if (begin.GetComponent<CanvasGroup>() == null)
            { begin.AddComponent<CanvasGroup>(); PrefabUtility.SaveAsPrefabAsset(begin, beginPath); }
        }
        finally { PrefabUtility.UnloadPrefabContents(begin); }
        const string settingsPath = "Assets/MyObjAssets/UIPanel/SetPanel.prefab";
        GameObject settings = PrefabUtility.LoadPrefabContents(settingsPath);
        try
        {
            if (settings.transform.Find("di") != null) return;
            RectTransform music = (RectTransform)settings.transform.Find("mu");
            RectTransform keys = (RectTransform)settings.transform.Find("ke");
            music.anchoredPosition = new Vector2(0, 150);
            keys.anchoredPosition = Vector2.zero;
            GameObject entry = Object.Instantiate(music.gameObject, settings.transform);
            entry.name = "di";
            ((RectTransform)entry.transform).anchoredPosition = new Vector2(0, -150);
            entry.GetComponentInChildren<Text>().text = "显示——";
            RectTransform area = Rect(settings.transform.Find("SettingsContent"), "DisplaySettingsArea", Vector2.zero, new Vector2(1000, 600));
            Label(area, "DisplayTitle", "显示设置", new Vector2(0, 230), new Vector2(800, 64), 42);
            Button(area, "DisplayResolution", "分辨率", 105);
            Button(area, "DisplayMode", "模式", -15);
            Button(area, "DisplayApply", "应用", -150);
            Label(area, "DisplayHelp", "画面比例固定 16:9\n非 16:9 屏幕自动留黑边；窗口保持固定比例", new Vector2(0, -260), new Vector2(950, 90), 26);
            area.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(settings, settingsPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(settings); }
        AssetDatabase.SaveAssets();
    }

    static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform)); item.layer = 5;
        RectTransform rect = (RectTransform)item.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
    static Text Label(Transform parent, string name, string caption, Vector2 position, Vector2 size, int fontSize)
    {
        Text text = Rect(parent, name, position, size).gameObject.AddComponent<Text>();
        text.text = caption; text.font = GameUIAssets.Shared.font; text.fontSize = fontSize;
        text.color = new Color(.18f, .14f, .20f); text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        return text;
    }
    static void Button(Transform parent, string name, string caption, float y)
    {
        RectTransform rect = Rect(parent, name, new Vector2(0, y), new Vector2(700, 82));
        Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.82f, .73f, .59f);
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        Label(rect, "Text (Legacy)", caption, Vector2.zero, new Vector2(670, 82), 34);
    }
}
#endif
