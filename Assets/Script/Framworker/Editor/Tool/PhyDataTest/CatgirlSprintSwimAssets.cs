using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Import approved action sheets and align sprint support frames with idle, without changing character transforms.</summary>
public static class CatgirlSprintSwimAssets
{
    private const string ReviewRoot = "output/imagegen/sprint-swim-sequences-review-20261007";
    private const string PushPullReviewRoot = "output/imagegen/push-pull-sequences-review-20261009";
    private const string ArtRoot = "Assets/MyObjAssets/gameObject/Art/sequence frame";
    private const string ClipRoot = "Assets/MyObjAssets/gameObject/Animation/Loop";
    private const string ControllerPath = "Assets/MyObjAssets/gameObject/Animation/player.controller";
    private const string ReportPath = "output/animation-integration-20261009/catgirl-action-assets.txt";
    private const int CellSize = 512;
    private const float PixelsPerUnit = 118f;

    // The approved sheets share eye position (344,220), measured from their top left.
    // Swimming retains the existing pivot. Push/pull pixels already match idle's scale and full-sheet anchor.
    private static readonly Vector2 Pivot = new Vector2(274.5f / CellSize, 258.5f / CellSize);
    private static readonly Vector2 PushPullPivot = new Vector2(192.5f / CellSize, 244.5f / CellSize);
    // Support phases 1,2,5,6 match idle's lowest opaque edge (-1.465 units); interpolate through swing/flight phases.
    private static readonly float[] SprintPivotY = { 274.536667f, 271.87f, 270.87f, 273.536667f, 276.203333f, 278.87f, 279.87f, 277.203333f };
    private static readonly Spec[] Specs =
    {
        new Spec("sprint", "catgirl-sprint-8f-512", "Sprint", 4, 2, 8, .08f, new Vector3(700, 440, 0)),
        new Spec("underwater-idle", "catgirl-underwater-idle-4f-512", "SwimIdle", 2, 2, 4, .24f, new Vector3(940, 200, 0)),
        new Spec("underwater-move", "catgirl-underwater-move-6f-512", "Swim", 3, 2, 6, .16f, new Vector3(700, 200, 0)),
        new Spec("surface-idle", "catgirl-surface-idle-4f-512", "SurfaceSwimIdle", 2, 2, 4, .24f, new Vector3(940, 360, 0)),
        new Spec("surface-move", "catgirl-surface-move-6f-512", "SurfaceSwim", 3, 2, 6, .16f, new Vector3(700, 360, 0)),
        new Spec("push", "catgirl-push-6f-512", "Push", 3, 2, 6, .16f, new Vector3(940, 440, 0), PushPullReviewRoot, 100f, PushPullPivot),
        new Spec("pull", "catgirl-pull-6f-512", "Pull", 3, 2, 6, .16f, new Vector3(940, 520, 0), PushPullReviewRoot, 100f, PushPullPivot)
    };

    private sealed class Spec
    {
        public readonly string Source, Stem, State;
        public readonly int Columns, Rows, Count;
        public readonly float Interval;
        public readonly float PPU;
        public readonly Vector2 SpritePivot;
        public readonly Vector3 Position;
        private readonly string root;
        public string SourcePath => root + "/" + Source + "/sheet.png";
        public string ImagePath => ArtRoot + "/" + Stem + ".png";
        public string ClipPath => ClipRoot + "/" + State + ".anim";
        public Spec(string source, string stem, string state, int columns, int rows, int count, float interval, Vector3 position,
            string sourceRoot = ReviewRoot, float ppu = PixelsPerUnit, Vector2? pivot = null)
        { Source = source; Stem = stem; State = state; Columns = columns; Rows = rows; Count = count; Interval = interval; Position = position;
            root = sourceRoot; PPU = ppu; SpritePivot = pivot ?? Pivot; }
    }

