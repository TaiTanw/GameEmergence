#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>P25 的一次性增量关卡制作；不改变角色、物理相位或能力池。</summary>
public static class P25LevelContentTools
{
    const string Folder = "Assets/MyObjAssets/Scenes/Designed/";
    const string Pf = "Assets/MyObjAssets/gameObject/Pf/";
    const string Extension = "L05_DepartureWorks";
    static readonly string[] Names = { "L00_Orientation", "L01_PropertyLabyrinth", "L02_SequentialGates", "L03_ElectricTraverse", "L04_FoldedWells", Extension };
    static readonly Color Stone = new Color(.32f, .39f, .47f);
    static Transform root;
    static Sprite square;

    [MenuItem("自定义工具/关卡/P25/制作原关增量与检修站")]
    public static void ApplyContent()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请先退出播放。");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存手工场景改动。");
        string previous = SceneManager.GetActiveScene().path;
        square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
        try
        {
            for (int index = 0; index < 5; index++)
            {
                var scene = EditorSceneManager.OpenScene(Folder + Names[index] + ".unity");
                if (!scene.GetRootGameObjects().Any(g => g.name == "09_P25关卡增量"))
                {
                    root = new GameObject("09_P25关卡增量").transform;
                    if (index == 0) Tutorial();
                    else
                    {
                        float floor = new[] { 0f, 20.2f, 44.2f, 72.2f, 86.2f }[index];
                        Annex(scene, floor, index);
                        if (index == 3) ToxicCrossing(scene);
                    }
                    LinkMap(scene);
                }
                P25LevelVisualTools.Apply(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (!File.Exists(Folder + Extension + ".unity")) BuildExtension();
            RegisterExtension();
            AssetDatabase.SaveAssets();
            Debug.Log("P25_CONTENT_SAVED: 教学、原四关与检修站已保存。");
        }
        finally
        {
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        }
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
    static void Set(Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) throw new InvalidOperationException(target.name + " 缺少制作字段 " + field);
        if (value is Object obj) p.objectReferenceValue = obj;
        else if (value is bool b) p.boolValue = b;
        else if (value is float f) p.floatValue = f;
        else if (value is int n) p.intValue = n;
        else if (value is string s) p.stringValue = s;
        else if (value is Vector2 v) p.vector2Value = v;
        else throw new ArgumentException(field);
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static GameObject Instance(string relative, string name, Vector2 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + relative);
        if (!prefab) throw new FileNotFoundException(Pf + relative);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name; go.transform.SetParent(root, false);
        go.transform.position = new Vector3(position.x, position.y, 0);
        return go;
    }
    static GameObject Solid(string name, float x, float top, float width, float height = .4f, bool oneWay = false)
    {
        var go = new GameObject("P25_" + name); go.layer = LayerMask.NameToLayer("Ground");
        go.transform.SetParent(root, false); go.transform.position = new Vector3(x, top - height / 2, 0);
        var shape = go.AddComponent<BoxCollider2D>(); shape.size = new Vector2(width, height);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = square; sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = shape.size; sr.color = Stone; sr.sortingOrder = 0;
        if (oneWay)
        {
            var e = go.AddComponent<PlatformEffector2D>(); e.useOneWay = true; e.surfaceArc = 170;
            e.useSideFriction = false; e.useSideBounce = false; shape.usedByEffector = true;
        }
        return go;
    }
    static void Resize(GameObject go, Vector2 size, Vector2? position = null)
    {
        if (position.HasValue) go.transform.position = new Vector3(position.Value.x, position.Value.y, go.transform.position.z);
        var c = go.GetComponent<BoxCollider2D>();
        if (c) { c.autoTiling = false; c.size = size; c.offset = Vector2.zero; }
        var s = go.GetComponent<SpriteRenderer>();
        if (s) { s.drawMode = SpriteDrawMode.Sliced; s.size = size; }
    }
    static void Notice(float x, float floor, string name, string text)
    {
        var go = Instance("Map/Signboard.prefab", "P25_" + name, new Vector2(x, floor));
        P25LevelVisualTools.SetNotice(go.GetComponent<Signboard>(), text);
        var box = go.GetComponent<BoxCollider2D>(); box.size = new Vector2(3.4f, 3); box.offset = new Vector2(0, 1);
    }
    static MapCell Cell(float x, float y, float width, float height, string name, float floor)
    {
        var go = Instance("Map/MapCell.prefab", "P25_地图_" + name, new Vector2(x, y));
        go.transform.localScale = Vector3.one;
        var cell = go.GetComponent<MapCell>(); Set(cell, "width", width); Set(cell, "height", height); Set(cell, "cognitionCost", 1f);
        Solid("安置托台_" + name, x, floor, 3.2f, .4f, true);
        return cell;
    }
    static GameObject Environment(string prefab, string name, float x, float y, float w, float h)
    {
        var go = Instance("SceneObj/" + prefab, "P25_" + name, new Vector2(x, y));
        go.transform.localScale = Vector3.one; Resize(go, new Vector2(w, h));
        return go;
    }
    static void Calibrate(BasicEntity entity)
    {
        var c = entity.GetComponent<BoxCollider2D>();
        foreach (var p in new[] { ("groundV", new Vector2(0, -c.size.y / 2)), ("upV", new Vector2(0, c.size.y / 2)), ("leftV", new Vector2(-c.size.x / 2, 0)), ("rightV", new Vector2(c.size.x / 2, 0)) })
        {
            var t = (Transform)new SerializedObject(entity).FindProperty(p.Item1).objectReferenceValue;
            t.localPosition = c.offset + p.Item2;
        }
        if (entity is PhysicalBox && c.size.x > 2f)
        {
            const string path = "Assets/Resources/Data/P25WideBoxPhysics.asset";
            var so = new SerializedObject(entity); var p = so.FindProperty("cPhysics");
            var config = AssetDatabase.LoadAssetAtPath<SO_CPhysics>(path);
            if (!config) { config = Object.Instantiate((SO_CPhysics)p.objectReferenceValue); AssetDatabase.CreateAsset(config, path); }
            config.boxCastH = new Vector2(c.size.x - .08f, .1f); config.boxCastV = new Vector2(.02f, c.size.y - .08f);
            EditorUtility.SetDirty(config); p.objectReferenceValue = config; so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    static GameObject Box(float x, float floor, string name, float width = 1.8f)
    {
        var go = Instance("SceneObj/box.prefab", "P25_" + name, new Vector2(x, floor + .77f));
        go.transform.localScale = Vector3.one; Resize(go, new Vector2(width, 1.5f)); Calibrate(go.GetComponent<PhysicalBox>());
        return go;
    }
    static void Cart(float x, float floor, float speed)
    {
        var go = Instance("SceneObj/MechanismCart.prefab", "P25_右行检修车", new Vector2(x, floor + .18f));
        go.transform.localScale = Vector3.one; Set(go.GetComponent<MechanismCart>(), "speed", speed);
        Set(go.GetComponent<MechanismCart>(), "initiallyOn", false); Calibrate(go.GetComponent<MechanismCart>());
    }
    static void Plate(float x, float floor, float doorX, float upperFloor, int index, float width = 3f)
    {
        var go = Instance("SceneObj/PressurePlate.prefab", "P25_箱位压板_" + index, new Vector2(x, floor + .05f));
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (Transform child in go.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        Resize(go, new Vector2(width, .18f));
        var door = Environment("MechanismDoor.prefab", "编号闸门_" + index, doorX, upperFloor + 2.15f, .85f, 4.3f);
        var target = door.GetComponent<Taijie>(); Set(target, "mechanismOffset", new Vector2(0, 4.7f)); Set(target, "mechanismTravelTime", .8f);
        var plate = go.GetComponent<MechanismPressurePlate>(); Set(plate, "target", target); Set(plate, "boxesOnly", true);
        // 编号是静态线索，开启状态仍由原机关表现提供。
        Label("踏板编号_" + index, index.ToString(), new Vector2(x, floor + .5f));
        Label("门编号_" + index, index.ToString(), new Vector2(doorX, upperFloor + 4.5f));
    }
    static void Label(string name, string text, Vector2 position)
    {
        var go = new GameObject("P25_" + name); go.transform.SetParent(root, false); go.transform.position = new Vector3(position.x, position.y, -.1f);
        var mesh = go.AddComponent<TextMesh>(); mesh.text = text; mesh.anchor = TextAnchor.MiddleCenter;
        mesh.characterSize = .35f; mesh.fontSize = 48; mesh.color = new Color(.8f, .84f, .7f);
        go.GetComponent<MeshRenderer>().sortingOrder = 6;
    }
    static void Tutorial()
    {
        Notice(34, 0, "箱车教学", "先把箱子摆在踏板前，再按 E 开车。\n箱子压住踏板时，门才会打开。\n先看一遍门的变化，再往前走。");
    }
    static void Annex(Scene scene, float floor, int index)
    {
        var boundary = All<BoxCollider2D>(scene).First(c => c.name == "右外边界");
        boundary.enabled = false; boundary.GetComponent<SpriteRenderer>().enabled = false;
        float height = new[] { 24f, 76f, 92f, 100f, 116f }[index];
        Solid("右侧下隔墙", 64.5f, floor, 1, floor);
        Solid("右侧上隔墙", 64.5f, height + 1, 1, height + 1 - (floor + 12));
        Solid("扩展外边界", 80.5f, height + 1, 1, height + 2);
        Solid("扩展室底板", 72, floor, 16); Solid("扩展室顶板", 72, floor + 12.4f, 16);
        Solid("入口连廊", 64, floor, 8);
        Cell(72, floor + 6, 16, 12, "侧室_" + index, floor + 4.2f);
        var water = All<SceneLevelSetup>(scene).Single().RisingWater;
        Resize(water.gameObject, new Vector2(80, water.GetComponent<BoxCollider2D>().size.y), new Vector2(40, water.transform.position.y));
        // 扩展远景覆盖，保留已制作主题、灯光和纵向比例。
        RepairBackdrops(scene);
        var camera = All<Camera>(scene).FirstOrDefault(c => c.orthographic);
        if (camera) camera.orthographicSize = 5.3f;
        if (index == 2)
        {
            Solid("侧室上层走道", 74, floor + 4.2f, 12);
            Solid("侧室阶梯1", 66, floor + 2, 2.4f, .4f, true);
            Cart(66.8f, floor, .8f); Box(69.2f, floor, "侧室时机箱");
            Plate(73.2f, floor, 74, floor + 4.2f, 1, 2.6f);
            Plate(77.2f, floor, 78, floor + 4.2f, 2, 2.6f);
            Solid("侧室门楣_1", 74, floor + 12, .85f, 3.5f);
            Solid("侧室门楣_2", 78, floor + 12, .85f, 3.5f);
            Solid("侧室末端挡块", 79.3f, floor + 1.6f, .6f, 1.6f);
            Notice(65.5f, floor, "调度室", "这里的踏板只认箱子，小车自己压上去不算。\n先调整箱子离踏板的距离，再开车。\n到门前等同号踏板压住，门开了再走。");
        }
        else if (index == 1)
        {
            Environment("WaterPool.prefab", "侧室性质来源", 69, floor + 1.5f, 3, 3);
            Environment("墙面.prefab", "侧室可穿墙练习", 76, floor + 2, .8f, 4);
            Notice(65.5f, floor, "性质练习室", "抽出水池顶部的触发器性质，并确认。\n把它放进墙的顶部独特槽，再确认。\n墙就能穿过；只抽游泳能力不会开墙。");
        }
        else if (index == 3)
            Notice(65.5f, floor, "检修休息台", "箱子不会自动带着你走，要左右跟住箱顶。\n先在模拟里练习，再开始逃生。\n逃生开始时箱子回原位，已确认的地面修改保留。");
        else
            Notice(65.5f, floor, "高厅侧廊", "先看好下一处能露出水面的地方。\n折返长廊淹没后，氧气未必够用。\n可以先改好捷径，再开始逃生。");
    }
    static void ToxicCrossing(Scene scene)
    {
        var first = All<BasicPhysicalObject>(scene).First(c => c.name == "电面横渡_0");
        Object.DestroyImmediate(first.gameObject);
        var ice = All<BaseGround>(scene).First(c => c.name == "ICE发射跑道_0");
        Resize(ice.gameObject, new Vector2(14.5f, .4f), new Vector2(17.25f, 11.8f));
        Set(ice, "slowingEffect", .02f); Set(ice, "jumpHeightNum", 0f);
        var iceSO = new SerializedObject(ice);
        foreach (string field in new[] { "speedChangeNum", "phySpeed" })
        {
            var p = iceSO.FindProperty(field);
            if (p != null) { if (p.propertyType == SerializedPropertyType.Vector2) p.vector2Value = Vector2.zero; else p.floatValue = 0; }
        }
        iceSO.ApplyModifiedPropertiesWithoutUndo();
        var stop = All<BoxCollider2D>(scene).First(c => c.name == "普通止动地_0");
        Resize(stop.gameObject, new Vector2(19.5f, .4f), new Vector2(54.25f, 11.8f));
        // 原宽托台、分隔墙和缓冲台不能留在新毒槽里成为垫脚石或暗地板。
        foreach (var c in All<BoxCollider2D>(scene).ToArray())
        {
            if (c.transform.IsChildOf(root) || c == ice.GetComponent<BoxCollider2D>() || c == stop || c.isTrigger || c.GetComponent<Rigidbody2D>()) continue;
            var b = c.bounds;
            if (b.max.x > 24.45f && b.min.x < 44.5f && b.max.y > 8.9f && b.min.y < 15.5f)
                Object.DestroyImmediate(c.gameObject);
        }
        Solid("毒槽左下封口", 24, 12, 1, 12);
        Solid("毒槽左上封口", 24, 22.2f, 1, 6.7f);
        Solid("毒槽顶棚", 34.5f, 16.3f, 21, .8f);
        Solid("毒槽深底", 34.5f, 9, 21);
        Solid("毒槽收箱挡块", 53, 13.5f, .6f, 1.5f);
        var toxic = Environment("绿色水体.prefab", "致死液体_必经槽", 34.5f, 10.5f, 20, 3);
        toxic.GetComponent<SpriteRenderer>().sortingOrder = 2;
        // 左仓顶层封闭，所有普通出口位于毒槽对岸；涨水不能绕过首段。
        foreach (var goal in All<SceneProp>(scene).Where(p => p.name.StartsWith("通关_") && p.transform.position.x < 24))
        { goal.transform.position = new Vector3(54.25f, 14.85f, goal.transform.position.z); goal.name = "通关_安全渡槽"; }
        RebuildLowMap(scene);
        SetCalculations(All<SceneLevelSetup>(scene).Single());
        scene.GetRootGameObjects().First(g => g.name == "RisingWaterMaximum").transform.position = new Vector3(0, 11.2f, 0);
        Notice(17.5f, 12, "毒液渡槽", "绿色液体碰到就会倒下，变硬后也一样。\n先抽出顶部触发器性质，确认变成实心。\n选地面阻力那一栏，连用两次 0.2，把阻力降到 0.04。");
        Notice(21.5f, 8.2f, "渡槽操作", "先推箱子加速，松开后跳上箱顶。\n在箱顶左右移动，跟住滑行的箱子。\n别踩绿色表面；对岸挡块会收住箱子。");
    }
    static void RebuildLowMap(Scene scene)
    {
        foreach (var c in All<MapCell>(scene).Where(c => c.Center.y < 36).ToArray()) Object.DestroyImmediate(c.gameObject);
        foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith("固定安置托台_") && t.position.y < 35).ToArray())
            Object.DestroyImmediate(t.gameObject);
        foreach (float y in new[] { 6f, 16f, 24f, 32f })
        {
            float h = y == 6 ? 12 : 8;
            Cell(12, y, 24, h, "毒槽左岸_" + y, y - 1.8f);
            Cell(54.25f, y, 19.5f, h, "毒槽右岸_" + y, y - 1.8f);
        }
        Cell(34.25f, 18, 20.5f, 36, "毒槽检修顶台", 16.5f);
    }
    static void SetCalculations(SceneLevelSetup setup)
    {
        var so = new SerializedObject(setup); var a = so.FindProperty("initialCognitionCalculations");
        float[] values = { 2f, .2f, .2f, .2f }; a.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) a.GetArrayElementAtIndex(i).floatValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void LinkMap(Scene scene)
    {
        var setup = All<SceneLevelSetup>(scene).Single();
        var cells = All<MapCell>(scene).ToArray(); var so = new SerializedObject(setup); var a = so.FindProperty("mapCells"); a.arraySize = cells.Length;
        for (int i = 0; i < cells.Length; i++) a.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void BuildExtension()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("09_P25关卡增量").transform;
        var level = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NecessaryAssets/SceneLevel.prefab"));
        var setup = level.GetComponent<SceneLevelSetup>(); Set(setup, "teaching", false); Set(setup, "mapEnabled", true); Set(setup, "initialCognition", 100f);
        level.transform.Find("BodySpawn").position = new Vector3(4, 1.225f, 0); level.transform.Find("SpiritSpawn").position = new Vector3(4, 4, 0);
        SetCalculations(setup);
        var camera = new GameObject("Main Camera"); camera.tag = "MainCamera"; camera.transform.position = new Vector3(8, 5, -10);
        var c = camera.AddComponent<Camera>(); c.orthographic = true; c.orthographicSize = 5.3f; c.backgroundColor = new Color(.045f, .055f, .08f); c.clearFlags = CameraClearFlags.SolidColor;
        camera.AddComponent<AudioListener>(); camera.AddComponent<MainCamera>();
        Solid("检修站底板", 40, 0, 80, .6f); Solid("检修站左墙", -.5f, 45, 1, 46); Solid("检修站右墙", 80.5f, 45, 1, 46); Solid("检修站顶棚", 40, 45, 82, 1);
        Solid("箱车底轨", 26, 2, 28); Solid("上层行人走道", 26, 6, 28);
        Solid("上层低顶", 26, 10.6f, 28, .6f); Solid("入口上隔墙", 12, 45, .7f, 34.8f);
        Solid("入口下隔墙", 12, 2, .7f, 2); Solid("入口中隔墙", 12, 6, .7f, 1.8f);
        Solid("轨道末端封口", 40, 6, .7f, 6); Solid("轨道收箱挡块", 38.5f, 3.6f, .6f, 1.6f);
        foreach (var p in new[] { new Vector2(6, 2), new Vector2(9, 4), new Vector2(10.5f, 6) }) Solid("入场阶梯_" + p.x, p.x, p.y, 2.4f, .4f, true);
        Cart(16, 2, 1.2f); Box(30.5f, 2, "待调整时机箱");
        Plate(23, 2, 26, 6, 1, 4); Plate(35, 2, 34, 6, 2, 4);
        Label("箱子起跑线", "↓", new Vector2(19.5f, 2.5f));
        Notice(5, 0, "检修站说明", "先到下层看看箱子和两块踏板。\n箱子已停过第一块踏板，要先拉回起跑线。\n再开车，回上层，按编号过门。\n开始逃生时箱车复位，要再摆一次。");
        Notice(17, 2, "调整箱距", "只认箱子的踏板，空车经过不会开门。\n箱子离踏板越远，门开得越晚。\n先在模拟里试，给自己留出上楼和过门的时间。");
        foreach (var p in new[] { new Vector2(41, 8), new Vector2(43, 10), new Vector2(45, 12) }) Solid("出门阶梯_" + p.x, p.x, p.y, 3, .4f, true);
        var launch = Environment("地板.prefab", "渡槽发射台", 46, 11.8f, 4, .4f);
        Set(launch.GetComponent<BaseGround>(), "slowingEffect", .02f);
        Set(launch.GetComponent<BaseGround>(), "jumpHeightNum", 0f);
        Box(46.1f, 12, "检修站滑箱", 2.8f);
        Solid("渡槽下隔墙", 48, 12, .8f, 12); Solid("渡槽上隔墙", 48, 45, .8f, 28f);
        Solid("检修站毒槽低顶", 58, 17.8f, 21, .8f); Solid("检修站毒槽深底", 58, 9, 21);
        Environment("绿色水体.prefab", "检修站致死液体", 58, 10.5f, 20, 3);
        Solid("检修站对岸", 74, 12, 12); Solid("检修站收箱挡块", 76, 13.5f, .6f, 1.5f);
        Notice(43.5f, 12, "最后的渡槽", "先变实心，再用两次 0.2 让地面变滑。\n走得慢时再用一次 0.2，留足滑行距离。\n推箱、登箱、跟住它，到对岸再下地。\n变硬的绿色地面也不能直接踩。");
        Cell(20, 20, 40, 40, "检修站前厅", 18.2f); Cell(60, 20, 40, 40, "检修站后厅", 18.2f);
        var goal = Instance("prop/SceneProp 6.prefab", "通关_检修完成", new Vector2(74, 12.65f));
        Set(goal.GetComponent<SceneProp>(), "clearsLevel", true); Set(goal.GetComponent<SceneProp>(), "hasEffect", false); Set(goal.GetComponent<SceneProp>(), "endingContribution", Vector2.zero);
        var water = Environment("WaterPool.prefab", "检修站上涨主水", 40, -2.75f, 80, 1.5f);
        var limit = new GameObject("RisingWaterMaximum"); limit.transform.position = new Vector3(0, 11.2f, 0);
        Set(setup, "risingWater", water.GetComponent<WaterRegion>()); Set(setup, "risingWaterMaximum", limit.transform); Set(setup, "waterRiseSpeed", .18f);
        LinkMap(scene); P25LevelVisualTools.Apply(scene);
        // 新关复用配电层静态远景，不复制机关或旧关照明状态。
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/service-annex-v1.png");
        if (sprite)
        {
            var bg = new GameObject("P25_检修站远景"); var sr = bg.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingOrder = -100; sr.color = new Color(.55f, .58f, .68f);
            bg.transform.position = new Vector3(40, 22, 5); float scale = Mathf.Max(98 / sprite.bounds.size.x, 56 / sprite.bounds.size.y); bg.transform.localScale = Vector3.one * scale;
        }
        EditorSceneManager.SaveScene(scene, Folder + Extension + ".unity");
    }
    static void RegisterExtension()
    {
        string path = Folder + Extension + ".unity";
        var builds = EditorBuildSettings.scenes.ToList();
        if (!builds.Any(s => s.path == path)) { builds.Add(new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = builds.ToArray(); }
        var order = JObject.Parse(File.ReadAllText("Assets/Resources/LevelOrder.json"));
        var scenes = (JArray)order["scenes"];
        if (!scenes.Values<string>().Contains(Extension)) { scenes.Add(Extension); File.WriteAllText("Assets/Resources/LevelOrder.json", order.ToString(Formatting.Indented)); AssetDatabase.ImportAsset("Assets/Resources/LevelOrder.json"); }
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!settings) throw new InvalidOperationException("缺少当前 Addressables 设置。");
        var original = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(Folder + Names[4] + ".unity"));
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), original != null ? original.parentGroup : settings.DefaultGroup);
        entry.address = Extension; EditorUtility.SetDirty(settings);
    }

    static void RepairBackdrops(Scene scene)
    {
        foreach (var sr in All<SpriteRenderer>(scene).Where(s => s.sprite && s.sortingOrder <= -90 && s.bounds.size.x > 60))
        {
            float targetHeight = Mathf.Max(sr.bounds.size.y, 56);
            float scale = Mathf.Max(100 / sr.sprite.bounds.size.x, targetHeight / sr.sprite.bounds.size.y);
            sr.drawMode = SpriteDrawMode.Simple; sr.transform.localScale = new Vector3(scale, scale, 1);
            sr.transform.position = new Vector3(40, sr.transform.position.y, sr.transform.position.z);
        }
    }

    [MenuItem("自定义工具/关卡/P25/修订水位与更新全部表现")]
    public static void RefineInstalled()
    {
        string previous = SceneManager.GetActiveScene().path;
        try
        {
            foreach (string name in Names)
            {
                var scene = EditorSceneManager.OpenScene(Folder + name + ".unity");
                P25LevelVisualTools.Apply(scene); RepairBackdrops(scene);
                root = scene.GetRootGameObjects().Single(g => g.name == "09_P25关卡增量").transform;
                square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
                if (name == Names[2] && !All<Transform>(scene).Any(t => t.name == "P25_侧室门楣_1"))
                {
                    Solid("侧室门楣_1", 74, 56.2f, .85f, 3.5f);
                    Solid("侧室门楣_2", 78, 56.2f, .85f, 3.5f);
                }
                if (name == Extension)
                {
                    // x40..44 留完整跳井，避免低顶与发射台拼成不足一人的暗缝。
                    var launch = All<Transform>(scene).Single(t => t.name == "P25_渡槽发射台");
                    if (!launch.GetComponent<BaseGround>())
                    {
                        Object.DestroyImmediate(launch.gameObject);
                        launch = Environment("地板.prefab", "渡槽发射台", 46, 11.8f, 4, .4f).transform;
                    }
                    Resize(launch.gameObject, new Vector2(4, .4f), new Vector2(46, 11.8f));
                    Set(launch.GetComponent<BaseGround>(), "slowingEffect", .02f); Set(launch.GetComponent<BaseGround>(), "jumpHeightNum", 0f);
                    var box = All<PhysicalBox>(scene).Single(b => b.name == "P25_检修站滑箱");
                    box.transform.position = new Vector3(46.1f, 12.77f, 0); Calibrate(box);
                    Resize(All<Transform>(scene).Single(t => t.name == "P25_检修站毒槽低顶").gameObject, new Vector2(21, .8f), new Vector2(58, 17.4f));
                    Resize(All<Transform>(scene).Single(t => t.name == "P25_渡槽上隔墙").gameObject, new Vector2(.8f, 28), new Vector2(48, 31));
                }
                foreach (var wide in All<PhysicalBox>(scene).Where(b => b.GetComponent<BoxCollider2D>().size.x > 2f)) Calibrate(wide);
                if (name == Names[3] || name == Extension)
                {
                    // 水面不淹没槽口，防止用主水游泳绕过毒液滑箱解法。
                    var limit = scene.GetRootGameObjects().First(g => g.name == "RisingWaterMaximum");
                    limit.transform.position = new Vector3(0, 11.2f, 0);
                }
                if (name == Names[3])
                {
                    var goal = All<SceneProp>(scene).First(p => p.name.StartsWith("通关_") && (p.name.Contains("等待") || p.name.Contains("安全渡槽")));
                    goal.transform.position = new Vector3(54.25f, 14.85f, goal.transform.position.z); goal.name = "通关_安全渡槽";
                    foreach (var cell in All<MapCell>(scene).Where(c => c.Marker == "出口" && c.Center.x < 24)) Set(cell, "marker", "");
                    Set(All<MapCell>(scene).First(c => c.name == "P25_地图_毒槽右岸_16"), "marker", "出口");
                    var sign = All<Signboard>(scene).First(s => s.name == "电面关路线");
                    P25LevelVisualTools.SetNotice(sign, "先处理绿色渡槽，再借箱子滑到对岸。\n对岸检修台可以完成本关。\n想继续向上探索，可挑战反向滑箱和更高的出口。");
                    P25LevelVisualTools.SetNotice(All<Signboard>(scene).First(s => s.name == "P25_毒液渡槽"), "绿色液体碰到就会倒下，变硬后也一样。\n先抽出顶部触发器性质，确认变成实心。\n选地面阻力那一栏，连用两次 0.2，把阻力降到 0.04。");
                }
                if (name == Extension)
                {
                    P25LevelVisualTools.SetNotice(All<Signboard>(scene).First(s => s.name == "P25_检修站说明"), "先到下层看看箱子和两块踏板。\n箱子已停过第一块踏板，要先拉回起跑线。\n再开车，回上层，按编号过门。\n开始逃生时箱车复位，要再摆一次。");
                    P25LevelVisualTools.SetNotice(All<Signboard>(scene).First(s => s.name == "P25_最后的渡槽"), "先变实心，再用两次 0.2 让地面变滑。\n走得慢时再用一次 0.2，留足滑行距离。\n推箱、登箱、跟住它，到对岸再下地。\n变硬的绿色地面也不能直接踩。");
                }
                P25LevelVisualTools.Apply(scene);
                if (name == Names[3]) P25LevelVisualTools.SetNotice(All<Signboard>(scene).First(s => s.name == "电面关路线"), "先处理绿色渡槽，再借箱子滑到对岸。\n对岸检修台可以完成本关。\n想继续向上探索，可挑战反向滑箱和更高的出口。");
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets(); Debug.Log("P25_REFINEMENT_SAVED");
        }
        finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
    }

    [MenuItem("自定义工具/关卡/P25/生成原生场景预览")]
    public static void RenderPreviews()
    {
        string previous = SceneManager.GetActiveScene().path;
        string dir = "Docs/待办具体事务文档集中/P_22-关卡预览/P25/"; Directory.CreateDirectory(dir);
        try
        {
            foreach (var shot in new[] {
                (Names[1], new Vector2(72,26.2f), 8f, "L01_性质侧室"),
                (Names[2], new Vector2(72,49.7f), 8f, "L02_箱位调度室"),
                (Names[3], new Vector2(35.5f,16), 12f, "L03_致死渡槽"),
                (Names[4], new Vector2(72,92.2f), 8f, "L04_高厅侧廊"),
                (Extension, new Vector2(40,22), 26f, "L05_检修站全景") })
            {
                var scene = EditorSceneManager.OpenScene(Folder + shot.Item1 + ".unity");
                var camera = All<Camera>(scene).First(c => c.orthographic);
                camera.transform.position = new Vector3(shot.Item2.x, shot.Item2.y, -10); camera.orthographicSize = shot.Item3; camera.aspect = 1.6f;
                var rt = new RenderTexture(1600, 1000, 24); var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false); var previousRT = RenderTexture.active;
                try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0,0,1600,1000),0,0); tex.Apply(); File.WriteAllBytes(dir + shot.Item4 + ".png", tex.EncodeToPNG()); }
                finally { camera.targetTexture = null; RenderTexture.active = previousRT; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            }
            Debug.Log("P25_PREVIEWS_SAVED " + dir);
        }
        finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
    }

    [MenuItem("自定义工具/关卡/P25/验证场景配置与安置")]
    public static void ValidateScenes()
    {
        string previous = SceneManager.GetActiveScene().path; var log = new StringBuilder(); int count = 0, failed = 0;
        Action<bool, string> check = (ok, message) => { count++; if (!ok) { failed++; log.AppendLine("FAIL " + message); } };
        try
        {
            foreach (string name in Names)
            {
                var scene = EditorSceneManager.OpenScene(Folder + name + ".unity"); Physics2D.SyncTransforms();
                var setup = All<SceneLevelSetup>(scene).Single(); check(setup.ValidateEscapePreparation(out string error), name + " setup " + error);
                if (name == Names[3] || name == Extension)
                {
                    var maximum = (Transform)new SerializedObject(setup).FindProperty("risingWaterMaximum").objectReferenceValue;
                    check(maximum && maximum.position.y <= 11.2f, name + " 主水不得淹没必经毒槽，避免游泳旁路");
                }
                var cells = All<MapCell>(scene).ToArray();
                if (cells.Length > 0)
                {
                    var record = new LevelMapRecord(cells, new LevelCognition(100), scene);
                    check(record.IsValid, name + " map " + record.ConfigurationError);
                }
                foreach (var plate in All<MechanismPressurePlate>(scene)) check(plate.ValidateConfiguration(out error), name + " plate " + error);
                foreach (var cart in All<MechanismCart>(scene)) check(cart.ValidateConfiguration(out error), name + " cart " + error);
                foreach (var door in All<Taijie>(scene)) check(door.ValidateMechanismConfiguration(out error), name + " door " + error);
                foreach (var cell in cells)
                {
                    Rect reserve = new Rect(cell.Center - new Vector2(.9f, 1.05f), new Vector2(1.8f, 2.1f));
                    foreach (var shape in All<BoxCollider2D>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && (c.GetComponent<BasicPhysicalObject>() != setup.RisingWater) && (!c.isTrigger || c.GetComponent<BasicPhysicalObject>())))
                    {
                        Bounds b = shape.bounds; check(!reserve.Overlaps(new Rect(b.min.x, b.min.y, b.size.x, b.size.y)), name + " map reserve " + cell.name + " / " + shape.name);
                    }
                }
                foreach (var sign in All<Signboard>(scene))
                {
                    var text = sign.GetComponentInChildren<Text>(true); Canvas.ForceUpdateCanvases();
                    check(text && text.preferredHeight <= text.rectTransform.rect.height + 2, name + " sign overflow " + sign.name);
                }
                log.AppendLine(name + ": " + cells.Length + " cells; " + All<Signboard>(scene).Count() + " signs");
            }
        }
        finally { if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
        Directory.CreateDirectory(".utmp/P25Validation");
        File.WriteAllText(".utmp/P25Validation/scene-checks.txt", (failed == 0 ? "PASS " : "FAIL ") + count + " checks, failures=" + failed + "\n" + log);
        if (failed > 0) throw new InvalidOperationException("P25 场景验证失败 " + failed + "，见 .utmp/P25Validation/scene-checks.txt");
        Debug.Log("P25_SCENES_PASS " + count);
    }

    [MenuItem("自定义工具/关卡/P25/重建当前Addressables")]
    public static void BuildAddressables()
    {
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
        Directory.CreateDirectory(".utmp/P25Validation");
        File.WriteAllText(".utmp/P25Validation/addressables-build.txt", "PASS\nOutputPath=" + result.OutputPath + "\nTarget=" + EditorUserBuildSettings.activeBuildTarget + "\n");
        Debug.Log("P25_ADDRESSABLES_PASS " + result.OutputPath);
    }
}
#endif
