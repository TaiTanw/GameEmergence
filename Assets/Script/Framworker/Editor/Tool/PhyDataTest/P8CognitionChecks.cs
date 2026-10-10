#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>P8.1 认知计时与真实形态结算的定向检查；只创建临时对象，不保存场景。</summary>
public static class P8CognitionChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<string> passed = new List<string>();
    static int group;

    [MenuItem("自定义工具/测试工具/验证/P8.1 验证认知计时（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行 P8.1 检查。");
        passed.Clear(); group = 0;
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = scheduler.GetValue(null);
        bool previousQuitting = MonoPublicMgr.IsQuitting;
        bool previousWorldPaused = ReadPaused(typeof(BasicEntity));
        bool previousPlatformPaused = ReadPaused(typeof(Taijie));
        SimulationMode2D previousSimulation = Physics2D.simulationMode;
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)previousScheduler == null)
            {
                var item = new GameObject("P8 temporary scheduler") { hideFlags = HideFlags.DontSave };
                objects.Add(item); scheduler.SetValue(null, item.AddComponent<MonoPublicMgr>());
            }
            CheckGameTime();
            CheckSharedCostAndExhaustion();
            CheckInitialZero();
            Debug.Log("P8.1 cognition: " + passed.Count + " checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] == null) continue;
                foreach (BasicEntity entity in objects[i].GetComponentsInChildren<BasicEntity>()) Invoke(entity, "OnDisable");
                Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            BasicEntity.SetWorldPaused(previousWorldPaused); Taijie.SetWorldPaused(previousPlatformPaused);
            scheduler.SetValue(null, previousScheduler); MonoPublicMgr.IsQuitting = previousQuitting;
            Physics2D.simulationMode = previousSimulation;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P8Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "cognition-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckGameTime()
    {
        var fine = new PlayerLifecycleRules(null, null, cognition: new LevelCognition(100f), simulationCognitionPerSecond: 2f);
        var coarse = new PlayerLifecycleRules(null, null, cognition: new LevelCognition(100f), simulationCognitionPerSecond: 2f);
        fine.ConfigureStage(PlayerGameplayStage.Exploration, true);
        coarse.ConfigureStage(PlayerGameplayStage.Exploration, true);
        for (int i = 0; i < 100; i++) fine.AdvanceSimulationTime(.01f);
        coarse.AdvanceSimulationTime(1f);
        Near(fine.Cognition.Remaining, coarse.Cognition.Remaining, "相同游戏时间在不同更新步长扣费一致");
        Near(coarse.Cognition.Remaining, 98f, "模拟按关卡每秒消耗率结算");
        coarse.AdvanceSimulationTime(0f);
        coarse.AdvanceSimulationTime(-1f);
        coarse.AdvanceSimulationTime(float.NaN);
        coarse.AdvanceSimulationTime(float.PositiveInfinity);
        Near(coarse.Cognition.Remaining, 98f, "暂停的零游戏时间及失效时间不产生费用");
        coarse.ConfigureStage(PlayerGameplayStage.Exploration);
        coarse.AdvanceSimulationTime(50f);
        Near(coarse.Cognition.Remaining, 98f, "漫游不计模拟时间费用");
        coarse.ConfigureStage(PlayerGameplayStage.Tutorial, true);
        coarse.AdvanceSimulationTime(50f);
        coarse.ConfigureStage(PlayerGameplayStage.Escape, true);
        coarse.AdvanceSimulationTime(50f);
        Near(coarse.Cognition.Remaining, 98f, "教学和逃生不扣模拟认知");
        var disabled = new PlayerLifecycleRules(null, null, cognition: new LevelCognition(8f), simulationCognitionPerSecond: float.NaN);
        disabled.ConfigureStage(PlayerGameplayStage.Exploration, true);
        disabled.AdvanceSimulationTime(100f);
        Near(disabled.Cognition.Remaining, 8f, "失效消耗配置沿资源规则归零");
    }

    static void CheckSharedCostAndExhaustion()
    {
        Player spirit = Actor();
        var rules = new PlayerLifecycleRules(null, spirit, cognition: new LevelCognition(5f), formCost: 1f,
            simulationCognitionPerSecond: 2f);
        int requests = 0, commits = 0;
        rules.CognitionExhaustionRequestedOnce += () => requests++;
        rules.FormCommitted += reason => commits++;
        Check(rules.BeginExploration(.5f), "真实精神体就绪后开始探索");
        Check(!rules.CognitionExhaustionRequested && requests == 0, "有余额的初始化不请求逃生");

        Bounds bounds = spirit.GetComponent<CharacterPhysics>().BodyBounds;
        var blocker = new GameObject("P8 occupied walking point") { hideFlags = HideFlags.DontSave };
        objects.Add(blocker); blocker.transform.position = bounds.center;
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(spirit.GetComponent<CharacterPhysics>());
        for (int i = 0; i < 32; i++) if ((config.wallLayer.value & (1 << i)) != 0) { blocker.layer = i; break; }
        blocker.AddComponent<BoxCollider2D>().size = new Vector2(4f, 4f);
        Physics2D.SyncTransforms();
        Check(!spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "占用拒绝的真实转换不提交");
        Near(rules.Cognition.Remaining, 5f, "失败转换不扣费");
        Check(commits == 0 && requests == 0, "失败转换不发提交或耗尽请求");
        blocker.SetActive(false); Physics2D.SyncTransforms();

        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "强制转换成功进入模拟");
        Near(rules.Cognition.Remaining, 4f, "强制转换使用同一余额扣费一次");
        rules.AdvanceSimulationTime(1f);
        Near(rules.Cognition.Remaining, 2f, "模拟计时接着消费转换后的同一余额");
        Check(commits == 1 && requests == 0, "时间费用不伪造形态提交");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "相同形态请求保持成功但不重复结算");
        Near(rules.Cognition.Remaining, 2f, "相同形态不重复扣费");
        Check(spirit.RequestSpiritForm(true, PlayerFormChangeReason.Death), "死亡强制回漫游不受手动冷却阻挡");
        Near(rules.Cognition.Remaining, 1f, "死亡强制转换仍付费");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "区域强制重新进入模拟并耗尽");
        Check(rules.Cognition.Remaining == 0f && requests == 1 && rules.CognitionExhaustionRequested,
            "转换扣到零锁存唯一耗尽请求");
        rules.AdvanceSimulationTime(100f);
        Check(spirit.RequestSpiritForm(true, PlayerFormChangeReason.Death), "无余额时强制形态仍完成自己的提交");
        Check(requests == 1 && rules.Cognition.Remaining == 0f, "后续计时和强制转换不重复耗尽请求或出现负余额");
        Check(rules.Stage == PlayerGameplayStage.Exploration, "消费者尚未接入时保持探索，请求不冒充进入逃生");

        var timed = new PlayerLifecycleRules(null, null, cognition: new LevelCognition(1f), simulationCognitionPerSecond: 2f);
        int timedRequests = 0; timed.CognitionExhaustionRequestedOnce += () => timedRequests++;
        timed.ConfigureStage(PlayerGameplayStage.Exploration, true);
        timed.AdvanceSimulationTime(.75f); timed.AdvanceSimulationTime(1f);
        Check(timed.Cognition.Remaining == 0f && timedRequests == 1 && timed.CognitionExhaustionRequested,
            "仅计时耗尽也汇入同一一次请求并钳制余额");
    }

    static void CheckInitialZero()
    {
        Player spirit = Actor();
        var rules = new PlayerLifecycleRules(null, spirit, cognition: new LevelCognition(0f), formCost: 0f);
        int requests = 0; rules.CognitionExhaustionRequestedOnce += () => requests++;
        Check(!rules.CognitionExhaustionRequested, "零初始尚未组装就绪时不提前请求");
        Check(rules.BeginExploration(), "零初始仍完成精神体初始化");
        Check(requests == 1 && rules.CognitionExhaustionRequested && !rules.IsSimulation
            && spirit._ActionData.isFlying && rules.Stage == PlayerGameplayStage.Exploration,
            "零初始在漫游探索就绪后请求一次，尚不改变阶段");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "零费用转换保持已有统一提交入口");
        rules.AdvanceSimulationTime(10f);
        Check(requests == 1 && rules.Cognition.Remaining == 0f, "零初始与后续零费用转换、计时仍只有一个请求");
    }

    static Player Actor()
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
        if (asset == null) throw new InvalidOperationException("Missing P8 fixture: SpiritPlayer.prefab");
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave;
        item.transform.position = new Vector2(30000f + 200f * group++, 30000f);
        objects.Add(item); Physics2D.SyncTransforms();
        Player player = item.GetComponent<Player>();
        Invoke(player.GetComponent<CharacterPhysics>(), "Awake"); Invoke(player, "Awake");
        Invoke(player.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(player, "Start");
        Invoke(player.GetComponent<CharacterPhysics>(), "OnEnable");
        player.ChangeInputAsset(new PlayerInputData());
        return player;
    }

    static bool ReadPaused(Type type) => (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Invoke(object instance, string name)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method == null) continue;
            method.Invoke(instance, null); return;
        }
        throw new MissingMethodException(name);
    }
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .002f, message);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P8.1 FAILED: " + message);
        passed.Add(message);
    }
}
#endif
