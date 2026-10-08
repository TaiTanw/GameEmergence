#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhyData;
using Object = UnityEngine.Object;

/// <summary>实际场景和实际预制体的原生物理回归；保留历史批处理入口，不再测试斜坡请求裁剪公式。</summary>
public static class SlopePushSceneChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("Require a fresh batch editor.");
        try
        {
            EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/TeskScenes1.unity");
            RunScene();
            Debug.Log("K11 actual scene physics checks passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void RunScene()
    {
        foreach (Rigidbody2D existing in Object.FindObjectsOfType<Rigidbody2D>()) existing.simulated = false;
        IceGround ground = null;
        foreach (IceGround ice in Object.FindObjectsOfType<IceGround>())
            if (Mathf.Abs(ice.transform.eulerAngles.z) < .01f) { ground = ice; break; }
        if (ground == null) throw new InvalidOperationException("Actual scene requires its flat ICE fixture.");
        CharacterPhysics player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab")).GetComponent<CharacterPhysics>();
        PhysicalBox box = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/box.prefab")).GetComponent<PhysicalBox>();
        SimulationMode2D oldMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        var passed = new List<string>();
        try
        {
            Invoke(player, "Awake"); Invoke(box, "Awake");
            var action = new MovementData();
            player.Init(new ReadOnly_ActionData(action), new LocalEventSystem<PlayerStateMachine.E_playEvent>());
            Bounds floor = ground.GetComponent<Collider2D>().bounds;
            Place(box, floor.center.x, floor.max.y);
            Place(player, box.BodyBounds.min.x - player.BodyBounds.extents.x - .01f, floor.max.y);
            Physics2D.SyncTransforms();
            float start = box.DebugActualPosition.x;
            BasicEntity[] entities = { player, box };
            for (int tick = 0; tick < 120; tick++)
            {
                action.onMove = tick < 5 ? 0 : 1;
                Physics2D.SyncTransforms();
                foreach (BasicEntity entity in entities) Invoke(entity, "GeometricQueryPhase");
                foreach (BasicEntity entity in entities) Invoke(entity, "PhyFunUpdate");
                foreach (BasicEntity entity in entities) Invoke(entity, "SpeedCalculation");
                Physics2D.Simulate(Time.fixedDeltaTime);
                foreach (BasicEntity entity in entities) Invoke(entity, "DisplacementCorrection");
            }
            Require(player.DebugBodyType == RigidbodyType2D.Dynamic && box.DebugBodyType == RigidbodyType2D.Dynamic,
                "实际玩家和箱预制体共同使用 Dynamic 刚体", passed);
            Require(box.DebugActualPosition.x > start + .5f, "实际场景冰面上可以接触推动箱子", passed);
            Require(box.BodyBounds.min.y >= floor.max.y - .03f && player.BodyBounds.min.y >= floor.max.y - .03f,
                "实际 ICE 地形阻挡两个原生实体", passed);
            Require(box.BodyBounds.min.x - player.BodyBounds.max.x >= -.03f, "实际场景推动不穿入箱子", passed);
            Directory.CreateDirectory(".utmp/SlopePush");
            File.WriteAllLines(".utmp/SlopePush/native-scene-checks.txt", passed);
        }
        finally
        {
            Physics2D.simulationMode = oldMode;
            Object.DestroyImmediate(player.gameObject); Object.DestroyImmediate(box.gameObject);
        }
    }
    private static void Place(BasicEntity entity, float x, float top)
    {
        Bounds bounds = entity.BodyBounds;
        entity.transform.position += (Vector3)(new Vector2(x, top + bounds.extents.y + .005f) - (Vector2)bounds.center);
        Physics2D.SyncTransforms();
    }
    private static void Invoke(object instance, string name)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method == null) continue;
            method.Invoke(instance, null); return;
        }
        throw new MissingMethodException(name);
    }
    private static void Require(bool condition, string message, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException("K11 scene FAILED: " + message);
        passed.Add(message);
    }
}
#endif
