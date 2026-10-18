#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P26 显式制作工具；只装配背景和无碰撞装饰，保留全部原玩法组件。</summary>
public static class P26DepthTools
{
    public const string Art = "Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/P26/";
    public const string Folder = "Assets/MyObjAssets/Scenes/Designed/";
    public const string RootName = "10_P26背景层次与边缘前景";
    public static readonly string[] Scenes = { "L01_PropertyLabyrinth", "L02_SequentialGates", "L03_ElectricTraverse", "L04_FoldedWells", "L05_DepartureWorks" };
    public static readonly float[] Heights = { 76, 92, 100, 116, 45 };
    static readonly string[] Themes = { "park", "garage", "service", "hall", "service" };

    public static IEnumerable<T> All<T>(Scene scene) where T : Component
        => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

    public static void CheckEditState()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后制作背景层次。");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景的手工改动。");
    }

    [MenuItem("自定义工具/关卡/P26/导入拆层图与透明构件")]
    public static void ImportArt()
    {
        foreach (string path in Directory.GetFiles(Art, "*-far-v2.png")) Import(path.Replace('\\', '/'), false);
        Import(Art + "depth-silhouettes-v1.png", true);
    }

    static void Import(string path, bool atlas)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path))) throw new InvalidOperationException("无法读取：" + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = atlas ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100; importer.mipmapEnabled = false;
            importer.filterMode = atlas ? FilterMode.Point : FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048; importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = atlas;
            importer.textureCompression = atlas ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            if (atlas)
            {
                Color32[] pixels = source.GetPixels32(); var slices = new SpriteMetaData[4];
                string[] names = { "P26_Park", "P26_Garage", "P26_Service", "P26_Hall" };
                var report = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    int left = (i % 2) * source.width / 2, right = (i % 2 + 1) * source.width / 2;
                    int bottom = source.height - (i / 2 + 1) * source.height / 2, top = source.height - (i / 2) * source.height / 2;
                    int x0 = right, x1 = left - 1, y0 = top, y1 = bottom - 1, clear = 0;
                    for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
                    {
                        byte alpha = pixels[y * source.width + x].a;
                        if (alpha <= 8) { clear++; continue; }
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                    }
                    if (x1 < x0) throw new InvalidOperationException(names[i] + " 图格为空。");
                    float clearRatio = (float)clear / ((right - left) * (top - bottom));
                    if (clearRatio < .35f) throw new InvalidOperationException(names[i] + " 缺少足够透明区域，请复查源图 alpha。");
                    slices[i] = new SpriteMetaData { name = names[i], rect = new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), alignment = 0, pivot = Vector2.one * .5f };
                    report.Add(names[i] + " clear alpha ratio=" + clearRatio.ToString("F3") + " rect=" + slices[i].rect);
                }
                var factory = new SpriteDataProviderFactories(); factory.Init();
                var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
                var ids = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
                var rects = slices.Select(s => new SpriteRect { name = s.name, rect = s.rect, pivot = s.pivot,
                    alignment = SpriteAlignment.Center, spriteID = ids.TryGetValue(s.name, out GUID id) ? id : GUID.Generate() }).ToArray();
                provider.SetSpriteRects(rects);
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                Directory.CreateDirectory(".utmp/P26Depth"); File.WriteAllLines(".utmp/P26Depth/alpha-audit.txt", report);
            }
            importer.SaveAndReimport();
        }
        finally { Object.DestroyImmediate(source); }
    }

    [MenuItem("自定义工具/关卡/P26/制作检修站分层样板")]
    public static void ApplyPrototype() => ApplyRange(new[] { 4 });

    [MenuItem("自定义工具/关卡/P26/应用五个普通关")]
    public static void ApplyAll() => ApplyRange(Enumerable.Range(0, Scenes.Length));

    static void ApplyRange(IEnumerable<int> indices)
    {
        CheckEditState(); ImportArt();
        string previous = SceneManager.GetActiveScene().path;
        var records = new List<string>();
        try
        {
            foreach (int index in indices)
            {
                string path = Folder + Scenes[index] + ".unity";
                string backup = ".utmp/P26Baseline/" + path;
                Directory.CreateDirectory(Path.GetDirectoryName(backup)); if (!File.Exists(backup)) File.Copy(path, backup);
                Scene scene = EditorSceneManager.OpenScene(path);
                var before = GameplaySnapshot(scene);
                Apply(scene, index);
                var after = GameplaySnapshot(scene);
                if (!before.OrderBy(p => p.Key).SequenceEqual(after.OrderBy(p => p.Key)))
                    throw new InvalidOperationException(scene.name + " 原玩法组件或位置被改变，拒绝保存。");
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                records.Add(scene.name + ": unchanged gameplay components=" + before.Count + ", parallax layers=2, mid sprites="
                    + scene.GetRootGameObjects().Single(g => g.name == RootName).transform.Find("透明中景").childCount);
            }
            Directory.CreateDirectory(".utmp/P26Depth"); File.WriteAllLines(".utmp/P26Depth/authoring-audit.txt", records);
            Debug.Log("P26_DEPTH_SAVED: " + string.Join(" | ", records));
        }
        finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
    }

    public static Dictionary<int, string> GameplaySnapshot(Scene scene)
    {
        var records = new Dictionary<int, string>();
        foreach (Component item in All<Component>(scene).Where(c => c is Collider2D || c is Rigidbody2D || c is BasicPhysicalObject
            || c is BasicEntity || c is SceneLevelSetup || c is MapCell || c is Signboard || c is SceneProp
            || c is MechanismPressurePlate || c is Taijie || c is MainCamera || c is Camera))
            records.Add(item.GetInstanceID(), EditorJsonUtility.ToJson(item) + " @ " + item.transform.position.ToString("F6") + "/" + item.transform.lossyScale.ToString("F6"));
        return records;
    }

    static void Apply(Scene scene, int index)
    {
        var installed = scene.GetRootGameObjects().SingleOrDefault(g => g.name == RootName);
        if (installed != null) Object.DestroyImmediate(installed);
        SpriteRenderer far = All<SpriteRenderer>(scene).Where(s => s.enabled && s.sprite && s.sortingOrder <= -90 && s.bounds.size.x > 60)
            .OrderByDescending(s => s.bounds.size.x * s.bounds.size.y).First();
        var existingLayer = far.GetComponent<SceneParallaxLayer>();
        if (existingLayer) { existingLayer.RestoreAuthoredPosition(); Object.DestroyImmediate(existingLayer); }
        string farPath = Art + Themes[index] + "-far-v2.png";
        if (!File.Exists(farPath)) throw new FileNotFoundException("请先完成本主题远景拆层：" + farPath);
        far.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(farPath);
        far.drawMode = SpriteDrawMode.Simple;
        // 按实际视差后的取景跨度留余量，不再把一张远景强行铺满整个世界。
        float scale = Mathf.Max(64 / far.sprite.bounds.size.x, (Heights[index] * .5f + 24) / far.sprite.bounds.size.y);
        far.transform.localScale = new Vector3(scale, scale, 1);
        far.transform.position = new Vector3(40, Heights[index] / 2, 5);
        far.color = new Color(.53f, .57f, .66f, 1);
        far.sortingOrder = -100;
        Vector2 reference = new Vector2(40, Heights[index] / 2);
        var farLayer = far.gameObject.AddComponent<SceneParallaxLayer>();
        Configure(farLayer, reference, new Vector2(.65f, .5f));
        var farConfig = new SerializedObject(farLayer); farConfig.FindProperty("coveringSprite").objectReferenceValue = far;
        farConfig.ApplyModifiedPropertiesWithoutUndo();

        var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
        var mid = new GameObject("透明中景"); mid.transform.SetParent(root.transform, false);
        var foreground = new GameObject("静态边缘前景_无碰撞"); foreground.transform.SetParent(root.transform, false);
        Configure(mid.AddComponent<SceneParallaxLayer>(), reference, new Vector2(.25f, .18f));
        string theme = index == 0 ? "P26_Park" : index == 1 ? "P26_Garage" : index == 3 ? "P26_Hall" : "P26_Service";
        Sprite motif = AssetDatabase.LoadAllAssetsAtPath(Art + "depth-silhouettes-v1.png").OfType<Sprite>().Single(s => s.name == theme);
        int rows = Mathf.Max(2, Mathf.CeilToInt(Heights[index] / 22f));
        for (int row = 0; row < rows; row++)
        {
            for (int side = 0; side < 2; side++)
            {
                float x = (row % 2 == 0 ? 11 : 25) + side * 43;
                AddSprite(mid.transform, "中景轮廓_" + row + "_" + side, motif, new Vector2(x, 10 + row * 22),
                    11 + row % 2 * 3, new Color(.48f, .54f, .66f, .34f), -65, false);
                // 前景完整外框收进外墙厚度，不跨入 0..80 的玩家活动空间。
                var sr = AddSprite(foreground.transform, "外墙近处轮廓_" + row + "_" + side, motif,
                    new Vector2(side == 0 ? -.5f : 80.5f, 9 + row * 22), 4, new Color(.34f, .39f, .48f, .9f), 15, true);
                float width = sr.bounds.size.x;
                sr.transform.localScale *= .7f / width;
            }
        }
    }

    static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Vector2 center, float height, Color color, int order, bool lit)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = new Vector3(center.x, center.y, 0);
        go.transform.localScale = Vector3.one * (height / sprite.bounds.size.y);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.color = color; sr.sortingOrder = order;
        if (lit) sr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        return sr;
    }

    static void Configure(SceneParallaxLayer layer, Vector2 reference, Vector2 follow)
    {
        var so = new SerializedObject(layer); so.FindProperty("referenceCameraPosition").vector2Value = reference;
        so.FindProperty("followFactor").vector2Value = follow; so.ApplyModifiedPropertiesWithoutUndo();
    }
}

[CustomEditor(typeof(SceneParallaxLayer))]
public sealed class SceneParallaxLayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("referenceCameraPosition"), new GUIContent("制作基准镜头位置"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("followFactor"), new GUIContent("跟随镜头比例 X／Y"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("coveringSprite"), new GUIContent("边缘覆盖精灵（远景）"));
        serializedObject.ApplyModifiedProperties();
        var layer = (SceneParallaxLayer)target;
        if (!layer.ValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.HelpBox("0 表示固定在世界，1 表示固定在画面。远景通常比中景更跟随镜头。挂在静止场景物体上；关闭组件恢复制作位置。", MessageType.Info);
    }
}
#endif
