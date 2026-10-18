#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>用实际 GPU 绘制检查受光、透明、原色恢复与原生灯光，不保存测试场景。</summary>
public static class SceneLightingChecks
{
    const string Output = ".utmp/P24Lighting/";
    static readonly List<string> results = new List<string>();

    public static void RunForBatch()
    {
        Directory.CreateDirectory(Output);
        try
        {
            SceneLightingTools.CreateAssets();
            Run();
            File.WriteAllLines(Output + "gpu-checks.txt", results);
            Debug.Log("P24 lighting: " + results.Count + " GPU/lifecycle checks passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllLines(Output + "gpu-checks.txt", results);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Run()
    {
        results.Clear();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new GameObject("光照验证");
        Camera camera = new GameObject("验证相机").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true; camera.orthographicSize = 3f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.renderingPath = RenderingPath.Forward;
        RenderTexture rt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(320, 180, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        SpriteRenderer sprite = root.AddComponent<SpriteRenderer>();
        sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
        sprite.color = new Color(.55f, .55f, .55f, 1f);
        sprite.transform.localScale = new Vector3(3f, 3f, 1f);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        Material unlit = new Material(Shader.Find("Sprites/Default"));
        GameObject settingRoot = new GameObject("本场景设置");
        SceneLightingSettings settings = settingRoot.AddComponent<SceneLightingSettings>();
        var config = new SerializedObject(settings);
        GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SceneLightingTools.LampPrefab));
        lamp.transform.Find("柔光晕_可关闭").gameObject.SetActive(false);
        Light light = lamp.GetComponentInChildren<Light>();
        light.color = Color.white; light.intensity = 2f; light.range = 9f;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Check(settings.ValidateConfiguration(out _), "场景设置默认参数有效");
            sprite.sharedMaterial = material;
            settings.enabled = false;
            Color original = Sample(camera, image, rt);
            sprite.sharedMaterial = unlit;
            Color baseline = Sample(camera, image, rt);
            Check(Difference(original, baseline) < .025f, "无光照设置时受光材质与默认精灵原色一致");
            sprite.sharedMaterial = material;
            settings.enabled = true;
            config.FindProperty("ambientColor").colorValue = Color.white;
            config.FindProperty("ambientIntensity").floatValue = .18f;
            config.ApplyModifiedPropertiesWithoutUndo(); settings.Apply();
            light.enabled = false;
            Color dark = Sample(camera, image, rt);
            Check(dark.grayscale < original.grayscale * .8f && dark.grayscale > .05f, "基础照度实际压暗但保留暗处可见性");
            File.WriteAllBytes(Output + "gpu-ambient.png", image.EncodeToPNG());
            light.enabled = true;
            Color lit = Sample(camera, image, rt);
            Debug.Log("P24 GPU original=" + original + " dark=" + dark + " lit=" + lit);
            Check(lit.grayscale > dark.grayscale + .08f, "原生点光实际照亮精灵表面");
            File.WriteAllBytes(Output + "gpu-lit.png", image.EncodeToPNG());
            lamp.transform.position = new Vector3(25f, 0, 0);
            Check(Difference(Sample(camera, image, rt), dark) < .025f, "移走灯源后表面恢复基础照度");
            lamp.transform.position = Vector3.zero;
            light.color = new Color(1f, .25f, .08f);
            Color warm = Sample(camera, image, rt);
            Check(warm.r > warm.b + .08f, "灯光颜色改变表面受光颜色");
            light.type = LightType.Spot; light.spotAngle = 80f; light.innerSpotAngle = 35f;
            light.transform.localRotation = Quaternion.identity;
            Check(Sample(camera, image, rt).grayscale > dark.grayscale + .06f, "原生聚光灯照亮锥内精灵");
            light.transform.localRotation = Quaternion.Euler(0,90,0);
            Check(Difference(Sample(camera, image, rt), dark) < .025f, "旋转聚光灯后锥外不受光");
            light.transform.localRotation = Quaternion.identity; light.type = LightType.Point;
            sprite.color = new Color(.55f,.55f,.55f,.35f);
            Color transparent = Sample(camera, image, rt);
            Check(transparent.grayscale < warm.grayscale * .8f, "受光保留精神体／环境形态透明度");
            config.FindProperty("lightingEnabled").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo(); settings.Apply();
            Color transparentOff = Sample(camera, image, rt);
            sprite.sharedMaterial = unlit;
            Check(Difference(transparentOff, Sample(camera, image, rt)) < .025f, "关闭设置后透明精灵也恢复原色");
            sprite.sharedMaterial = material;
            sprite.flipX = true;
            config.FindProperty("lightingEnabled").boolValue = true;
            config.ApplyModifiedPropertiesWithoutUndo(); settings.Apply();
            Check(Difference(Sample(camera, image, rt), transparent) < .025f, "精灵翻转不改变受光及透明度");
            Shader.SetGlobalFloat("_SceneLightingTransition", 1f);
            Check(Difference(Sample(camera, image, rt), transparentOff) < .025f, "全黑过场前景恢复原色，不依赖即将卸载的灯光");
            light.enabled = false;
            Check(Difference(Sample(camera, image, rt), transparentOff) < .025f, "转场全黑时关闭旧灯不改变前景亮度");
            Shader.SetGlobalFloat("_SceneLightingTransition", 0f);
            light.enabled = true;
            Check(Difference(Sample(camera, image, rt), transparent) < .025f, "揭幕后恢复场景受光");
            config.FindProperty("ambientIntensity").floatValue = float.NaN;
            config.ApplyModifiedPropertiesWithoutUndo();
            Check(!settings.ValidateConfiguration(out _), "非法基础照度明确拒绝");
            config.FindProperty("ambientIntensity").floatValue = .18f;
            config.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(settingRoot);
            Check(Shader.GetGlobalFloat("_SceneLightingEnabled") == 0f, "退出场景设置清理全局受光开关");
            Check(Difference(Sample(camera, image, rt), transparentOff) < .025f, "清理后无场景遗留染色");
            Check(!ShaderUtil.ShaderHasError(material.shader), "真实绘制后受光 Shader 无编译错误");
            Check(light.renderMode == LightRenderMode.ForcePixel && light.shadows == LightShadows.None, "灯光不随质量等级降为顶点照明，首版无投影开销");
            Check(lamp.GetComponentsInChildren<Collider>(true).Length == 0 && lamp.GetComponentsInChildren<Collider2D>(true).Length == 0, "灯光预设无物理组件");
            Directory.CreateDirectory(Output);
            File.WriteAllLines(Output + "gpu-checks.txt", results);
            Debug.Log("P24 lighting: " + results.Count + " GPU/lifecycle checks passed.");
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Shader.SetGlobalFloat("_SceneLightingTransition", 0f);
            Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject);
            if (settingRoot != null) Object.DestroyImmediate(settingRoot);
            Object.DestroyImmediate(lamp); Object.DestroyImmediate(unlit);
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        }
    }

    static Color Sample(Camera camera, Texture2D image, RenderTexture rt)
    {
        camera.Render(); RenderTexture.active = rt;
        image.ReadPixels(new Rect(0,0,320,180),0,0); image.Apply();
        return image.GetPixel(160,90);
    }
    static float Difference(Color a, Color b) => Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        results.Add("PASS " + message);
    }
}
#endif
