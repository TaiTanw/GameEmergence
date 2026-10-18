#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P25 压感对象筛选检查；临时对象不启用，不推进物理或保存关卡。</summary>
public static class P25PressurePlateChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> fixtures = new List<GameObject>();
    static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/P25 验证压感仅箱体筛选（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行压感筛选检查。");
        passed.Clear();
        Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            MechanismPressurePlate plate = New("P25 pressure", preview).AddComponent<MechanismPressurePlate>();
            CharacterPhysics playerBody = Actor<CharacterPhysics>("P25 player", preview);
            playerBody.gameObject.AddComponent<Player>();
            PhysicalBox ordinary = Actor<PhysicalBox>("P25 ordinary box", preview);
            PhysicalBox electrified = Actor<PhysicalBox>("P25 electrified box", preview);
            typeof(PhysicalBox).GetField("electrified", Hidden).SetValue(electrified, true);
            MechanismCart cart = Actor<MechanismCart>("P25 cart", preview);
            CharacterPhysics unrelated = Actor<CharacterPhysics>("P25 unrelated entity", preview);

            Check(!plate.BoxesOnly && plate.Kind == MechanismPressurePlate.PressureKind.Ordinary,
                "新选项默认关闭，原普通压感类型保留");
            CheckMatrix(plate, false, false, playerBody, ordinary, electrified, cart, unrelated);
            CheckMatrix(plate, false, true, playerBody, ordinary, electrified, cart, unrelated);
            CheckMatrix(plate, true, false, playerBody, ordinary, electrified, cart, unrelated);
            CheckMatrix(plate, true, true, playerBody, ordinary, electrified, cart, unrelated);
            Debug.Log($"P25 pressure plate: {passed.Count} checks passed.");
        }
        finally
        {
            for (int i = fixtures.Count - 1; i >= 0; i--)
                if (fixtures[i] != null) Object.DestroyImmediate(fixtures[i]);
            fixtures.Clear();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P25Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "pressure-plate-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckMatrix(MechanismPressurePlate plate, bool electricOnly, bool boxesOnly,
        BasicEntity player, PhysicalBox ordinary, PhysicalBox electrified, MechanismCart cart, BasicEntity unrelated)
    {
        typeof(MechanismPressurePlate).GetField("kind", Hidden).SetValue(plate, electricOnly
            ? MechanismPressurePlate.PressureKind.ElectrifiedBoxOnly : MechanismPressurePlate.PressureKind.Ordinary);
        typeof(MechanismPressurePlate).GetField("boxesOnly", Hidden).SetValue(plate, boxesOnly);
        string mode = $"{(electricOnly ? "带电" : "普通")}／仅箱体={boxesOnly}";
        Check(Accepts(plate, player) == (!electricOnly && !boxesOnly), mode + "：玩家资格正确");
        Check(Accepts(plate, cart) == (!electricOnly && !boxesOnly), mode + "：小车资格正确");
        Check(Accepts(plate, ordinary) == !electricOnly, mode + "：普通箱资格正确");
        Check(Accepts(plate, electrified), mode + "：带电箱资格正确");
        Check(!Accepts(plate, unrelated), mode + "：拒绝无关实体");
    }

    static bool Accepts(MechanismPressurePlate plate, BasicEntity entity) =>
        (bool)typeof(MechanismPressurePlate).GetMethod("Accepts", Hidden).Invoke(plate, new object[] { entity });

    static T Actor<T>(string name, Scene scene) where T : BasicEntity
    {
        GameObject item = New(name, scene);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        return item.AddComponent<T>();
    }

    static GameObject New(string name, Scene scene)
    {
        var item = new GameObject(name);
        item.SetActive(false);
        item.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(item, scene);
        fixtures.Add(item);
        return item;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
}
#endif
