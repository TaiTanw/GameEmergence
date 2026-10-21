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

/// <summary>克隆 L05 的实际层，验证箱链、毒槽和停箱；不推进或保存原场景。</summary>
public static class P29L05LevelChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Scene fixtureScene;
    static GameObject fixture;
    static BasicEntity[] actors;
    static MechanismPressurePlate[] plates;
    static readonly List<string> report = new List<string>();
    static readonly MethodInfo sidePush = typeof(BasicEntity).Assembly.GetType("EntitySidePush").GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("只在 Edit Mode 检查。");
        Scene source = SceneManager.GetSceneByPath("Assets/MyObjAssets/Scenes/Designed/L05_DepartureWorks.unity");
        Transform root = source.GetRootGameObjects().Single(g => g.name == "P29_05_竖直四层").transform;
        var colliderStates = Object.FindObjectsOfType<Collider2D>(true).ToDictionary(c => c, c => c.enabled);
        var bodyStates = Object.FindObjectsOfType<Rigidbody2D>(true).ToDictionary(b => b, b => b.simulated);
        var scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object oldScheduler = scheduler.GetValue(null);
        var current = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object oldSetup = current.GetValue(null);
        var mode = Physics2D.simulationMode;
        bool paused = BasicEntity.IsWorldPaused;
        bool platesPaused = (bool)typeof(MechanismPressurePlate).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        bool platformsPaused = (bool)typeof(Taijie).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        bool quitting = MonoPublicMgr.IsQuitting;
        Scene active = SceneManager.GetActiveScene();
        GameObject temporaryScheduler = null;
        report.Clear();
        CheckMap(source);
        try
        {
            foreach (var e in colliderStates) e.Key.enabled = false;
            foreach (var e in bodyStates) e.Key.simulated = false;
            fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(active);
            current.SetValue(null, null);
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)oldScheduler == null)
            {
                temporaryScheduler = new GameObject("P29 临时物理调度");
                SceneManager.MoveGameObjectToScene(temporaryScheduler, fixtureScene);
                scheduler.SetValue(null, temporaryScheduler.AddComponent<MonoPublicMgr>());
            }
            BasicEntity.SetWorldPaused(false);
            MechanismPressurePlate.SetWorldPaused(false);
            Taijie.SetWorldPaused(false);
            Physics2D.simulationMode = SimulationMode2D.Script;
            foreach (var floor in new[] { root.GetChild(1), root.GetChild(3) })
                foreach (var electric in floor.GetComponentsInChildren<PhysicalBox>().Where(b => b.IsElectrified))
                    foreach (var ability in new[] { new Vector2(7, 15), new Vector2(5.6f, 15), new Vector2(7, 13.5f) })
                        foreach (int direction in new[] { -1, 1 }) TestElectricVault(floor, electric.name, ability, direction);
            TestChain(root.GetChild(1), false, false);
            TestChain(root.GetChild(1), false, true);
            TestChain(root.GetChild(3), true, false);
            TestChain(root.GetChild(3), true, true);
            TestToxic(root.GetChild(2), 1);
            TestToxic(root.GetChild(2), 3);
            TestPlayerCrossing(root.GetChild(2), 7f);
            TestPlayerCrossing(root.GetChild(2), 5.6f);
            TestCurl(root.GetChild(4), false);
            TestCurl(root.GetChild(4), true);
            TestRunwaySpeed(root.GetChild(4), 7f);
            TestRunwaySpeed(root.GetChild(4), 5.6f);
            TestConfiguredResources(root.GetChild(2), root.GetChild(4));
            TestRunwayUI(root.GetChild(4));
            return string.Join("\n", report);
        }
        finally
        {
            Retire();
            if (temporaryScheduler) Object.DestroyImmediate(temporaryScheduler);
            scheduler.SetValue(null, oldScheduler);
            current.SetValue(null, oldSetup);
            BasicEntity.SetWorldPaused(paused);
            MechanismPressurePlate.SetWorldPaused(platesPaused);
            Taijie.SetWorldPaused(platformsPaused);
            MonoPublicMgr.IsQuitting = quitting;
            Physics2D.simulationMode = mode;
            if (fixtureScene.IsValid() && fixtureScene.isLoaded) EditorSceneManager.CloseScene(fixtureScene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            foreach (var e in colliderStates) if (e.Key) e.Key.enabled = e.Value;
            foreach (var e in bodyStates) if (e.Key) e.Key.simulated = e.Value;
            Physics2D.SyncTransforms();
            Directory.CreateDirectory(".utmp/P29L05Build-20261021");
            File.WriteAllLines(".utmp/P29L05Build-20261021/native-checks.txt", report);
        }
    }
    static void Clone(Transform source, Action prepare = null)
    {
        fixture = Object.Instantiate(source.gameObject);
        fixture.hideFlags = HideFlags.DontSave;
        SceneManager.MoveGameObjectToScene(fixture, fixtureScene);
        foreach (var c in fixture.GetComponentsInChildren<Collider2D>(true)) c.enabled = true;
        actors = fixture.GetComponentsInChildren<BasicEntity>(true);
        plates = fixture.GetComponentsInChildren<MechanismPressurePlate>(true);
        prepare?.Invoke();
        foreach (var entity in actors) { Invoke(entity, "Awake"); Invoke(entity, "OnEnable"); }
        foreach (var plate in plates) Invoke(plate, "OnEnable");
        Physics2D.SyncTransforms();
    }
    static void TestChain(Transform source, bool third, bool solved)
    {
        Clone(source);
        try
        {
            for (int i = 0; i < 30; i++) Step();
            var boxes = actors.OfType<PhysicalBox>().ToArray();
            if (solved)
            {
                string[] targets = third ? new[] { "后间距箱_内", "前间距箱_内" } : new[] { "末端普通箱" };
                foreach (string name in targets)
                {
                    var box = boxes.Single(b => b.name.EndsWith(name));
                    Record((bool)Invoke(box, "TryStore"), "真实收纳入口受理 " + box.name);
                }
            }
            var cart = actors.OfType<MechanismCart>().Single();
            typeof(MechanismCart).GetField("isOn", Hidden).SetValue(cart, true);
            int held = 0;
            for (int tick = 0; tick < (third ? 3500 : 1500); tick++)
            {
                Step();
                if (plates.All(p => p.IsPressed)) held++; else held = 0;
            }
            string positions = string.Join(", ", boxes.Where(b => b.IsElectrified).Select(b => b.DebugActualPosition.x.ToString("F2")));
            Record(solved ? held >= 100 : held == 0,
                $"{(third ? "三层" : "底层")} {(solved ? "整理后持续压板" : "直接开车无法全开")}: held={held}, 车x={cart.DebugActualPosition.x:F2}, 电箱 x={positions}");
            if (solved) Record(plates.All(p => (bool)typeof(Taijie).GetField("mechanismActive", Hidden).GetValue(p.TargetComponent)), "对应机关均收到开启命令");
        }
        finally { Retire(); }
    }
    static void TestToxic(Transform source, int points)
    {
        Clone(source);
        try
        {
            var water = fixture.GetComponentInChildren<WaterRegion>();
            var bag = new CognitionBackpack(12, 0, 0, initialCalculations: Enumerable.Repeat(.2f, points).ToArray());
            var draft = new EnvironmentEditDraft(water, bag);
            if (!draft.TryExtractTrigger(out string error)) throw new InvalidOperationException(error);
            for (int i = 0; i < points; i++)
                if (!draft.TryUseItem(bag.Items[i].Id, false, 0, false, out error)) throw new InvalidOperationException(error);
            if (!draft.TryCommit(new LevelCognition(100), out error)) throw new InvalidOperationException(error);
            var box = actors.OfType<PhysicalBox>().Single();
            for (int i = 0; i < 30; i++) Step();
            box.RefreshMotionState(new Vector2(56.58f, box.DebugActualPosition.y));
            box.GetComponent<Rigidbody2D>().velocity = new Vector2(-7, 0);
            string sample = "";
            for (int i = 0; i < 1400; i++)
            {
                Step();
                if (i == 100 || i == 250) sample += $" [t={i}, x={box.DebugActualPosition.x:F2}, vx={box.GetComponent<Rigidbody2D>().velocity.x:F2}, 阻力={box.DebugEnvironmentSlowingMultiplier:F3}]";
            }
            float x = box.DebugActualPosition.x;
            Record(points == 3 ? box.BodyBounds.min.x < 38 : box.BodyBounds.min.x > 38, $"毒槽 {points} 个0.2点: 停点 x={x:F2}" + sample);
        }
        finally { Retire(); }
    }
    static void TestElectricVault(Transform source, string target, Vector2 ability, int direction)
    {
        Clone(source);
        try
        {
            for (int i = 0; i < 30; i++) Step();
            var box = actors.OfType<PhysicalBox>().Single(b => b.name == target);
            float x = box.BodyBounds.center.x - direction * (box.BodyBounds.extents.x + .8f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab");
            var go = Object.Instantiate(prefab);
            go.transform.SetParent(fixture.transform, false);
            var shape = go.GetComponent<BoxCollider2D>();
            go.transform.position = new Vector3(x, box.BodyBounds.min.y + (shape.size.y * .5f - shape.offset.y) * go.transform.localScale.y + .02f, -1);
            var owner = go.GetComponent<Player>();
            var body = go.GetComponent<CharacterPhysics>();
            Invoke(body, "Awake"); Invoke(body, "OnEnable");
            Invoke(owner, "Awake"); Invoke(owner.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(owner, "Start");
            var input = new PlayerInputData(); var deaths = new DeathCounter();
            owner.ChangeInputAsset(input); owner.BindDeathReceiver(deaths);
            body.speed = ability.x; body.upSpeed = ability.y;
            actors = actors.Concat(new BasicEntity[] { body }).ToArray();
            for (int i = 0; i < 30; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            owner.RefreshRuntime();
            var state = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(owner);
            state.jumpNum = 0; state.JumpCountToNum();
            input.jumpPressed = true;
            bool moving = false, crossed = false;
            float maxFeet = body.BodyBounds.min.y;
            for (int tick = 0; tick < 160; tick++)
            {
                // 先在箱间起跳，脚越过危险顶面后再横移；不依赖踩电箱或额外空跳。
                if (body.BodyBounds.min.y > box.BodyBounds.max.y + .18f) moving = true;
                input.moveInput = moving ? direction : 0;
                owner.TickLogic(Time.fixedDeltaTime); Step(); Invoke(owner, "UpdateBoxRelations");
                maxFeet = Mathf.Max(maxFeet, body.BodyBounds.min.y);
                crossed = direction > 0 ? body.BodyBounds.min.x > box.BodyBounds.max.x + .08f : body.BodyBounds.max.x < box.BodyBounds.min.x - .08f;
                if (deaths.count > 0 || crossed) break;
            }
            Record(crossed && deaths.count == 0,
                $"越过{target} dir={direction} 移速/跳速={ability.x}/{ability.y} 零空跳: 到另一侧={crossed}, 危险请求={deaths.count}, 最高脚底={maxFeet:F2}");
        }
        finally { Retire(); }
    }
    static void TestPlayerCrossing(Transform source, float speed)
    {
        Clone(source);
        try
        {
            var water = fixture.GetComponentInChildren<WaterRegion>();
            foreach (var hazard in fixture.GetComponentsInChildren<FixedContactHazard>()) Invoke(hazard, "OnEnable");
            var bag = new CognitionBackpack(12, 0, 0, initialCalculations: new[] { .2f, .2f, .2f });
            var draft = new EnvironmentEditDraft(water, bag);
            if (!draft.TryExtractTrigger(out string error)) throw new InvalidOperationException(error);
            foreach (var item in bag.Items)
                if (!draft.TryUseItem(item.Id, false, 0, false, out error)) throw new InvalidOperationException(error);
            if (!draft.TryCommit(new LevelCognition(100), out error)) throw new InvalidOperationException(error);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab");
            var go = Object.Instantiate(prefab);
            go.transform.SetParent(fixture.transform, false);
            go.transform.position = new Vector3(66, 23.24f, -1);
            var owner = go.GetComponent<Player>();
            var body = go.GetComponent<CharacterPhysics>();
            Invoke(body, "Awake"); Invoke(body, "OnEnable");
            Invoke(owner, "Awake"); Invoke(owner.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(owner, "Start");
            var input = new PlayerInputData(); var deaths = new DeathCounter();
            owner.ChangeInputAsset(input); owner.BindDeathReceiver(deaths);
            body.speed = speed;
            actors = actors.Concat(new BasicEntity[] { body }).ToArray();
            for (int i = 0; i < 30; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            owner.RefreshRuntime();
            var state = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(owner);
            state.jumpNum = 0; state.JumpCountToNum();
            var box = actors.OfType<PhysicalBox>().Single();
            bool jumped = false, boarded = false, leaving = false, arrived = false, perched = false, dropped = false;
            int jumpTick = -1;
            for (int tick = 0; tick < 1600; tick++)
            {
                input.moveInput = -1;
                if (!jumped && body.BodyBounds.min.x <= 58.15f) { input.jumpPressed = true; jumped = true; jumpTick = tick; }
                if (jumped && !perched)
                {
                    input.moveInput = 0;
                    if (tick > jumpTick + 8) input.jumpRelease = true;
                    if (tick > jumpTick + 8 && body.DebugIsGrounded && body.BodyBounds.min.y > 23.7f) perched = true;
                }
                if (perched && !boarded)
                {
                    input.moveInput = -1;
                    if (body.BodyBounds.center.x < box.BodyBounds.center.x + 1f) dropped = true;
                    if (dropped)
                    {
                        input.verticalInput = -1;
                        float gap = box.BodyBounds.center.x - body.BodyBounds.center.x;
                        input.moveInput = gap > .08f ? 1 : gap < -.08f ? -1 : 0;
                    }
                }
                if (jumped && body.DebugIsGrounded && Mathf.Abs(body.BodyBounds.min.y - box.BodyBounds.max.y) < .1f) boarded = true;
                if (boarded && box.BodyBounds.min.x <= 37.95f) leaving = true;
                if (boarded && !leaving)
                {
                    float gap = box.BodyBounds.center.x - .15f - body.BodyBounds.center.x;
                    input.moveInput = gap > .08f ? 1 : gap < -.08f ? -1 : 0;
                }
                owner.TickLogic(Time.fixedDeltaTime); Step(); Invoke(owner, "UpdateBoxRelations");
                if (deaths.count > 0 || body.BodyBounds.min.y < 21) break;
                if (leaving && body.BodyBounds.max.x < 38 && body.DebugIsGrounded) { arrived = true; break; }
            }
            Record(arrived && boarded && deaths.count == 0,
                $"毒槽实际左右/跳跃/下落输入 speed={speed}: 追箱台={perched}, 登箱={boarded}, 到岸={arrived}, 危险请求={deaths.count}, 人x={body.DebugActualPosition.x:F2}, 箱x={box.DebugActualPosition.x:F2}");
        }
        finally { Retire(); }
    }
    sealed class DeathCounter : IPlayerDeathReceiver
    {
        public int count;
        public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request) { count++; return PlayerDeathStatus.Handled; }
    }
    static void TestCurl(Transform source, bool point)
    {
        Clone(source, () =>
        {
            var ice = fixture.GetComponentInChildren<IceGround>();
            var resistance = new GroundSlowingParameter(2);
            if (point) resistance.TryAttachCalculation(new EnvironmentCalculationPoint(.2f));
            Configure(ice, resistance, new GroundJumpMultiplierParameter(1));
        });
        try
        {
            var box = actors.OfType<PhysicalBox>().Single();
            for (int i = 0; i < 30; i++) Step();
            var position = box.DebugActualPosition;
            box.RefreshMotionState(new Vector2(13, position.y));
            box.GetComponent<Rigidbody2D>().velocity = new Vector2(7, 0);
            for (int i = 0; i < 800; i++) Step();
            Record(point ? plates.Single().IsPressed : !plates.Single().IsPressed,
                $"冰道 {(point ? "借用0.2" : "默认阻力")}: 停点 x={box.DebugActualPosition.x:F2}, 压板={plates.Single().IsPressed}");
        }
        finally { Retire(); }
    }
    static void TestConfiguredResources(Transform second, Transform fourth)
    {
        Clone(second);
        try
        {
            var bag = new CognitionBackpack(12, 1, 1, initialCalculations: new[] { 2f, .2f });
            var cognition = new LevelCognition(100);
            foreach (string name in new[] { "右岸_可借计算点", "入口上方_加速可下落平台" })
            {
                var donor = fixture.GetComponentsInChildren<BasicPhysicalObject>().Single(b => b.name == name);
                var take = new EnvironmentEditDraft(donor, bag);
                if (!take.TryExtractCalculation(false, 0, out string error) || !take.TryCommit(cognition, out error)) throw new InvalidOperationException(error);
            }
            var water = fixture.GetComponentInChildren<WaterRegion>();
            var draft = new EnvironmentEditDraft(water, bag);
            if (!draft.TryExtractTrigger(out string problem)) throw new InvalidOperationException(problem);
            foreach (var item in bag.Items.Where(i => i.IsCalculation && Mathf.Approximately(i.CalculationPoint.Multiplier, .2f)).ToArray())
                if (!draft.TryUseItem(item.Id, false, 0, false, out problem)) throw new InvalidOperationException(problem);
            if (!draft.TryCommit(cognition, out problem)) throw new InvalidOperationException(problem);
            Record(Mathf.Approximately(water.CopyEnvironmentSlots(false)[0].Value, .04f), "初始一个0.2 + 右岸/追箱台各抽一个0.2，真实背包与费用确认可将毒水5降为0.04");
        }
        finally { Retire(); }
        Clone(fourth);
        try
        {
            var bag = new CognitionBackpack(12, 1, 1);
            var cognition = new LevelCognition(100);
            Record(fixture.GetComponentsInChildren<ForceField>().Length == 0, "长路已移除全部重叠空气区域");
            var grounds = fixture.GetComponentsInChildren<BaseGround>().Where(g => g.name == "四层入口与分路厅"
                || g.name == "长路折返层_0" || g.name == "长路折返层_1" || g.name == "长路折返层_2").ToArray();
            Record(grounds.Length == 4, "四段长路均由现有实心地面承载计算点");
            Physics2D.SyncTransforms();
            foreach (var ground in grounds)
            {
                var shape = ground.GetComponent<Collider2D>();
                var parameter = ground.CopyEnvironmentSlots(false)[0];
                var candidates = Physics2D.OverlapPointAll(shape.bounds.center).Select(c => c.GetComponent<BasicPhysicalObject>()).Where(b => b).Distinct().ToArray();
                Record(!shape.isTrigger && parameter.CalculationCount == 1 && Mathf.Approximately(parameter.CalculationPoints[0].Multiplier, .2f)
                    && candidates.Length == 1 && candidates[0] == ground,
                    ground.name + "：实心性质显示×0.2，实际点选范围只有一个环境来源");
            }
            var donor = grounds.Single(g => g.name == "四层入口与分路厅");
            var take = new EnvironmentEditDraft(donor, bag);
            if (!take.TryExtractCalculation(false, 0, out string error) || !take.TryCommit(cognition, out error)) throw new InvalidOperationException(error);
            var ice = fixture.GetComponentInChildren<IceGround>();
            var inject = new EnvironmentEditDraft(ice, bag);
            if (!inject.TryUseItem(bag.Items[0].Id, false, 0, false, out error) || !inject.TryCommit(cognition, out error)) throw new InvalidOperationException(error);
            Record(donor.CopyEnvironmentSlots(false)[0].CalculationCount == 0 && Mathf.Approximately(ice.CopyEnvironmentSlots(false)[0].Value, .4f),
                "长路实心地面抽取0.2并确认入包，同一点注入冰道得到0.4阻力");
        }
        finally { Retire(); }
    }
    static void TestRunwaySpeed(Transform source, float speed)
    {
        Clone(source);
        try
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab"));
            go.transform.SetParent(fixture.transform, false);
            go.transform.position = new Vector3(53, 65.24f, -1);
            var owner = go.GetComponent<Player>();
            var body = go.GetComponent<CharacterPhysics>();
            Invoke(body, "Awake"); Invoke(body, "OnEnable");
            Invoke(owner, "Awake"); Invoke(owner.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(owner, "Start");
            var input = new PlayerInputData(); owner.ChangeInputAsset(input);
            body.speed = speed;
            actors = actors.Concat(new BasicEntity[] { body }).ToArray();
            for (int i = 0; i < 30; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            owner.RefreshRuntime();
            input.moveInput = 1;
            for (int i = 0; i < 20; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            float slow = body.GetPlanarVelocity().x;
            Record(Mathf.Abs(slow - speed * .2f) < .02f, $"长路基础移速{speed}×0.2，实际走速={slow:F2}");
            var ground = fixture.GetComponentsInChildren<BaseGround>().Single(g => g.name == "四层入口与分路厅");
            var draft = new EnvironmentEditDraft(ground, new CognitionBackpack(12, 0, 0));
            if (!draft.TryExtractCalculation(false, 0, out string error) || !draft.TryCommit(new LevelCognition(100), out error)) throw new InvalidOperationException(error);
            for (int i = 0; i < 20; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            float restored = body.GetPlanarVelocity().x;
            Record(Mathf.Abs(restored - speed) < .02f && Mathf.Approximately(body.speed, speed), $"抽点后恢复基础移速：实际{restored:F2}，基础字段仍为{body.speed}");
            ground.RestoreEnvironmentDefaults();
            for (int i = 0; i < 20; i++) { owner.TickLogic(Time.fixedDeltaTime); Step(); }
            Record(Mathf.Abs(body.GetPlanarVelocity().x - speed * .2f) < .02f, "默认恢复重新附带原0.2，脚下采样恢复减速");
        }
        finally { Retire(); }
    }
    static void TestRunwayUI(Transform source)
    {
        var sceneSingleton = typeof(BaseMgr<SceneChangeMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        var stageSingleton = typeof(BaseMgr<LevelStageMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        var inputSingleton = typeof(BaseAutoMonoMgr<InputControlMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        var current = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object oldScene = sceneSingleton.GetValue(null), oldStage = stageSingleton.GetValue(null), oldInput = inputSingleton.GetValue(null), oldSetup = current.GetValue(null);
        float oldTime = Time.timeScale;
        EnvironmentEditView view = null;
        Clone(source);
        try
        {
            var manager = new SceneChangeMgr(); sceneSingleton.SetValue(null, manager);
            var stage = new LevelStageMgr(); stageSingleton.SetValue(null, stage);
            var inputObject = new GameObject("L05 UI input"); inputObject.transform.SetParent(fixture.transform, false);
            var input = inputObject.AddComponent<InputControlMgr>(); inputSingleton.SetValue(null, input);
            var spiritObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab"));
            spiritObject.transform.SetParent(fixture.transform, false);
            var spirit = spiritObject.GetComponent<Player>();
            var body = spirit.GetComponent<CharacterPhysics>();
            Invoke(body, "Awake"); Invoke(body, "OnEnable"); Invoke(spirit, "Awake");
            Invoke(spirit.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(spirit, "Start");
            spirit.ChangeInputAsset(new PlayerInputData());
            actors = actors.Concat(new BasicEntity[] { body }).ToArray();
            typeof(InputControlMgr).GetField("player", Hidden).SetValue(input, spirit);
            var setupObject = new GameObject("L05 UI setup"); setupObject.transform.SetParent(fixture.transform, false);
            var setup = setupObject.AddComponent<SceneLevelSetup>(); current.SetValue(null, setup);
            typeof(SceneLevelSetup).GetField("initialCognitionCalculations", Hidden).SetValue(setup, Array.Empty<float>());
            var rules = new PlayerLifecycleRules(null, spirit, cognition: new LevelCognition(100));
            if (!rules.BindCognitionBackpack(setup) || !rules.BeginExploration()) throw new InvalidOperationException("UI 漫游夹具未就绪");
            typeof(SceneChangeMgr).GetField("spirit", Hidden).SetValue(manager, spirit);
            typeof(SceneChangeMgr).GetField("levelRules", Hidden).SetValue(manager, rules);
            typeof(LevelStageMgr).GetField("rules", Hidden).SetValue(stage, rules); Time.timeScale = 1;
            var panel = new GameObject("L05 actual UI", typeof(RectTransform)); panel.transform.SetParent(fixture.transform, false);
            panel.AddComponent<GameplayPanel>();
            var area = new GameObject("EnvironmentEditArea", typeof(RectTransform)); area.transform.SetParent(panel.transform, false);
            EnvironmentEditLayout.Ensure(area.transform, panel.transform);
            view = area.AddComponent<EnvironmentEditView>(); Invoke(view, "Awake");
            var prepare = typeof(EnvironmentBackpackChecks).GetMethod("PrepareUIAction", BindingFlags.Static | BindingFlags.NonPublic);
            Action<bool> clear = slots => prepare.Invoke(null, new object[] { view, slots });
            var donor = fixture.GetComponentsInChildren<BaseGround>().Single(g => g.name == "四层入口与分路厅");
            Physics2D.SyncTransforms();
            typeof(EnvironmentEditView).GetMethod("SelectAt", Hidden).Invoke(view, new object[] { (Vector2)donor.GetComponent<Collider2D>().bounds.center });
            Record(view.DisplayedDraft != null && !view.IsViewingTriggerList, "真实点选入口选中长廊地面，并默认展示实心性质列表");
            var editor = view.transform.Find("EnvironmentEditorContent");
            string text = string.Join(" ", editor.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text));
            Record(text.Contains("地面移速倍率") && text.Contains("×0.2"), "真实环境面板显示地面移速倍率与×0.2计算公式");
            var extract = editor.Find("EnvironmentAbilityScroll/Viewport/Content/ExtractEnvironmentCalculation_0").GetComponent<UnityEngine.UI.Button>();
            Record(extract.interactable, "真实地面数值行的抽点按钮可用");
            clear(false); extract.onClick.Invoke();
            Record(donor.CopyEnvironmentSlots(false)[0].CalculationCount == 1 && view.DisplayedDraft.GetSlot(false, 0).CalculationCount == 0,
                "真实抽点按钮只改草稿，统一确认前地面保留0.2");
            var confirm = editor.Find("ConfirmEnvironmentButton").GetComponent<UnityEngine.UI.Button>();
            clear(true); confirm.onClick.Invoke();
            var point = rules.CognitionBackpack.Items.Single(i => i.IsCalculation);
            Record(Mathf.Approximately(point.CalculationPoint.Multiplier, .2f) && Mathf.Approximately(donor.CopyEnvironmentSlots(false)[0].Value, 1),
                "真实确认按钮将0.2加入背包，原地面移速倍率恢复1");
            clear(true); view.SelectEnvironment(fixture.GetComponentInChildren<IceGround>());
            clear(false); view.SelectAbilitySlot(0);
            var bag = panel.transform.Find("CognitionBackpackArea/CognitionBackpackScroll/Viewport/Content");
            var item = bag.Find("CognitionBackpackItem_" + point.Id).GetComponent<UnityEngine.UI.Button>();
            clear(false); item.onClick.Invoke();
            clear(true); confirm.onClick.Invoke();
            Record(Mathf.Approximately(fixture.GetComponentInChildren<IceGround>().CopyEnvironmentSlots(false)[0].Value, .4f)
                && rules.CognitionBackpack.Items.Count == 0, "真实背包按钮及面板确认将同一个0.2转用冰道");
        }
        finally
        {
            if (view)
            {
                view.enabled = false;
                var panel = view.GetComponentInParent<GameplayPanel>().transform;
                foreach (string name in new[] { "CognitionBackpackArea", "OpenCognitionBackpackButton" })
                    if (panel.Find(name)) Object.DestroyImmediate(panel.Find(name).gameObject);
            }
            Retire();
            sceneSingleton.SetValue(null, oldScene); stageSingleton.SetValue(null, oldStage); inputSingleton.SetValue(null, oldInput);
            current.SetValue(null, oldSetup); Time.timeScale = oldTime;
        }
    }
    static void CheckMap(Scene source)
    {
        var setup = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SceneLevelSetup>()).Single();
        var record = new LevelMapRecord(setup.MapCells, new LevelCognition(100), source);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
        Vector2 size = Vector2.Scale(prefab.GetComponent<BoxCollider2D>().size, prefab.transform.localScale);
        var bounds = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider2D>())
            .Where(c => c.enabled && (!c.isTrigger || c.GetComponent<FixedContactHazard>())).Select(c => c.bounds).ToArray();
        int blocked = 0;
        foreach (var cell in setup.MapCells)
        {
            var target = new Rect(cell.Center - size * .5f, size);
            if (bounds.Any(b => target.Overlaps(new Rect(b.min, b.size)))) blocked++;
        }
        Record(record.IsValid && blocked == 0, $"{setup.MapCells.Count}格：关联/无重叠有效，默认行走碰撞框落点被挡={blocked}，全图认知总占用={setup.MapCells.Sum(c => c.CognitionCost):F1}");
    }
    static void Configure(BasicPhysicalObject host, params EnvironmentParameter[] parameters)
    {
        var so = new SerializedObject(host);
        var p = so.FindProperty("environmentParameters"); p.arraySize = parameters.Length;
        for (int i = 0; i < parameters.Length; i++) p.GetArrayElementAtIndex(i).managedReferenceValue = parameters[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Step()
    {
        Physics2D.SyncTransforms();
        foreach (var a in actors) if (a.IsMotionParticipating) Invoke(a, "GeometricQueryPhase");
        foreach (var a in actors) if (a.IsMotionParticipating) Invoke(a, "PhyFunUpdate");
        foreach (var p in plates) Invoke(p, "SamplePressure");
        foreach (var a in actors) if (a.IsMotionParticipating) Invoke(a, "SpeedCalculation");
        sidePush.Invoke(null, null);
        Physics2D.Simulate(Time.fixedDeltaTime);
        foreach (var a in actors) if (a.IsMotionParticipating) Invoke(a, "DisplacementCorrection");
    }
    static object Invoke(object instance, string name)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method != null) return method.Invoke(instance, null);
        }
        throw new MissingMethodException(name);
    }
    static void Retire()
    {
        if (!fixture) return;
        foreach (var a in actors) if (a) Invoke(a, "OnDisable");
        foreach (var p in plates) if (p) Invoke(p, "OnDisable");
        Object.DestroyImmediate(fixture);
        fixture = null;
    }
    static void Record(bool passed, string text) => report.Add((passed ? "PASS " : "FAIL ") + text);
}
#endif
