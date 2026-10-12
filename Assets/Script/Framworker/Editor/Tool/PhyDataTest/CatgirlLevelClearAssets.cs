#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>导入已确认的人工图；原 PNG 不重绘，通过 PPU 与每帧枢轴对齐实体待机。</summary>
public static class CatgirlLevelClearAssets
{
    public const string ImagePath = "Assets/MyObjAssets/gameObject/Art/sequence frame/catgirl-level-clear-0.png";
    const string IdlePath = "Assets/MyObjAssets/gameObject/Art/sequence frame/catgirl-idle-side-blink-walk-scale-4x2.png";
    const string ControllerPath = "Assets/MyObjAssets/gameObject/Animation/player.controller";
    const string ClipRoot = "Assets/MyObjAssets/gameObject/Animation/";
    const int Width = 384, Height = 512;
    const float Interval = .16f;

    [MenuItem("自定义工具/开发工具/角色/导入实体通关动画 0")]
    public static void ImportAndValidate()
    {
        var idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<Sprite>().Single(s => s.name.EndsWith("_0", StringComparison.Ordinal));
        Texture2D reference = ReadPng(IdlePath), image = ReadPng(ImagePath);
        try
        {
            Require(image.width == Width * 4 && image.height == Height * 2, "通关图必须为 1536×1024、4×2 格。");
            Vector2 referenceFeet = Measure(reference, PixelRect(idle.rect), out int referenceHeight);
            Vector2 feetWorld = (referenceFeet - idle.pivot) / idle.pixelsPerUnit;
            Measure(image, Cell(0), out int characterHeight);
            float ppu = idle.pixelsPerUnit * characterHeight / referenceHeight;
            ImportSlices(image, ppu, feetWorld);
            Sprite[] sprites = Sprites();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var existing = machine.states.ToDictionary(s => s.state.name, s => s.state.motion);
            AnimatorState originalDefault = machine.defaultState;
            Install(machine, MakeClip("LevelClear_0", sprites.Take(4).ToArray(), false), new Vector3(720, 360));
            Install(machine, MakeClip("LevelClearLoop_0", sprites.Skip(4).ToArray(), true), new Vector3(720, 430));
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Require(machine.defaultState == originalDefault, "不能替换原默认状态。");
            foreach (var entry in existing)
                if (entry.Key != "LevelClear_0" && entry.Key != "LevelClearLoop_0")
                    Require(machine.states.Single(s => s.state.name == entry.Key).state.motion == entry.Value, "旧动作被修改：" + entry.Key);
            Validate();
        }
        finally { UnityEngine.Object.DestroyImmediate(reference); UnityEngine.Object.DestroyImmediate(image); }
    }

