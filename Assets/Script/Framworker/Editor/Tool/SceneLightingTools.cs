#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>制作入口：只在明确执行菜单时创建资源或修改选中精灵。</summary>
public static class SceneLightingTools
{
    public const string Art = "Assets/MyObjAssets/gameObject/Art/Lighting/";
    public const string Prefabs = "Assets/MyObjAssets/gameObject/Pf/Lighting/";
    public const string SpriteMaterial = Art + "AtmosphereSprite.mat";
    public const string SettingsPrefab = Prefabs + "SceneLighting.prefab";
    public const string LampPrefab = Prefabs + "EnvironmentLamp.prefab";
    public const string CoolLampPrefab = Prefabs + "EnvironmentLampCool.prefab";
    public const string SpotLampPrefab = Prefabs + "EnvironmentSpotLamp.prefab";

    [MenuItem("自定义工具/场景光照/创建缺少的光照预设")]
    public static void CreateAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后制作光照预设。");
        System.IO.Directory.CreateDirectory(Prefabs);
        AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Art + "AtmosphereSprite.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("受光 Shader 未正确编译。");
        if (AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterial) == null)
            AssetDatabase.CreateAsset(new Material(shader), SpriteMaterial);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefab) == null)
        {
            var root = new GameObject("全场景光照设置");
            root.AddComponent<SceneLightingSettings>();
            PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefab);
            UnityEngine.Object.DestroyImmediate(root);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefab) == null)
        {
            var root = new GameObject("环境灯_暖色");
            var source = new GameObject("照明_调整颜色强度范围");
            source.transform.SetParent(root.transform, false);
            source.transform.localPosition = new Vector3(0f, 0f, -3f);
            Light light = source.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .77f, .53f);
            light.intensity = 1.7f;
            light.range = 9f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = LightShadows.None;
            light.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            var glowShader = AssetDatabase.LoadAssetAtPath<Shader>(ScenePropLightPrefabBuilder.EffectFolder + "ScenePropLightPoint.shader");
            string glowPath = Art + "LampHalo.mat";
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
            if (glow == null)
            {
                glow = new Material(glowShader);
                glow.SetColor("_Color", new Color(1f, .64f, .35f, .10f));
                AssetDatabase.CreateAsset(glow, glowPath);
            }
            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "柔光晕_可关闭";
            UnityEngine.Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(root.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0f, -.1f);
            halo.transform.localScale = new Vector3(1.8f, 1.8f, 1f);
            var renderer = halo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = glow;
            renderer.sortingOrder = -4;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, LampPrefab);
            UnityEngine.Object.DestroyImmediate(root);
        }
        CreateLampVariant(CoolLampPrefab, "环境灯_冷色", new Color(.66f,.83f,1f), false);
        CreateLampVariant(SpotLampPrefab, "环境灯_定向光", new Color(1f,.82f,.64f), true);
        AssetDatabase.SaveAssets();
    }

    static void CreateLampVariant(string path, string name, Color color, bool spot)
    {
        // 已有资源保留人工参数，菜单只补缺少的制作模板。
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefab));
        root.name = name;
        var light = root.GetComponentInChildren<Light>();
        light.color = color;
        if (spot)
        {
            light.type = LightType.Spot;
            light.spotAngle = 80f;
            light.innerSpotAngle = 35f;
            light.range = 12f;
            light.intensity = 2.2f;
            light.transform.localRotation = Quaternion.Euler(32f, 0f, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light.transform);
        }
        else
        {
            string pathHalo = Art + "LampHaloCool.mat";
            var halo = AssetDatabase.LoadAssetAtPath<Material>(pathHalo);
            if (halo == null)
            {
                halo = new Material(AssetDatabase.LoadAssetAtPath<Material>(Art + "LampHalo.mat"));
                halo.SetColor("_Color", new Color(color.r, color.g, color.b, .08f));
                AssetDatabase.CreateAsset(halo, pathHalo);
            }
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            renderer.sharedMaterial = halo;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
    }

    [MenuItem("自定义工具/场景光照/选中物体及子物体使用受光材质")]
    static void ApplySelected()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterial);
        if (material == null) throw new InvalidOperationException("请先创建光照预设。");
        foreach (GameObject root in Selection.gameObjects)
            foreach (SpriteRenderer sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                // 特殊 Shader 与水保持原样，只处理默认精灵和已有道具提亮材质。
                if (!CanReceive(sprite)) continue;
                Undo.RecordObject(sprite, "使用场景受光材质");
                sprite.sharedMaterial = material;
                PrefabUtility.RecordPrefabInstancePropertyModifications(sprite);
                EditorUtility.SetDirty(sprite);
            }
    }

    public static bool CanReceive(SpriteRenderer sprite)
    {
        if (sprite == null || sprite.sharedMaterial == null || sprite.sharedMaterial.shader == null) return false;
        if (sprite.gameObject.layer == LayerMask.NameToLayer("UI") || sprite.GetComponentInParent<Canvas>() != null
            || sprite.GetComponentInParent<WaterRegion>() != null) return false;
        string shader = sprite.sharedMaterial.shader.name;
        return shader == "Sprites/Default" || shader == "GameEmergence/ScenePropBrightness";
    }
}

[CustomEditor(typeof(SceneLightingSettings))]
public sealed class SceneLightingSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lightingEnabled"), new GUIContent("启用环境光照"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ambientColor"), new GUIContent("暗处色调"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ambientIntensity"), new GUIContent("基础照度"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("localLightIntensity"), new GUIContent("局部灯光总倍率"));
        bool changed = serializedObject.ApplyModifiedProperties();
        var settings = (SceneLightingSettings)target;
        if (!settings.ValidateConfiguration(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.HelpBox("每场景只放一份。灯光预设可自由复制；子物体 Light 控制颜色、范围和强度。编辑器打开 Scene Lighting 可预览。受光只作用于 AtmosphereSprite 材质，水、UI 和远景保持原表现。", MessageType.Info);
        if (changed)
        {
            settings.Apply();
            SceneView.RepaintAll();
        }
    }
}
#endif
