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

        PhysicsSolverMgr.Instance.Init();
        var item = new GameObject("F8 receiver");
        item.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        item.AddComponent<BoxCollider2D>();
        var body = item.AddComponent<PhysicsObservationCheckBody>();
        PhysicsEditorEntityObservation.StartObserving();
        PhysicsEditorEntityObservation.StartObserving();
        PhysicsEditorContactObservation.StartObserving();
        PhysicsEditorContactObservation.StartObserving();
        try
        {
            Tick(manager);
            EntityPhysicsDebugSnapshot first = PhysicsEditorEntityObservation.GetSnapshot(body);
            Check(first.phase == 5 && first.fixedTick == PhysicsEditorObservationBridge.FixedTick, "实体发布当前物理帧相位五快照");
            Check(first.plannedWorldDelta == new Vector2(2, 0) * Time.fixedDeltaTime, "运动事实与运行时公式一致");
            Check(first.requestedDelta == first.plannedWorldDelta, "无阻挡提交与基础位移一致");
            body.GetComponent<Rigidbody2D>().position = first.requestedTarget + Vector2.up;
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).engineCorrection == Vector2.up, "下一帧实际位置对应上一请求");
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).fixedTick == PhysicsEditorObservationBridge.FixedTick, "一个消费者退出不停止其他消费者");

            ReportPair(body);
            Check(PhysicsEditorContactObservation.Contacts.Count == 0, "接触批次完成前不提前发布");
            PhysicsEditorObservationBridge.CompletePhysicsPhase(4);
            Check(PhysicsEditorContactObservation.Contacts.Count == 1, "接触批次只采集一次，无重复订阅");
            Check(PhysicsEditorContactObservation.PublishedFixedTick == PhysicsEditorObservationBridge.FixedTick, "接触批次携带帧号");
            body.enabled = false;
            Check(PhysicsEditorContactObservation.Contacts.Count == 0, "禁用实体立即移除已发布接触");
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase == -2, "禁用实体立即失去旧样本");
            body.enabled = true;
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).engineCorrection == Vector2.zero, "重新启用不继承上次生命周期请求");
            Check(PhysicsEditorContactObservation.Contacts.Count == 0, "空接触帧清除旧批次");

            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            Vector2 off = CaptureRequest(manager, body);
            PhysicsEditorEntityObservation.StartObserving();
            PhysicsEditorContactObservation.StartObserving();
            Vector2 on = CaptureRequest(manager, body);
            Check(off == on, "观测开关不改变物理请求位移");
            var window = ScriptableObject.CreateInstance<PlayerDataWindow>();
            Object.DestroyImmediate(window);
            Tick(manager);
            Check(PhysicsEditorEntityObservation.GetSnapshot(body).phase == 5, "窗口开关不破坏已有观测消费者");
            var otherItem = new GameObject("F8 contact receiver");
            try
            {
                otherItem.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                otherItem.AddComponent<BoxCollider2D>();
                var other = otherItem.AddComponent<PhysicsObservationCheckBody>();
                other.GetComponent<Rigidbody2D>().position = body.DebugActualPosition + Vector2.right * .75f;
                Physics2D.SyncTransforms();
                Tick(manager);
                Check(PhysicsEditorContactObservation.Contacts.Count == 1, "实际预测与接触解算产生单个观测记录");
                Check(PhysicsEditorEntityObservation.GetSnapshot(body).solverOffset != Vector2.zero,
                    "实际接触纠偏进入实体提交观测");
                other.enabled = false;
                Check(PhysicsEditorContactObservation.Contacts.Count == 0, "实际接触对象禁用后清理批次");
            }
            finally { Object.DestroyImmediate(otherItem); }
            ReportPair(body);
            PhysicsEditorObservationBridge.CompletePhysicsPhase(4);
            Object.DestroyImmediate(item);
            Check(PhysicsEditorContactObservation.Contacts.Count == 0, "销毁实体即时清理接触记录");
        }
        finally
        {
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorEntityObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            PhysicsEditorContactObservation.StopObserving();
            if (item != null) Object.DestroyImmediate(item);
        }
    }

    private static Vector2 CaptureRequest(MonoPublicMgr manager, BasicEntity entity)
    {
        Vector2 result = default;
        PhysicsEditorObservationBridge.MovementRequestedObserver capture = (body, offset, before, delta, target, clamped) => { if (body == entity) result = delta; };
        PhysicsEditorObservationBridge.MovementRequested += capture;
        try { Tick(manager); }
        finally { PhysicsEditorObservationBridge.MovementRequested -= capture; }
        return result;
    }

    private static void ReportPair(BasicEntity body) => PhysicsEditorObservationBridge.ReportContactPair(
        body, body, Vector2.zero, Vector2.right, Vector2.left, true, .1f, .098f, 2,
        true, false, 1, 0, .5f, .5f, Vector2.left, Vector2.right);
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

// 保留真实实体调度、速度计算、预测及提交，仅以固定意图替代角色输入和场景探针。
public sealed class PhysicsObservationCheckBody : BasicEntity
{
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
    protected override void GeometricQuery() { }
    protected override void HActiveSpeedOperation() => playerPhysicsData.horizontalSpeed = 2;
}
#endif
