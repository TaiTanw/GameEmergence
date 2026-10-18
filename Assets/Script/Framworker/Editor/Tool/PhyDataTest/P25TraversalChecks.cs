#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P25 毒槽定向原生检查：克隆真实玩家、水体和箱子，附加临时场景，不保存关卡。</summary>
public static class P25TraversalChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Prefabs = "Assets/MyObjAssets/gameObject/Pf/SceneObj/";
    static readonly List<Object> fixtures = new List<Object>();
    static readonly List<string> report = new List<string>();
    static readonly MethodInfo sidePush = typeof(BasicEntity).Assembly.GetType("EntitySidePush")
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
    static Scene scene;
    static float nextX;
    static int passed;

    [MenuItem("自定义工具/测试工具/验证/P25 验证毒槽滑箱通路（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行毒槽通路检查。");
        report.Clear(); passed = 0; nextX = 20000f;
        Scene original = SceneManager.GetActiveScene();
        SimulationMode2D oldMode = Physics2D.simulationMode;
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = scheduler.GetValue(null);
        FieldInfo currentSetup = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousSetup = currentSetup.GetValue(null);
        bool previousPause = (bool)typeof(BasicEntity).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        bool previousQuitting = MonoPublicMgr.IsQuitting;
        var externalBodies = new Dictionary<Rigidbody2D, bool>();
        try
        {
            // 运行时查询固定使用默认物理世界；暂时停止其它场景刚体，避免原关卡被本检查推进。
            foreach (Rigidbody2D body in Object.FindObjectsOfType<Rigidbody2D>(true))
            { externalBodies.Add(body, body.simulated); body.simulated = false; }
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(original);
            currentSetup.SetValue(null, null);
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)previousScheduler == null)
                scheduler.SetValue(null, New("P25 temporary scheduler", Vector2.zero).AddComponent<MonoPublicMgr>());
            BasicEntity.SetWorldPaused(false);
            foreach (var entry in externalBodies) if (entry.Key != null) entry.Key.simulated = false;
            Physics2D.simulationMode = SimulationMode2D.Script;

            CheckWaterAndConversion();
            CheckSlideDistance();
            bool routePassed = CheckBoardingAndEscape();
            bool bypassPassed = CheckJumpBypass();
            Check(routePassed && bypassPassed, "滑箱通路与八种直接越槽尝试全部满足预期");
            report.Add("人工待验：本工具是与 L03 首槽尺寸一致的临时夹具；未签收正式场景引用、涨水计时、镜头、告示牌及人类键盘手感。");
            Debug.Log($"P25 traversal: {passed} checks passed. " + string.Join(" | ", report));
        }
        finally
        {
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                if (fixtures[i] == null) continue;
                if (fixtures[i] is GameObject item)
                    foreach (BasicEntity entity in item.GetComponentsInChildren<BasicEntity>(true)) Invoke(entity, "OnDisable");
                Object.DestroyImmediate(fixtures[i]);
            }
            fixtures.Clear();
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            currentSetup.SetValue(null, previousSetup);
            BasicEntity.SetWorldPaused(previousPause);
            scheduler.SetValue(null, previousScheduler);
            MonoPublicMgr.IsQuitting = previousQuitting;
            Physics2D.simulationMode = oldMode;
            foreach (var entry in externalBodies) if (entry.Key != null) entry.Key.simulated = entry.Value;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P25Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "traversal-checks.txt"), report);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/P25 验证正式 L03 首槽（Edit Mode）")]
    public static void RunActualL03()
        => RunActual(false);

    [MenuItem("自定义工具/测试工具/验证/P25 验证正式 L05 毒槽（Edit Mode）")]
    public static void RunActualL05()
        => RunActual(true);

    static void RunActual(bool extension)
    {
        string tag = extension ? "L05" : "L03";
        string path = "Assets/MyObjAssets/Scenes/Designed/" + (extension ? "L05_DepartureWorks" : "L03_ElectricTraverse") + ".unity";
        float offset = extension ? 23.5f : 0f;
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行正式首槽检查。");
        if (SceneManager.GetSceneByPath(path).isLoaded)
            throw new InvalidOperationException("请先保存当前检查关卡，再切换到 BeginScenes；检查只附加打开磁盘场景。");
        report.Clear(); passed = 0;
        Scene original = SceneManager.GetActiveScene();
        SimulationMode2D oldMode = Physics2D.simulationMode;
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = scheduler.GetValue(null);
        FieldInfo currentSetup = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousSetup = currentSetup.GetValue(null);
        bool previousPause = BasicEntity.IsWorldPaused, previousQuitting = MonoPublicMgr.IsQuitting;
        var externalBodies = new Dictionary<Rigidbody2D, bool>();
        var externalColliders = new Dictionary<Collider2D, bool>();
        PhysicalBox actualBox = null;
        FixedContactHazard[] hazards = Array.Empty<FixedContactHazard>();
        scene = default;
        try
        {
            // 真实首槽使用原坐标；连其它已打开场景的静态碰撞体也暂时隔离，避免重合关卡干扰。
            foreach (Rigidbody2D body in Object.FindObjectsOfType<Rigidbody2D>(true))
            { externalBodies.Add(body, body.simulated); body.simulated = false; }
            foreach (Collider2D collider in Object.FindObjectsOfType<Collider2D>(true))
            { externalColliders.Add(collider, collider.enabled); collider.enabled = false; }
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(original);
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)previousScheduler == null)
                scheduler.SetValue(null, New("P25 actual L03 scheduler", Vector2.zero).AddComponent<MonoPublicMgr>());
            BasicEntity.SetWorldPaused(false);
            foreach (var entry in externalBodies) if (entry.Key != null) entry.Key.simulated = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.SyncTransforms();
            GameObject[] roots = scene.GetRootGameObjects();
            SceneLevelSetup setup = roots.SelectMany(root => root.GetComponentsInChildren<SceneLevelSetup>(true)).Single();
            currentSetup.SetValue(null, setup);
            foreach (BasicPhysicalObject host in roots.SelectMany(root => root.GetComponentsInChildren<BasicPhysicalObject>(true)))
            {
                if (!host.isActiveAndEnabled) continue;
                if (!host.ValidateEnvironmentParameters(out string error) || host.EnvironmentSlotCapacity <= 0)
                    throw new InvalidOperationException(host.name + "：环境制作配置无效；" + error);
            }
            hazards = roots.SelectMany(root => root.GetComponentsInChildren<FixedContactHazard>(true))
                .Where(hazard => hazard.isActiveAndEnabled).ToArray();
            foreach (FixedContactHazard hazard in hazards) Invoke(hazard, "OnEnable");
            foreach (Rigidbody2D body in roots.SelectMany(root => root.GetComponentsInChildren<Rigidbody2D>(true)))
                body.simulated = false;
            WaterRegion water = roots.SelectMany(root => root.GetComponentsInChildren<WaterRegion>(true))
                .Where(pool => pool.isActiveAndEnabled && pool.GetComponent<FixedContactHazard>() != null)
                .OrderBy(pool => pool.GetComponent<Collider2D>().bounds.min.y).First();
            actualBox = roots.SelectMany(root => root.GetComponentsInChildren<PhysicalBox>(true))
                .Where(box => box.isActiveAndEnabled && !box.IsElectrified && (!extension || box.name == "P25_检修站滑箱"))
                .OrderBy(box => box.GetComponent<Collider2D>().bounds.center.y).First();
            Initialize(actualBox);
            Check(setup.RisingWaterMaximum != null && setup.RisingWaterMaximum.position.y <= 11.2f,
                "正式 " + tag + " 主水最高点不超过 11.2，不淹没首槽顶面制造游泳旁路");
            Check(setup.ValidateRisingWaterConfiguration(out string waterError), "正式主水引用和最高点有效：" + waterError);
            Bounds poolBounds = water.GetComponent<Collider2D>().bounds;
            Check(water.IsTriggerEnvironment && water.GetComponent<Collider2D>().isTrigger,
                "正式首槽保存为触发器水体，不预先替玩家转换");
            Near(poolBounds.min.x, offset + 24.5f, .02f, "正式首槽左岸 X");
            Near(poolBounds.max.x, offset + 44.5f, .02f, "正式首槽右岸 X");
            Near(poolBounds.max.y, 12f, .02f, "正式首槽水面 Y");
            Check(poolBounds.size.y >= actualBox.BodyBounds.size.y, "正式毒槽深度至少覆盖整箱，未转换不能踩箱露顶通过");
            Near(actualBox.BodyBounds.center.x, extension ? 46.1f : 18f, .1f, "正式滑箱默认位置 X");
            Near(water.CopyEnvironmentSlots(false)[0].Value, 1f, .001f, "正式首槽默认地面消退为 1");

            var w = new Corridor { x = offset, water = water, box = actualBox,
                startBoxX = extension ? 46.1f - offset : 18f,
                startPlayerX = extension ? 44.35f - offset : 16.2f };
            w.owner = Prefab<Player>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab", new Vector2(16.2f, 14f));
            w.body = w.owner.GetComponent<CharacterPhysics>(); Initialize(w.body);
            Invoke(w.owner, "Awake"); Invoke(w.owner.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(w.owner, "Start");
            w.owner.ChangeInputAsset(w.input); w.owner.BindDeathReceiver(w.deaths);
            w.bag = setup.CreateCognitionBackpack();
            var draft = new EnvironmentEditDraft(w.water, w.bag);
            Check(draft.TryExtractTrigger(out string editError), "正式首槽抽取独特触发器性质：" + editError);
            int pointCount = 0;
            foreach (CognitionBackpackItem item in w.bag.Items)
            {
                if (!item.IsCalculation || Mathf.Abs(item.CalculationPoint.Multiplier - .2f) > .0001f) continue;
                Check(draft.TryUseItem(item.Id, false, 0, false, out editError), "使用正式背包中的 0.2 点：" + editError);
                if (++pointCount == 2) break;
            }
            Check(pointCount == 2 && draft.TryCommit(new LevelCognition(setup.InitialCognition), out editError),
                "正式背包两点沿本关认知费用统一提交：" + editError);
            Near(w.water.CopyEnvironmentSlots(false)[0].Value, .04f, .0001f, "正式首槽两点结果为 0.04");
            bool success = true;
            foreach (Vector2 ability in new[] { new Vector2(7f, 15f), new Vector2(7f, 13.5f), new Vector2(5.6f, 15f) })
            {
                // 检修站跑道更短，低移速使用最后一颗 0.2 增加滑行余量。
                if (extension && ability.x < 7f) AddPoints(w, 1);
                float slowing = w.water.CopyEnvironmentSlots(false)[0].Value;
                w.body.speed = ability.x; w.body.upSpeed = ability.y; State(w.owner).jumpNum = 0;
                bool solved = PlayBoxRoute(w, out bool boarded);
                success &= Record(boarded, $"正式 {tag}：移速 {ability.x:F1}／跳速 {ability.y:F1}／0 空跳，使用真实跑道与滑箱单跳登箱");
                success &= Record(solved, $"正式 {tag}：移速 {ability.x:F1}／跳速 {ability.y:F1}／0 空跳、消退 {slowing:F3}，真实跟箱并无伤落岸");
            }
            Check(success, "正式 " + tag + " 毒槽基础、低移速、低起跳配置均通过");
            w.box.SetMotionParticipation(false);
            w.body.speed = 7f; w.body.upSpeed = 15f;
            foreach (int delay in new[] { 1, 4, 8, 12, 18, 24, 30, 36 })
                Check(TryJumpBypass(w, delay, out float furthest),
                    $"正式 {tag}：基础角色疾跑／1 空跳延迟 {delay} 帧仍不能直接越槽（最远前缘 {furthest:F2}）");
            report.Add("未重建或调整正式场景几何、箱体探针、跑道能力及地图托台；仅按现有提交入口编辑毒槽，并克隆玩家输入测试。");
            report.Add("人工待验：涨水按最高点静态核对，未推进水上涨与整关生命周期；镜头、告示牌和键盘手感仍需试玩。");
            Debug.Log($"P25 actual {tag}: {passed} checks passed. " + string.Join(" | ", report));
        }
        finally
        {
            if (actualBox != null) Invoke(actualBox, "OnDisable");
            foreach (FixedContactHazard hazard in hazards) if (hazard != null) Invoke(hazard, "OnDisable");
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                if (fixtures[i] == null) continue;
                if (fixtures[i] is GameObject item)
                    foreach (BasicEntity entity in item.GetComponentsInChildren<BasicEntity>(true)) Invoke(entity, "OnDisable");
                Object.DestroyImmediate(fixtures[i]);
            }
            fixtures.Clear();
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            currentSetup.SetValue(null, previousSetup);
            BasicEntity.SetWorldPaused(previousPause);
            scheduler.SetValue(null, previousScheduler); MonoPublicMgr.IsQuitting = previousQuitting;
            Physics2D.simulationMode = oldMode;
            foreach (var entry in externalColliders) if (entry.Key != null) entry.Key.enabled = entry.Value;
            foreach (var entry in externalBodies) if (entry.Key != null) entry.Key.simulated = entry.Value;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P25Validation"));
            Directory.CreateDirectory(folder); File.WriteAllLines(Path.Combine(folder, extension ? "actual-l05-traversal-checks.txt" : "actual-l03-traversal-checks.txt"), report);
        }
    }

    static void CheckWaterAndConversion()
    {
        Corridor w = World();
        Stand(w.box, w.x + 30f, 12f);
        for (int tick = 0; tick < 65; tick++) Step(w.box);
        Check(w.box.BodyBounds.max.y < 12f && w.box.IsSupported, "未转实心时真实箱体沉到槽底，箱顶不足以渡过水面");
        Stand(w.body, w.x + 34f, 10f);
        Invoke(w.owner, "UpdateBoxRelations");
        Check(w.deaths.count == 1 && w.deaths.cause == PlayerDeathCause.ContactHazard, "真实触发器水域接触立即沿统一入口致死");
        MoveCenter(w.body, new Vector2(w.x + 15f, 13f));
        w.owner.RefreshRuntime();
        Convert(w, 2);
        Check(!w.water.GetComponent<Collider2D>().isTrigger && !w.water.SwimmingEnabled,
            "真实抽取独特触发器性质并确认后变为实心，不再提供游泳");
        Near(w.water.CopyEnvironmentSlots(false)[0].Value, .04f, .0001f, "两颗 0.2 点沿真实草稿计算得到 0.04 消退倍率");
        Stand(w.body, w.x + 34f, 12f); Invoke(w.owner, "UpdateBoxRelations");
        Check(w.deaths.count == 2 && w.deaths.cause == PlayerDeathCause.ContactHazard,
            "转实心后直接踩面仍致死，危险没有被抽走");
        MoveCenter(w.body, new Vector2(w.x + 15f, 13f)); w.owner.RefreshRuntime();
        Stand(w.box, w.x + 34f, 12f); Stand(w.body, w.x + 34f, w.box.BodyBounds.max.y);
        Invoke(w.owner, "UpdateBoxRelations");
        Check(w.deaths.count == 2 && !LethalContact.Touches(w.owner, .03f), "普通箱顶隔离危险面，不向箱顶玩家传播危险");
        Retire(w);
    }

    static void CheckSlideDistance()
    {
        Corridor w = World(); Convert(w, 0);
        float ordinary = Slide(w, 7f);
        Check(ordinary < 3f && w.box.BodyBounds.max.x < w.x + 44.5f,
            "未减消退时原生箱体滑程不足跨越 20 宽毒槽");
        AddPoints(w, 2);
        float normal = Slide(w, 7f);
        Check(w.box.BodyBounds.max.x >= w.x + 44.55f, "0.04 消退、基础速度 7 的真实箱体前缘到达对岸");
        float weak = Slide(w, 5.6f);
        bool weakTwo = w.box.BodyBounds.max.x >= w.x + 44.55f;
        report.Add($"滑程：未减消退={ordinary:F3}，0.04/7={normal:F3}，0.04/5.6={weak:F3}，最弱两点到岸={weakTwo}。");
        if (!weakTwo)
        {
            AddPoints(w, 1);
            float three = Slide(w, 5.6f);
            Check(w.box.BodyBounds.max.x >= w.x + 44.55f, "第三颗 0.2 点降至 0.008 后，最弱移速 5.6 的真实箱体到达对岸");
            report.Add($"建议用三点 0.008：最弱实际滑程={three:F3}，两点到岸余量不足。");
        }
        else Check(true, "0.04 消退下最弱移速 5.6 的箱体前缘也到达对岸");
        Retire(w);
    }

    static float Slide(Corridor w, float speed)
    {
        w.body.SetMotionParticipation(false);
        Stand(w.box, w.x + 25.92f, 12f);
        w.box.GetComponent<Rigidbody2D>().velocity = Vector2.right * speed;
        float start = w.box.DebugActualPosition.x;
        for (int tick = 0; tick < 600; tick++)
        {
            Step(w.box);
            if (w.box.BodyBounds.max.x >= w.x + 45f || Mathf.Abs(w.box.GetPlanarVelocity().x) < .001f) break;
        }
        return w.box.DebugActualPosition.x - start;
    }

    static bool CheckBoardingAndEscape()
    {
        bool success = true;
        foreach (float speed in new[] { 7f, 5.6f })
        {
            Corridor w = World(); Convert(w, 2);
            // 移速负面与跳数负面不会同时抽中；这里额外检验更严格的 5.6／0 空跳组合。
            State(w.owner).jumpNum = 0;
            w.body.speed = speed;
            bool solved = PlayBoxRoute(w, out bool boarded);
            report.Add($"两点路线：speed={speed:F1}，单跳登箱={boarded}，无伤到岸={solved}。");
            success &= Record(boarded, $"移速 {speed:F1}、0 额外空中跳，经真实输入单跳登上运动箱顶");
            success &= Record(solved, $"移速 {speed:F1}、0 额外空中跳，经真实左右输入跟箱并无伤落上对岸");
            Retire(w);
        }
        return success;
    }

    static bool PlayBoxRoute(Corridor w, out bool boarded)
    {
        w.body.SetMotionParticipation(true);
        w.deaths.count = 0;
        w.input.moveInput = 0f; w.input.sprintHeld = false;
        Stand(w.box, w.x + w.startBoxX, 12f);
        Stand(w.body, w.x + w.startPlayerX, 12f);
        w.owner.RefreshRuntime(); State(w.owner).JumpCountToNum();
        bool jumped = false, leavingBox = false; boarded = false;
        int jumpTick = 0;
        for (int tick = 0; tick < 1200; tick++)
        {
            w.input.moveInput = 1f;
            if (!jumped && w.body.BodyBounds.max.x >= w.x + 24.12f)
            { w.input.jumpPressed = true; jumped = true; jumpTick = tick; }
            if (jumped && tick > jumpTick + 3 && w.body.DebugIsGrounded
                && Mathf.Abs(w.body.BodyBounds.min.y - w.box.BodyBounds.max.y) < .1f)
                boarded = true;
            // 箱前缘已经搭上对岸就向右走下；不能继续等箱心前移，否则岸边制动会让跟箱控制永久原地等候。
            if (boarded && w.box.BodyBounds.max.x >= w.x + 44.55f) leavingBox = true;
            if (boarded && !leavingBox)
            {
                float error = w.box.BodyBounds.center.x + .15f - w.body.BodyBounds.center.x;
                // 只提供键盘可表达的左／停／右，未写位置或箱体速度来托住玩家。
                w.input.moveInput = error > .08f ? 1f : error < -.08f ? -1f : 0f;
            }
            Tick(w);
            if (w.deaths.count > 0 || w.body.BodyBounds.min.y < 10f)
            {
                report.Add($"路线失败：tick={tick}，玩家中心=({w.body.BodyBounds.center.x - w.x:F3},{w.body.BodyBounds.center.y:F3})，箱中心=({w.box.BodyBounds.center.x - w.x:F3},{w.box.BodyBounds.center.y:F3})，箱速={w.box.GetPlanarVelocity().x:F3}，死亡={w.deaths.count}。");
                return false;
            }
            if (w.body.BodyBounds.min.x >= w.x + 44.6f && w.body.DebugIsGrounded
                && Mathf.Abs(w.body.BodyBounds.min.y - 12f) < .08f)
            {
                report.Add($"真实落岸：tick={tick}，玩家左缘={w.body.BodyBounds.min.x - w.x:F3}，脚底={w.body.BodyBounds.min.y:F3}，箱前缘={w.box.BodyBounds.max.x - w.x:F3}。");
                return true;
            }
        }
        report.Add($"路线超时：玩家 x={w.body.BodyBounds.center.x - w.x:F3}，箱 x={w.box.BodyBounds.center.x - w.x:F3}。");
        return false;
    }

    static bool CheckJumpBypass()
    {
        bool success = true;
        foreach (int extraJumpDelay in new[] { 1, 4, 8, 12, 18, 24, 30, 36 })
        {
            Corridor w = World(); Convert(w, 0); w.box.SetMotionParticipation(false);
            Stand(w.body, w.x + 20.5f, 12f); w.owner.RefreshRuntime();
            State(w.owner).jumpNum = 1; State(w.owner).JumpCountToNum();
            w.input.moveInput = 1f; w.input.sprintHeld = true;
            int jumpTick = -1; float furthest = 20.5f;
            for (int tick = 0; tick < 180; tick++)
            {
                if (jumpTick < 0 && w.body.BodyBounds.max.x >= w.x + 24.05f)
                { w.input.jumpPressed = true; jumpTick = tick; }
                else if (jumpTick >= 0 && tick == jumpTick + extraJumpDelay) w.input.jumpPressed = true;
                Tick(w); furthest = Mathf.Max(furthest, w.body.BodyBounds.max.x - w.x);
                if (w.deaths.count > 0 || w.body.BodyBounds.min.y < 10f) break;
            }
            success &= Record(jumpTick >= 0 && furthest < 44.5f && w.deaths.count > 0,
                $"基础 7、疾跑、1 空中跳延迟 {extraJumpDelay} 帧：实际跳跃不能跨越低顶毒槽（最远前缘 {furthest:F2}）");
            Retire(w);
        }
        report.Add("跳跃封路覆盖八种空中跳时机；不穷举所有输入，正式场景仍需人工尝试边缘起跳与强化捷径。");
        return success;
    }

    static bool TryJumpBypass(Corridor w, int delay, out float furthest)
    {
        w.deaths.count = 0; w.input.moveInput = 0f; w.input.sprintHeld = false;
        Stand(w.body, w.x + w.startPlayerX, 12f); w.owner.RefreshRuntime();
        State(w.owner).jumpNum = 1; State(w.owner).JumpCountToNum();
        w.input.moveInput = 1f; w.input.sprintHeld = true;
        int jumpTick = -1; furthest = w.startPlayerX;
        for (int tick = 0; tick < 180; tick++)
        {
            if (jumpTick < 0 && w.body.BodyBounds.max.x >= w.x + 24.05f)
            { w.input.jumpPressed = true; jumpTick = tick; }
            else if (jumpTick >= 0 && tick == jumpTick + delay) w.input.jumpPressed = true;
            Tick(w); furthest = Mathf.Max(furthest, w.body.BodyBounds.max.x - w.x);
            if (w.deaths.count > 0 || w.body.BodyBounds.min.y < 10f) break;
        }
        return jumpTick >= 0 && furthest < 44.5f && w.deaths.count > 0;
    }

    sealed class Corridor
    {
        public float x;
        public float startBoxX = 18f;
        public float startPlayerX = 16.2f;
        public WaterRegion water;
        public PhysicalBox box;
        public CharacterPhysics body;
        public Player owner;
        public PlayerInputData input = new PlayerInputData();
        public DeathCounter deaths = new DeathCounter();
        public CognitionBackpack bag;
    }

    static Corridor World()
    {
        var w = new Corridor { x = nextX }; nextX += 120f;
        Ground(w, "safe approach", 10f, 24.5f, 12f);
        Ground(w, "pool bottom", 24.5f, 44.5f, 9f);
        Ground(w, "landing", 44.5f, 54f, 12f);
        Solid(w, "lower separator", 24f, 6f, 1f, 12f);
        Solid(w, "upper separator", 24f, 27.75f, 1f, 24.5f);
        Solid(w, "low roof", 34.5f, 16.5f, 21f, 2f);
        Solid(w, "box stopper", 53f, 14f, 1f, 4f);
        w.water = Prefab<WaterRegion>(Prefabs + "绿色水体.prefab", new Vector2(w.x + 34.5f, 10.5f));
        w.water.transform.localScale = Vector3.one;
        w.water.GetComponent<BoxCollider2D>().size = new Vector2(20f, 3f);
        Invoke(w.water.GetComponent<FixedContactHazard>(), "OnEnable");
        w.box = Prefab<PhysicalBox>(Prefabs + "box.prefab", new Vector2(w.x + 18f, 12.77f));
        BoxCollider2D boxShape = w.box.GetComponent<BoxCollider2D>();
        w.box.transform.localScale = new Vector3(2.8f / boxShape.size.x, 1.5f / boxShape.size.y, 1f);
        Initialize(w.box); Stand(w.box, w.x + 18f, 12f);
        w.owner = Prefab<Player>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab", new Vector2(w.x + 16f, 14f));
        w.body = w.owner.GetComponent<CharacterPhysics>(); Initialize(w.body);
        Invoke(w.owner, "Awake"); Invoke(w.owner.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(w.owner, "Start");
        w.owner.ChangeInputAsset(w.input); w.owner.BindDeathReceiver(w.deaths);
        Stand(w.body, w.x + 16f, 12f); w.owner.RefreshRuntime();
        return w;
    }

    static void Convert(Corridor w, int points)
    {
        w.bag = new CognitionBackpack(12, 0f, 0f, initialCalculations: new[] { .2f, .2f, .2f });
        var draft = new EnvironmentEditDraft(w.water, w.bag);
        Check(draft.TryExtractTrigger(out string error), "抽取独特触发器性质：" + error);
        for (int i = 0; i < points; i++)
            Check(draft.TryUseItem(w.bag.Items[i].Id, false, 0, false, out error), "注入 0.2 计算点：" + error);
        Check(draft.TryCommit(new LevelCognition(100f), out error), "整面板确认：" + error);
        Physics2D.SyncTransforms();
    }

    static void AddPoints(Corridor w, int count)
    {
        var draft = new EnvironmentEditDraft(w.water, w.bag);
        foreach (CognitionBackpackItem item in w.bag.Items)
        {
            if (!item.IsCalculation || !Mathf.Approximately(item.CalculationPoint.Multiplier, .2f)) continue;
            Check(draft.TryUseItem(item.Id, false, 0, false, out string error), "追加 0.2 计算点：" + error);
            if (--count == 0) break;
        }
        Check(count == 0 && draft.TryCommit(new LevelCognition(100f), out _), "追加计算点沿真实确认生效");
    }

    static void Tick(Corridor w)
    {
        w.owner.TickLogic(Time.fixedDeltaTime);
        Step(w.body, w.box);
        Invoke(w.owner, "UpdateBoxRelations");
    }

    static void Step(params BasicEntity[] entities)
    {
        Physics2D.SyncTransforms();
        foreach (BasicEntity entity in entities) if (entity.IsMotionParticipating) Invoke(entity, "GeometricQueryPhase");
        foreach (BasicEntity entity in entities) if (entity.IsMotionParticipating) Invoke(entity, "PhyFunUpdate");
        foreach (BasicEntity entity in entities) if (entity.IsMotionParticipating) Invoke(entity, "SpeedCalculation");
        sidePush.Invoke(null, null); Physics2D.Simulate(Time.fixedDeltaTime);
        foreach (BasicEntity entity in entities) if (entity.IsMotionParticipating) Invoke(entity, "DisplacementCorrection");
    }

    static void Ground(Corridor w, string name, float left, float right, float top)
    { Solid(w, name, (left + right) / 2f, top - .5f, right - left, 1f).gameObject.AddComponent<BaseGround>(); }

    static BoxCollider2D Solid(Corridor w, string name, float x, float y, float width, float height)
    {
        GameObject item = New("P25 " + name, new Vector2(w.x + x, y));
        item.layer = LayerMask.NameToLayer("Ground");
        BoxCollider2D shape = item.AddComponent<BoxCollider2D>(); shape.size = new Vector2(width, height); return shape;
    }

    static T Prefab<T>(string path, Vector2 position) where T : Component
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) throw new InvalidOperationException("缺少预设：" + path);
        GameObject item = Object.Instantiate(source); item.hideFlags = HideFlags.DontSave;
        SceneManager.MoveGameObjectToScene(item, scene); item.transform.position = position; fixtures.Add(item);
        Physics2D.SyncTransforms(); return item.GetComponent<T>();
    }

    static GameObject New(string name, Vector2 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(item, scene); item.transform.position = position; fixtures.Add(item); return item;
    }

    static void Initialize(BasicEntity entity) { Invoke(entity, "Awake"); Invoke(entity, "OnEnable"); Physics2D.SyncTransforms(); }
    static void Retire(Corridor w) { w.body.SetMotionParticipation(false); w.box.SetMotionParticipation(false); }
    static void Stand(BasicEntity entity, float x, float top) => MoveCenter(entity, new Vector2(x, top + entity.BodyBounds.extents.y + .005f));
    static void MoveCenter(BasicEntity entity, Vector2 center) { entity.RefreshMotionState(entity.DebugActualPosition + center - (Vector2)entity.BodyBounds.center); Physics2D.SyncTransforms(); }
    static PlayerStateMachine State(Player player) => (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(player);

    static void Invoke(object item, string name)
    {
        for (Type type = item.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method != null) { method.Invoke(item, null); return; }
        }
        throw new MissingMethodException(name);
    }

    static void Near(float actual, float expected, float tolerance, string message) => Check(Mathf.Abs(actual - expected) <= tolerance, message + $" [{actual}/{expected}]");
    static void Check(bool condition, string message)
    {
        Record(condition, message);
        if (!condition) throw new InvalidOperationException(message);
    }

    static bool Record(bool condition, string message)
    {
        report.Add((condition ? "PASS: " : "FAIL: ") + message);
        if (condition) passed++;
        return condition;
    }

    sealed class DeathCounter : IPlayerDeathReceiver
    {
        public int count;
        public PlayerDeathCause cause;
        public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request) { count++; cause = request.cause; return PlayerDeathStatus.Handled; }
    }
}
#endif