    static void ImportSlices(Texture2D image, float ppu, Vector2 feetWorld)
    {
        AssetDatabase.ImportAsset(ImagePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = ppu;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        var factories = new SpriteDataProviderFactories(); factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        Require(provider != null, "缺少 Sprite Editor 数据提供者。");
        provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        var rects = new SpriteRect[8];
        for (int i = 0; i < 8; i++)
        {
            string name = "catgirl-level-clear-0_" + i.ToString("D2");
            Vector2 feet = Measure(image, Cell(i), out _);
            Vector2 pivot = feet - feetWorld * ppu;
            rects[i] = new SpriteRect
            {
                name = name, spriteID = previous.TryGetValue(name, out GUID id) ? id : GUID.Generate(),
                rect = new Rect(Cell(i).x, Cell(i).y, Width, Height), alignment = SpriteAlignment.Custom,
                pivot = new Vector2(pivot.x / Width, pivot.y / Height), border = Vector4.zero
            };
        }
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
            .SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    static RectInt Cell(int frame) => new RectInt(frame % 4 * Width, (1 - frame / 4) * Height, Width, Height);
    static RectInt PixelRect(Rect rect) => new RectInt(Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
    static Sprite[] Sprites() => AssetDatabase.LoadAllAssetsAtPath(ImagePath).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
    static Texture2D ReadPng(string path)
    {
        var texture = new Texture2D(2, 2);
        Require(texture.LoadImage(File.ReadAllBytes(path)), "不能读取 PNG：" + path);
        return texture;
    }

    // 忽略原图中极低透明度的底边残色，以实际不透明脚底对齐；不改写源图颜色或 Alpha。
    static Vector2 Measure(Texture2D texture, RectInt cell, out int characterHeight)
    {
        Color32[] pixels = texture.GetPixels32();
        int bottom = cell.height, top = -1;
        for (int y = 0; y < cell.height; y++)
            for (int x = 0; x < cell.width; x++)
                if (pixels[(cell.y + y) * texture.width + cell.x + x].a >= 128)
                { bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
        Require(top >= bottom, "切片没有可见角色。");
        int left = cell.width, right = -1;
        for (int y = bottom; y <= Math.Min(top, bottom + 4); y++)
            for (int x = 0; x < cell.width; x++)
                if (pixels[(cell.y + y) * texture.width + cell.x + x].a >= 128)
                { left = Math.Min(left, x); right = Math.Max(right, x); }
        characterHeight = top - bottom + 1;
        return new Vector2((left + right) * .5f, bottom);
    }

    static AnimationClip MakeClip(string name, Sprite[] frames, bool loop)
    {
        string path = ClipRoot + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves(); clip.name = name; clip.frameRate = 100f; clip.legacy = false;
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var keys = new ObjectReferenceKeyframe[5];
        for (int i = 0; i < 4; i++) keys[i] = new ObjectReferenceKeyframe { time = i * Interval, value = frames[i] };
        keys[4] = new ObjectReferenceKeyframe { time = 4 * Interval - 1f / clip.frameRate, value = frames[3] };
        AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0; settings.stopTime = 4 * Interval; settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void Install(AnimatorStateMachine machine, AnimationClip clip, Vector3 position)
    {
        var state = machine.states.FirstOrDefault(s => s.state.name == clip.name).state;
        if (state == null) state = machine.AddState(clip.name, position);
        Require(state.transitions.Length == 0, "通关状态不能配置自动 Transition。");
        state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
    }

    public static void Validate()
    {
        Sprite[] sprites = Sprites();
        Require(sprites.Length == 8, "需要完整八帧切片。");
        var idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<Sprite>().Single(s => s.name.EndsWith("_0", StringComparison.Ordinal));
        Texture2D image = ReadPng(ImagePath), reference = ReadPng(IdlePath);
        try
        {
            Vector2 idleFeet = (Measure(reference, PixelRect(idle.rect), out int idleHeight) - idle.pivot) / idle.pixelsPerUnit;
            foreach (Sprite sprite in sprites)
            {
                Vector2 feet = (Measure(image, PixelRect(sprite.rect), out int height) - sprite.pivot) / sprite.pixelsPerUnit;
                Require(Vector2.Distance(feet, idleFeet) < .002f, "脚底与待机未对齐：" + sprite.name);
                Require(Mathf.Abs(height / sprite.pixelsPerUnit - idleHeight / idle.pixelsPerUnit) < .04f, "大小与待机不一致：" + sprite.name);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
            Require(importer.sRGBTexture && importer.alphaIsTransparency && !importer.mipmapEnabled
                && importer.textureCompression == TextureImporterCompression.Uncompressed, "颜色、透明度或压缩导入配置错误。");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (string name in new[] { "LevelClear_0", "LevelClearLoop_0" })
            {
                var state = machine.states.Single(s => s.state.name == name).state;
                var clip = state.motion as AnimationClip;
                Require(clip != null && clip.name == name && clip.isLooping == (name == "LevelClearLoop_0"), "通关片段循环配置错误。");
                Require(state.transitions.Length == 0, "通关状态必须由共享表现脚本直接选择。");
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));
                Require(keys.Length == 5, "通关片段缺帧。");
                int offset = clip.isLooping ? 4 : 0;
                for (int i = 0; i < 4; i++) Require(keys[i].value == sprites[i + offset], "通关切片引用或顺序错误。");
            }
            Debug.Log($"Level clear assets PASS: 8 slices, PPU={sprites[0].pixelsPerUnit}, idle feet={idleFeet}, start/loop=0.64s.");
        }
        finally { UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(reference); }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
