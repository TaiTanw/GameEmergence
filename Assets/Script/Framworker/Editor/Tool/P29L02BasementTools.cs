#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Move L02's existing box cart lesson into a single-screen basement.</summary>
public static class P29L02BasementTools
{
    const string ScenePath = "Assets/MyObjAssets/Scenes/Designed/L02_SequentialGates.unity";
    const string RootName = "11_P29_地下箱车演示";
    static readonly Color Stone = new Color(.32f, .39f, .47f);
    static Transform root;
    static Sprite square;

    [MenuItem("Tools/P29/Build L02 Basement Demo")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open L02_SequentialGates first.");
        if (scene.GetRootGameObjects().Any(g => g.name == RootName))
        {
            Debug.Log("P29_L02_BASEMENT_ALREADY_BUILT");
            return;
        }

        Transform source = scene.GetRootGameObjects().Single(g => g.name == "09_P25关卡增量").transform;
        MechanismCart cart = source.GetComponentInChildren<MechanismCart>(true);
        PhysicalBox box = source.GetComponentInChildren<PhysicalBox>(true);
        MechanismPressurePlate[] plates = source.GetComponentsInChildren<MechanismPressurePlate>(true)
            .OrderBy(p => p.transform.position.x).ToArray();
        Signboard sign = source.GetComponentInChildren<Signboard>(true);
        SceneLevelSetup setup = All<SceneLevelSetup>(scene).Single();
        if (!cart || !box || !sign || plates.Length != 2 || !setup.RisingWater)
            throw new InvalidOperationException("L02 cart lesson is incomplete; scene was not modified.");
        Taijie[] doors = plates.Select(p => new SerializedObject(p).FindProperty("target").objectReferenceValue as Taijie).ToArray();
        if (doors.Any(d => !d) || doors[0] == doors[1])
            throw new InvalidOperationException("The two plates must have separate linked doors.");

        BoxCollider2D originalFloor = All<BoxCollider2D>(scene).Single(c => Match(c, 0, -.6f, 64, 0));
        BoxCollider2D originalWalk = All<BoxCollider2D>(scene).Single(c => Match(c, 2, 1.6f, 48.8f, 2));
        BoxCollider2D rightBorder = All<BoxCollider2D>(scene).Single(c => !c.enabled &&
            Mathf.Abs(c.transform.position.x - 64.5f) < .05f && Mathf.Abs(c.size.y - 94) < .05f);
        square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
        if (!square) throw new InvalidOperationException("Missing Square.png sprite.");

        root = new GameObject(RootName).transform;
        MoveUnder(cart.gameObject, 2.5f, -11.02f);
        MoveUnder(box.gameObject, 4.5f, -10.43f);
        MoveUnder(plates[0].gameObject, 7.1f, -11.15f);
        MoveUnder(plates[1].gameObject, 11.1f, -11.15f);
        MoveUnder(doors[0].gameObject, 8.05f, -3.85f);
        MoveUnder(doors[1].gameObject, 12.05f, -3.85f);
        MoveUnder(sign.gameObject, 1.5f, -11.2f);
        P25LevelVisualTools.SetNotice(sign, "箱子才能压动踏板。留意两扇门的时机。");
        Object.DestroyImmediate(source.gameObject);
        foreach (var decoration in scene.GetRootGameObjects()
            .Where(g => g.name.StartsWith("08_P25") || g.name.StartsWith("10_P26"))
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t.parent != null && t.position.x > 64.5f && t.position.x < 81f &&
                        t.position.y > 40f && t.position.y < 60f)
            .Select(t => t.gameObject).ToArray())
            Object.DestroyImmediate(decoration);
        rightBorder.enabled = true;
        rightBorder.GetComponent<SpriteRenderer>().enabled = true;

        // One image and one collider per segment. The opening is an actual shaft to the old route.
        Split(originalFloor, 0, 13.4f, 15.5f, 64);
        Split(originalWalk, 2, 13.4f, 15.5f, 48.8f);
        Solid("轨道地面", 8, -11.2f, 16, .6f);
        Solid("左侧墙", -.5f, -1, 1, 10.2f);
        Solid("右侧墙", 16.5f, -1, 1, 10.2f);
        Solid("涨水槽底", 32, -19.75f, 64, .6f);
        Solid("涨水槽左壁", -.5f, -11.2f, 1, 8.55f);
        Solid("涨水槽右壁", 64.5f, -1, 1, 18.75f);
        Solid("观察走道", 9.3f, -6, 10.4f, .4f);
        Solid("登台1", 1.3f, -9.4f, 2.2f, .4f, true);
        Solid("登台2", 2.8f, -7.7f, 2.2f, .4f, true);
        Solid("登台3", 4.25f, -6, 2.2f, .4f, true);
        Solid("箱车止动块", 15.2f, -9.6f, .6f, 1.6f);
        Solid("一号门楣", 8.05f, -.6f, .85f, 1.1f);
        Solid("二号门楣", 12.05f, -.6f, .85f, 1.1f);
        Solid("出口阶梯1", 14.25f, -4.1f, 2.1f, .4f, true);
        Solid("出口阶梯2", 14.45f, -2.2f, 2.1f, .4f, true);
        Solid("出口阶梯3", 14.45f, -.4f, 2.1f, .4f, true);
        Label("踏板1", "1", 7.1f, -10.7f);
        Label("踏板2", "2", 11.1f, -10.7f);
        Label("门1", "1", 8.05f, -1.4f);
        Label("门2", "2", 12.05f, -1.4f);

