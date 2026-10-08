using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>批处理 Play Mode 验证：使用真实 Player 生命周期、原相位管线和连续 Unity 2D 模拟，不保存场景或改键文件。</summary>
public static class PlayerSwimmingPlayModeChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> passed = new List<string>();
    private static readonly Vector2 origin = new Vector2(1000f, 1000f);
    private static Player body, spirit;
    private static WaterRegion water;
    private static MonoPublicMgr scheduler;
    private static Collider2D floor, wall, ceiling;
    private static bool previousOptionsEnabled;
    private static EnterPlayModeOptions previousOptions;
    private static SimulationMode2D previousSimulation;
    private static float previousTimeScale;
    private static int stage;
    private static double deadline;
    private static Exception failure;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("Require a fresh batch editor.");
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        passed.Clear(); stage = 0; failure = null;
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
    }

    private static void Advance()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Swimming Play Mode timed out.");
            if (stage == 0 && Application.isPlaying)
            { Setup(); stage = 1; }
            else if (stage == 1 && typeof(CharacterPhysics).GetField("playActionData", Private).GetValue(body.GetComponent<CharacterPhysics>()) != null)
            {
                RunChecks();
                Time.timeScale = previousTimeScale;
                Physics2D.simulationMode = previousSimulation;
                stage = 2; EditorApplication.isPlaying = false;
            }
            else if (stage == 2 && !EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }
        catch (Exception error)
        {
            failure = error; Debug.LogException(error);
            stage = 2; EditorApplication.isPlaying = false;
            if (!Application.isPlaying) Finish();
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Advance;
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        Directory.CreateDirectory(".utmp/EnvironmentValidation");
        File.WriteAllLines(".utmp/EnvironmentValidation/p53-play-checks.txt", passed);
        Debug.Log("P5.3 swimming Play Mode: " + passed.Count + " checks passed; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }

    private static void Setup()
    {
        previousTimeScale = Time.timeScale; Time.timeScale = 0f;
        previousSimulation = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
        MonoPublicMgr.IsQuitting = false;
        scheduler = MonoPublicMgr.Instance; scheduler.enabled = false;
        body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/player20.prefab"), origin, Quaternion.identity).GetComponent<Player>();
        spirit = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab"), origin + Vector2.right * 20f, Quaternion.identity).GetComponent<Player>();
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        water = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/WaterPool.prefab"), origin, Quaternion.identity).GetComponent<WaterRegion>();
        water.transform.localScale = new Vector3(12f, 8f, 1f);
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Private).GetValue(body.GetComponent<CharacterPhysics>());
        floor = Solid("floor", origin + new Vector2(0f, -2.5f), new Vector2(12f, .5f), FirstLayer(config.groundLayer));
        wall = Solid("wall", origin + new Vector2(5.5f, 0f), new Vector2(.5f, 8f), FirstLayer(config.wallLayer));
        ceiling = Solid("ceiling", origin + new Vector2(0f, 3.5f), new Vector2(12f, .5f), FirstLayer(config.groundLayer));
        Physics2D.SyncTransforms();
    }

    private static Collider2D Solid(string name, Vector2 position, Vector2 size, int layer)
    {
        var item = new GameObject("P53 " + name) { layer = layer };
        item.transform.position = position;
        var collider = item.AddComponent<BoxCollider2D>(); collider.size = size;
        return collider;
    }

    private static int FirstLayer(int mask)
    {
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        throw new InvalidOperationException("Fixture requires a nonempty collision mask.");
    }

    private static void PhysicsStep()
    {
        typeof(MonoPublicMgr).GetMethod("FixedUpdate", Private).Invoke(scheduler, null);
    }

    private static void Step(Player actor, PlayerInputData input, int count = 1)
    {
        actor.ChangeInputAsset(input);
        for (int i = 0; i < count; i++) { actor.TickLogic(Time.fixedDeltaTime); PhysicsStep(); }
    }

    private static PlayerStateMachine State(Player actor) => (PlayerStateMachine)typeof(Player).GetField("fsm", Private).GetValue(actor);
    private static void RunChecks()
    {
        body.RefreshRuntime(origin);
        Step(body, null, 3);
        Check(body._ActionData.isSwimming && body.WaterFacts.submerged, "真实 Start 后无控制角色仍进入游泳");
        Vector2 hover = body.GetComponent<Rigidbody2D>().position;
        float oxygen = body.ResourceFacts.oxygen;
        Step(body, null, 120);
        Check(Vector2.Distance(hover, body.GetComponent<Rigidbody2D>().position) < .01f, "连续 120 物理帧零输入悬停");
        Near(body.ResourceFacts.oxygen, oxygen - 120f * Time.fixedDeltaTime * State(body).oxygenDrainPerSecond, "无输入连续耗氧", .02f);

        CheckSwimmingDynamicForce();
        body.RefreshRuntime(origin);
        Step(body, new PlayerInputData { moveInput = 1f, verticalInput = 1f }, 10);
        Near(body.GetComponent<CharacterPhysics>().GetPlanarVelocity().magnitude, 6f, "真实双轴游泳斜向速度归一化");
        Check(body.GetComponent<Rigidbody2D>().position.x > origin.x && body.GetComponent<Rigidbody2D>().position.y > origin.y, "四向请求经原提交和 Unity 模拟移动");
        body.RefreshRuntime(origin);
        Step(body, new PlayerInputData { verticalInput = -1f }, 60);
        Check(Bounds(body).min.y >= floor.bounds.max.y - .02f && body._ActionData.isSwimming, "池底碰撞阻挡且游泳优先于着地");
        body.RefreshRuntime(origin);
        Step(body, new PlayerInputData { moveInput = 1f }, 70);
        Check(Bounds(body).max.x <= wall.bounds.min.x + .02f && body._ActionData.isSwimming, "连续游泳仍受实体侧墙约束");
        body.RefreshRuntime(origin);
        Step(body, new PlayerInputData { verticalInput = 1f }, 65);
        Check(Bounds(body).max.y <= ceiling.bounds.min.y + .02f && body._ActionData.isSwimming, "连续向上游泳仍受实体天花板约束");

        CheckOneWayAndEntry();
        CheckRisingWater();
        PhysicsStep(); // 消费水位恢复配置后再验证水面泳姿。
        ceiling.enabled = false;
        try { passed.AddRange(PlayerSurfaceSwimmingChecks.Run(body, water, PhysicsStep, spirit)); }
        finally { ceiling.enabled = true; }
        body.RefreshRuntime(origin);
        body.ChangeInputAsset(null);
        body.TickLogic(.1f);
        float once = body.ResourceFacts.oxygen;
        for (int i = 0; i < 5; i++) PhysicsStep();
        Near(body.ResourceFacts.oxygen, once, "一次逻辑更新后五个物理帧不重复扣氧");
        body.RefreshRuntime(origin);
        body.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, false, true));
        Vector2 pausedPosition = body.GetComponent<Rigidbody2D>().position;
        Step(body, null, 5);
        Check(body.WaterFacts.submerged, "真实运行中暂停运动仍保留水事实");
        Near(body.ResourceFacts.oxygen, 99f, "真实运行中运动暂停与耗氧许可独立");
        Check(body.GetComponent<Rigidbody2D>().position == pausedPosition, "暂停运动不提交旧位移也不原生下落");
        body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        CheckInputAndRebind();
    }

    private static void CheckSwimmingDynamicForce()
    {
        var host = new GameObject("K11 swimming dynamic field");
        host.transform.position = origin;
        var trigger = host.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true; trigger.size = new Vector2(20f, 20f);
        var field = host.AddComponent<ForceField>();
        typeof(ForceField).GetField("add", Private).SetValue(field, 2f);
        typeof(ForceField).GetField("maxSpeed", Private).SetValue(field, 8f);
        typeof(ForceField).GetField("speedChangeNum", Private).SetValue(field, 0f);
        try
        {
            body.RefreshRuntime(origin);
            Step(body, new PlayerInputData { moveInput = 1f }, 20);
            float moving = body.GetComponent<Rigidbody2D>().velocity.x;
            Check(body._ActionData.isSwimming && moving > 6.5f && moving < 7.1f,
                "真实游泳有输入仍逐步累积动态水流 [" + moving + "]");
            Step(body, null, 20);
            float idle = body.GetComponent<Rigidbody2D>().velocity.x;
            Check(body._ActionData.isSwimming && idle > 1.3f && idle < 1.9f,
                "真实游泳零输入保留已有环境惯性并继续受力 [" + idle + "]");
        }
        finally { Object.DestroyImmediate(host); body.RefreshRuntime(origin); }
    }

    private static void CheckOneWayAndEntry()
    {
        body.RefreshRuntime(origin + Vector2.up * 2f);
        var platform = (BoxCollider2D)Solid("oneway", origin + Vector2.up, new Vector2(8f, .2f), floor.gameObject.layer);
        platform.usedByEffector = true;
        var effector = platform.gameObject.AddComponent<PlatformEffector2D>(); effector.useOneWay = true; effector.surfaceArc = 180f;
        Physics2D.SyncTransforms();
        Step(body, new PlayerInputData { verticalInput = -1f }, 60);
        Check(Bounds(body).max.y < platform.bounds.min.y && Bounds(body).min.y >= floor.bounds.max.y - .02f, "水下向下输入穿过单向顶面而普通池底仍阻挡");
        Object.DestroyImmediate(platform.gameObject);
        ceiling.enabled = false;
        body.RefreshRuntime(new Vector2(origin.x, water.SurfaceY + .5f));
        var speeds = (PhyData.PlayerPhysicsData)typeof(BasicEntity).GetField("playerPhysicsData", Private).GetValue(body.GetComponent<CharacterPhysics>());
        body.GetComponent<Rigidbody2D>().velocity = Vector2.down * 30f;
        Step(body, null, 8);
        Check((body.WaterFacts.submerged || body.WaterFacts.partlyAboveSurface) && body._ActionData.isSwimming,
            "高速落水后按相位二几何进入水面或水下游泳; position="
            + body.GetComponent<Rigidbody2D>().position + "; top=" + Bounds(body).max.y + "; surface=" + water.SurfaceY
            + "; velocity=" + body.GetComponent<CharacterPhysics>().GetPlanarVelocity());
        body.RefreshRuntime(new Vector2(origin.x, water.SurfaceY - .8f));
        var up = new PlayerInputData { verticalInput = 1f };
        for (int i = 0; i < 30; i++)
        {
            Step(body, up);
            if (!body.WaterFacts.submerged) break;
        }
        body.TickLogic(0f); // 消费刚完成的离水采样，核对离水首帧而非跳跃顶点之后。
        Check(!body.WaterFacts.submerged && body._ActionData.isSurfaceSwimming,
            "向上上浮后按半露出事实进入水面泳姿");
        ceiling.enabled = true;
    }

    private static void CheckRisingWater()
    {
        body.RefreshRuntime(origin);
        float top = Bounds(body).max.y;
        water.ConfigureWaterLevel(origin.y - 10f, top - .5f, 1f, true);
        PhysicsStep(); // 配置在原相位 0 提交，调用配置方法本身不会提前写世界。
        body.GetComponent<CharacterPhysics>().ResampleEnvironment();
        Check(!body.WaterFacts.submerged, "低水位未淹没上缘中心");
        Step(body, null, 60);
        Check(body.WaterFacts.partlyAboveSurface && body._ActionData.isSurfaceSwimming,
            "相位零涨水通过同一碰撞体自动触发半身水面游泳");
        water.ConfigureWaterLevel(origin.y - 10f, origin.y + 4f, 0f, false);
    }

    private static void CheckInputAndRebind()
    {
        var data = DataAndInitMgr.Instance;
        FieldInfo assetField = typeof(DataAndInitMgr).GetField("assetin", Private);
        var original = (InputActionAsset)assetField.GetValue(data);
        var fresh = InputActionAsset.FromJson(Resources.Load<TextAsset>("player").text);
        var mode = InputSystem.settings.updateMode;
        object inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo editUpdates = inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool previousEditUpdates = (bool)editUpdates.GetValue(inputManager);
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        InputControlMgr manager = InputControlMgr.Instance;
        try
        {
            assetField.SetValue(data, fresh);
            editUpdates.SetValue(inputManager, true); // EditorApplication 回调中手动派发真实设备事件。
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            manager.BindInputAsset();
            var native = manager.GetComponent<PlayerInput>();
            native.neverAutoSwitchControlSchemes = true;
            native.actions.devices = new InputDevice[] { keyboard };
            manager.InputOpenOrClose(true);
            body.RefreshRuntime(origin);
            spirit.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
            spirit.RefreshRuntime(origin + Vector2.left * 3f);
            manager.BindPlayer(body);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.S, Key.LeftShift));
            InputSystem.Update(); InputSystem.Update();
            body.TickLogic(.02f);
            Check(body._ActionData.onMove > 0f && body._ActionData.verticalInput < 0f, "真实键盘复合轴驱动游泳; input="
                + manager.PlayerInputData.moveInput + "," + manager.PlayerInputData.verticalInput + "; swim=" + body._ActionData.isSwimming);
            manager.PlayerInputData.jumpPressed = manager.PlayerInputData.interactTapped = true;
            manager.BindPlayer(spirit);
            spirit.TickLogic(.02f);
            Check(body._ActionData.onMove == 0f && body._ActionData.verticalInput == 0f, "控制切换立即撤销旧角色主动输出");
            Check(spirit._ActionData.onMove > 0f && spirit._ActionData.verticalInput < 0f && manager.PlayerInputData.sprintHeld,
                "切换到精神体时同步设备当前持续输入");
            Check(!manager.PlayerInputData.jumpPressed && !manager.PlayerInputData.interactTapped, "控制切换不转交旧点按");
            float before = body.ResourceFacts.oxygen;
            body.TickLogic(.2f);
            Near(body.ResourceFacts.oxygen, before - 2f, "失去控制的实体仍按许可耗氧");
            manager.BindPlayer(body);
            State(body).oxygen = .1f;
            manager.StartRebind("Sprint", 0, null);
            body.TickLogic(.02f);
            Check(body.PendingDeath.HasValue && body.DeathStatus == PlayerDeathStatus.Pending, "改键暂停操作时氧气仍归零并请求死亡");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.Escape));
            InputSystem.Update(); InputSystem.Update();
            Check(manager.GetComponent<PlayerInput>().currentActionMap.enabled, "真实 Escape 取消改键恢复原输入开关");
        }
        finally
        {
            var operation = (InputActionRebindingExtensions.RebindingOperation)typeof(InputControlMgr).GetField("rebindOperation", Private).GetValue(manager);
            if (operation != null) { operation.Cancel(); InputSystem.Update(); }
            manager.BindPlayer(null); manager.InputOpenOrClose(false);
            assetField.SetValue(data, original);
            InputSystem.RemoveDevice(keyboard); InputSystem.settings.updateMode = mode;
            editUpdates.SetValue(inputManager, previousEditUpdates);
            Object.DestroyImmediate(fresh);
        }
    }
    private static Bounds Bounds(Player actor) => actor.GetComponent<BoxCollider2D>().bounds;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P53 FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float value, float expected, string message, float tolerance = .002f) => Check(Mathf.Abs(value - expected) <= tolerance, message + " (" + value + ")");
}
