#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>P7 实际飞行执行和漫游／模拟规则回归。只创建临时对象，不保存场景或预制体。</summary>
public static class SpiritFlightChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static readonly MethodInfo sidePush = typeof(BasicEntity).Assembly.GetType("EntitySidePush")
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
    private static int group;

    [MenuItem("自定义工具/测试工具/验证/P7 验证精神体飞行（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行 P7 检查。");
        passed.Clear(); group = 0;
        SimulationMode2D oldMode = Physics2D.simulationMode;
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = scheduler.GetValue(null);
        bool quitting = MonoPublicMgr.IsQuitting;
        bool oldWorldPaused = ReadPaused(typeof(BasicEntity));
        bool oldPlatformPaused = ReadPaused(typeof(Taijie));
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)previous == null)
                scheduler.SetValue(null, NewObject("P7 temporary scheduler", Vector2.zero).AddComponent<MonoPublicMgr>());
            CheckFlightMotion();
            CheckEnvironmentalIsolation();
            CheckExitOccupancy();
            CheckRestoreTolerance();
            CheckPausedOccupancy();
            CheckWaterResample();
            CheckModeAndDeath();
            CheckManualCooldown();
            CheckCommitNotifications();
            CheckConfiguredCognitionCost();
            CheckPlatformPause();
            Debug.Log("P7 spirit flight: " + passed.Count + " checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] == null) continue;
                foreach (PhysicalBox box in objects[i].GetComponentsInChildren<PhysicalBox>()) LevelBoxContext.Unregister(box);
                foreach (BasicEntity entity in objects[i].GetComponentsInChildren<BasicEntity>()) Invoke(entity, "OnDisable");
                foreach (Taijie platform in objects[i].GetComponentsInChildren<Taijie>()) Invoke(platform, "OnDisable");
                Object.DestroyImmediate(objects[i]);
            }
            SetWorldPaused(oldWorldPaused); SetPlatformPaused(oldPlatformPaused);
            objects.Clear(); scheduler.SetValue(null, previous); MonoPublicMgr.IsQuitting = quitting;
            Physics2D.simulationMode = oldMode;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P7Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "spirit-flight-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunRegressionForBatch()
    {
        try
        {
            Run();
            EnvironmentIntegrationChecks.Run();
            BoxInteractionChecks.Run();
            PlayerDeathCycleChecks.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckFlightMotion()
    {
        World w = NewWorld(); Begin(w);
        int id = w.spirit.GetInstanceID();
        Check(w.spirit._ActionData.isFlying && !w.rules.IsSimulation, "探索初始化为漫游飞行");
        Check(w.spirit.GetComponent<Collider2D>().isTrigger && w.physics.IsMotionParticipating,
            "飞行取消实心阻挡且继续参与现有运动管线");
        w.input.moveInput = 1f; w.input.verticalInput = 1f;
        Step(w);
        float speed = w.physics.GetPlanarVelocity().magnitude;
        Near(speed, (float)typeof(CharacterPhysics).GetField("fourWaySpeed", Hidden).GetValue(w.physics), .02f,
            "飞行斜向速度按四向速率归一，不叠普通行走加速");
        w.input.moveInput = w.input.verticalInput = 0f;
        Vector2 start = w.physics.DebugActualPosition;
        for (int i = 0; i < 30; i++) Step(w);
        Near((w.physics.DebugActualPosition - start).magnitude, 0f, .005f, "零输入飞行悬停，不叠重力或惯性");

        Collider2D wall = Solid(w.physics.BodyBounds.center + Vector3.right * 2f, new Vector2(.25f, 10f), w.wallLayer);
        w.input.moveInput = 1f;
        for (int i = 0; i < 40; i++) Step(w);
        Check(w.physics.BodyBounds.min.x > wall.bounds.max.x, "真正穿过原生实心薄墙，静态速度裁剪不拦截飞行");
        Check(w.spirit.GetInstanceID() == id, "飞行移动保持同一个精神体实例");
        Check(!w.body.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "本体不能通过形态规则启用飞行");
    }

    private static void CheckEnvironmentalIsolation()
    {
        World w = NewWorld();
        WaterWithEffects water = Prefab<WaterWithEffects>("WaterPoolWithEffects.prefab", w.physics.BodyBounds.center);
        water.transform.localScale = Vector3.one;
        water.GetComponent<BoxCollider2D>().size = new Vector2(12f, 12f);
        typeof(WaterWithEffects).GetField("flowVelocity", Hidden).SetValue(water, new Vector2(3f, 2f));
        ForceField force = Prefab<ForceField>("F1.prefab", w.physics.BodyBounds.center);
        force.transform.localScale = Vector3.one;
        force.GetComponent<BoxCollider2D>().size = new Vector2(12f, 12f);
        force.GetComponent<BoxCollider2D>().isTrigger = true;
        typeof(ForceField).GetField("add", Hidden).SetValue(force, 5f);
        typeof(ForceField).GetField("maxSpeed", Hidden).SetValue(force, 20f);
        w.physics.ResampleEnvironment();
        Check(w.physics.EnvironmentContext.DebugSourceCount > 0 && w.physics.DebugDynamicForceCount > 0,
            "隔离前真实水流和力场已通过环境管线接入");
        w.physics.AddTimeSpeed(10f, 20f);
        Begin(w);
        Vector2 start = w.physics.DebugActualPosition;
        for (int i = 0; i < 50; i++) Step(w);
        Near((w.physics.DebugActualPosition - start).magnitude, 0f, .005f, "飞行不消费水流、力场或旧限时速度");
        Check(w.physics.EnvironmentContext.DebugSourceCount == 0 && w.physics.DebugDynamicForceCount == 0,
            "飞行释放外部效果绑定，持续环境不在退出前积累");
        water.gameObject.SetActive(false); force.gameObject.SetActive(false);
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "离开效果范围后可以退出飞行");
        Step(w);
        Near(w.physics.GetPlanarVelocity().x, 0f, .02f, "退出飞行没有旧水流、力场和限时速度尾效");
        Check(w.physics.GetPlanarVelocity().y < 0f, "退出飞行后恢复原重力执行");
    }

    private static void CheckExitOccupancy()
    {
        World w = NewWorld(); Begin(w);
        Vector2 position = w.physics.DebugActualPosition;
        BoxCollider2D solid = (BoxCollider2D)Solid(w.physics.BodyBounds.center, new Vector2(4f, 4f), w.wallLayer);
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "当前位置重合实心地形时拒绝行走");
        Check(w.spirit._ActionData.isFlying && !w.rules.IsSimulation && w.physics.DebugActualPosition == position,
            "退出失败保持飞行、原模式及当前位置，不搬移寻找落点");
        solid.isTrigger = true;
        Physics2D.SyncTransforms();
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "区域 Trigger 和自身碰撞体不阻止恢复行走");
        Check(!w.spirit.GetComponent<Collider2D>().isTrigger && w.rules.IsSimulation, "成功退出恢复实心碰撞并同步模拟模式");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "同一个精神体可从模拟回漫游");
        w.physics.transform.localScale = new Vector3(1.5f, 1.2f, 1f);
        Physics2D.SyncTransforms();
        solid.isTrigger = false;
        solid.transform.position = w.physics.BodyBounds.center + Vector3.right * (w.physics.BodyBounds.extents.x - .05f);
        solid.size = new Vector2(.1f, .4f);
        Physics2D.SyncTransforms();
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "缩放碰撞框边缘真实重合也拒绝恢复，不只检查角色中心");
    }

    private static void CheckPausedOccupancy()
    {
        World w = NewWorld();
        PhysicalBox box = Prefab<PhysicalBox>("box.prefab", w.physics.BodyBounds.center);
        Invoke(box, "Awake"); Invoke(box, "OnEnable"); LevelBoxContext.Register(box); w.entities.Add(box);
        MoveCenter(box, w.physics.BodyBounds.center);
        Begin(w);
        Check(!box.GetComponent<Rigidbody2D>().simulated && box.GetComponent<Collider2D>().enabled,
            "漫游暂停箱子原生模拟并保留实心碰撞范围");
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "暂停箱子的碰撞体仍参与退出飞行占用拒绝");
        MoveCenter(box, (Vector2)w.physics.BodyBounds.center + Vector2.right * 20f);
        MoveCenter(w.body.GetComponent<CharacterPhysics>(), w.physics.BodyBounds.center);
        Check(!w.body.GetComponent<Rigidbody2D>().simulated, "探索本体待机不参加原生模拟");
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "待机本体碰撞范围同样阻止恢复行走");
        MoveCenter(w.body.GetComponent<CharacterPhysics>(), (Vector2)w.physics.BodyBounds.center + Vector2.left * 20f);
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "仅剩自身且暂停实体已退开时恢复行走");
        Check(box.GetComponent<Rigidbody2D>().simulated && !w.body.GetComponent<Rigidbody2D>().simulated,
            "进入模拟恢复箱子运动，待机本体保持暂停");
    }

    private static void CheckRestoreTolerance()
    {
        World w = NewWorld(); Begin(w);
        Bounds bounds = w.physics.BodyBounds;
        Collider2D floor = Solid(new Vector2(bounds.center.x, bounds.min.y - .5f), new Vector2(12f, 1f), w.wallLayer);
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "接触地面可进入模拟");
        for (int i = 0; i < 90; i++) Step(w);
        float restingDepth = -w.spirit.GetComponent<BoxCollider2D>().Distance(floor).distance;
        Debug.Log("P7 walking restore resting penetration=" + restingDepth);
        Vector2 standing = w.physics.DebugActualPosition;
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual), "站稳后原地转飞行");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "站稳后不移动原地转回行走，不被原生残余嵌入拒绝");
        Near((w.physics.DebugActualPosition - standing).magnitude, 0f, .0001f, "站立转换不手动搬移角色");
        for (int i = 0; i < 20; i++) Step(w);
        Near((w.physics.DebugActualPosition - standing).magnitude, 0f, .03f, "原地转换后保持站稳，没有跳起或显著漂移");

        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual), "浅重叠夹具转飞行");
        // 固定制造接触级重叠，避免只依赖本次原生模拟恰好留下多少残差。
        bounds = w.physics.BodyBounds;
        MoveCenter(w.physics, new Vector2(bounds.center.x, floor.bounds.max.y + bounds.extents.y - .01f));
        bounds = w.physics.BodyBounds;
        Collider2D wall = Solid(new Vector2(bounds.max.x + .5f - .01f, bounds.center.y), new Vector2(1f, 8f), w.wallLayer);
        Debug.Log("P7 corner penetration floor=" + -w.spirit.GetComponent<BoxCollider2D>().Distance(floor).distance
            + "; wall=" + -w.spirit.GetComponent<BoxCollider2D>().Distance(wall).distance
            + "; actual=" + w.physics.DebugActualPosition + "; transform=" + w.physics.transform.position);
        float cognition = w.rules.Cognition.Remaining;
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "地面与墙角同时浅嵌入可恢复行走");
        Near(w.rules.Cognition.Remaining, cognition - 1f, .001f, "墙角浅嵌入成功仍只扣一次转换费用");
        Vector2 corner = w.physics.DebugActualPosition;
        for (int i = 0; i < 35; i++) Step(w);
        Near((w.physics.DebugActualPosition - corner).magnitude, 0f, .06f, "墙角浅重叠由原生模拟稳定处理，无大幅挤出");
        Check(w.spirit.GetComponent<BoxCollider2D>().Distance(wall).distance >= -.02f
            && w.spirit.GetComponent<BoxCollider2D>().Distance(floor).distance >= -.02f,
            "原生模拟后地面和墙面重叠均留在接触容差内");

        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual), "深重叠夹具转飞行");
        bounds = w.physics.BodyBounds;
        MoveCenter(w.physics, new Vector2(wall.bounds.min.x - bounds.extents.x + .01f,
            floor.bounds.max.y + bounds.extents.y - .05f));
        Check(w.physics.BodyBounds.max.x > wall.bounds.min.x
            && w.physics.BodyBounds.max.x - wall.bounds.min.x < .02f
            && floor.bounds.max.y - w.physics.BodyBounds.min.y > .04f,
            "拒绝夹具确实同时包含浅墙面和深地面重叠");
        Vector2 rejected = w.physics.DebugActualPosition;
        cognition = w.rules.Cognition.Remaining;
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "同时命中浅墙面和深地面时仍拒绝，不因一个浅命中放行");
        Check(w.spirit._ActionData.isFlying && !w.rules.IsSimulation && w.physics.DebugActualPosition == rejected,
            "深重叠拒绝保持飞行与位置");
        Near(w.rules.Cognition.Remaining, cognition, .001f, "深重叠拒绝不扣费");
        floor.gameObject.SetActive(false); wall.gameObject.SetActive(false);

        World paused = NewWorld(); Begin(paused);
        PhysicalBox box = Prefab<PhysicalBox>("box.prefab", paused.physics.BodyBounds.center);
        Invoke(box, "Awake"); Invoke(box, "OnEnable"); paused.entities.Add(box);
        Bounds playerBounds = paused.physics.BodyBounds, boxBounds = box.BodyBounds;
        MoveCenter(box, new Vector2(playerBounds.max.x + boxBounds.extents.x - .01f, playerBounds.center.y));
        Check(!box.GetComponent<Rigidbody2D>().simulated, "浅重叠箱子保持漫游暂停，确实走暂停形状补查");
        Check(paused.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "暂停箱子边缘浅重叠使用与地形相同容差");
        Check(paused.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual), "暂停箱子深重叠夹具转飞行");
        MoveCenter(box, new Vector2(playerBounds.max.x + boxBounds.extents.x - .05f, playerBounds.center.y));
        Check(!paused.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "暂停箱子超过容差的重叠仍拒绝");
        MoveCenter(box, new Vector2(playerBounds.max.x + boxBounds.extents.x - .01f, playerBounds.center.y));
        typeof(CharacterPhysics).GetField("walkingRestoreTolerance", Hidden).SetValue(paused.physics, 0f);
        Check(!paused.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "容差配置为零可恢复原严格判定");
    }

    private static void CheckWaterResample()
    {
        World w = NewWorld(); Begin(w);
        WaterRegion water = Prefab<WaterRegion>("WaterPool.prefab", w.physics.BodyBounds.center);
        water.transform.localScale = Vector3.one;
        water.GetComponent<BoxCollider2D>().size = new Vector2(8f, 8f);
        Physics2D.SyncTransforms();
        w.physics.ResampleEnvironment();
        Check(!w.spirit.WaterFacts.submerged && w.spirit._ActionData.isFlying, "飞行期间屏蔽外部水事实");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "纯水区域不会阻止恢复行走");
        Check(w.spirit.WaterFacts.submerged && w.spirit._ActionData.isSwimming,
            "退出飞行当次立即采样当前位置水事实并转游泳，不等待下一物理步");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "淹没模拟可转飞行并清水事实");
        water.gameObject.SetActive(false);
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "水来源撤销后原地恢复行走");
        Check(!w.spirit.WaterFacts.submerged && !w.spirit._ActionData.isSwimming,
            "恢复只用当前水来源，不带回飞行前缓存");
    }

    private static void CheckModeAndDeath()
    {
        World w = NewWorld();
        PhysicalBox box = Prefab<PhysicalBox>("box.prefab", (Vector2)w.physics.BodyBounds.center + Vector2.right * 12f);
        Invoke(box, "Awake"); Invoke(box, "OnEnable"); LevelBoxContext.Register(box); w.entities.Add(box);
        Begin(w);
        int spiritId = w.spirit.GetInstanceID();
        PlayerStateMachine state = State(w.spirit);
        state.stamina = 37f; state.oxygen = 23f;
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "进入模拟经同一规则提交行走和世界恢复");
        Check(w.rules.IsSimulation && w.spirit.Permissions.pushPull && !w.body.Permissions.motionUpdates,
            "模拟权限允许精神体推拉，本体仍待机");
        Near(state.stamina, 37f, .001f, "普通模式切换不补体力");
        Near(state.oxygen, 23f, .001f, "普通模式切换不补氧气");
        box.GetComponent<Rigidbody2D>().velocity = new Vector2(4f, -2f);
        Vector2 boxPosition = box.DebugActualPosition;
        Vector2 spiritPosition = w.physics.DebugActualPosition;
        Check(w.spirit.TryRequestDeath(PlayerDeathCause.Abyss), "模拟精神体死亡走统一周期入口");
        Check(!w.rules.IsSimulation && w.spirit._ActionData.isFlying && !w.spirit.Permissions.pushPull,
            "死亡返回漫游同时提交模式、飞行和推拉撤权");
        Check(w.spirit.DeathStatus == PlayerDeathStatus.Handled && !w.spirit.PendingDeath.HasValue,
            "实际飞行执行成功才完成死亡周期");
        Check(box.DebugActualPosition == boxPosition && w.physics.DebugActualPosition == spiritPosition
            && w.spirit.GetInstanceID() == spiritId, "模拟死亡保留箱子现场和精神体实例及坐标");
        Near(state.stamina, 37f, .001f, "模拟死亡返回漫游不调用全量资源刷新");
        Near(state.oxygen, 23f, .001f, "模拟死亡保留当前氧气");
        for (int i = 0; i < 10; i++) Step(w);
        Check(box.DebugActualPosition == boxPosition, "漫游暂停期间箱子现场保持");
        Near((box.GetPlanarVelocity() - new Vector2(4f, -2f)).magnitude, 0f, .001f,
            "漫游暂停保留箱子现场速度，不以清运动冒充暂停");
        Check(!w.spirit.TryRequestDeath(PlayerDeathCause.Electrocuted), "已进入飞行免死，不产生新电击死亡");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "返回模拟仍使用同一精神体");
        Near((box.GetPlanarVelocity() - new Vector2(4f, -2f)).magnitude, 0f, .001f,
            "恢复模拟沿用暂停前箱子速度，不全量刷新现场");
        Check(w.spirit.TryRequestDeath(PlayerDeathCause.Abyss) && !w.rules.IsSimulation,
            "再次转行走可完成新的死亡周期，不被旧请求锁住");
    }

    private static void CheckPlatformPause()
    {
        World w = NewWorld();
        Taijie platform = Prefab<Taijie>("Taijie.prefab", (Vector2)w.physics.BodyBounds.center + Vector2.right * 40f);
        Invoke(platform, "OnEnable");
        platform.ToMovePoint(platform.transform.position + Vector3.down * 5f);
        typeof(Taijie).GetField("nowtime", Hidden).SetValue(platform, AudioSettings.dspTime - .75d);
        Invoke(platform, "FixFun");
        Begin(w);
        Vector2 start = platform.transform.position;
        FieldInfo clock = typeof(Taijie).GetField("nowtime", Hidden);
        FieldInfo pausedAt = typeof(Taijie).GetField("pausedAt", Hidden);
        // 同时平移两个时间戳模拟暂停十秒，检查恢复扣除暂停时长，不真实等待。
        clock.SetValue(platform, (double)clock.GetValue(platform) - 10d);
        pausedAt.SetValue(platform, (double)pausedAt.GetValue(platform) - 10d);
        Invoke(platform, "FixFun");
        Check((Vector2)platform.transform.position == start && platform.Delta == Vector2.zero, "漫游移动平台暂停且不产生携带位移");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "恢复模拟可打开平台运行许可");
        Invoke(platform, "FixFun");
        Near(((Vector2)platform.transform.position - start).magnitude, 0f, .04f,
            "平台恢复扣除暂停时间，不瞬移追赶暂停期间进度");
        clock.SetValue(platform, AudioSettings.dspTime - 1d);
        Invoke(platform, "FixFun");
        Check(((Vector2)platform.transform.position - start).magnitude > .1f,
            "回模拟后已有平台从保留的运行状态继续移动");
    }

    private static void CheckManualCooldown()
    {
        World w = NewWorld(); Begin(w, .5f);
        Near(w.rules.Cognition.Remaining, 100f, .001f, "初始漫游不扣认知");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "初始化探索不预扣手动冷却，首次手动转模拟成功");
        Near(w.rules.Cognition.Remaining, 99f, .001f, "默认手动转换成功扣认知1");
        Check(!w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual) && w.rules.IsSimulation,
            "成功手动转换后冷却拒绝紧接着的手动转换，保持原模式");
        Near(w.rules.Cognition.Remaining, 99f, .001f, "冷却拒绝不扣认知");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion) && !w.rules.IsSimulation,
            "区域强制转换绕过手动冷却并同步漫游");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "区域强制转模拟不受手动冷却限制");
        Check(w.spirit.TryRequestDeath(PlayerDeathCause.Abyss) && w.spirit._ActionData.isFlying && !w.rules.IsSimulation,
            "死亡强制转换同样绕过冷却并回漫游");
    }

    private static void CheckCommitNotifications()
    {
        World w = NewWorld();
        var reasons = new List<PlayerFormChangeReason>();
        var settledBalances = new List<float>();
        PlayerDeathRequest? death = null;
        w.rules.FormCommitted += reason =>
        {
            reasons.Add(reason);
            settledBalances.Add(w.rules.Cognition.Remaining);
            if (reason == PlayerFormChangeReason.Death) death = w.spirit.PendingDeath;
        };
        Begin(w);
        Check(reasons.Count == 0, "探索初始化不触发转换结算出口");
        Near(w.rules.Cognition.Remaining, 100f, .001f, "默认初始认知100，初始化飞行不扣费");
        Collider2D blocker = Solid(w.physics.BodyBounds.center, new Vector2(4f, 4f), w.wallLayer);
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual) && reasons.Count == 0,
            "真实重叠拒绝不触发转换结算出口");
        Near(w.rules.Cognition.Remaining, 100f, .001f, "当前位置占用拒绝不扣认知");
        blocker.enabled = false; Physics2D.SyncTransforms();
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual)
            && reasons.Count == 1 && reasons[0] == PlayerFormChangeReason.Manual,
            "成功手动转换仅发一次带原因的结算通知");
        Near(w.rules.Cognition.Remaining, 99f, .001f, "手动成功只扣一次认知");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual) && reasons.Count == 1,
            "请求当前相同形态不重复发结算通知");
        Near(w.rules.Cognition.Remaining, 99f, .001f, "相同形态请求不重复扣认知");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion)
            && reasons.Count == 2 && reasons[1] == PlayerFormChangeReason.ForcedRegion,
            "成功区域强制转换经过同一结算出口且保留原因");
        Near(w.rules.Cognition.Remaining, 98f, .001f, "强制转换成功沿同一认知余额扣费");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion) && reasons.Count == 3,
            "区域强制再次转换只新增一次结算通知");
        Near(w.rules.Cognition.Remaining, 97f, .001f, "返回行走为死亡验证准备时也只扣一次费用");
        Check(w.spirit.TryRequestDeath(PlayerDeathCause.Abyss) && reasons.Count == 4
            && reasons[3] == PlayerFormChangeReason.Death && death.HasValue,
            "死亡周期成功转漫游只结算一次并保留死亡原因");
        Near(w.rules.Cognition.Remaining, 96f, .001f, "死亡转换是本例第四次成功，余额仅减少一次");
        Check(w.rules.ReceiveDeath(death.Value) == PlayerDeathStatus.Handled && reasons.Count == 4,
            "重放完成的死亡周期不再发结算通知");
        Near(w.rules.Cognition.Remaining, 96f, .001f, "死亡周期重放不重复扣认知");
        Check(!w.spirit.TryRequestDeath(PlayerDeathCause.Electrocuted) && reasons.Count == 4,
            "同周期继续请求及飞行免死不重复结算");
        Check(settledBalances.Count == 4 && settledBalances[0] == 99f && settledBalances[1] == 98f
            && settledBalances[2] == 97f && settledBalances[3] == 96f,
            "转换通知发出时真实费用已结算，订阅者读取同一次提交后的余额");
    }

    private static void CheckConfiguredCognitionCost()
    {
        var cognition = new LevelCognition(100f);
        World w = NewWorld(7f, cognition); Begin(w);
        Check(ReferenceEquals(w.rules.Cognition, cognition) && cognition.Remaining == 100f,
            "外部配置的本关认知由同一实例持有，初始化不消费");
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual), "配置费用7可成功进入模拟");
        Near(cognition.Remaining, 93f, .001f, "配置费用7按一次成功转换消费");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion), "配置费用7的强制转换经同一规则");
        Near(cognition.Remaining, 86f, .001f, "两次成功转换总共消费14");
        Check(w.spirit.RequestSpiritForm(true, PlayerFormChangeReason.Manual) && cognition.Remaining == 86f,
            "配置费用也不因同形态请求重复消费");
        Collider2D blocker = Solid(w.physics.BodyBounds.center, new Vector2(4f, 4f), w.wallLayer);
        Check(!w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion) && cognition.Remaining == 86f,
            "配置费用的强制退出被重叠拒绝时不消费");
        blocker.enabled = false; Physics2D.SyncTransforms();
        Check(w.spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "移除阻挡后配置费用转换成功");
        Near(cognition.Remaining, 79f, .001f, "三次成功转换只消费21，无拒绝旁路扣费");
    }

    private sealed class World
    {
        public Player body, spirit;
        public CharacterPhysics physics;
        public PlayerInputData input;
        public PlayerLifecycleRules rules;
        public int wallLayer;
        public readonly List<BasicEntity> entities = new List<BasicEntity>();
    }

    private static World NewWorld(float formCost = 1f, LevelCognition cognition = null)
    {
        SetWorldPaused(false); SetPlatformPaused(false);
        Vector2 position = new Vector2(10000f + 200f * group++, 10000f);
        var w = new World();
        w.body = Actor("player20.prefab", position + Vector2.left * 40f);
        w.spirit = Actor("SpiritPlayer.prefab", position);
        w.physics = w.spirit.GetComponent<CharacterPhysics>();
        w.input = new PlayerInputData(); w.spirit.ChangeInputAsset(w.input);
        w.rules = new PlayerLifecycleRules(w.body, w.spirit, cognition: cognition, formCost: formCost);
        w.entities.Add(w.body.GetComponent<CharacterPhysics>()); w.entities.Add(w.physics);
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(w.physics);
        for (int i = 0; i < 32; i++) if ((config.wallLayer.value & (1 << i)) != 0) { w.wallLayer = i; break; }
        return w;
    }

    private static void Begin(World w, float cooldown = 0f)
    {
        MethodInfo method = typeof(PlayerLifecycleRules).GetMethod("BeginExploration");
        if (method == null) throw new MissingMethodException("PlayerLifecycleRules.BeginExploration");
        Check((bool)method.Invoke(w.rules, new object[] { cooldown }), "真实规则成功初始化探索与飞行");
    }

    private static Player Actor(string prefab, Vector2 position)
    {
        Player player = Prefab<Player>(prefab, position);
        Invoke(player.GetComponent<CharacterPhysics>(), "Awake"); Invoke(player, "Awake");
        Invoke(player.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(player, "Start");
        Invoke(player.GetComponent<CharacterPhysics>(), "OnEnable");
        return player;
    }

    private static PlayerStateMachine State(Player player) =>
        (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(player);

    private static void Step(World w)
    {
        Physics2D.SyncTransforms();
        foreach (BasicEntity entity in w.entities) Invoke(entity, "GeometricQueryPhase");
        foreach (BasicEntity entity in w.entities) Invoke(entity, "PhyFunUpdate");
        w.spirit.TickLogic(0f);
        foreach (BasicEntity entity in w.entities) Invoke(entity, "SpeedCalculation");
        sidePush.Invoke(null, null); Physics2D.Simulate(Time.fixedDeltaTime);
        foreach (BasicEntity entity in w.entities) Invoke(entity, "DisplacementCorrection");
    }

    private static void MoveCenter(BasicEntity entity, Vector2 center)
    {
        // 插值显示可能落后于模拟刚体；测试搬移同样使用正式刷新入口，避免误造深重叠。
        entity.RefreshMotionState(entity.DebugActualPosition + center - (Vector2)entity.BodyBounds.center);
        Physics2D.SyncTransforms();
    }

    private static T Prefab<T>(string prefab, Vector2 position) where T : Component
    {
        string folder = prefab == "player20.prefab" || prefab == "SpiritPlayer.prefab" ? "Player/" : "SceneObj/";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/" + folder + prefab);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing P7 fixture: " + prefab);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave;
        item.transform.position = position; objects.Add(item); Physics2D.SyncTransforms();
        return item.GetComponent<T>();
    }

    private static GameObject NewObject(string name, Vector2 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position; objects.Add(item); return item;
    }

    private static Collider2D Solid(Vector2 position, Vector2 size, int layer)
    {
        var item = NewObject("P7 blocking collider", position); item.layer = layer;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>(); collider.size = size;
        Physics2D.SyncTransforms(); return collider;
    }

    private static void SetWorldPaused(bool paused) => InvokeStatic(typeof(BasicEntity), "SetWorldPaused", paused);
    private static void SetPlatformPaused(bool paused) => InvokeStatic(typeof(Taijie), "SetWorldPaused", paused);
    private static bool ReadPaused(Type type) => (bool)(type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? false);

    private static void InvokeStatic(Type type, string name, bool argument)
    {
        MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method?.Invoke(null, new object[] { argument });
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

    private static void Near(float actual, float expected, float tolerance, string message) =>
        Check(Mathf.Abs(actual - expected) <= tolerance, message + " [" + actual + " / " + expected + "]");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P7 FAILED: " + message);
        passed.Add(message);
    }
}
#endif
