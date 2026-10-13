#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>定向升级现有两种水域预设；重复执行保留人工波形与颜色。</summary>
public static class WaterSurfaceAssets
{
    public const string Folder = "Assets/MyObjAssets/gameObject/Art/WaterEffects/";
    public static readonly string[] Prefabs = {
        "Assets/MyObjAssets/gameObject/Pf/SceneObj/WaterPool.prefab",
        "Assets/MyObjAssets/gameObject/Pf/SceneObj/水体.prefab"
    };

    [MenuItem("自定义工具/开发工具/环境/装配 P18 水体表现")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后装配水体。");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "WaterSurface.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("水体 shader 编译失败。");
        string materialPath = Folder + "WaterSurface.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        foreach (string path in Prefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var source = root.GetComponent<WaterRegion>();
                var box = root.GetComponent<BoxCollider2D>();
                var sprite = root.GetComponent<SpriteRenderer>();
                if (source == null || box == null || sprite == null) throw new InvalidOperationException(path + " 缺少原水域组件。");
                Transform child = root.transform.Find("WaterSurface");
                if (child == null) { child = new GameObject("WaterSurface").transform; child.SetParent(root.transform, false); }
                child.gameObject.layer = root.layer;
                var mesh = child.GetComponent<MeshFilter>();
                if (mesh == null) mesh = child.gameObject.AddComponent<MeshFilter>();
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = child.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                var effect = root.GetComponent<WaterSurfaceVisual>();
                if (effect == null) effect = root.AddComponent<WaterSurfaceVisual>();
                var fields = new SerializedObject(effect);
                fields.FindProperty("source").objectReferenceValue = source;
                fields.FindProperty("range").objectReferenceValue = box;
                fields.FindProperty("bodySprite").objectReferenceValue = sprite;
                fields.FindProperty("surfaceMesh").objectReferenceValue = mesh;
                fields.FindProperty("surfaceRenderer").objectReferenceValue = renderer;
                fields.ApplyModifiedPropertiesWithoutUndo();
                if (!effect.ValidateConfiguration(out string error)) throw new InvalidOperationException(path + ": " + error);
                // 临时动态网格不保存为资源；每个实例自行生成并释放。
                mesh.sharedMesh = null;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    public static void BuildForBatch()
    {
        try { Build(); WaterSurfaceChecks.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
