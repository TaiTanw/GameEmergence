#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>定向制作 P11 显示，保留其它面板与关卡资产。菜单可重复执行。</summary>
public static class P11UIPrefabUpgrade
{
    const string Art = "Assets/MyObjAssets/gameObject/Art/UI/";
    const string Panels = "Assets/MyObjAssets/UIPanel/";
    static readonly Color Ink = new Color(.18f, .14f, .20f);

    [MenuItem("自定义工具/开发工具/UI/应用 P11 面板调整")]
    public static void Apply()
    {
        var assets = AssetDatabase.LoadAssetAtPath<GameUIAssets>("Assets/Resources/GameUIAssets.asset");
        if (assets == null)
        { assets = ScriptableObject.CreateInstance<GameUIAssets>(); AssetDatabase.CreateAsset(assets, "Assets/Resources/GameUIAssets.asset"); }
        assets.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/MyObjAssets/PlayDataA/FZYTK.TTF");
        assets.panel = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "大面板底图.png");
        assets.environmentPanel = EnvironmentPaperSprite();
        if (assets.note == null) assets.note = NoteSprite();
        assets.bar = BarSprite();
        EditorUtility.SetDirty(assets); AssetDatabase.SaveAssets();
        Edit("GameplayPanel", UpgradeGameplay);
        Edit("BeginPanel", UpgradeBegin);
        Edit("SetPanel", UpgradeSettings);
        Edit("MusicSetPanel", UpgradeMusic);
        Edit("InputChangePanel", UpgradeKeys);
        Edit("GameMenuPanel", root => {
            Image card = root.transform.Find("MenuCard").GetComponent<Image>();
            card.sprite = assets.panel; card.type = Image.Type.Sliced; card.color = Color.white;
            foreach (Text text in card.GetComponentsInChildren<Text>(true)) text.color = Ink;
            card.GetComponentInChildren<Button>().GetComponent<Image>().color = new Color(.82f, .73f, .59f);
        });
        foreach (string path in Directory.GetFiles(Panels, "*.prefab"))
            Edit(Path.GetFileNameWithoutExtension(path), root => {
                foreach (Text text in root.GetComponentsInChildren<Text>(true)) text.font = assets.font;
                foreach (TMPro.TMP_Text text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    text.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/MyObjAssets/PlayDataA/FZYTK SDF.asset");
            });
        AssetDatabase.SaveAssets();
        Debug.Log("P11 UI prefabs updated; texture size/compression settings preserved.");
    }

