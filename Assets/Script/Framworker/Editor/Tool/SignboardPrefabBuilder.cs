#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只创建缺失的告示牌预设；已人工编辑的内容和布局不重建。</summary>
[InitializeOnLoad]
public static class SignboardPrefabBuilder
{
    public const string PrefabPath = "Assets/MyObjAssets/gameObject/Pf/Map/Signboard.prefab";
    const string Output = ".utmp/SignboardValidation/";

    static SignboardPrefabBuilder()
    {
        // 仅响应本轮明确创建的请求，利用现有编辑器生成资产，不切换用户场景或播放状态。
        if (File.Exists(Output + "create.request")) EditorApplication.update += CreateRequested;
    }

    static void CreateRequested()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= CreateRequested;
        if (!File.Exists(Output + "create.request")) return;
        try
        {
            Build(); RenderAndCheck();
            File.WriteAllText(Output + "result.txt", "PASS: 原生预设生成、引用与字体、范围几何、初始隐藏、黑底白字与实际渲染检查。\n" + PrefabPath);
            Debug.Log("P21 Signboard prefab: PASS");
        }
        catch (Exception error)
        {
            File.WriteAllText(Output + "result.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
        finally { File.Delete(Output + "create.request"); }
    }

    [MenuItem("自定义工具/开发工具/地图/创建告示牌预设")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后创建告示牌预设。");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
        Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/MyObjAssets/PlayDataA/FZYTK.TTF");
        if (square == null || font == null) throw new InvalidOperationException("告示牌需要现有方形精灵和中文字体。");
        Scene preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Signboard");
        SceneManager.MoveGameObjectToScene(root, preview);
        try
        {
            var range = root.AddComponent<BoxCollider2D>();
            range.isTrigger = true; range.size = new Vector2(4.5f, 3f); range.offset = new Vector2(0, 1f);
            var board = root.AddComponent<Signboard>();
            RectSprite(root.transform, square, "Post", new Vector2(0, .5f), new Vector2(.14f, 1f), new Color(.29f, .26f, .22f), 1);
            RectSprite(root.transform, square, "Board", new Vector2(0, 1f), new Vector2(1.3f, .8f), new Color(.22f, .23f, .26f), 2);
            for (int i = 0; i < 3; i++)
                RectSprite(root.transform, square, "Mark" + (i + 1), new Vector2(-.05f, 1.18f - i * .18f),
                    new Vector2(i == 1 ? .82f : .62f, .035f), new Color(.77f, .77f, .72f), 3);

            var panel = new GameObject("NoticePanel", typeof(RectTransform), typeof(Canvas), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.sizeDelta = new Vector2(600, 220);
            rect.localPosition = new Vector3(0, 3f, 0);
            rect.localScale = Vector3.one * .01f;
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = true; canvas.sortingOrder = 40;
            var background = panel.GetComponent<Image>();
            background.color = new Color(0, 0, 0, .65f); background.raycastTarget = false;
            var caption = new GameObject("NoticeText", typeof(RectTransform), typeof(Text));
            caption.transform.SetParent(panel.transform, false);
            var textRect = (RectTransform)caption.transform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28, 22); textRect.offsetMax = new Vector2(-28, -22);
            var text = caption.GetComponent<Text>();
            text.font = font; text.fontSize = 30; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = "在这里填写告示内容。\n\n文字、字号和面板大小都可以直接在预设中修改。";
            panel.SetActive(false);
            var config = new SerializedObject(board);
            config.FindProperty("proximityRange").objectReferenceValue = range;
            config.FindProperty("noticePanel").objectReferenceValue = panel;
            config.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void RectSprite(Transform parent, Sprite sprite, string name, Vector2 point, Vector2 size, Color color, int order)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false); child.transform.localPosition = point;
        child.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order;
    }

    static void RenderAndCheck()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null) throw new InvalidOperationException("告示牌预设未生成。");
        Scene preview = EditorSceneManager.NewPreviewScene();
        var root = Object.Instantiate(asset);
        SceneManager.MoveGameObjectToScene(root, preview);
        RenderTexture previous = RenderTexture.active;
        var render = new RenderTexture(960, 540, 24);
        var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        Camera camera = null;
        try
        {
            var config = new SerializedObject(root.GetComponent<Signboard>());
            var range = (BoxCollider2D)config.FindProperty("proximityRange").objectReferenceValue;
            var panel = (GameObject)config.FindProperty("noticePanel").objectReferenceValue;
            if (range == null || range.gameObject != root || !range.isTrigger || panel == null
                || !panel.transform.IsChildOf(root.transform) || panel.activeSelf)
                throw new InvalidOperationException("预设的范围、面板引用或初始隐藏无效。");
            var caption = panel.GetComponentInChildren<Text>(true);
            if (caption == null || caption.font == null || string.IsNullOrWhiteSpace(caption.text)
                || caption.color != Color.white || caption.raycastTarget || panel.GetComponent<Image>().raycastTarget)
                throw new InvalidOperationException("告示文字、中文字体或无点击遮挡配置无效。");
            Physics2D.SyncTransforms();
            if (!range.OverlapPoint(root.transform.TransformPoint(range.offset)) || range.OverlapPoint(new Vector2(1000, 1000)))
                throw new InvalidOperationException("触发器范围几何无效。");
            panel.SetActive(true);
            var cameraRoot = new GameObject("SignboardPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraRoot, preview);
            camera = cameraRoot.GetComponent<Camera>();
            camera.scene = preview; camera.orthographic = true; camera.orthographicSize = 2.5f;
            camera.transform.position = new Vector3(0, 2, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .10f, .17f);
            camera.targetTexture = render;
            caption.font.RequestCharactersInTexture(caption.text, caption.fontSize, caption.fontStyle);
            Canvas.ForceUpdateCanvases(); camera.Render(); camera.Render();
            if (caption.cachedTextGenerator.vertexCount == 0) throw new InvalidOperationException("告示文字未生成渲染几何。");
            RenderTexture.active = render;
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            Directory.CreateDirectory(Output);
            File.WriteAllBytes(Output + "preview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (camera != null) camera.targetTexture = null;
            Object.DestroyImmediate(render); Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
