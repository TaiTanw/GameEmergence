#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>制作入口只生成 SceneObj 机关预设，不改关卡摆放或原 Taijie 预设。</summary>
public static class MechanismPrefabBuilder
{
    public const string Folder = "Assets/MyObjAssets/gameObject/Pf/SceneObj/";
    [MenuItem("自定义工具/开发工具/机关/生成 P15 机关预设")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后生成机关预设。");
        BuildCart();
        Save(MovingTarget("MechanismPlatform", new Vector2(3f, .3f), true), "MechanismPlatform");
        Save(MovingTarget("MechanismDoor", new Vector2(1f, 2.5f), false), "MechanismDoor");
        BuildPlate("PressurePlate", MechanismPressurePlate.PressureKind.Ordinary);
        BuildPlate("ElectrifiedPressurePlate", MechanismPressurePlate.PressureKind.ElectrifiedBoxOnly);
        GameObject electric = Surface("ElectrifiedLowFrictionGround", new Vector2(8f, .3f), new Color(.5f, .8f, 1f));
        BaseGround host = electric.AddComponent<BaseGround>();
        Set(host, "slowingEffect", .05f);
        electric.AddComponent<FixedContactHazard>();
        Save(electric, "ElectrifiedLowFrictionGround");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("自定义工具/开发工具/环境/生成接触即死绿色水体预设")]
    public static void BuildLethalWater()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后生成水体预设。");
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "WaterPool.prefab");
        if (asset == null) throw new InvalidOperationException("缺少制作来源 SceneObj/WaterPool.prefab。");
        GameObject item = UnityEngine.Object.Instantiate(asset);
        try
        {
            item.name = "绿色水体";
            WaterRegion host = item.GetComponent<WaterRegion>();
            item.AddComponent<FixedContactHazard>();
            var data = new SerializedObject(host);
            SerializedProperty parameters = data.FindProperty("environmentParameters");
            parameters.arraySize = 1;
            // 实心列表预留可编辑的普通消退倍率；变地面后仍由玩家注入计算点降低。
            parameters.GetArrayElementAtIndex(0).managedReferenceValue = new GroundSlowingParameter(1f);
            data.ApplyModifiedPropertiesWithoutUndo();
            item.GetComponent<SpriteRenderer>().color = new Color(.25f, .75f, .3f, host.GetEnvironmentOpacity(true));
            Set(item.GetComponent<WaterSurfaceVisual>(), "surfaceColor", new Color(.65f, 1f, .35f, .75f));
            if (!host.ValidateEnvironmentParameters(out string error)
                || !item.GetComponent<FixedContactHazard>().ValidateConfiguration(out error)
                || !item.GetComponent<WaterSurfaceVisual>().ValidateConfiguration(out error))
                throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(item, Folder + "绿色水体.prefab");
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(item); }
    }

    public static void BuildLethalWaterForBatch()
    {
        try { BuildLethalWater(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void BuildCart()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "box.prefab");
        if (asset == null) throw new InvalidOperationException("缺少制作来源 SceneObj/box.prefab。");
        GameObject item = UnityEngine.Object.Instantiate(asset);
        item.name = "MechanismCart"; item.transform.position = Vector3.zero;
        PhysicalBox box = item.GetComponent<PhysicalBox>();
        var source = new SerializedObject(box);
        UnityEngine.Object config = source.FindProperty("cPhysics").objectReferenceValue;
        Transform ground = (Transform)source.FindProperty("groundV").objectReferenceValue;
        Transform left = (Transform)source.FindProperty("leftV").objectReferenceValue;
        Transform right = (Transform)source.FindProperty("rightV").objectReferenceValue;
        Transform top = (Transform)source.FindProperty("upV").objectReferenceValue;
        UnityEngine.Object.DestroyImmediate(box);
        MechanismCart cart = item.AddComponent<MechanismCart>();
        Set(cart, "cPhysics", config); Set(cart, "groundV", ground); Set(cart, "leftV", left);
        Set(cart, "rightV", right); Set(cart, "upV", top);
        item.GetComponent<BoxCollider2D>().excludeLayers = 1 << Player.PlayerLayer;
        foreach (SpriteRenderer sprite in item.GetComponentsInChildren<SpriteRenderer>()) sprite.color = new Color(.3f, .85f, .55f);
        if (!cart.ValidateConfiguration(out string error)) { UnityEngine.Object.DestroyImmediate(item); throw new InvalidOperationException(error); }
        Save(item, "MechanismCart");
    }

    static void BuildPlate(string name, MechanismPressurePlate.PressureKind kind)
    {
        GameObject item = Surface(name, new Vector2(3f, .25f), kind == MechanismPressurePlate.PressureKind.Ordinary
            ? new Color(.9f, .7f, .2f) : new Color(.7f, .4f, 1f));
        // 压感只检测脚底进入；实心地面由关卡提供，凸起的板面不阻挡箱子。
        item.GetComponent<BoxCollider2D>().isTrigger = true;
        item.AddComponent<BaseGround>();
        // 单个预设带明确关联的门样例；可把目标字段改为其它场景目标，运行时不寻找替代物。
        GameObject door = MovingTarget("LinkedDoor", new Vector2(1f, 2.5f), false);
        door.transform.SetParent(item.transform, false);
        door.transform.localPosition = new Vector3(4f, 1.125f, 0f);
        MechanismPressurePlate plate = item.AddComponent<MechanismPressurePlate>();
        Set(plate, "kind", (int)kind); Set(plate, "target", door.GetComponent<Taijie>());
        Save(item, name);
    }

    static GameObject MovingTarget(string name, Vector2 size, bool oneWay)
    {
        GameObject item = Surface(name, size, oneWay ? new Color(.25f, .65f, .85f) : new Color(.65f, .35f, .2f));
        item.AddComponent<Taijie>();
        if (oneWay)
        {
            PlatformEffector2D effector = item.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true; effector.surfaceArc = 180f;
            item.GetComponent<BoxCollider2D>().usedByEffector = true;
        }
        return item;
    }

    static GameObject Surface(string name, Vector2 size, Color color)
    {
        var item = new GameObject(name) { layer = LayerMask.NameToLayer("Ground") };
        item.AddComponent<BoxCollider2D>().size = size;
        SpriteRenderer sprite = item.AddComponent<SpriteRenderer>();
        sprite.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        sprite.drawMode = SpriteDrawMode.Sliced; sprite.size = size; sprite.color = color;
        return item;
    }

    static void Save(GameObject item, string name)
    {
        try { PrefabUtility.SaveAsPrefabAsset(item, Folder + name + ".prefab"); }
        finally { UnityEngine.Object.DestroyImmediate(item); }
    }

    static void Set(UnityEngine.Object item, string field, object value)
    {
        var data = new SerializedObject(item);
        SerializedProperty property = data.FindProperty(field);
        if (value is UnityEngine.Object reference) property.objectReferenceValue = reference;
        else if (value is int number) property.intValue = number;
        else if (value is float scalar) property.floatValue = scalar;
        else if (value is Color color) property.colorValue = color;
        else throw new ArgumentException(field);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void RunForBatch()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
