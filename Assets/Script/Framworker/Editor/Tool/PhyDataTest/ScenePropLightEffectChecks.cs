#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>验证真实装配预设的实例隔离、关闭恢复、粒子清理及 GPU 输出；不保存场景。</summary>
public static class ScenePropLightEffectChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Output = ".utmp/ScenePropLightValidation/";
    static readonly List<string> passed = new List<string>();
    static readonly int Brightness = Shader.PropertyToID("_Brightness");

    public static void Run()
    {
        passed.Clear(); Directory.CreateDirectory(Output);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ScenePropLightPrefabBuilder.PrefabPath);
        if (asset == null) throw new InvalidOperationException("缺少 SceneProp 制作预设。");
        GameObject first = Object.Instantiate(asset), second = Object.Instantiate(asset);
        try
        {
            ScenePropLightEffect effect = first.GetComponent<ScenePropLightEffect>();
            SpriteRenderer sprite = first.GetComponent<SpriteRenderer>();
            ParticleSystem points = first.GetComponentInChildren<ParticleSystem>();
            Check(first.GetComponent<SceneProp>() != null && first.GetComponent<Collider2D>().isTrigger,
                "预设保留原通关宿主与交互触发框");
            Check(effect != null && !effect.enabled && effect.ValidateConfiguration(out _),
                "预设光效关联完整，默认关闭，由阶段接口启用");
            Check(new SerializedObject(first.GetComponent<SceneProp>()).FindProperty("explorationLightEffect").objectReferenceValue == effect,
                "通关阶段接口已关联本实例光效组件");
            Check(!points.main.playOnAwake && points.main.maxParticles <= 16 && !points.collision.enabled,
                "光点不自行启动，数量有限且不参与碰撞");
            Material material = sprite.sharedMaterial;
            Check(material == second.GetComponent<SpriteRenderer>().sharedMaterial, "复制道具共享制作材质");
            float materialBrightness = material.GetFloat(Brightness);
            Color baseColor = sprite.color;
            var original = new MaterialPropertyBlock(); original.SetFloat(Brightness, 1.07f);
            sprite.SetPropertyBlock(original);
            var other = new MaterialPropertyBlock(); second.GetComponent<SpriteRenderer>().GetPropertyBlock(other);
            float otherBrightness = other.GetFloat(Brightness);
            Invoke(effect, "OnEnable");
            var lit = new MaterialPropertyBlock(); sprite.GetPropertyBlock(lit);
            Check(lit.GetFloat(Brightness) > 1.07f && sprite.color == baseColor, "启用实际提亮本体且保留原精灵颜色");
            second.GetComponent<SpriteRenderer>().GetPropertyBlock(other);
            Check(other.GetFloat(Brightness) == otherBrightness && material.GetFloat(Brightness) == materialBrightness,
                "本实例提亮不修改共享材质或另一件道具");
            points.Emit(6);
            Check(points.particleCount > 0, "启用后的光点可实际生成");
            Invoke(effect, "OnDisable");
            sprite.GetPropertyBlock(lit);
            Check(points.particleCount == 0 && !points.isPlaying, "停用立即停止并清除残留光点");
            Check(Mathf.Approximately(lit.GetFloat(Brightness), 1.07f), "停用精确恢复启用前的亮度属性");
            Invoke(effect, "OnEnable"); Invoke(effect, "OnDisable"); sprite.GetPropertyBlock(lit);
            Check(Mathf.Approximately(lit.GetFloat(Brightness), 1.07f), "反复启停不会累积提亮倍率");
            var config = new SerializedObject(effect);
            config.FindProperty("brightness").floatValue = float.NaN; config.ApplyModifiedPropertiesWithoutUndo();
            Check(!effect.ValidateConfiguration(out _), "非有限亮度明确拒绝");
            Material unsupported = new Material(Shader.Find("Sprites/Default"));
            sprite.sharedMaterial = unsupported;
            config.FindProperty("brightness").floatValue = 1.35f; config.ApplyModifiedPropertiesWithoutUndo();
            try { Check(!effect.ValidateConfiguration(out _), "不支持提亮的材质明确拒绝"); }
            finally { sprite.sharedMaterial = material; Object.DestroyImmediate(unsupported); }
        }
        finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        RenderPreview(asset);
        File.WriteAllLines(Output + "checks.txt", passed);
        Debug.Log("Scene prop light: " + passed.Count + " checks passed.");
    }

    static void RenderPreview(GameObject asset)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            GameObject off = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            GameObject on = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            float width = off.GetComponent<SpriteRenderer>().bounds.size.x;
            float separation = Mathf.Max(1.2f, width * 1.8f);
            off.transform.position = Vector3.left * separation / 2f;
            on.transform.position = Vector3.right * separation / 2f;
            ScenePropLightEffect effect = on.GetComponent<ScenePropLightEffect>();
            Invoke(effect, "OnEnable");
            var cameraRoot = new GameObject("light preview camera"); SceneManager.MoveGameObjectToScene(cameraRoot, preview);
            Camera camera = cameraRoot.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -10);
            camera.scene = preview;
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(.8f, separation * .6f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .065f, .09f, 1f);
            target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            Color[] pixels = image.GetPixels();
            float difference = 0f;
            for (int y = 0; y < 540; y++)
                for (int x = 0; x < 480; x++)
                {
                    Color left = pixels[y * 960 + x], right = pixels[y * 960 + 959 - x];
                    difference += right.r + right.g + right.b - left.r - left.g - left.b;
                }
            File.WriteAllBytes(Output + "brightness.png", image.EncodeToPNG());
            Debug.Log("Scene prop GPU brightness difference: " + difference);
            Check(difference > 1f, "GPU 实际输出中启用侧比原亮度侧更亮");
            ParticleSystem points = on.GetComponentInChildren<ParticleSystem>();
            points.useAutoRandomSeed = false; points.randomSeed = 17;
            points.Simulate(2f, true, true, false);
            camera.Render(); image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "preview.png", image.EncodeToPNG());
            Check(points.particleCount > 0, "真实粒子模拟产生悬浮光点预览");
            Check(!ShaderUtil.ShaderHasError(off.GetComponent<SpriteRenderer>().sharedMaterial.shader)
                && !ShaderUtil.ShaderHasError(points.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader),
                "精灵及光点着色器实际渲染后均无编译错误");
            Invoke(effect, "OnDisable");
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (image != null) Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static void Invoke(object target, string method) => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add("PASS " + message);
    }
}
#endif
