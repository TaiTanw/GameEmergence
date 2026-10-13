#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>只装配现有 SceneProp 光效，保留道具图片、触发范围、动画编号与预设身份。</summary>
public static class ScenePropLightPrefabBuilder
{
    public const string PrefabPath = "Assets/MyObjAssets/gameObject/Pf/prop/SceneProp 0.prefab";
    public const string EffectFolder = "Assets/MyObjAssets/gameObject/Art/PropEffects/";

    [MenuItem("自定义工具/开发工具/道具/装配 SceneProp 基础光效")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后装配通关道具。");
        Material spriteMaterial = MaterialAt("ScenePropBrightness");
        Material pointMaterial = MaterialAt("ScenePropLightPoint");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SceneProp prop = root.GetComponent<SceneProp>();
            SpriteRenderer sprite = root.GetComponent<SpriteRenderer>();
            Collider2D range = root.GetComponent<Collider2D>();
            if (prop == null || sprite == null || sprite.sprite == null || range == null || !range.isTrigger)
                throw new InvalidOperationException("SceneProp 预设缺少道具、精灵或触发范围，请先修正制作配置。");
            sprite.sharedMaterial = spriteMaterial;
            Transform child = root.transform.Find("LightPoints");
            if (child == null)
            {
                child = new GameObject("LightPoints").transform;
                child.SetParent(root.transform, false);
            }
            child.gameObject.layer = root.layer;
            ParticleSystem particles = child.GetComponent<ParticleSystem>();
            if (particles == null) particles = child.gameObject.AddComponent<ParticleSystem>();
            ConfigurePoints(particles, sprite, pointMaterial);
            ScenePropLightEffect effect = root.GetComponent<ScenePropLightEffect>();
            if (effect == null) effect = root.AddComponent<ScenePropLightEffect>();
            effect.enabled = false;
            SetReference(effect, "targetSprite", sprite);
            SetReference(effect, "lightPoints", particles);
            SetReference(prop, "explorationLightEffect", effect);
            if (!effect.ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Material MaterialAt(string name)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(EffectFolder + name + ".shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("光效着色器无效：" + name);
        string path = EffectFolder + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void ConfigurePoints(ParticleSystem particles, SpriteRenderer sprite, Material material)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.duration = 5f; main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(.035f, .08f);
        main.startColor = new Color(1f, .95f, .55f, 1f);
        main.maxParticles = 16;
        main.cullingMode = ParticleSystemCullingMode.Pause;
        var emission = particles.emission; emission.enabled = true; emission.rateOverTime = 4f;
        var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        Bounds bounds = sprite.sprite.bounds;
        shape.scale = new Vector3(Mathf.Max(.4f, bounds.size.x * 1.6f), Mathf.Max(.4f, bounds.size.y * 1.4f), .02f);
        var velocity = particles.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-.035f, .035f);
        velocity.y = new ParticleSystem.MinMaxCurve(.025f, .075f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var noise = particles.noise; noise.enabled = true; noise.strength = .035f;
        noise.frequency = .8f; noise.scrollSpeed = .08f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Low;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.75f, .3f), new GradientAlphaKey(.55f, .65f), new GradientAlphaKey(0f, 1f) });
        fade.color = new ParticleSystem.MinMaxGradient(gradient);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerID = sprite.sortingLayerID; renderer.sortingOrder = sprite.sortingOrder + 1;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
    }

    static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var data = new SerializedObject(target);
        data.FindProperty(name).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void BuildForBatch()
    {
        try { Build(); ScenePropLightEffectChecks.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