    [MenuItem("自定义工具/开发工具/素材/导入已确认的猫娘疾跑、游泳与推拉素材")]
    public static void ImportAndValidate()
    {
        Require(!Application.isPlaying, "Import must run outside Play Mode.");
        var protectedFiles = ProtectedFileHashes();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Require(controller != null && controller.layers.Length > 0 && controller.layers[0].name == "Base Layer", "Missing Base Layer.");
        var machine = controller.layers[0].stateMachine;
        var oldStates = machine.states.ToDictionary(s => s.state.name, s => s.state);
        var oldMotions = oldStates.ToDictionary(s => s.Key, s => s.Value.motion);
        var oldTransitions = oldStates.ToDictionary(s => s.Key, s => s.Value.transitions.ToArray());
        var oldDefault = machine.defaultState;
        var oldAnyTransitions = machine.anyStateTransitions.ToArray();
        Require(oldStates.ContainsKey("Swim") && oldStates.ContainsKey("SurfaceSwim"), "Missing compatible swimming placeholders.");

        foreach (var spec in Specs)
        {
            ImportSheet(spec);
            var sprites = LoadSprites(spec);
            var clip = MakeClip(spec, sprites);
            AnimatorState state;
            if (!oldStates.TryGetValue(spec.State, out state)) state = machine.AddState(spec.State, spec.Position);
            state.motion = clip;
            state.speed = 1f;
            state.writeDefaultValues = true;
            Require(state.transitions.Length == 0, "The imported state must have no transitions: " + spec.State);
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);

        foreach (var entry in oldStates)
        {
            Require(machine.states.Single(s => s.state.name == entry.Key).state == entry.Value, "Existing state ID was replaced: " + entry.Key);
            Require(entry.Value.transitions.SequenceEqual(oldTransitions[entry.Key]), "Existing transition changed: " + entry.Key);
            if (entry.Key != "Swim" && entry.Key != "SurfaceSwim")
                Require(entry.Value.motion == oldMotions[entry.Key], "Existing motion changed: " + entry.Key);
        }
        Require(machine.defaultState == oldDefault, "Default animation changed.");
        Require(machine.anyStateTransitions.SequenceEqual(oldAnyTransitions), "Any State transitions changed.");
        foreach (var entry in protectedFiles) Require(FileHash(entry.Key) == entry.Value, "Protected file changed: " + entry.Key);
        Validate();
    }

    // Suitable for -executeMethod with -quit; can also be called before another batch check.
    public static void ImportAndValidateForBatch() => ImportAndValidate();