        var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Map/MapCell.prefab");
        var map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
        map.name = "P29_地下演示地图格";
        map.transform.SetParent(root, false);
        map.transform.position = new Vector3(8, -5.6f, 0);
        Set(map.GetComponent<MapCell>(), "width", 16f);
        Set(map.GetComponent<MapCell>(), "height", 11.2f);
        Set(map.GetComponent<MapCell>(), "cognitionCost", 1f);

        // Starting on the second landing puts the rail and both doors in one camera frame.
        setup.transform.Find("BodySpawn").position = new Vector3(2.3f, -6.475f, -1);
        setup.transform.Find("SpiritSpawn").position = new Vector3(2.3f, -4, 0);
        var water = setup.RisingWater.transform;
        water.position = new Vector3(32, -19, -2);
        var waterShape = water.GetComponent<BoxCollider2D>();
        waterShape.size = new Vector2(64, waterShape.size.y);
        var waterSprite = water.GetComponent<SpriteRenderer>();
        waterSprite.drawMode = SpriteDrawMode.Sliced;
        waterSprite.size = waterShape.size;

        // The existing illustrated background now covers the new underground floor as well.
        var backdrop = All<SpriteRenderer>(scene).Single(s => s.sprite && s.sortingOrder <= -90 && s.bounds.size.x > 60);
        backdrop.transform.localScale *= 1.25f;
        backdrop.transform.position = new Vector3(backdrop.transform.position.x, 40, backdrop.transform.position.z);

        var so = new SerializedObject(setup);
        var cells = All<MapCell>(scene).ToArray();
        var array = so.FindProperty("mapCells");
        array.arraySize = cells.Length;
        for (int i = 0; i < cells.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("P29_L02_BASEMENT_SAVED: original L02 cart and linked plates moved; old annex removed.");
    }

    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

    static bool Match(BoxCollider2D c, float minX, float minY, float maxX, float maxY)
    {
        Bounds b = c.bounds;
        return Mathf.Abs(b.min.x - minX) < .05f && Mathf.Abs(b.min.y - minY) < .05f &&
               Mathf.Abs(b.max.x - maxX) < .05f && Mathf.Abs(b.max.y - maxY) < .05f;
    }

    static void MoveUnder(GameObject go, float x, float y)
    {
        go.transform.SetParent(root, true);
        go.transform.position = new Vector3(x, y, go.transform.position.z);
    }

    static void Split(BoxCollider2D original, float minX, float leftEnd, float rightStart, float maxX)
    {
        var copy = Object.Instantiate(original.gameObject, original.transform.parent);
        copy.name = original.name + "_右段";
        SetSegment(original.gameObject, minX, leftEnd);
        SetSegment(copy, rightStart, maxX);
    }

    static void SetSegment(GameObject go, float minX, float maxX)
    {
        float width = maxX - minX;
        go.transform.position = new Vector3((minX + maxX) / 2, go.transform.position.y, go.transform.position.z);
        var collider = go.GetComponent<BoxCollider2D>();
        collider.size = new Vector2(width, collider.size.y);
        collider.offset = Vector2.zero;
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = collider.size;
    }

    static GameObject Solid(string name, float x, float top, float width, float height, bool oneWay = false)
    {
        var go = new GameObject("P29_" + name);
        go.layer = LayerMask.NameToLayer("Ground");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(x, top - height / 2, 0);
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(width, height);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = square;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = collider.size;
        renderer.color = Stone;
        if (oneWay)
        {
            var effector = go.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.surfaceArc = 170;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
            collider.usedByEffector = true;
        }
        return go;
    }

    static void Label(string name, string text, float x, float y)
    {
        var go = new GameObject("P29_" + name);
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(x, y, -.1f);
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.characterSize = .35f;
        mesh.fontSize = 48;
        mesh.color = new Color(.8f, .84f, .7f);
        mesh.GetComponent<MeshRenderer>().sortingOrder = 6;
    }

    static void Set(Object target, string field, float value)
    {
        var so = new SerializedObject(target);
        var property = so.FindProperty(field);
        if (property == null) throw new InvalidOperationException(target.name + " is missing " + field);
        property.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
