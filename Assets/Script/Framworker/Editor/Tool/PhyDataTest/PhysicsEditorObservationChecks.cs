#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

/// <summary>F8 批处理回归：空场景中两次进入 Play Mode，验证实际调度和观测生命周期，不保存场景。</summary>
public static class PhysicsEditorObservationChecks
{
    private static readonly List<string> passed = new List<string>();
    private static readonly MethodInfo fixedUpdate = typeof(MonoPublicMgr).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool previousOptionsEnabled;
    private static EnterPlayModeOptions previousOptions;
    private static int cycle;
    private static int stage;
    private static double deadline;
    private static Exception failure;

    // 只限批处理：避免替换开发者正在编辑的场景或打断其 Play Mode。
    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying)
            throw new InvalidOperationException("F8 Play Mode checks require a fresh batch-mode editor.");
        EnvironmentIntegrationChecks.Run();
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        passed.Clear();
        cycle = 0;
        stage = 0;
        failure = null;
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    private static void Advance()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("F8 Play Mode transition timed out.");
            if (stage == 0 && Application.isPlaying)
            {
                RunPlayChecks();
                stage = 1;
                EditorApplication.isPlaying = false;
            }
            else if (stage == 1 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Check(PhysicsEditorContactObservation.Contacts.Count == 0, "退出播放清空接触记录");
                if (++cycle < 2)
                {
                    stage = 0;
                    EditorApplication.isPlaying = true;
                }
                else Finish();
            }
            else if (stage == 2 && !EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }
        catch (Exception error)
        {
            failure = error;
            Debug.LogException(error);
            stage = 2;
            EditorApplication.isPlaying = false;
            if (!Application.isPlaying) Finish();
        }
    }

    private static void RunPlayChecks()
    {
        MonoPublicMgr.IsQuitting = false;
        MonoPublicMgr manager = MonoPublicMgr.Instance;
        manager.Init();
        manager.enabled = false; // 精确推进真实 FixedUpdate，避免等待期间额外产生物理帧。
        Check(manager.actionsLen.Length == 7, "逻辑数组仍为七槽");
        Check(PhysicsEditorObservationBridge.FixedTick == 0, "重进播放重置帧号");
        var order = new List<string>();
        UnityAction pre = () => order.Add("pre");
        Action<int> begin = tick => order.Add("begin");
        Action<int, int> after = (tick, phase) => order.Add("after" + phase);
        var callbacks = new UnityAction[7];
        manager.AddFixedUpdateEvent(pre);
        PhysicsEditorObservation.PhysicsFrameStarted += begin;
        PhysicsEditorObservation.PhysicsPhaseCompleted += after;
        try
        {
            for (int i = 0; i < 7; i++)
            {
                int slot = i;
                callbacks[i] = () => order.Add("slot" + slot);
                manager.AddPhysicalTimingUpdate(callbacks[i], i);
            }
            Tick(manager);
            var expected = new List<string> { "pre", "begin" };
            for (int i = 0; i < 7; i++) { expected.Add("slot" + i); expected.Add("after" + i); }
            Check(string.Join(",", order) == string.Join(",", expected), "全部相位结束后通知且顺序不变");
            // 预期异常由日志回调捕获；不关闭真实错误输出。
            int logged = 0;
            Application.LogCallback capture = (message, stack, type) => { if (message.Contains("F8 expected observer failure")) logged++; };
            Action<int, int> bad = (tick, phase) => { throw new InvalidOperationException("F8 expected observer failure"); };
            Application.logMessageReceived += capture;
            PhysicsEditorObservationBridge.PhysicsPhaseCompleted += bad;
            try { order.Clear(); Tick(manager); }
            finally { PhysicsEditorObservationBridge.PhysicsPhaseCompleted -= bad; Application.logMessageReceived -= capture; }
            Check(logged == 7 && order.Contains("after6"), "观测异常逐项隔离且后续物理相位继续");
        }
        finally
        {
            manager.RemoveFixedUpdateEvent(pre);
            PhysicsEditorObservation.PhysicsFrameStarted -= begin;
            PhysicsEditorObservation.PhysicsPhaseCompleted -= after;
            for (int i = 0; i < 7; i++) manager.RemovePhysicalTimingUpdate(callbacks[i], i);
        }

        var item = new GameObject("K11 observation receiver");
        Rigidbody2D rigidbody = item.AddComponent<Rigidbody2D>();
        rigidbody.bodyType = RigidbodyType2D.Dynamic;
        rigidbody.position = new Vector2(1000, 1000);
        rigidbody.freezeRotation = true;
        item.AddComponent<BoxCollider2D>();
        var body = item.AddComponent<PhysicsObservationCheckBody>();
        PhysicsEditorEntityObservation.StartObserving();
        PhysicsEditorEntityObservation.StartObserving();
        PhysicsEditorContactObservation.StartObserving();
        PhysicsEditorContactObservation.StartObserving();
        try
        {
            Vector2 origin = rigidbody.position;
            Tick(manager);
            EntityPhysicsDebugSnapshot first = PhysicsEditorEntityObservation.GetSnapshot(body);
            Check(first.phase == 5 && first.fixedTick == PhysicsEditorObservationBridge.FixedTick, "相位五读取当前物理帧实际快照");
            Check(Mathf.Abs(first.actualPosition.x - origin.x - 2f * Time.fixedDeltaTime) < .0001f,
                $"一个真实调度物理步只推进一次 Unity 模拟 [origin={origin}, actual={first.actualPosition}, velocity={rigidbody.velocity}, dt={Time.fixedDeltaTime}, mode={Physics2D.simulationMode}, participating={body.IsMotionParticipating}]");
            Check(first.actualPosition == rigidbody.position && first.planarVelocity == rigidbody.velocity,
                "观测速度和位置与实际刚体一致");
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).fixedTick == PhysicsEditorObservationBridge.FixedTick,
                "一个消费者退出不停止其他消费者");
            body.enabled = false;
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase == -2, "禁用实体立即移除旧样本");
            body.enabled = true;
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase == 5, "重新启用恢复实际样本");

            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            origin = rigidbody.position; Tick(manager); Vector2 off = rigidbody.position - origin;
            PhysicsEditorEntityObservation.StartObserving();
            PhysicsEditorContactObservation.StartObserving();
            origin = rigidbody.position; Tick(manager); Vector2 on = rigidbody.position - origin;
            Check((off - on).sqrMagnitude < .00000001f, "观测开关不改变实际位移");
            var window = ScriptableObject.CreateInstance<PlayerDataWindow>();
            Object.DestroyImmediate(window);
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase == 5, "窗口开关不破坏已有消费者");
            var otherItem = new GameObject("K11 native contact receiver");
            try
            {
                Rigidbody2D otherBody = otherItem.AddComponent<Rigidbody2D>();
                otherBody.bodyType = RigidbodyType2D.Dynamic;
                otherBody.freezeRotation = true;
                otherBody.position = rigidbody.position + Vector2.right * .98f;
                otherItem.AddComponent<BoxCollider2D>();
                var other = otherItem.AddComponent<PhysicsObservationCheckBody>();
                Physics2D.SyncTransforms();
                for (int i = 0; i < 5; i++) Tick(manager);
                bool found = false;
                foreach (var pair in PhysicsEditorContactObservation.Contacts)
                    found |= (pair.entityA == body && pair.entityB == other) || (pair.entityA == other && pair.entityB == body);
                Check(found, "实际 Unity 碰撞产生接触观测");
                Check(PhysicsEditorContactObservation.PublishedFixedTick == PhysicsEditorObservationBridge.FixedTick,
                    "真实接触携带当前物理帧号");
                other.enabled = false;
                Check(PhysicsEditorContactObservation.Contacts.Count == 0, "禁用接触实体即时移除接触样本");
            }
            finally { Object.DestroyImmediate(otherItem); }
            Tick(manager);
            Check(PhysicsEditorContactObservation.Contacts.Count == 0, "空接触帧没有上一步残留");
            Object.DestroyImmediate(item);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase != 5, "销毁实体不再提供旧样本");
        }
        finally
        {
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            if (item != null) Object.DestroyImmediate(item);
        }
        CheckSidePush(manager);
    }

    // 每次播放都验证模拟前的真实推动，避免原生碰撞掩盖相位 4 订阅丢失。
    private static void CheckSidePush(MonoPublicMgr manager)
    {
        var driverItem = new GameObject("K11 side push driver");
        var boxItem = new GameObject("K11 side push box");
        UnityAction sample = null;
        try
        {
            driverItem.transform.position = new Vector2(2000, 1000);
            boxItem.transform.position = driverItem.transform.position + Vector3.right * 1.01f;
            Rigidbody2D driverBody = driverItem.AddComponent<Rigidbody2D>();
            driverBody.freezeRotation = true;
            driverItem.AddComponent<BoxCollider2D>();
            var driver = driverItem.AddComponent<PhysicsObservationCheckBody>();
            Rigidbody2D boxBody = boxItem.AddComponent<Rigidbody2D>();
            boxBody.freezeRotation = true;
            boxItem.AddComponent<BoxCollider2D>();
            boxItem.AddComponent<PhysicsObservationCheckBody>().horizontalDrive = false;
            driver.enabled = false;
            driver.enabled = true;
            int registrations = 0;
            foreach (Delegate callback in manager.actionsLen[4]?.GetInvocationList() ?? Array.Empty<Delegate>())
                if (callback.Method.DeclaringType?.Name == "EntitySidePush") registrations++;
            Check(registrations == 1, $"多实体和重新启用仍只注册一次侧面推动 [count={registrations}]");
            int samples = 0;
            float driverVx = 0f, boxVx = 0f;
            sample = () => { samples++; driverVx = driverBody.velocity.x; boxVx = boxBody.velocity.x; };
            manager.AddPhysicalTimingUpdate(sample, 4);
            Physics2D.SyncTransforms();
            Vector2 driverOrigin = driverBody.position, boxOrigin = boxBody.position;
            Tick(manager);
            Check(samples == 1 && Mathf.Abs(driverVx - 2f) < .001f && Mathf.Abs(boxVx - driverVx) < .001f,
                $"Unity 模拟前已执行侧面推动并写入双方水平速度 [samples={samples}, driver={driverVx}, box={boxVx}]");
            Check(Mathf.Abs(driverBody.position.x - driverOrigin.x - 2f * Time.fixedDeltaTime) < .001f
                && Mathf.Abs(boxBody.position.x - boxOrigin.x - 2f * Time.fixedDeltaTime) < .001f,
                "侧面推动后双方只推进一个固定模拟步");
        }
        finally
        {
            if (sample != null) manager.RemovePhysicalTimingUpdate(sample, 4);
            Object.DestroyImmediate(driverItem);
            Object.DestroyImmediate(boxItem);
        }
    }

    private static void Tick(MonoPublicMgr manager) => fixedUpdate.Invoke(manager, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("F8 FAILED: " + message);
        passed.Add("Cycle " + cycle + ": " + message);
    }
    private static void Finish()
    {
        EditorApplication.update -= Advance;
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        Directory.CreateDirectory(".utmp/EnvironmentValidation");
        File.WriteAllLines(".utmp/EnvironmentValidation/f84-checks.txt", passed);
        Debug.Log("F8 observation: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}

// 保留真实实体调度和 Unity 模拟，仅以固定意图替代角色输入和场景探针。
public sealed class PhysicsObservationCheckBody : BasicEntity
{
    public bool horizontalDrive = true;
    protected override void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        rb.gravityScale = 0;
        gravity = 0;
        playerPhysicsData = new PhyData.PlayerPhysicsData();
        nowGemetry = new PhyData.GeometryPhysicsData();
    }
    protected override bool HasHorizontalDrive => horizontalDrive;
    protected override void GeometricQuery() { }
    protected override void HActiveSpeedOperation() => playerPhysicsData.horizontalSpeed = 2;
}
#endif
