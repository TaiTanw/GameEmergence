#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只创建结局示例及增补两个菜单节点，已有制作资产不重建。</summary>
public static class EndingAssets
{
    public const string ActorPath = "Assets/MyObjAssets/gameObject/Pf/Player/Performance/EndingActor.prefab";
    public const string ScenePath = "Assets/MyObjAssets/Scenes/EndScenes/EndingExample.unity";
    const string ControllerPath = "Assets/MyObjAssets/gameObject/Animation/EndingActor.controller";

    [MenuItem("自定义工具/开发工具/结局/创建演出角色、示例与进度入口")]
    public static void Create()
    {
        CreateActor();
        CreateScene();
        AddSettings();
        AddMenuStatus();
        AssetDatabase.SaveAssets();
    }
    public static void RunForBatch()
    {
        try { EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity");
            Create(); PlayerProgressChecks.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void CreateActor()
    {
        if (File.Exists(ActorPath)) return;
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            string[] states = { "Idle", "Walk", "JumpUp", "JumpDown", "Finale" };
            string[] clips = { "Loop/idle", "Loop/move_loop", "Loop/OnAir_Up", "Loop/OnAir_Down", "Death" };
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = machine.AddState(states[i]);
                state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/MyObjAssets/gameObject/Animation/" + clips[i] + ".anim");
                if (state.motion == null) throw new InvalidOperationException("结局基础动画缺失：" + clips[i]);
                if (i == 0) machine.defaultState = state;
            }
        }
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab");
        SpriteRenderer visual = source.GetComponentInChildren<SpriteRenderer>();
        BoxCollider2D originalShape = source.GetComponent<BoxCollider2D>();
        var item = new GameObject("EndingActor");
        try
        {
            item.layer = Player.PlayerLayer;
            item.transform.localScale = source.transform.localScale;
            Rigidbody2D body = item.AddComponent<Rigidbody2D>();
            body.gravityScale = 2f; body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BoxCollider2D shape = item.AddComponent<BoxCollider2D>();
            shape.size = originalShape.size; shape.offset = originalShape.offset;
            const string materialPath = "Assets/MyObjAssets/gameObject/Animation/EndingActorPhysics.physicsMaterial2D";
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(materialPath);
            if (material == null)
            { material = new PhysicsMaterial2D("EndingActorPhysics") { friction = 0f, bounciness = 0f }; AssetDatabase.CreateAsset(material, materialPath); }
            shape.sharedMaterial = material;
            var appearance = new GameObject("Appearance"); appearance.layer = item.layer;
            appearance.transform.SetParent(item.transform, false);
            appearance.transform.localPosition = visual.transform.localPosition;
            appearance.transform.localRotation = visual.transform.localRotation;
            appearance.transform.localScale = visual.transform.localScale;
            SpriteRenderer sprite = appearance.AddComponent<SpriteRenderer>();
            sprite.sprite = visual.sprite; sprite.sharedMaterial = visual.sharedMaterial;
            sprite.sortingLayerID = visual.sortingLayerID; sprite.sortingOrder = visual.sortingOrder; sprite.color = visual.color;
            Animator animator = appearance.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.Normal; animator.applyRootMotion = false;
            EndingActor actor = item.AddComponent<EndingActor>();
            var settings = new SerializedObject(actor);
            settings.FindProperty("animator").objectReferenceValue = animator;
            settings.FindProperty("sprite").objectReferenceValue = sprite;
            settings.FindProperty("groundLayers").intValue = LayerMask.GetMask("Ground");
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(item, ActorPath);
        }
        finally { Object.DestroyImmediate(item); }
    }
    static void CreateScene()
    {
        if (File.Exists(ScenePath)) return;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Scene previous = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        try
        {
            var actorObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ActorPath), scene);
            actorObject.transform.position = new Vector3(0f, .1f, 0f);
            EndingActor actor = actorObject.GetComponent<EndingActor>();
            var floor = new GameObject("EndingStage"); floor.layer = LayerMask.NameToLayer("Ground");
            BoxCollider2D shape = floor.AddComponent<BoxCollider2D>(); shape.size = new Vector2(24f, .4f);
            BoxCollider2D actorShape = actorObject.GetComponent<BoxCollider2D>();
            floor.transform.position = new Vector3(0f, (actorShape.offset.y - actorShape.size.y * .5f) * actorObject.transform.localScale.y - .2f, 0f);
            var title = new GameObject("EndingTitle"); title.transform.position = new Vector3(-3.5f, 2.5f, 0f);
            TextMesh text = title.AddComponent<TextMesh>(); text.font = GameUIAssets.Shared.font;
            text.fontSize = 48; text.characterSize = .08f; text.color = Color.white;
            text.text = "结局示例\n可行走与跳跃；ESC 暂停";
            title.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            var host = new GameObject("EndingExample"); EndingExample example = host.AddComponent<EndingExample>();
            var config = new SerializedObject(example);
            config.FindProperty("actor").objectReferenceValue = actor;
            config.FindProperty("revealObject").objectReferenceValue = title;
            config.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
    }
    static void AddSettings()
    {
        const string path = "Assets/MyObjAssets/UIPanel/SetPanel.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.transform.Find("ClearGameProgress") != null) return;
            RectTransform entry = Rect(root.transform, "ClearGameProgress", new Vector2(0, -300), new Vector2(500, 70));
            Text caption = Label(entry, "ClearProgressCaption", "清空游戏进度", Vector2.zero, new Vector2(500, 70), 32);
            caption.color = new Color(.75f, .12f, .14f); caption.raycastTarget = true;
            Button button = entry.gameObject.AddComponent<Button>(); button.targetGraphic = caption;
            Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            Label(root.transform, "SettingsStatus", "", new Vector2(0, -400), new Vector2(1100, 90), 26);
            RectTransform confirmation = Rect(root.transform.Find("SettingsContent"), "ClearProgressConfirmation", Vector2.zero, new Vector2(1000, 600));
            Image background = confirmation.gameObject.AddComponent<Image>(); background.color = new Color(.93f, .86f, .72f);
            Label(confirmation, "ClearProgressMessage", "", new Vector2(0, 130), new Vector2(900, 200), 32);
            MakeButton(confirmation, "ClearProgressConfirm", "确认清空", new Vector2(-190, -130));
            MakeButton(confirmation, "ClearProgressCancel", "取消", new Vector2(190, -130));
            confirmation.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void AddMenuStatus()
    {
        const string path = "Assets/MyObjAssets/UIPanel/BeginPanel.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.transform.Find("BeginStatus") != null) return;
            Text text = Label(root.transform, "BeginStatus", "", new Vector2(0, -390), new Vector2(1500, 100), 28);
            text.color = new Color(.9f, .2f, .2f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform)); item.layer = 5;
        RectTransform rect = (RectTransform)item.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static Text Label(Transform parent, string name, string caption, Vector2 position, Vector2 size, int fontSize)
    {
        Text text = Rect(parent, name, position, size).gameObject.AddComponent<Text>();
        text.text = caption; text.font = GameUIAssets.Shared.font; text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter; text.color = new Color(.18f, .14f, .2f); text.raycastTarget = false;
        return text;
    }
    static void MakeButton(Transform parent, string name, string caption, Vector2 position)
    {
        RectTransform rect = Rect(parent, name, position, new Vector2(310, 85));
        Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.82f, .73f, .59f);
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        Navigation navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        Label(rect, "Text (Legacy)", caption, Vector2.zero, new Vector2(290, 85), 30);
    }
}
#endif