    public static void RunForBatch()
    { try { Apply(); EditorApplication.Exit(0); } catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); } }

    static Sprite NoteSprite()
    {
        // 黑底实际为透明外围；精灵裁到纸面范围，避免标签文字挤在空白里。
        string path = Art + "黑底纸样.png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritesheet = new[] { new SpriteMetaData { name = "SmallPaper", rect = new Rect(198, 96, 884, 528),
                pivot = new Vector2(.5f, .5f), alignment = (int)SpriteAlignment.Center, border = new Vector4(64, 64, 64, 64) } };
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First(sprite => sprite.name == "SmallPaper");
    }

    static Sprite EnvironmentPaperSprite()
    {
        string path = Art + "去掉文字细黑描边.png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        Vector4 border = new Vector4(24, 24, 24, 24);
        if (importer.spriteBorder != border) { importer.spriteBorder = border; importer.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void Edit(string name, Action<GameObject> edit)
    {
        string path = Panels + name + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void UpgradeGameplay(GameObject root)
    {
        Transform panel = root.transform;
        foreach (string name in new[] { "ResourceArea", "StageArea", "MenuHintArea", "StartEscapeButton", "CognitionArea", "EndExplorationNote", "StaminaBar", "OxygenBar" }) Remove(panel, name);
        RectTransform cognition = Rect(panel, "CognitionArea", new Vector2(.5f, 1f), new Vector2(0, -12), new Vector2(1728, 74));
        cognition.anchorMin = new Vector2(.05f, 1f); cognition.anchorMax = new Vector2(.95f, 1f); cognition.sizeDelta = new Vector2(0, 74);
        Text value = Label(cognition, "CognitionText", "100 / 100", 26, Color.white);
        value.rectTransform.anchorMin = new Vector2(0, 1); value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.pivot = new Vector2(.5f, 1); value.rectTransform.anchoredPosition = Vector2.zero; value.rectTransform.sizeDelta = new Vector2(0, 32);
        RectTransform track = Rect(cognition, "CognitionTrack", new Vector2(.5f, 1), new Vector2(0, -38), new Vector2(0, 14));
        track.anchorMin = new Vector2(0, 1); track.anchorMax = Vector2.one;
        Image background = track.gameObject.AddComponent<Image>(); background.color = new Color(.08f, .065f, .11f, .85f); background.raycastTarget = false;
        Fill(track, "CognitionFill", new Color(1f, .55f, .12f), false);
        RectTransform hold = Rect(cognition, "EndExplorationFill", new Vector2(.5f, 1), new Vector2(0, -59), new Vector2(0, 4));
        hold.anchorMin = new Vector2(0, 1); hold.anchorMax = Vector2.one;
        Image white = hold.gameObject.AddComponent<Image>(); white.color = Color.white; white.raycastTarget = false;
        white.sprite = GameUIAssets.Shared.bar;
        white.type = Image.Type.Simple;
        RectTransform note = Rect(panel, "EndExplorationNote", new Vector2(0, 1), new Vector2(18, -88), new Vector2(300, 96));
        Paper(note, GameUIAssets.Shared.note);
        Text hint = Label(note, "EndExplorationText", "长按回车\n结束探索", 26, Ink);
        hint.rectTransform.offsetMin = new Vector2(28, 12); hint.rectTransform.offsetMax = new Vector2(-28, -12);
        ResourceBar(panel, "StaminaBar", "StaminaFill", new Color(.91f, .75f, .31f));
        ResourceBar(panel, "OxygenBar", "OxygenFill", new Color(.43f, .81f, .93f));
        foreach (Selectable control in root.GetComponentsInChildren<Selectable>(true))
        { Navigation navigation = control.navigation; navigation.mode = Navigation.Mode.None; control.navigation = navigation; }
        foreach (string path in new[] { "MessageArea", "EscapeChoiceArea/Choices" })
        {
            Transform item = panel.Find(path);
            if (item == null) continue;
            Image image = item.GetComponent<Image>();
            if (image != null) { image.sprite = GameUIAssets.Shared.panel; image.type = Image.Type.Sliced; image.color = Color.white; }
            foreach (Text text in item.GetComponentsInChildren<Text>(true)) text.color = Ink;
        }
        Transform viewport = panel.Find("EscapeChoiceArea/Choices/Viewport");
        if (viewport != null) viewport.GetComponent<Image>().color = new Color(.82f, .75f, .65f, .7f);
        // 旧按钮底色调整为浅色，让纸面上的深色字保有对比。
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            Image image = button.GetComponent<Image>();
            if (image != null && image.sprite == null) image.color = new Color(.82f, .73f, .59f);
            foreach (Text text in button.GetComponentsInChildren<Text>(true)) text.color = Ink;
        }
        EnvironmentEditLayout.Ensure(panel.Find("ActionArea/EnvironmentEditArea"), panel);
        // 地图恢复最初的纯色底与浅色文字，不替换地图记录或点击行为。
        Transform map = panel.Find("MapArea");
        Image mapBackground = map.GetComponent<Image>();
        mapBackground.sprite = null; mapBackground.type = Image.Type.Simple;
        mapBackground.color = new Color(.055f, .09f, .12f, .92f);
        Image cells = map.Find("MapCellsArea").GetComponent<Image>();
        cells.sprite = null; cells.type = Image.Type.Simple; cells.color = new Color(.025f, .045f, .065f, .7f);
        foreach (Text text in map.GetComponentsInChildren<Text>(true)) text.color = new Color(.88f, .94f, .96f);
    }

    static void UpgradeBegin(GameObject root)
    {
        Remove(root.transform, "CoverVideo");
        RectTransform videoRect = Rect(root.transform, "CoverVideo", new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        Stretch(videoRect); videoRect.SetAsFirstSibling();
        RawImage image = videoRect.gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
        VideoPlayer video = videoRect.gameObject.AddComponent<VideoPlayer>();
        video.clip = AssetDatabase.LoadAssetAtPath<VideoClip>(Art + "头发摇摆牌子晃动循环.mp4");
        video.playOnAwake = false; video.isLooping = true; video.waitForFirstFrame = true;
        video.audioOutputMode = VideoAudioOutputMode.None; video.renderMode = VideoRenderMode.RenderTexture;
        // 7:4 视频以画布高度铺满，左右对称裁掉少量边缘。
        AspectRatioFitter fit = videoRect.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = 1344f / 768f;
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        { Image graphic = button.GetComponent<Image>(); if (graphic != null) graphic.color = Color.clear; }
    }

    static void UpgradeSettings(GameObject root)
    {
        Transform panel = root.transform;
        Remove(panel, "SettingsContent");
        Rect(panel, "SettingsContent", new Vector2(.5f, .5f), new Vector2(0, -30), new Vector2(1100, 650));
        Position(panel.Find("mu") as RectTransform, new Vector2(0, 85), new Vector2(360, 80));
        Position(panel.Find("ke") as RectTransform, new Vector2(0, -65), new Vector2(360, 80));
        RectTransform back = panel.Find("hui") as RectTransform;
        back.anchorMin = back.anchorMax = back.pivot = Vector2.one;
        back.anchoredPosition = new Vector2(-280, -140); back.sizeDelta = new Vector2(220, 72); back.SetAsLastSibling();
        foreach (Text text in root.GetComponentsInChildren<Text>(true)) text.color = Ink;
    }

    static void ClearPage(GameObject root, string returnName)
    {
        Image image = root.GetComponent<Image>();
        if (image != null) { image.sprite = null; image.color = Color.clear; image.raycastTarget = false; }
        Transform back = root.transform.Find(returnName);
        if (back != null) back.gameObject.SetActive(false);
    }

    static void UpgradeMusic(GameObject root)
    {
        ClearPage(root, "fan");
        for (int i = 1; i <= 2; i++)
        {
            float y = i == 1 ? 120 : -100;
            Toggle toggle = root.transform.Find("tt" + i).GetComponent<Toggle>();
            Position((RectTransform)toggle.transform, new Vector2(-310, y), new Vector2(360, 64));
            Text text = toggle.GetComponentInChildren<Text>(true);
            text.text = i == 1 ? "背景音乐" : "环境音效"; text.fontSize = 36; text.color = Ink;
            Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(72, 0);
            RectTransform check = toggle.transform.Find("Background") as RectTransform;
            if (check != null) { check.anchoredPosition = new Vector2(28, -32); check.sizeDelta = new Vector2(48, 48); }
            Position(root.transform.Find("s" + i) as RectTransform, new Vector2(205, y), new Vector2(550, 48));
        }
    }

    static void UpgradeKeys(GameObject root)
    {
        ClearPage(root, "out");
        foreach (Transform row in root.transform)
        {
            Button button = row.GetComponentInChildren<Button>(true);
            if (button == null || button.name == "out") continue;
            Position((RectTransform)button.transform, new Vector2(340, 0), new Vector2(220, 70));
            string caption = button.name == "left" ? "向左" : button.name == "right" ? "向右"
                : button.name == "up" ? "向上" : button.name == "down" ? "向下" : button.name == "ff" ? "跳跃"
                : button.name == "interact" ? "交互" : button.name == "sprint" ? "疾跑" : "形态切换";
            Image image = button.GetComponent<Image>(); if (image != null) image.color = new Color(.82f, .73f, .59f);
            foreach (Text text in row.GetComponentsInChildren<Text>(true))
            {
                text.color = Ink; text.fontSize = 30; text.resizeTextForBestFit = false;
                if (text.transform.IsChildOf(button.transform)) text.text = "修改";
                else if (text.name == "Text (Legacy)")
                { text.text = caption; Position(text.rectTransform, new Vector2(-350, 0), new Vector2(250, 70)); text.alignment = TextAnchor.MiddleLeft; }
                else
                { Position(text.rectTransform, Vector2.zero, new Vector2(350, 70)); text.alignment = TextAnchor.MiddleCenter; }
            }
        }
    }

    static void ResourceBar(Transform parent, string name, string fillName, Color color)
    {
        RectTransform rect = Rect(parent, name, new Vector2(.5f, .5f), Vector2.zero, new Vector2(14, 96));
        Image background = rect.gameObject.AddComponent<Image>(); background.color = new Color(.08f, .065f, .11f, .8f); background.raycastTarget = false;
        Fill(rect, fillName, color, true); rect.gameObject.SetActive(false);
    }
    static void Fill(RectTransform parent, string name, Color color, bool vertical)
    {
        RectTransform rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero); Stretch(rect);
        rect.offsetMin = new Vector2(2, 2); rect.offsetMax = new Vector2(-2, -2);
        Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
        image.sprite = GameUIAssets.Shared.bar;
        image.type = vertical ? Image.Type.Filled : Image.Type.Simple;
        image.fillMethod = Image.FillMethod.Vertical; image.fillOrigin = 0;
        if (!vertical) { rect.offsetMin = new Vector2(0, 2); rect.offsetMax = new Vector2(0, -2); }
    }
    static void Paper(RectTransform rect, Sprite sprite)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.type = Image.Type.Sliced; image.raycastTarget = false;
        EnvironmentEditLayout.ApplyLabelShadow(image);
    }
    static Text Label(RectTransform parent, string name, string value, int size, Color color)
    {
        RectTransform rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero); Stretch(rect);
        Text text = rect.gameObject.AddComponent<Text>(); text.font = GameUIAssets.Shared.font; text.text = value;
        text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
    }
    static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject item = new GameObject(name, typeof(RectTransform)) { layer = 5 }; item.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)item.transform; rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static void Position(RectTransform rect, Vector2 point, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = point; rect.sizeDelta = size; }
    static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    static void Remove(Transform parent, string name)
    { Transform item = parent.Find(name); if (item != null) UnityEngine.Object.DestroyImmediate(item.gameObject); }

    static Sprite BarSprite()
    {
        const string path = "Assets/Resources/UIBarFill.png";
        if (!File.Exists(path))
        {
            Texture2D pixels = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            pixels.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            TextureImporterSettings settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.mipmapEnabled = false; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
