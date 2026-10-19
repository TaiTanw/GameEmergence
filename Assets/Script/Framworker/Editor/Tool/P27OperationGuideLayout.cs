#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只制作 P27.4 暂停菜单、只读指南与两块入门告示，不改其它纸样界面。</summary>
public static class P27OperationGuideLayout
{
    const string Prefab = "Assets/MyObjAssets/UIPanel/GameMenuPanel.prefab";
    const string Output = ".utmp/P274Validation/";
    static readonly Color Ink = new Color(.18f, .14f, .20f);

    [MenuItem("自定义工具/开发工具/UI/应用 P27 操作指南")]
    public static void Apply()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            Transform menu = root.transform.Find("MenuCard");
            ((RectTransform)menu).sizeDelta = new Vector2(520, 400);
            Place(menu.Find("ReturnToMenuButton") as RectTransform, new Vector2(.5f, .5f), new Vector2(0, -68), new Vector2(320, 64));
            var buttonStyle = menu.Find("ReturnToMenuButton").gameObject;
            Button(menu, "OperationGuideButton", "操作指南", new Vector2(.5f, .5f), new Vector2(0, 24), buttonStyle);
            Transform oldGuide = root.transform.Find("OperationGuideCard");
            if (oldGuide != null) Object.DestroyImmediate(oldGuide.gameObject);
            RectTransform guide = Rect(root.transform, "OperationGuideCard", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1320, 820));
            Image paper = guide.gameObject.AddComponent<Image>();
            paper.sprite = GameUIAssets.Shared.environmentPanel; paper.type = Image.Type.Sliced; paper.color = Color.white;
            Label(guide, "GuideTitleText", "操作指南", 36, new Vector2(0, -34), new Vector2(1100, 54), TextAnchor.MiddleCenter);
            Label(guide, "GuideKeysTitleText", "当前按键", 28, new Vector2(-300, -114), new Vector2(520, 42), TextAnchor.MiddleLeft);
            Label(guide, "GuideFixedTitleText", "常用操作", 28, new Vector2(300, -114), new Vector2(540, 42), TextAnchor.MiddleLeft);
            Text keys = Label(guide, "GuideBindingsText", "", 27, new Vector2(-300, -174), new Vector2(520, 450), TextAnchor.UpperLeft);
            keys.lineSpacing = 1.55f;
            Label(guide, "GuideFixedText", "长按回车：结束探索\n松开回车：取消确认\n\n点按交互键：选中／使用\n选中箱子后长按交互键：收纳\n\n鼠标左键：查看／修改蓝框环境\n点击已亮地图格：传送（探索中）\n滚轮：滚动列表\n\nESC：打开暂停菜单\n指南内按 ESC 返回，菜单内按 ESC 继续。", 25,
                new Vector2(300, -174), new Vector2(540, 450), TextAnchor.UpperLeft);
            Button(guide, "GuideBackButton", "返回暂停菜单", new Vector2(.5f, 0), new Vector2(0, 82), buttonStyle);
            Text hint = Label(guide, "GuideReadOnlyText", "查看按键；如需修改，请返回主菜单 → 设置 → 按键。", 23,
                Vector2.zero, new Vector2(1140, 36), TextAnchor.MiddleCenter);
            Place(hint.rectTransform, new Vector2(.5f, 0), new Vector2(0, 30), new Vector2(1140, 36));
            guide.gameObject.SetActive(false);
            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            }
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        string previous = SceneManager.GetActiveScene().path;
        try
        {
            UpdateNotice("L00_Orientation", "通关交互");
            UpdateNotice("L01_PropertyLabyrinth", "认知与试验说明");
        }
        finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
        AssetDatabase.SaveAssets();
    }

    static void UpdateNotice(string sceneName, string noticeName)
    {
        string[] matches = AssetDatabase.FindAssets(sceneName + " t:Scene", new[] { "Assets/MyObjAssets/Scenes/Designed" });
        if (matches.Length != 1) throw new InvalidOperationException("无法唯一定位场景 " + sceneName);
        Scene scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(matches[0]));
        int changed = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var sign in root.GetComponentsInChildren<Signboard>(true))
                if (sign.name == noticeName)
                { P25LevelVisualTools.SetNotice(sign, P25LevelVisualTools.NoticeText(noticeName)); changed++; }
        if (changed != 1) throw new InvalidOperationException("无法唯一定位告示 " + noticeName);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var sign in root.GetComponentsInChildren<Signboard>(true))
                if (sign.name == noticeName)
                {
                    var text = sign.GetComponentInChildren<Text>(true);
                    if (text.preferredHeight > text.rectTransform.rect.height + 1)
                        throw new InvalidOperationException("告示文字溢出 " + noticeName);
                }
        EditorSceneManager.SaveScene(scene);
    }

    static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 point, Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform)); root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform; Place(rect, anchor, point, size); return rect;
    }

    static void Place(RectTransform rect, Vector2 anchor, Vector2 point, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = point; rect.sizeDelta = size; }

    static Text Label(Transform parent, string name, string caption, int fontSize, Vector2 point, Vector2 size, TextAnchor alignment)
    {
        var rect = Rect(parent, name, new Vector2(.5f, 1f), point, size);
        var text = rect.gameObject.AddComponent<Text>(); text.font = GameUIAssets.Shared.font;
        text.text = caption; text.fontSize = fontSize; text.color = Ink; text.alignment = alignment; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static void Button(Transform parent, string name, string caption, Vector2 anchor, Vector2 point, GameObject style)
    {
        Transform old = parent.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject);
        var button = Object.Instantiate(style, parent, false); button.name = name;
        Place((RectTransform)button.transform, anchor, point, new Vector2(320, 64));
        var label = button.GetComponentInChildren<Text>(true); label.text = caption;
    }

    public static void RunForBatch()
    {
        try { Apply(); RenderAndCheck(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RenderAndCheck()
    {
        Directory.CreateDirectory(Output);
        Scene preview = EditorSceneManager.NewPreviewScene();
        InputActionAsset bindings = InputActionAsset.FromJson(Resources.Load<TextAsset>("player").text);
        try
        {
            var cameraRoot = new GameObject("P274 camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraRoot, preview);
            Camera camera = cameraRoot.GetComponent<Camera>(); camera.scene = preview; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10); camera.backgroundColor = new Color(.16f, .20f, .26f); camera.clearFlags = CameraClearFlags.SolidColor;
            var canvasRoot = new GameObject("P274 canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasRoot, preview);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), canvas.transform, false);
            Transform guide = root.transform.Find("OperationGuideCard");
            if (guide.gameObject.activeSelf) throw new InvalidOperationException("指南不能默认打开");
            foreach (int width in new[] { 1920, 960 })
            {
                Save(camera, width, "menu");
                root.transform.Find("MenuCard").gameObject.SetActive(false); guide.gameObject.SetActive(true);
                guide.Find("GuideBindingsText").GetComponent<Text>().text = GameMenuPanel.BindingSummary(bindings);
                Save(camera, width, "guide");
                foreach (Text text in guide.GetComponentsInChildren<Text>())
                    if (text.preferredHeight > text.rectTransform.rect.height + 1)
                        throw new InvalidOperationException(width + " 文字溢出 " + text.name + ": " + text.preferredHeight);
                guide.gameObject.SetActive(false); root.transform.Find("MenuCard").gameObject.SetActive(true);
            }
            File.WriteAllText(Output + "layout-checks.txt", "PASS paper menu and read-only guide rendered at 1920x1080 and 960x540\nPASS all guide text fits without clipping\nPASS guide hidden initially; no rebind controls\n");
        }
        finally { Object.DestroyImmediate(bindings); EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void Save(Camera camera, int width, string name)
    {
        RenderTexture previous = RenderTexture.active;
        var render = new RenderTexture(width, width * 9 / 16, 24);
        var texture = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = render; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); texture.Apply();
            File.WriteAllBytes(Output + name + "-" + width + ".png", texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(render); }
    }
}
#endif
