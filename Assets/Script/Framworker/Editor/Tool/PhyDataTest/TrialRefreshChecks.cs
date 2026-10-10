#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P9.1–9.3 试验轻刷新定向回归。只创建临时对象；跨场景过滤留真实 Play Flow 验证。</summary>
public static class TrialRefreshChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<string> passed = new List<string>();
    static Scene trialScene;
    static int group;

    [MenuItem("自定义工具/测试工具/验证/P9 验证试验轻刷新（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行试验刷新检查。");
        passed.Clear(); group = 0;
        SimulationMode2D oldMode = Physics2D.simulationMode;
        Scene oldActive = SceneManager.GetActiveScene();
        var registry = (List<PhysicalBox>)typeof(LevelBoxContext).GetField("boxes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var previousBoxes = new List<PhysicalBox>(registry);
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = scheduler.GetValue(null);
        bool quitting = MonoPublicMgr.IsQuitting;
        bool oldWorldPaused = ReadPaused(typeof(BasicEntity));
        bool oldPlatformPaused = ReadPaused(typeof(Taijie));
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            MonoPublicMgr.IsQuitting = false;
            trialScene = oldActive;
            // 隔离夹具登记，防止整关入口恢复用户当前场景中已登记的箱子。
            registry.Clear();
            if ((MonoPublicMgr)previous == null)
                scheduler.SetValue(null, NewObject("P9 temporary scheduler", Vector2.zero, trialScene).AddComponent<MonoPublicMgr>());
            CheckTrialPoint();
            CheckDefaultLifetime();
            CheckWholeLevelReset();
            CheckMapCenterAlignment();
            CheckRejectedRequests();
            Debug.Log("P9 trial refresh: " + passed.Count + " checks passed.");
        }
        finally
        {
            DestroyObjects(0);
            registry.Clear(); registry.AddRange(previousBoxes);
            BasicEntity.SetWorldPaused(oldWorldPaused); Taijie.SetWorldPaused(oldPlatformPaused);
            scheduler.SetValue(null, previous); MonoPublicMgr.IsQuitting = quitting;
            Physics2D.simulationMode = oldMode;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P9Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "trial-refresh-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckTrialPoint()
    {
        GameObject host = NewObject("P9 trial setup", new Vector2(9000f, 9000f), trialScene);
        SceneLevelSetup setup = host.AddComponent<SceneLevelSetup>();
        Near(setup.TrialPoint, host.transform.position, "未配试验点时固定使用本关配置位置");
        GameObject point = NewObject("P9 authored trial point", new Vector2(9013f, 9027f), trialScene);
        typeof(SceneLevelSetup).GetField("trialStart", Hidden).SetValue(setup, point.transform);
        Near(setup.TrialPoint, point.transform.position, "已配试验点使用人工点位，与精神体当前位置无关");
    }

    static void CheckDefaultLifetime()
    {
        World w = NewWorld();
        PhysicalBox box = Box(w.origin + new Vector2(15f, 1f), trialScene);
        Vector2 home = box.DebugActualPosition;
        box.RefreshMotionState(home + new Vector2(12f, 7f));
        box.gameObject.SetActive(false);
        box.gameObject.SetActive(true);
        Invoke(box, "OnEnable");
        BasicEntity.SetWorldPaused(true);
        box.RestoreDefault();
        Near(box.DebugActualPosition, home, "移动、停用、启用和暂停不覆盖首次记录的默认位置");
        Check(!box.IsMotionParticipating, "恢复默认不绕过现有世界暂停许可");
        BasicEntity.SetWorldPaused(false);
        Check(box.IsMotionParticipating, "恢复世界后箱子重新参与运动");
    }

    static void CheckWholeLevelReset()
    {
        World w = NewWorld();
        PhysicalBox selected = BoxOnGround(w, w.origin.x + 2f);
        PhysicalBox support = BoxOnGround(w, w.origin.x + 40f);
        PhysicalBox stored = BoxOnGround(w, w.origin.x + 80f);
        Vector2 selectedHome = selected.DebugActualPosition;
        Vector2 supportHome = support.DebugActualPosition;
        Vector2 storedHome = stored.DebugActualPosition;
        Bounds supportBounds = support.BodyBounds;
        Vector2 targetCenter = new Vector2(supportBounds.center.x,
            supportBounds.max.y + w.physics.BodyBounds.extents.y + .005f);
        Vector2 target = w.physics.DebugActualPosition + targetCenter - (Vector2)w.physics.BodyBounds.center;

        Check(w.rules.BeginExploration(3600f), "真实规则初始化探索漫游");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "真实手动转换进入试验模拟并记录冷却");
        w.rules.AdvanceSimulationTime(3f);
        float cognition = w.rules.Cognition.Remaining;
        float nextChange = (float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules);
        int spiritId = w.spirit.GetInstanceID();
        int formCommits = 0;
        w.rules.FormCommitted += _ => formCommits++;

        MoveCenter(w.physics, new Vector2(selected.BodyBounds.min.x - w.physics.BodyBounds.extents.x - .01f,
            w.groundTop + w.physics.BodyBounds.extents.y + .005f));
        LevelBoxContext.Resample(trialScene);
        w.physics.ResampleEnvironment();
        w.spirit.TickLogic(0f);
        w.input.interactTapped = true;
        w.spirit.TickLogic(0f);
        Check(w.spirit._ActionData.PushPullEnabled && w.spirit._ActionData.PushPullBox == selected,
            "刷新前经实际玩家交互建立箱子选中关系");

        selected.RefreshMotionState(selectedHome + Vector2.right * 11f);
        support.RefreshMotionState(supportHome + Vector2.right * 20f);
        stored.RefreshMotionState(storedHome + Vector2.right * 30f);
        Check(stored.Interact(new PlayerInteractRequest(w.spirit, PlayerInteractKind.LongPress, true))
            == PlayerInteractOutcome.Stored, "远处箱子在本次试验中进入收纳");

        PlayerStateMachine state = State(w.spirit);
        state.staminaMax = 150f; state.stamina = 17f; state.oxygen = 12f;
        w.physics.GetComponent<Rigidbody2D>().velocity = new Vector2(8f, -9f);
        selected.GetComponent<Rigidbody2D>().velocity = new Vector2(5f, 6f);
        support.GetComponent<Rigidbody2D>().velocity = new Vector2(7f, 8f);
        w.physics.AddTimeSpeed(1f, 30f);
        Check(w.rules.TryResetTrial(target), "试验重置使用已确认目标完成一次全关轻刷新");

        Near(selected.DebugActualPosition, selectedHome, "原选中箱回默认位置");
        Near(support.DebugActualPosition, supportHome, "远处另一片区域的箱子也回默认位置");
        Near(stored.DebugActualPosition, storedHome, "更远区域的收纳箱同样回默认位置");
        Check(!stored.IsStored && stored.GetComponent<BoxCollider2D>().enabled && stored.IsMotionParticipating,
            "整关恢复退出收纳并恢复碰撞与运动参与");
        Check(selected.GetPlanarVelocity() == Vector2.zero && support.GetPlanarVelocity() == Vector2.zero
            && stored.GetPlanarVelocity() == Vector2.zero, "全关箱子的旧运动均清除");
        Check(!w.spirit._ActionData.PushPullEnabled && w.spirit._ActionData.PushPullBox == null,
            "刷新解除角色选中与推拉关系");
        Check(w.spirit.GetInstanceID() == spiritId && w.rules.Stage == PlayerGameplayStage.Exploration
            && w.rules.IsSimulation && !w.spirit._ActionData.isFlying, "同一精神体仍处于探索模拟行走");
        Near(w.physics.DebugActualPosition, target, "同一精神体安置到请求的人工目标");
        Near(state.stamina, 150f, "恢复体力但保留当前生效上限");
        Near(state.oxygen, state.oxygenMax, "恢复氧气");
        Check(w.physics.GetPlanarVelocity() == Vector2.zero
            && !(bool)typeof(CharacterPhysics).GetProperty("HasPendingJump", Hidden).GetValue(w.physics),
            "恢复角色速度与待消费运动请求");
        Near(w.rules.Cognition.Remaining, cognition, "轻刷新保留已经消费后的共享认知余额");
        Near((float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules), nextChange,
            "轻刷新不修改真实手动转换冷却截止时间");
        Check(w.rules.ManualFormCooldownRemaining > 0f && formCommits == 0,
            "试验重置不发生形态转换结算");
        Check(!w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual) && w.rules.IsSimulation,
            "重置后原手动转换冷却仍拒绝立刻退出模拟");
        Check(selected.IsMotionParticipating && support.IsMotionParticipating && stored.IsMotionParticipating
            && w.physics.IsMotionParticipating, "全关箱子与角色的模拟许可恢复");
        Check(w.spirit.NowPhyData.isGrounded && w.physics.DebugGroundCollider == support.GetComponent<BoxCollider2D>(),
            "全部箱子恢复参与后最终采样能发现目标脚下的箱顶");
    }

    static void CheckMapCenterAlignment()
    {
        Scene previousScene = trialScene;
        int firstObject = objects.Count;
        // 预览场景不要求保存当前 untitled 场景，也不替换或切换用户关卡。
        trialScene = EditorSceneManager.NewPreviewScene();
        try
        {
            World w = NewWorld();
            BoxCollider2D collider = w.physics.GetComponent<BoxCollider2D>();
            collider.offset = new Vector2(.65f, -.8f);
            w.spirit.transform.localScale = new Vector3(-1.5f, .75f, 1f);
            Physics2D.SyncTransforms();
            MoveCenter(w.physics, w.origin + Vector2.up * 6f);
            Check(w.rules.BeginExploration(3600f)
                && w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual),
                "带碰撞框偏移及镜像缩放的精神体进入真实模拟");

            SceneLevelSetup setup = NewObject("P9 map setup", w.origin, trialScene).AddComponent<SceneLevelSetup>();
            Transform fallback = NewObject("P9 fixed root point", w.origin + new Vector2(10f, 7f), trialScene).transform;
            typeof(SceneLevelSetup).GetField("trialStart", Hidden).SetValue(setup, fallback);
            Check(w.rules.TryGetTrialResetPoint(setup.TrialPoint, out Vector2 point, out _)
                && w.rules.TryResetTrial(point), "未启用地图仍由固定 TrialStart 完成轻刷新");
            Near(w.physics.DebugActualPosition, setup.TrialPoint, "无地图的 TrialStart 保持原角色根坐标语义");
            Check(Vector2.Distance(w.physics.BodyBounds.center, setup.TrialPoint) > .5f,
                "无地图固定起点不隐式改为碰撞框中心");

            MapCell current = Cell("P9 narrow current cell", w.physics.BodyBounds.center);
            MapCell target = Cell("P9 narrow target cell", current.Center + Vector2.right * 12f);
            typeof(SceneLevelSetup).GetField("mapEnabled", Hidden).SetValue(setup, true);
            typeof(SceneLevelSetup).GetField("mapCells", Hidden).SetValue(setup, new[] { current, target });
            Check(w.rules.BindExplorationMap(setup), "启用合法高度小于角色中心偏移的手工格子");
            w.rules.AdvanceExplorationMap();
            MoveCenter(w.physics, target.Center); w.rules.AdvanceExplorationMap();
            MoveCenter(w.physics, current.Center); w.rules.AdvanceExplorationMap();
            Check(w.rules.MapRecord.IsLit(current) && w.rules.MapRecord.IsLit(target),
                "正常探索采样已点亮当前及目标格");
            w.rules.AdvanceSimulationTime(3f);
            float cognition = w.rules.Cognition.Remaining;
            float nextChange = (float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules);
            int formCommits = 0;
            w.rules.FormCommitted += _ => formCommits++;
            PlayerStateMachine state = State(w.spirit);
            state.stamina = 17f; state.oxygen = 12f;

            Check(w.rules.TryGetTrialResetPoint(setup.TrialPoint, out point, out _) && point == current.Center
                && w.rules.TryResetTrial(point), "地图试验重置使用当前格中心");
            Near(w.physics.BodyBounds.center, current.Center, "偏移及缩放碰撞框中心准确对齐当前格中心");
            Check(w.rules.MapRecord.CurrentCell == current, "合法矮格重置后当前格不丢失");
            Check(w.rules.TryResetTrial(target.Center), "地图传送复用同一轻刷新入口");
            Near(w.physics.BodyBounds.center, target.Center, "地图目标对齐碰撞框中心而非角色根");
            Check(w.rules.MapRecord.CurrentCell == target, "合法矮格传送后当前格为目标格");
            Check(w.rules.MapRecord.IsLit(current) && w.rules.MapRecord.IsLit(target), "地图轻刷新保留原点亮记录");
            Near(state.stamina, state.staminaMax, "地图轻刷新显式恢复体力");
            Near(state.oxygen, state.oxygenMax, "地图轻刷新显式恢复氧气");
            Near(w.rules.Cognition.Remaining, cognition, "中心安置不额外点亮或改动认知余额");
            Near((float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules), nextChange,
                "中心安置保留手动转换冷却");
            Check(formCommits == 0 && w.rules.IsSimulation && !w.spirit._ActionData.isFlying,
                "中心安置不重复结算转换并保持模拟行走");
        }
        finally
        {
            DestroyObjects(firstObject);
            EditorSceneManager.ClosePreviewScene(trialScene);
            trialScene = previousScene;
        }
    }

    static void CheckRejectedRequests()
    {
        World w = NewWorld();
        PhysicalBox box = BoxOnGround(w, w.origin.x + 20f);
        Check(w.rules.BeginExploration(), "拒绝场景初始化为真实漫游");
        box.RefreshMotionState(box.DebugActualPosition + Vector2.right * 6f);
        State(w.spirit).stamina = 21f; State(w.spirit).oxygen = 14f;
        AssertRejected(w, box, w.origin, "漫游阶段拒绝试验重置");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "拒绝验证随后进入真实模拟");
        Check(box.Interact(new PlayerInteractRequest(w.spirit, PlayerInteractKind.LongPress, true))
            == PlayerInteractOutcome.Stored, "拒绝验证保留一次已收纳的试验现场");
        AssertRejected(w, box, new Vector2(float.NaN, w.origin.y), "无效 NaN 目标拒绝整次重置");
        AssertRejected(w, box, new Vector2(w.origin.x, float.PositiveInfinity), "无效无限目标拒绝整次重置");
        w.rules.ConfigureStage(PlayerGameplayStage.Tutorial);
        AssertRejected(w, box, w.origin, "教学阶段拒绝试验重置");
        w.rules.ConfigureStage(PlayerGameplayStage.Escape);
        AssertRejected(w, box, w.origin, "逃生阶段拒绝试验重置");
    }

    static void AssertRejected(World w, PhysicalBox box, Vector2 target, string message)
    {
        Vector2 position = w.physics.DebugActualPosition, boxPosition = box.DebugActualPosition;
        bool stored = box.IsStored, worldPaused = ReadPaused(typeof(BasicEntity));
        bool flying = w.spirit._ActionData.isFlying, simulation = w.rules.IsSimulation;
        PlayerGameplayStage stage = w.rules.Stage;
        float stamina = State(w.spirit).stamina, oxygen = State(w.spirit).oxygen, cognition = w.rules.Cognition.Remaining;
        float nextChange = (float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules);
        Check(!w.rules.TryResetTrial(target), message);
        Check(w.physics.DebugActualPosition == position && box.DebugActualPosition == boxPosition && box.IsStored == stored
            && State(w.spirit).stamina == stamina && State(w.spirit).oxygen == oxygen
            && w.rules.Cognition.Remaining == cognition && ReadPaused(typeof(BasicEntity)) == worldPaused
            && w.rules.Stage == stage && w.rules.IsSimulation == simulation && w.spirit._ActionData.isFlying == flying
            && (float)typeof(PlayerLifecycleRules).GetField("nextManualChange", Hidden).GetValue(w.rules) == nextChange,
            message + "且不部分恢复世界、位置或资源");
    }

    sealed class World
    {
        public Player body, spirit;
        public CharacterPhysics physics;
        public PlayerLifecycleRules rules;
        public PlayerInputData input;
        public Vector2 origin;
        public float groundTop;
    }

    static World NewWorld()
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        var w = new World { origin = new Vector2(10000f + 1000f * group++, 10000f), groundTop = 10000f };
        w.body = Actor("player20.prefab", w.origin + new Vector2(-40f, 6f));
        w.spirit = Actor("SpiritPlayer.prefab", w.origin + Vector2.up * 6f);
        w.physics = w.spirit.GetComponent<CharacterPhysics>();
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(w.physics);
        GameObject ground = NewObject("P9 trial ground", w.origin + new Vector2(40f, -.5f), trialScene);
        ground.layer = FirstLayer(config.groundLayer);
        ground.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);
        Physics2D.SyncTransforms();
        MoveCenter(w.physics, w.origin + Vector2.up * (w.physics.BodyBounds.extents.y + .005f));
        w.input = new PlayerInputData(); w.spirit.ChangeInputAsset(w.input);
        w.rules = new PlayerLifecycleRules(w.body, w.spirit, cognition: new LevelCognition(100f),
            formCost: 7f, simulationCognitionPerSecond: 2f);
        return w;
    }

    static Player Actor(string prefab, Vector2 position)
    {
        Player player = Prefab<Player>(prefab, position, trialScene);
        Invoke(player.GetComponent<CharacterPhysics>(), "Awake"); Invoke(player, "Awake");
        Invoke(player.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(player, "Start");
        Invoke(player.GetComponent<CharacterPhysics>(), "OnEnable");
        return player;
    }

    static PhysicalBox BoxOnGround(World w, float x)
    {
        PhysicalBox box = Prefab<PhysicalBox>("box.prefab", new Vector2(x, w.groundTop + 2f), trialScene);
        Bounds bounds = box.GetComponent<BoxCollider2D>().bounds;
        box.transform.position += (Vector3)(new Vector2(x, w.groundTop + bounds.extents.y + .005f) - (Vector2)bounds.center);
        Physics2D.SyncTransforms();
        Invoke(box, "Awake"); Invoke(box, "OnEnable");
        box.ResampleEnvironment();
        return box;
    }

    static PhysicalBox Box(Vector2 position, Scene scene)
    {
        PhysicalBox box = Prefab<PhysicalBox>("box.prefab", position, scene);
        Invoke(box, "Awake"); Invoke(box, "OnEnable");
        return box;
    }

    static T Prefab<T>(string prefab, Vector2 position, Scene scene) where T : Component
    {
        string folder = prefab == "player20.prefab" || prefab == "SpiritPlayer.prefab" ? "Player/" : "SceneObj/";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/" + folder + prefab);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing P9 fixture: " + prefab);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave;
        if (item.scene != scene) SceneManager.MoveGameObjectToScene(item, scene);
        item.transform.position = position;
        objects.Add(item); Physics2D.SyncTransforms();
        return item.GetComponent<T>();
    }

    static GameObject NewObject(string name, Vector2 position, Scene scene)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        if (item.scene != scene) SceneManager.MoveGameObjectToScene(item, scene);
        item.transform.position = position; objects.Add(item); return item;
    }

    static MapCell Cell(string name, Vector2 center)
    {
        MapCell cell = NewObject(name, center, trialScene).AddComponent<MapCell>();
        typeof(MapCell).GetField("width", Hidden).SetValue(cell, 4f);
        typeof(MapCell).GetField("height", Hidden).SetValue(cell, .3f);
        typeof(MapCell).GetField("cognitionCost", Hidden).SetValue(cell, 3f);
        return cell;
    }

    static void DestroyObjects(int first)
    {
        for (int i = objects.Count - 1; i >= first; i--)
        {
            if (objects[i] == null) continue;
            foreach (PhysicalBox box in objects[i].GetComponentsInChildren<PhysicalBox>()) LevelBoxContext.Unregister(box);
            foreach (BasicEntity entity in objects[i].GetComponentsInChildren<BasicEntity>()) Invoke(entity, "OnDisable");
            Object.DestroyImmediate(objects[i]);
        }
        objects.RemoveRange(first, objects.Count - first);
    }

    static void MoveCenter(BasicEntity entity, Vector2 center)
    {
        entity.RefreshMotionState(entity.DebugActualPosition + center - (Vector2)entity.BodyBounds.center);
        Physics2D.SyncTransforms();
    }

    static PlayerStateMachine State(Player player) => (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(player);
    static bool ReadPaused(Type type) => (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static int FirstLayer(LayerMask mask)
    {
        for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
        throw new InvalidOperationException("P9 fixture 缺少地面查询层。");
    }
    static void Invoke(object instance, string method)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo found = type.GetMethod(method, Hidden | BindingFlags.DeclaredOnly);
            if (found == null) continue;
            found.Invoke(instance, null); return;
        }
        throw new MissingMethodException(method);
    }
    static void Near(Vector2 actual, Vector2 expected, string message) => Check((actual - expected).magnitude <= .001f, message);
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) <= .001f, message);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P9 FAILED: " + message);
        passed.Add(message);
    }
}
#endif
