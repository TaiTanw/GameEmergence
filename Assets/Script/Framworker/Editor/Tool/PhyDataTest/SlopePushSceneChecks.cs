#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhyData;

/// <summary>
/// 编辑器批处理回归夹具：读取原场景地形，克隆实际预制体并手动推进物理执行相位与 Dynamic 模拟。
/// 覆盖上坡、持续受阻、松手与反向，输出逐帧 CSV 并断言支撑和实际位置稳定。
/// 不运行输入、行为 FSM 或表现层；只在新批处理编辑器中使用，不保存测试场景。
/// </summary>
public static class SlopePushSceneChecks
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic; // 私有实例成员反射范围
    /// <summary>打开原场景，运行两组连续物理回归和已有环境检查，成功/失败通过批处理退出码报告。</summary>
    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("Requires a fresh batch editor.");
        try
        {
            EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/TeskScenes1.unity");
            Physics2D.SyncTransforms();
            var report = new StringBuilder();
            foreach (Collider2D c in UnityEngine.Object.FindObjectsOfType<Collider2D>())
                report.AppendLine($"{c.name}: center={c.bounds.center:F5} size={c.bounds.size:F5} angle={c.transform.eulerAngles.z:F5} trigger={c.isTrigger} entity={c.GetComponent<BasicEntity>() != null}");
            Directory.CreateDirectory(".utmp/SlopePush");
            File.WriteAllText(".utmp/SlopePush/inventory.txt", report.ToString());
            TraceScene("default", 15f, false);
            TraceScene("slow-reversed-order", 3f, true);
            EnvironmentIntegrationChecks.Run();
            Debug.Log("Slope push checks passed: 1080 Dynamic steps and environment regressions");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    /// <summary>
    /// 保留原 ICE 和浅蓝坡面，用指定速度与实体登记顺序推进 540 个物理步。
    /// 对照请求与实际位移；断言受阻高度稳定、沿途支撑不断开、松手静止及反向可退出。
    /// 场景中原有动态刚体暂不模拟，测试结束销毁克隆体并恢复自动模拟模式。
    /// </summary>
    private static void TraceScene(string label, float speed, bool reverseOrder)
    {
        var slope = UnityEngine.Object.FindObjectsOfType<Taijie>().Single().GetComponent<BoxCollider2D>();
        foreach (Rigidbody2D body in UnityEngine.Object.FindObjectsOfType<Rigidbody2D>()) body.simulated = false;
        var box = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/box.prefab")).GetComponent<PhysicalBox>();
        var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/player20.prefab")).GetComponent<CharacterPhysics>();
        Invoke(box, "Awake");
        Invoke(player, "Awake");
        player.speed = speed;
        var action = new MovementData();
        player.Init(new ReadOnly_ActionData(action), new LocalEventSystem<PlayerStateMachine.E_playEvent>());
        BoxCollider2D bc = box.GetComponent<BoxCollider2D>(), pc = player.GetComponent<BoxCollider2D>();
        Vector2 top = slope.transform.TransformPoint(slope.offset + Vector2.up * slope.size.y * .5f);
        Vector2 normal = slope.transform.up;
        Place(box, bc, -44.5f, top, normal);
        Physics2D.SyncTransforms();
        Place(player, pc, bc.bounds.center.x + bc.bounds.extents.x + pc.bounds.extents.x + .01f, top, normal);
        Physics2D.SyncTransforms();
        var trace = new StringBuilder("tick,input,bx,by,px,py,bGround,pGround,bNx,bNy,pNx,pNy,bReqX,bReqY,pReqX,pReqY,bActualX,bActualY,pActualX,pActualY,bEngineX,bEngineY,pEngineX,pEngineY,bBlock,pBlock\n");
        var heldBoxY = new List<float>();
        var heldPlayerY = new List<float>();
        int airborneHeld = 0;
        Vector2 pushStart = player.DebugActualPosition, releaseStart = default, reverseStart = default;
        bool hasPreviousRequest = false;
        Vector2 previousBoxOrigin = default, previousPlayerOrigin = default;
        Vector2 previousBoxRequest = default, previousPlayerRequest = default;
        Vector2 previousBoxActual = default, previousPlayerActual = default;
        BasicEntity[] entities = reverseOrder ? new BasicEntity[] { player, box } : new BasicEntity[] { box, player };
        SimulationMode2D oldMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        try
        {
            for (int tick = 0; tick < 540; tick++)
            {
                action.onMove = tick < 20 ? 0f : tick < 420 ? -1f : tick < 480 ? 0f : 1f;
                Physics2D.SyncTransforms();
                foreach (BasicEntity e in entities) Invoke(e, "GeometricQueryPhase");
                if (hasPreviousRequest)
                {
                    CheckObservation(box, previousBoxOrigin, previousBoxRequest, previousBoxActual);
                    CheckObservation(player, previousPlayerOrigin, previousPlayerRequest, previousPlayerActual);
                }
                foreach (BasicEntity e in entities) Invoke(e, "PhyFunUpdate");
                foreach (BasicEntity e in entities) Invoke(e, "SpeedCalculation");
                Invoke(PhysicsSolverMgr.Instance, "ContactForSolution");
                Vector2 b0 = box.DebugActualPosition, p0 = player.DebugActualPosition;
                EntitySolutionResult bs = Solution(box), ps = Solution(player);
                foreach (BasicEntity e in entities) Invoke(e, "DisplacementCorrection");
                Physics2D.Simulate(Time.fixedDeltaTime);
                Vector2 ba = box.DebugActualPosition - b0, pa = player.DebugActualPosition - p0;
                previousBoxOrigin = b0;
                previousPlayerOrigin = p0;
                previousBoxRequest = bs.requestedWorldDelta;
                previousPlayerRequest = ps.requestedWorldDelta;
                previousBoxActual = box.DebugActualPosition;
                previousPlayerActual = player.DebugActualPosition;
                hasPreviousRequest = true;
                Vector2 be = ba - bs.requestedWorldDelta, pe = pa - ps.requestedWorldDelta;
                trace.AppendLine(FormattableString.Invariant($"{tick},{action.onMove},{b0.x:F6},{b0.y:F6},{p0.x:F6},{p0.y:F6},{box.DebugIsGrounded},{player.DebugIsGrounded},{box.DebugGroundNormal.x:F6},{box.DebugGroundNormal.y:F6},{player.DebugGroundNormal.x:F6},{player.DebugGroundNormal.y:F6},{bs.requestedWorldDelta.x:F6},{bs.requestedWorldDelta.y:F6},{ps.requestedWorldDelta.x:F6},{ps.requestedWorldDelta.y:F6},{ba.x:F6},{ba.y:F6},{pa.x:F6},{pa.y:F6},{be.x:F6},{be.y:F6},{pe.x:F6},{pe.y:F6},{bs.blockedHorizontalDirection},{ps.blockedHorizontalDirection}"));
                if (tick >= 250 && tick < 420)
                {
                    heldBoxY.Add(b0.y);
                    heldPlayerY.Add(p0.y);
                }
                if (tick >= 20 && tick < 420 && (!box.DebugIsGrounded || !player.DebugIsGrounded)) airborneHeld++;
                if (tick == 420) releaseStart = p0;
                if (tick == 480)
                {
                    reverseStart = p0;
                    Require((p0 - releaseStart).magnitude < .003f, label + ": release stays still");
                }
            }
            File.WriteAllText(".utmp/SlopePush/" + label + ".csv", trace.ToString());
            float boxRange = heldBoxY.Max() - heldBoxY.Min(), playerRange = heldPlayerY.Max() - heldPlayerY.Min();
            Require(airborneHeld == 0, label + $": held support flickers {airborneHeld} frames");
            Require(boxRange < .003f && playerRange < .003f, label + $": held Y ranges {boxRange}/{playerRange}");
            Require(reverseStart.x < pushStart.x - 2f && reverseStart.y > pushStart.y + .5f, label + ": uphill advance");
            Require(player.DebugActualPosition.x > reverseStart.x + 2f, label + ": reverse exits contact");
            CheckIndependentVerticalDisplacements(box);
            Debug.Log(label + $": 540 Dynamic steps passed, held Y ranges={boxRange}/{playerRange}, airborne={airborneHeld}");
        }
        finally
        {
            Physics2D.simulationMode = oldMode;
            UnityEngine.Object.DestroyImmediate(box.gameObject);
            UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
    }

    /// <summary>相位 1 发布的是上次模拟后的实际位置，且可回溯到对应的位移请求。</summary>
    private static void CheckObservation(BasicEntity entity, Vector2 origin, Vector2 request,
        Vector2 actual)
    {
        Require(entity.TryGetLastDisplacementObservation(out EntityDisplacementObservation observation),
            entity.name + ": missing displacement observation");
        Require(observation.observedStep == observation.requestStep + 1,
            entity.name + ": observation step mismatch");
        Require((observation.requestOrigin - origin).sqrMagnitude < 0.00000001f &&
            (observation.requestedWorldDelta - request).sqrMagnitude < 0.00000001f &&
            (observation.actualPosition - actual).sqrMagnitude < 0.00000001f,
            entity.name + ": request/actual observation mismatch");
    }

    /// <summary>
    /// 单独设置本帧运动与解算输入，核对零/半/完整水平保留率下的 Y 分量。
    /// 这是合成公式回归：只缩短坡面分量，独立竖直速度、接触 Y 和平台 XY 保留。
    /// </summary>
    private static void CheckIndependentVerticalDisplacements(PhysicalBox box)
    {
        var data = (PlayerPhysicsData)typeof(BasicEntity).GetField("playerPhysicsData", InstanceFlags).GetValue(box);
        data.verticalSpeed = 2f;
        var frame = new EntityMotionFrame(new Vector2(-5f, 2f), new Vector2(-5f, 4.5f),
            new Vector2(-.1f, .05f + 2f * Time.fixedDeltaTime), new Vector2(0f, .02f));
        typeof(BasicEntity).GetField("motionFrame", InstanceFlags).SetValue(box, frame);
        object geometry = typeof(BasicEntity).GetField("candidateGeometry", InstanceFlags).GetValue(box);
        geometry.GetType().GetField("candidateDelta", InstanceFlags).SetValue(geometry, Vector2.zero);
        foreach (float platformX in new[] { 0f, .03f })
        foreach (float retained in new[] { 0f, .5f, 1f })
        {
            frame = new EntityMotionFrame(frame.freeVelocity, frame.motionVelocity, frame.motionDelta, new Vector2(platformX, .02f));
            typeof(BasicEntity).GetField("motionFrame", InstanceFlags).SetValue(box, frame);
            var solution = new EntitySolutionResult { allowedHorizontalDelta = -.1f * retained + platformX,
                displacementOffset = new Vector2(.1f * (1f - retained), .03f) };
            typeof(BasicEntity).GetField("entitySolutionResult", InstanceFlags).SetValue(box, solution);
            Invoke(box, "FinalizeRequestedDisplacement");
            float expectedY = .05f * retained + 2f * Time.fixedDeltaTime + .03f + .02f;
            Require(Mathf.Abs(Solution(box).requestedWorldDelta.y - expectedY) < .0001f,
                "independent vertical components retained, ratio=" + retained + ", platformX=" + platformX);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
    }

    private static EntitySolutionResult Solution(BasicEntity e) => (EntitySolutionResult)typeof(BasicEntity).GetField("entitySolutionResult", InstanceFlags).GetValue(e);
    /// <summary>按坡面法线与碰撞框前缘计算贴坡初始中心，补偿预制体碰撞框相对根节点的偏移。</summary>
    private static void Place(BasicEntity e, BoxCollider2D c, float x, Vector2 top, Vector2 normal)
    {
        Vector2 center = c.bounds.center;
        float leadingX = x - c.bounds.extents.x;
        float y = top.y - normal.x / normal.y * (leadingX - top.x) + c.bounds.extents.y + .005f;
        e.transform.position += (Vector3)(new Vector2(x, y) - center);
    }
    /// <summary>沿继承链查找并调用真实执行方法；让回归复用运行算法，无需新增运行时测试入口。</summary>
    private static void Invoke(object instance, string method)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo found = type.GetMethod(method, InstanceFlags | BindingFlags.DeclaredOnly);
            if (found == null) continue;
            found.Invoke(instance, null);
            return;
        }
        throw new MissingMethodException(method);
    }
}
#endif
