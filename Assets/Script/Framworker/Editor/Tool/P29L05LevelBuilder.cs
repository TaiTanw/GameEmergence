#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P29 的一次性 L05 制作入口；只显式调用，不在导入或打开场景时重建人工地图。</summary>
public static class P29L05LevelBuilder
{
    const string ScenePath = "Assets/MyObjAssets/Scenes/Designed/L05_DepartureWorks.unity";
    const string Pf = "Assets/MyObjAssets/gameObject/Pf/SceneObj/NewPf/";
    static Scene scene;
    static Transform content;
    static Sprite square;
    static Material material;
    static readonly List<MapCell> cells = new List<MapCell>();

    public static string Build()
    {
        if (Application.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("须在空闲 Edit Mode 制作 L05。");
        scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("L05 存在未保存的人工改动。");
        SceneManager.SetActiveScene(scene);
        square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/MyObjAssets/gameObject/Art/Image/Square.png");
        material = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + "P29_带电压板.prefab")
            .GetComponentInChildren<SpriteRenderer>(true).sharedMaterial;
        SceneLevelSetup setup = All<SceneLevelSetup>().Single();
        SceneProp oldGoal = All<SceneProp>().Single(p => p.name == "通关_检修完成");
        GameObject goal = Object.Instantiate(oldGoal.gameObject);
        goal.name = "通关_检修完成";
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == "09_P25关卡增量" || root.name == "08_P25近景与装饰" || root.name == "P29_05_竖直四层")
                Object.DestroyImmediate(root);
        content = Group("P29_05_竖直四层", null);
        cells.Clear();
        Enclosure();
        FirstFloor();
        SecondFloor();
        ThirdFloor();
        FourthFloor();
        ApplyTextures();
        goal.transform.SetParent(content, true);
        goal.transform.position = new Vector3(49, 80.7f, 0);
        Physics2D.SyncTransforms();
        ConfigureMap(setup);
        ConfigureLevel(setup);
        ResizeBackdrops();
        Camera camera = All<Camera>().First(c => c.orthographic);
        camera.transform.position = new Vector3(10, 6, -10);
        camera.orthographicSize = 7f;
        Physics2D.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("L05 保存失败。");
        return Check();
    }

    static IEnumerable<T> All<T>() where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
    static GameObject Instance(string stem, string name, Transform parent, float x, float y, float z = 0)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Pf + stem + ".prefab");
        if (!asset) throw new InvalidOperationException("缺少预设 " + stem);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(x, y, z);
        return go;
    }
    static void Set(Object component, string name, object value)
    {
        var so = new SerializedObject(component);
        var p = so.FindProperty(name);
        if (p == null) throw new InvalidOperationException(component.name + ": " + name);
        if (value is Object obj) p.objectReferenceValue = obj;
        else if (value is float f) p.floatValue = f;
        else if (value is int i) p.intValue = i;
        else if (value is bool b) p.boolValue = b;
        else if (value is Vector2 v) p.vector2Value = v;
        else if (value is string text) p.stringValue = text;
        else throw new ArgumentException(name);
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Parameters(BasicPhysicalObject host, params EnvironmentParameter[] values)
    {
        var so = new SerializedObject(host);
        var p = so.FindProperty("environmentParameters");
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).managedReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static GroundSlowingParameter Resistance(float value, params float[] points)
    {
        var result = new GroundSlowingParameter(value);
        foreach (float point in points)
            if (!result.TryAttachCalculation(new EnvironmentCalculationPoint(point))) throw new InvalidOperationException("计算点配置失败");
        return result;
    }
    static GameObject Solid(Transform parent, string name, float x, float top, float width, float height = .5f, bool oneWay = false)
    {
        var go = Group(name, parent).gameObject;
        go.layer = LayerMask.NameToLayer("Ground");
        go.transform.position = new Vector3(x, top - height * .5f, 0);
        go.transform.localScale = new Vector3(width, height, 1);
        var shape = go.AddComponent<BoxCollider2D>();
        shape.size = Vector2.one;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = square;
        sr.sharedMaterial = material;
        sr.color = new Color(.24f, .29f, .34f, 1);
        if (oneWay)
        {
            var effector = go.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.surfaceArc = 170;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
            shape.usedByEffector = true;
        }
        return go;
    }
    static void ScaleTo(GameObject go, float width, float height)
    {
        var box = go.GetComponent<BoxCollider2D>();
        go.transform.localScale = new Vector3(width / box.size.x, height / box.size.y, 1);
    }
    static GameObject Floor(Transform parent, string name, float x, float top, float width, GroundSlowingParameter slowing)
    {
        var go = Instance("P29_可缩放地面", name, parent, x, top - .2f);
        ScaleTo(go, width, .4f);
        Parameters(go.GetComponent<BaseGround>(), slowing, new GroundJumpMultiplierParameter(1));
        return go;
    }
    static PhysicalBox Box(Transform parent, string name, float x, float floor, bool electric = false, bool small = false, bool wide = false)
    {
        var go = Instance(electric ? "P29_带电箱" : wide ? "P29_宽箱" : "P29_普通箱", name, parent, x, 0);
        if (small) go.transform.localScale = new Vector3(1, .6f, 1);
        var shape = go.GetComponent<BoxCollider2D>();
        go.transform.position = new Vector3(x, floor + (shape.size.y * .5f - shape.offset.y) * go.transform.localScale.y + .015f, 0);
        var box = go.GetComponent<PhysicalBox>();
        Calibrate(box);
        return box;
    }
    static void Calibrate(BasicEntity entity)
    {
        var shape = entity.GetComponent<BoxCollider2D>();
        var so = new SerializedObject(entity);
        string[] names = { "groundV", "upV", "leftV", "rightV" };
        Vector2[] directions = { Vector2.down, Vector2.up, Vector2.left, Vector2.right };
        for (int i = 0; i < names.Length; i++)
        {
            var point = so.FindProperty(names[i]).objectReferenceValue as Transform;
            if (point) point.localPosition = shape.offset + Vector2.Scale(shape.size * .5f, directions[i]);
        }
    }
    static MechanismCart Cart(Transform parent, string name, float x, float floor, float speed)
    {
        var go = Instance("P29_贴轨小车", name, parent, x, 0);
        var shape = go.GetComponent<BoxCollider2D>();
        go.transform.position = new Vector3(x, floor + (shape.size.y * .5f - shape.offset.y) * go.transform.localScale.y + .015f, 0);
        var cart = go.GetComponent<MechanismCart>();
        Set(cart, "speed", speed);
        Set(cart, "initiallyOn", false);
        Set(cart, "direction", 1);
        Calibrate(cart);
        return cart;
    }
    static MechanismPressurePlate Pair(Transform parent, string name, bool electric, float plateX, float floor, float doorX, float doorFloor, Vector2 offset)
    {
        var go = Instance(electric ? "P29_带电压板" : "P29_普通压板", name, parent, plateX, floor + .07f);
        var plate = go.GetComponentInChildren<MechanismPressurePlate>(true);
        var door = plate.TargetComponent as Taijie;
        plate.transform.position = new Vector3(plateX, floor + .07f, 0);
        ScaleTo(plate.gameObject, electric ? 1f : 1.1f, .18f);
        Set(plate, "boxesOnly", true);
        door.transform.position = new Vector3(doorX, doorFloor + 2, 0);
        ScaleTo(door.gameObject, .9f, 4);
        Set(door, "mechanismOffset", offset);
        Set(door, "mechanismTravelTime", .65f);
        plate.GetComponent<SpriteRenderer>().sortingOrder = 5;
        StylePressurePlate(plate);
        return plate;
    }
    public static void StylePressurePlate(MechanismPressurePlate plate)
    {
        bool electric = plate.Kind == MechanismPressurePlate.PressureKind.ElectrifiedBoxOnly;
        var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/P25/interactive-atlas-v1.png")
            .OfType<Sprite>().Single(s => s.name == (electric ? "P25_PressurePlate" : "P25_Platform"));
        var sr = plate.GetComponent<SpriteRenderer>();
        var shape = plate.GetComponent<BoxCollider2D>();
        Vector2 size = shape.size, offset = shape.offset;
        Vector3 scale = plate.transform.localScale;
        sr.sprite = sprite;
        sr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = Vector2.Scale(size, new Vector2(1, electric ? 1.7f : 1.3f));
        sr.color = electric ? Color.white : new Color(1f, .72f, .3f, 1f);
        sr.enabled = true;
        plate.transform.localScale = scale;
        shape.autoTiling = false; shape.size = size; shape.offset = offset;
        foreach (var obj in new Object[] { sr, shape, plate.transform })
        {
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
    }
    public static void ConfigureRunwayGround(GameObject go)
    {
        var ground = go.GetComponent<BaseGround>();
        if (!ground) ground = go.AddComponent<BaseGround>();
        var movement = new GroundMovementMultiplierParameter(1f);
        movement.TryAttachCalculation(new EnvironmentCalculationPoint(.2f));
        Parameters(ground, movement, Resistance(1f), new GroundJumpMultiplierParameter(1f));
        EditorUtility.SetDirty(ground);
    }
    static void Notice(Transform parent, string name, float x, float floor, string text)
    {
        var go = Instance("P29_提示牌", name, parent, x, floor);
        P25LevelVisualTools.SetNotice(go.GetComponent<Signboard>(), text);
    }
    static void Number(Transform parent, string text, float x, float y)
    {
        var go = Group("层号_" + text, parent).gameObject;
        go.transform.position = new Vector3(x, y, -.1f);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.fontSize = 64;
        tm.characterSize = .6f;
        tm.color = new Color(.7f, .8f, .84f);
        go.GetComponent<MeshRenderer>().sortingOrder = 3;
    }
    static void Enclosure()
    {
        var g = Group("00_固定围护", content);
        Solid(g, "主槽深底", 40, -3.5f, 80, .8f);
        Solid(g, "左外墙", -.5f, 86, 1, 90);
        Solid(g, "右外墙", 80.5f, 86, 1, 90);
        Solid(g, "总顶棚", 40, 86.5f, 82, 1);
        var left = Solid(g, "左侧机关收回遮板", -4, 85, 7, 86);
        var right = Solid(g, "右侧机关收回遮板", 85, 85, 9, 86);
        left.GetComponent<SpriteRenderer>().sortingOrder = right.GetComponent<SpriteRenderer>().sortingOrder = 8;
        left.GetComponent<SpriteRenderer>().color = right.GetComponent<SpriteRenderer>().color = new Color(.065f, .085f, .11f, 1);
    }
    static void FirstFloor()
    {
        var g = Group("01_底层_墙角摆箱", content);
        Solid(g, "底层地面", 40, 2, 80);
        Solid(g, "压板轨道止挡_连通上走道", 42.5f, 8, 1, 6);
        Solid(g, "底层上走道", 39, 8, 50, .5f);
        Solid(g, "底层封顶_只留右出口", 33, 12.2f, 66, .5f);
        Solid(g, "上走道入口一级", 8, 4, 3, .4f, true);
        Solid(g, "上走道入口二级", 11, 6, 3, .4f, true);
        Cart(g, "底层_右行小车", 14, 2, 1.5f);
        Box(g, "底层_隔离箱", 18, 2);
        Box(g, "底层_带电箱", 21.3f, 2, true);
        Box(g, "底层_靠前普通箱", 24.6f, 2);
        Box(g, "底层_末端普通箱", 28, 2);
        Pair(g, "底层_电板与出口门", true, 39.4432f, 2, 50, 8, new Vector2(34, 0));
        float[] tops = { 8.7f, 11.2f, 13.7f, 16.2f, 18.7f, 21.2f };
        for (int i = 0; i < tops.Length; i++) Solid(g, "右侧上二层踏台_" + i, i == 0 ? 74 : i % 2 == 0 ? 73 : 77, tops[i], 3.6f, .4f, true);
        Solid(g, "出口到爬升井", 67.5f, 8, 7, .5f);
        Notice(g, "墙角提示", 5, 2, "顶住墙以后，位置就不会再变了。");
        Number(g, "01", 5, 7);
    }
    static void SecondFloor()
    {
        var g = Group("02_二层_低顶毒水渡槽", content);
        Floor(g, "右岸_可借计算点", 72.5f, 22, 5, Resistance(1, .2f));
        Solid(g, "左岸", 20, 22, 36);
        Solid(g, "毒槽深底", 48, 19, 20, .6f);
        Solid(g, "毒槽右下封口", 58.5f, 21.8f, 1, 5.8f);
        Solid(g, "毒槽右上封口", 53.5f, 42, 1, 16.2f);
        Solid(g, "毒槽不可越过顶棚", 46, 26.3f, 16, .8f);
        Solid(g, "二层封顶_只留左井", 46.5f, 30.5f, 67, .5f);
        Solid(g, "左岸收箱挡块", 32.5f, 23.5f, .6f, 1.5f);
        var water = Instance("P29_可缩放毒水", "二层_高阻力毒水", g, 48, 20.5f, -2);
        ScaleTo(water, 20, 3);
        Parameters(water.GetComponent<WaterRegion>(), Resistance(5));
        var launch = Instance("P29_可缩放冰面", "二层_左向发射地", g, 64, 21.8f);
        ScaleTo(launch, 12, .4f);
        var ice = launch.GetComponent<IceGround>();
        Parameters(ice, Resistance(.02f), new GroundJumpMultiplierParameter(1));
        Set(ice, "speedChangeNum", 0f); Set(ice, "phySpeed", Vector2.zero); Set(ice, "Idex", 2f); Set(ice, "Odex", 1f);
        Box(g, "二层_横渡宽箱", 63.6f, 22, wide: true);
        var chase = Solid(g, "入口上方_加速可下落平台", 62, 23.9f, 22, .18f, true);
        var speed = chase.AddComponent<Taijie>();
        Parameters(speed, Resistance(1, .2f), new GroundJumpMultiplierParameter(1), new MovementSpeedOffsetParameter(1.5f));
        Set(speed, "speedChangeNum", 1.5f);
        for (int i = 0; i < 8; i++) Solid(g, "左侧上三层踏台_" + i, i % 2 == 0 ? 7 : 11, 24.5f + i * 2.4f, 3.4f, .4f, true);
        Notice(g, "毒槽提示", 69, 22, "只带来的还不够。旧地面里也藏着余量。");
        Number(g, "02", 73, 27);
    }
    static void ThirdFloor()
    {
        var g = Group("03_三层_毒气箱链与三层门", content);
        Solid(g, "三层主轨与安全区", 40, 42, 80, .5f, true);
        Solid(g, "箱链末端止挡", 67.5f, 47.6f, 1, 5.6f);
        Solid(g, "贴顶毒气上盖与行人道", 45, 47.6f, 66, .6f);
        Solid(g, "安全区上走道踏台", 6, 44.8f, 4, .4f, true);
        Solid(g, "安全区上走道踏台_上", 10, 47.6f, 4, .4f, true);
        Cart(g, "三层_推送小车", 8.5f, 42, 2.4f);
        bool[] electric = { false, true, false, false, true, false, false, true, false };
        string[] names = { "尾部隔离箱", "带电箱_靠车", "后间距箱_外", "后间距箱_内", "带电箱_中央", "前间距箱_外", "前间距箱_内", "带电箱_靠止挡", "头部隔离箱" };
        float cursor = 11;
        for (int i = 0; i < electric.Length; i++)
        {
            float width = electric[i] ? 1.72f * .88f : 1.8f;
            Box(g, "三层_" + names[i], cursor + width * .5f, 42, electric[i]);
            cursor += width + 1.6f;
        }
        for (int i = 0; i < 4; i++)
        {
            var gas = Instance("P29_可缩放毒气", "贴顶毒气_" + i, g, 45 + i * 6, 44.7f, -2);
            ScaleTo(gas, 6.2f, 4.6f);
            var force = gas.GetComponent<ForceField>();
            Set(force, "add", 0f); Set(force, "maxSpeed", 0f); Set(force, "speedChangeNum", 0f);
            Parameters(force, new MovementSpeedOffsetParameter(0));
        }
        float[] px = { 57.816f, 61.1296f, 64.4432f };
        for (int i = 0; i < 3; i++) Pair(g, "三层_电板与门_" + (i + 1), true, px[i], 42, i == 1 ? 64 : 69, 47.6f + i * 4, new Vector2(17, 0));
        Solid(g, "门一顶棚与门二走道", 36.5f, 51.6f, 73, .4f);
        Solid(g, "右折返中间踏台", 76, 49.6f, 3.4f, .4f, true);
        Solid(g, "右折返踏台", 76, 51.6f, 3.4f, .4f, true);
        Solid(g, "门二顶棚与门三走道", 70, 55.6f, 20, .4f);
        Solid(g, "左折返中间踏台", 58, 53.6f, 3.4f, .4f, true);
        Solid(g, "左折返踏台", 58, 55.6f, 3.4f, .4f, true);
        Solid(g, "门三顶棚", 66.5f, 59.6f, 13, .4f);
        Solid(g, "离开三层中间踏台", 76, 57.6f, 3.4f, .4f, true);
        Solid(g, "右侧离开三层", 76, 59.6f, 3.4f, .4f, true);
        Solid(g, "上四层踏台", 76, 62, 3.4f, .4f, true);
        Notice(g, "箱链提示", 16, 42, "留下间隔，还是收起间隔？雾后的门要一起亮着。");
        Number(g, "03", 7, 47);
    }
    static void FourthFloor()
    {
        var g = Group("04_四层_冰面与长路二选一", content);
        Solid(g, "四层冰道发射岸", 6, 64, 12, .6f);
        ConfigureRunwayGround(Solid(g, "四层入口与分路厅", 54, 64, 40, .6f));
        Solid(g, "冰道与长路隔墙", 34.5f, 77, 1, 9.8f);
        Solid(g, "长路左封墙_底部留入口", 40.5f, 80.5f, 1, 14.7f);
        var iceGo = Instance("P29_可缩放冰面", "四层_冰壶滑道", g, 23, 63.8f);
        ScaleTo(iceGo, 22, .4f);
        var ice = iceGo.GetComponent<IceGround>();
        Parameters(ice, Resistance(2f), new GroundJumpMultiplierParameter(1));
        Set(ice, "speedChangeNum", 0f); Set(ice, "phySpeed", Vector2.zero); Set(ice, "Idex", 0f); Set(ice, "Odex", 5f);
        Box(g, "四层_滑行矮箱", 8, 64, small: true);
        Solid(g, "矮箱通道_行人不能追入", 23, 65.25f, 22, .25f);
        Pair(g, "四层_停箱压板与捷径门", false, 26, 64, 18, 70, new Vector2(-24, 0));
        Solid(g, "冰道上方捷径", 23, 70, 22, .4f);
        Solid(g, "捷径门上封顶", 14, 73.8f, 28, .4f);
        Solid(g, "捷径入口一级", 7, 66.6f, 4, .4f, true);
        Solid(g, "捷径入口二级", 9, 69.1f, 4, .4f, true);
        Solid(g, "捷径出口一级", 31, 72.5f, 4, .4f, true);
        Solid(g, "捷径出口二级", 31, 75, 4, .4f, true);
        Solid(g, "捷径出口三级", 31, 77.5f, 4, .4f, true);
        Solid(g, "捷径越墙落脚", 38, 79, 4, .4f, true);
        for (int i = 0; i < 4; i++)
        {
            float top = 68 + i * 4;
            var floor = i % 2 == 0 ? Solid(g, "长路折返层_" + i, 57, top, 32, .4f)
                : Solid(g, "长路折返层_" + i, 62.5f, top, 35, .4f);
            if (i < 3) ConfigureRunwayGround(floor);
            Solid(g, "长路转弯中间踏台_" + i, i % 2 == 0 ? 76 : 42, top - 2, 3.4f, .4f, true);
            Solid(g, "长路唯一转弯踏台_" + i, i % 2 == 0 ? 76 : 42, top, 3.4f, .4f, true);
        }
        Notice(g, "第四层选择", 43, 64, "左边等它停稳；右边，走完长路。");
        Notice(g, "冰道提示", 5, 64, "经过不等于停留。");
        Notice(g, "长路提示", 68, 64, "地面里的余量，也能借给冰道。");
        Number(g, "04", 70, 69);
    }
    /// <summary>只给已有地形关联贴图；不生成装饰，不改变世界碰撞范围。</summary>
    public static string ApplyTextures()
    {
        scene = SceneManager.GetSceneByPath(ScenePath);
        if (Application.isPlaying || !scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("须在 Edit Mode 打开 L05。");
        var root = scene.GetRootGameObjects().Single(g => g.name == "P29_05_竖直四层");
        var surfaces = AssetDatabase.LoadAllAssetsAtPath("Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/surface-atlas-v1.png").OfType<Sprite>().ToDictionary(s => s.name);
        var platform = AssetDatabase.LoadAllAssetsAtPath("Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/P25/interactive-atlas-v1.png").OfType<Sprite>().Single(s => s.name == "P25_Platform");
        var lit = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        int count = 0;
        Physics2D.SyncTransforms();
        foreach (var shape in root.GetComponentsInChildren<BoxCollider2D>(true))
        {
            var sr = shape.GetComponent<SpriteRenderer>();
            if (!sr || shape.isTrigger || shape.GetComponent<BasicEntity>() || shape.GetComponent<MechanismPressurePlate>()
                || shape.GetComponent<WaterRegion>() || shape.GetComponent<ForceField>() || shape.GetComponent<Signboard>()) continue;
            // 门继续使用已关联的机关图；本入口只给静态地形换皮。
            if (sr.sprite && sr.sprite.name == "P25_Gate") continue;
            if (shape.transform.childCount != 0)
                throw new InvalidOperationException("地形须保持独立碰撞框：" + shape.name);
            // 贴图平铺只影响显示，禁止 SpriteRenderer 的自动平铺回写碰撞框。
            shape.autoTiling = false;
            Bounds bounds = shape.bounds;
            Vector2 dimensions = bounds.size;
            Vector2 offset = bounds.center - shape.transform.position;
            shape.transform.localScale = Vector3.one;
            bool step = shape.GetComponent<PlatformEffector2D>() && dimensions.x <= 8;
            bool ice = shape.GetComponent<IceGround>();
            bool wall = dimensions.y > dimensions.x;
            bool runway = shape.GetComponent<BaseGround>() && (shape.name == "四层入口与分路厅" || shape.name.StartsWith("长路折返层_"));
            sr.sprite = step ? platform : surfaces[ice ? "Marble" : wall || shape.name.Contains("深底") || shape.name.Contains("左岸") ? "Concrete" : "Steel"];
            sr.drawMode = step ? SpriteDrawMode.Sliced : SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = dimensions;
            sr.color = ice ? new Color(.6f, .85f, 1f, 1f)
                : shape.name.Contains("遮板") ? new Color(.2f, .25f, .3f, 1f)
                : shape.name.Contains("加速") ? new Color(.58f, .88f, .85f, 1f)
                : runway ? new Color(1f, .75f, .4f, 1f)
                : step ? new Color(.87f, .91f, .95f, 1f) : new Color(.8f, .85f, .91f, 1f);
            sr.sharedMaterial = lit;
            sr.enabled = true;
            // Unity 切换精灵／绘制模式时会重算形状，须在全部显示赋值后恢复物理范围。
            shape.transform.localScale = Vector3.one;
            shape.autoTiling = false;
            shape.size = dimensions;
            shape.offset = offset;
            foreach (var obj in new Object[] { shape, shape.transform, sr })
            {
                EditorUtility.SetDirty(obj);
                if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            count++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return $"L05: {count} existing terrain renderers textured; no objects added.";
    }
    static void ConfigureMap(SceneLevelSetup setup)
    {
        var parent = Group("05_地图格_最大5x5_中心避障", content);
        var spirit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
        Vector2 bodySize = Vector2.Scale(spirit.GetComponent<BoxCollider2D>().size, spirit.transform.localScale);
        var blockers = All<Collider2D>().Where(c => c.enabled && c.gameObject.activeInHierarchy
            && (!c.isTrigger || c.GetComponent<FixedContactHazard>())).Select(c => c.bounds).ToArray();
        for (int col = 0; col < 16; col++)
        {
            float x = 2.5f + col * 5;
            var spans = blockers.Where(b => b.max.x > x - bodySize.x * .5f - .03f && b.min.x < x + bodySize.x * .5f + .03f)
                .Select(b => new Vector2(Mathf.Max(0, b.min.y), Mathf.Min(85, b.max.y)))
                .Where(v => v.x < v.y).OrderBy(v => v.x).ToList();
            spans.Add(new Vector2(85, 85));
            float bottom = 0;
            int row = 0;
            foreach (Vector2 span in spans)
            {
                if (span.x > bottom && span.x - bottom >= bodySize.y + .06f)
                {
                    int count = Mathf.CeilToInt((span.x - bottom) / 5f);
                    float height = (span.x - bottom) / count;
                    for (int i = 0; i < count; i++)
                    {
                        float y = bottom + height * (i + .5f);
                        var go = Instance("P29_地图格", $"地图_{col:00}_{row++:00}", parent, x, y);
                        var cell = go.GetComponent<MapCell>();
                        Set(cell, "width", 5f); Set(cell, "height", height - .002f);
                        bool guarded = col == 0 && y >= 40 && y <= 55 || col == 15 && y >= 30 && y <= 50;
                        Set(cell, "cognitionCost", (guarded ? 5f : .2f) * height / 5f);
                        Set(cell, "marker", col == 9 && y > 80 && y < 84 ? "出口" : "");
                        cells.Add(cell);
                    }
                }
                bottom = Mathf.Max(bottom, span.y);
            }
        }
        var so = new SerializedObject(setup);
        var refs = so.FindProperty("mapCells"); refs.arraySize = cells.Count;
        for (int i = 0; i < cells.Count; i++) refs.GetArrayElementAtIndex(i).objectReferenceValue = cells[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void ConfigureLevel(SceneLevelSetup setup)
    {
        Set(setup, "teaching", false); Set(setup, "mapEnabled", true); Set(setup, "initialCognition", 100f);
        var body = setup.transform.Find("BodySpawn"); var spirit = setup.transform.Find("SpiritSpawn");
        body.position = new Vector3(5, 3.24f, -1); spirit.position = new Vector3(5, 5, -1);
        var so = new SerializedObject(setup);
        var points = so.FindProperty("initialCognitionCalculations"); points.arraySize = 2;
        points.GetArrayElementAtIndex(0).floatValue = 2f; points.GetArrayElementAtIndex(1).floatValue = .2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var water = Instance("P29_上涨主水", "P29_上涨主水", content, 40, -2.7f, -2);
        ScaleTo(water, 80, 1.4f);
        Set(setup, "risingWater", water.GetComponent<WaterRegion>());
        Set(setup, "waterRiseSpeed", .14f);
        var maximum = scene.GetRootGameObjects().Single(r => r.name == "RisingWaterMaximum");
        maximum.transform.position = new Vector3(40, 21.2f, 0);
        Set(setup, "risingWaterMaximum", maximum.transform);
    }
    static void ResizeBackdrops()
    {
        var backdrop = scene.GetRootGameObjects().Single(g => g.name == "P25_检修站远景");
        var sr = backdrop.GetComponent<SpriteRenderer>();
        float scale = Mathf.Max(106 / sr.sprite.bounds.size.x, 104 / sr.sprite.bounds.size.y);
        backdrop.transform.position = new Vector3(40, 42, 5);
        backdrop.transform.localScale = new Vector3(scale, scale, 1);
        var layers = scene.GetRootGameObjects().Single(g => g.name == "10_P26背景层次与边缘前景");
        foreach (var t in layers.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("中景轮廓_") || t.name.StartsWith("外墙近处轮廓_"))
                t.position = new Vector3(t.position.x, t.position.y * 1.4f, t.position.z);
    }
    public static string Check()
    {
        scene = SceneManager.GetSceneByPath(ScenePath);
        var plates = All<MechanismPressurePlate>().ToArray();
        if (plates.Length != 5 || plates.Count(p => p.Kind == MechanismPressurePlate.PressureKind.ElectrifiedBoxOnly) != 4)
            throw new InvalidOperationException("须有四块电板和一块停箱板。");
        foreach (var plate in plates)
        {
            if (plate.TargetComponent == null || plate.transform.parent != plate.TargetComponent.transform.parent
                || plate.transform.parent.childCount != 2 || !plate.BoxesOnly)
                throw new InvalidOperationException("压板组合或引用错误：" + plate.name);
            if (!plate.ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
        }
        MapCell[] maps = All<MapCell>().ToArray();
        if (maps.Length < 150 || maps.Any(m => m.Width != 5 || m.Height > 5.001f)) throw new InvalidOperationException("地图格未细分。");
        foreach (var map in maps) if (!map.ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
        var setup = All<SceneLevelSetup>().Single();
        var record = new LevelMapRecord(setup.MapCells, new LevelCognition(100), scene);
        if (!record.IsValid) throw new InvalidOperationException(record.ConfigurationError);
        foreach (var cart in All<MechanismCart>()) if (!cart.ValidateConfiguration(out string error)) throw new InvalidOperationException(error);
        foreach (var c in All<Transform>().SelectMany(t => t.GetComponents<Component>()))
            if (c == null) throw new InvalidOperationException("场景缺失脚本。");
        return $"L05 saved: four vertical tiers at y=2/22/42/64; {maps.Length} cells at most 5x5, centers avoid default obstacles; {plates.Length} linked pairs; {All<PhysicalBox>().Count()} boxes; two fourth-floor alternatives.";
    }
}
#endif