    private static void ImportSheet(Spec spec)
    {
        string source = spec.SourcePath;
        Require(File.Exists(source), "Approved sheet missing: " + source);
        Directory.CreateDirectory(ArtRoot);
        if (!File.Exists(spec.ImagePath) || FileHash(source) != FileHash(spec.ImagePath)) File.Copy(source, spec.ImagePath, true);
        AssetDatabase.ImportAsset(spec.ImagePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(spec.ImagePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = spec.PPU;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteExtrude = 1;
        importer.SetTextureSettings(settings);

        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        Require(provider != null, "Sprite Editor data provider unavailable.");
        provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        var rects = new SpriteRect[spec.Count];
        for (int i = 0; i < spec.Count; i++)
        {
            string name = spec.Stem + "_" + i.ToString("D2");
            GUID id;
            if (!previous.TryGetValue(name, out id)) id = GUID.Generate();
            rects[i] = new SpriteRect
            {
                name = name,
                spriteID = id,
                rect = ExpectedRect(spec, i),
                alignment = SpriteAlignment.Custom,
                pivot = ExpectedPivot(spec, i),
                border = Vector4.zero
            };
        }
        provider.SetSpriteRects(rects);
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        Require(names != null, "Sprite name/ID data provider unavailable.");
        names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
    }

    private static Rect ExpectedRect(Spec spec, int frame)
    {
        return new Rect((frame % spec.Columns) * CellSize, (spec.Rows - 1 - frame / spec.Columns) * CellSize, CellSize, CellSize);
    }

    private static Vector2 ExpectedPivot(Spec spec, int frame)
    {
        return spec.State == "Sprint" ? new Vector2(Pivot.x, SprintPivotY[frame] / CellSize) : spec.SpritePivot;
    }

    private static Sprite[] LoadSprites(Spec spec)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(spec.ImagePath).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        Require(sprites.Length == spec.Count, "Incorrect slice count: " + spec.Stem);
        for (int i = 0; i < sprites.Length; i++) Require(sprites[i].name == spec.Stem + "_" + i.ToString("D2"), "Incorrect slice ordering.");
        return sprites;
    }

    private static AnimationClip MakeClip(Spec spec, Sprite[] sprites)
    {
        string path = spec.ClipPath;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.name = spec.State;
        clip.frameRate = 100f;
        clip.legacy = false;
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var keys = new ObjectReferenceKeyframe[spec.Count + 1];
        for (int i = 0; i < spec.Count; i++) keys[i] = new ObjectReferenceKeyframe { time = i * spec.Interval, value = sprites[i] };
        // Unity extends a Sprite PPtr curve by one sample; the terminal key keeps the final hold within the approved duration.
        keys[spec.Count] = new ObjectReferenceKeyframe { time = spec.Count * spec.Interval - 1f / clip.frameRate, value = sprites[spec.Count - 1] };
        var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0f;
        settings.stopTime = spec.Count * spec.Interval;
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        return clip;
    }

    [MenuItem("自定义工具/测试工具/验证/验证猫娘疾跑、游泳与推拉素材")]
    public static void Validate()
    {
        var lines = new List<string>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Require(controller != null && controller.layers.Length > 0 && controller.layers[0].name == "Base Layer", "Missing Base Layer.");
        var machine = controller.layers[0].stateMachine;
        var scene = EditorSceneManager.NewPreviewScene();
        var probe = new GameObject("CatgirlSpriteAssetValidation", typeof(SpriteRenderer), typeof(Animator)) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(probe, scene);
        var renderer = probe.GetComponent<SpriteRenderer>();
        var animator = probe.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.Rebind();
        animator.Update(0f);
        try
        {
            foreach (var spec in Specs)
            {
                Require(FileHash(spec.SourcePath) == FileHash(spec.ImagePath), "Imported pixels differ from approval.");
                var importer = (TextureImporter)AssetImporter.GetAtPath(spec.ImagePath);
                Require(importer != null && importer.spriteImportMode == SpriteImportMode.Multiple && Mathf.Approximately(importer.spritePixelsPerUnit, spec.PPU), "Incorrect Sprite import settings.");
                Require(importer.textureCompression == TextureImporterCompression.Uncompressed, "Sprite pixels must not use lossy compression.");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.ImagePath);
                Require(texture.width == spec.Columns * CellSize && texture.height == spec.Rows * CellSize, "Imported texture was resized: " + spec.Stem);
                var sprites = LoadSprites(spec);
                for (int i = 0; i < sprites.Length; i++)
                {
                    Require(sprites[i].rect == ExpectedRect(spec, i), "Incorrect slice rectangle: " + sprites[i].name);
                    Require(Vector2.Distance(sprites[i].pivot, ExpectedPivot(spec, i) * CellSize) < .01f && Mathf.Approximately(sprites[i].pixelsPerUnit, spec.PPU), "Incorrect Sprite pivot or PPU: " + sprites[i].name);
                    Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprites[i], out string guid, out long fileId) && fileId != 0 && !string.IsNullOrEmpty(guid), "Unresolved Sprite reference.");
                }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.ClipPath);
                Require(clip != null && clip.isLooping && Mathf.Abs(clip.length - spec.Count * spec.Interval) < .001f,
                    "Incorrect clip loop duration: " + spec.State + "; actual=" + (clip == null ? "missing" : clip.length + "s, looping=" + clip.isLooping));
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                Require(bindings.Length == 1 && bindings[0].path == string.Empty && bindings[0].type == typeof(SpriteRenderer) && bindings[0].propertyName == "m_Sprite", "Incorrect SpriteRenderer binding.");
                Require(AnimationUtility.GetCurveBindings(clip).Length == 0, "Clip contains unexpected transform curves.");
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
                Require(keys.Length == spec.Count + 1 && keys.All(k => k.value != null), "Missing Sprite key reference.");
                for (int i = 0; i < spec.Count; i++)
                {
                    Require(keys[i].value == sprites[i] && Mathf.Abs(keys[i].time - i * spec.Interval) < .0001f, "Incorrect animation frame ordering.");
                    animator.Play(Animator.StringToHash("Base Layer." + spec.State), 0, (i + .5f) * spec.Interval / clip.length);
                    animator.Update(0f);
                    Require(renderer.sprite == sprites[i], "Animator Sprite sampling failed: " + spec.State + " frame " + i
                        + "; actual=" + (renderer.sprite == null ? "null" : renderer.sprite.name));
                }
                animator.Play(Animator.StringToHash("Base Layer." + spec.State), 0, 1f - .001f / clip.length);
                animator.Update(0f);
                Require(renderer.sprite == sprites[spec.Count - 1], "Last frame is not held until the loop boundary.");
                var matches = machine.states.Where(s => s.state.name == spec.State).ToArray();
                Require(matches.Length == 1 && matches[0].state.motion == clip && matches[0].state.transitions.Length == 0, "Incorrect Base Layer state: " + spec.State);
                lines.Add(spec.State + ": " + spec.Count + " slices, " + texture.width + "x" + texture.height + ", PPU=" + spec.PPU + ", frame=" + spec.Interval + "s, loop=" + clip.length + "s; all pivots, references and sampling passed.");
            }
        }
        finally
        {
            Object.DestroyImmediate(probe);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        lines.Add("PASS: 7 approved sheets, 40 complete 512x512 slices, 7 looping clips, 7 unconnected Base Layer action states; existing states/transitions preserved.");
        File.WriteAllLines(ReportPath, lines);
        Debug.Log(lines[lines.Count - 1] + " Report: " + ReportPath);
    }

    private static Dictionary<string, string> ProtectedFileHashes()
    {
        var paths = Directory.GetFiles(ArtRoot).Where(p => !Specs.Any(s => p.Replace('\\', '/') == s.ImagePath || p.Replace('\\', '/') == s.ImagePath + ".meta"));
        paths = paths.Concat(Directory.GetFiles("Assets/MyObjAssets/gameObject/Animation", "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".anim", StringComparison.Ordinal) || p.EndsWith(".anim.meta", StringComparison.Ordinal))
            .Where(p => !Specs.Any(s => p.Replace('\\', '/') == s.ClipPath || p.Replace('\\', '/') == s.ClipPath + ".meta")));
        paths = paths.Concat(new[] { "Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab", "Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab.meta" });
        return paths.Distinct().ToDictionary(p => p, FileHash);
    }

    private static string FileHash(string path)
    {
        using (var algorithm = SHA256.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(stream));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
