using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>由现有 Play Mode 批处理调用，覆盖真实水面游泳及输入、刷新生命周期；不保存场景。</summary>
public static class PlayerSurfaceSwimmingChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> passed = new List<string>();
    private static Player actor;
    private static WaterRegion water;
    private static Action physicsStep;
    private static CharacterPhysics physics;
    private static float centerOffset;
    private static Vector2 start;

    public static IReadOnlyList<string> Run(Player player, WaterRegion pool, Action step, Player controlTarget)
    {
        passed.Clear(); actor = player; water = pool; physicsStep = step;
        physics = actor.GetComponent<CharacterPhysics>();
        centerOffset = Bounds().center.y - actor.GetComponent<Rigidbody2D>().position.y;
        start = actor.GetComponent<Rigidbody2D>().position;
        try
        {
            CheckSurfaceFacts();
            CheckSwimming();
            CheckJumping();
            CheckLeapLifecycle(controlTarget);
            CheckSurfaceConstraints();
            CheckSurfaceEffects();
            return passed.ToArray();
        }
        finally
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/EnvironmentValidation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "surface-swimming-checks.txt"), passed);
        }
    }

    public static void RunRegressionForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("Require a fresh batch editor.");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnvironmentIntegrationChecks.Run();
            PlayerDeathCycleChecks.Run();
            Debug.Log("P53 surface regression: environment and V6 checks passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }



    private static void Step(PlayerInputData input, int count = 1)
    {
        actor.ChangeInputAsset(input);
        for (int i = 0; i < count; i++) { actor.TickLogic(Time.fixedDeltaTime); physicsStep(); }
    }

    private static void CenterAt(float height)
    {
        actor.RefreshRuntime(new Vector2(start.x, height - centerOffset));
        actor.TickLogic(0f);
    }

    private static void Surface() { CenterAt(water.SurfaceY); Step(null, 2); }
    private static Bounds Bounds() => actor.GetComponent<BoxCollider2D>().bounds;
    private static PlayerStateMachine State() => (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(actor);

    private static void CheckSurfaceFacts()
    {
        Surface();
        Check(actor.WaterFacts.partlyAboveSurface && !actor.WaterFacts.submerged, "中心贴水面时，半露出和完全淹没是独立几何事实");
        Check(actor.WaterFacts.surfaceSwimmingAvailable && actor.ResolveFourWayAbility() == FourWayMode.SurfaceSwimming,
            "同源水面许可通过角色统一能力入口解析");
        Check(actor._ActionData.isSurfaceSwimming && actor._ActionData.isSwimming && !actor._ActionData.isFlying,
            "逻辑输出独立水面泳姿，同时保留游泳汇总事实");
        water.SetSwimmingEnabled(false); physics.ResampleEnvironment(); actor.TickLogic(0f);
        Check(actor.WaterFacts.partlyAboveSurface && !actor.WaterFacts.surfaceSwimmingAvailable
            && !actor._ActionData.isSwimming, "关闭游泳不清几何半露出，也不保留水面行为");
        water.SetSwimmingEnabled(true); physics.ResampleEnvironment(); actor.TickLogic(0f);
        Check(actor._ActionData.isSurfaceSwimming, "同一水源重新允许后重选水面泳姿");

        var otherItem = new GameObject("P53 surface second pool");
        var otherCollider = otherItem.AddComponent<BoxCollider2D>(); otherCollider.isTrigger = true;
        otherCollider.size = water.GetComponent<BoxCollider2D>().bounds.size;
        otherItem.transform.position = water.GetComponent<BoxCollider2D>().bounds.center + Vector3.down * .1f;
        var other = otherItem.AddComponent<WaterRegion>(); other.SetSwimmingEnabled(false);
        try
        {
            Physics2D.SyncTransforms(); physics.ResampleEnvironment();
            Near(actor.WaterFacts.surfaceY, water.SurfaceY, "禁用游泳的重叠池不覆盖有效水面高度");
            water.enabled = false; physics.ResampleEnvironment(); actor.TickLogic(0f);
            Check(actor.WaterFacts.partlyAboveSurface && !actor.WaterFacts.surfaceSwimmingAvailable,
                "禁用有效水源后，不借另一池的几何与旧许可拼接水面动作");
            water.enabled = true;
        }
        finally { Object.DestroyImmediate(otherItem); }
        Surface();

        var presentation = actor.GetComponentInChildren<PresentationLayer>();
        typeof(PresentationLayer).GetMethod("Update", Hidden).Invoke(presentation, null);
        var animator = presentation.GetComponent<Animator>(); animator.Update(0f);
        Check(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash("Base Layer.SurfaceSwimIdle"),
            "真实 Animator 从零移动意图的水面事实播放 SurfaceSwimIdle 状态");
        Check(typeof(PresentationLayer).GetField("activeTrigger", Hidden).GetValue(presentation).ToString() == "None",
            "水面悬浮不制造跳跃或落地动画触发");
    }

    private static void CheckSwimming()
    {
        Surface(); State().oxygen = 40f;
        Step(new PlayerInputData { verticalInput = 1f }, 240);
        Near(Bounds().center.y, water.SurfaceY, "持续按上 240 帧保持半身高度，不反复出入水", .015f);
        Check(actor._ActionData.isSurfaceSwimming && actor._ActionData.verticalInput == 0f, "水面行为屏蔽主动向上方向");
        Check(State().oxygen > 40f && !actor.PendingDeath.HasValue, "水面露出按几何恢复氧气");
        float beforeX = actor.GetComponent<Rigidbody2D>().position.x;
        Step(new PlayerInputData { moveInput = .5f }, 12);
        Check(actor.GetComponent<Rigidbody2D>().position.x > beforeX + .5f && actor._ActionData.isSurfaceSwimming,
            "水面可以水平游泳");
        Near(Bounds().center.y, water.SurfaceY, "水平游泳保持水面高度", .015f);

        Surface(); Step(new PlayerInputData { verticalInput = -1f });
        Check(actor._ActionData.isSurfaceSwimming && actor.WaterFacts.partlyAboveSurface,
            "开始下潜首帧仍是水面泳姿，按键不能直接改成水下");
        Check(physics.GetPlanarVelocity().y < 0f, "下潜方向真实进入原执行管线");
        Step(new PlayerInputData { verticalInput = -1f }, 12);
        actor.TickLogic(0f);
        Check(actor.WaterFacts.submerged && actor._ActionData.NowState == PlayerStateMachine.E_playerState.swimming,
            "上缘实际完全入水后才切回水下游泳");
        Step(new PlayerInputData { verticalInput = 1f }, 80);
        Check(actor._ActionData.isSurfaceSwimming && actor.WaterFacts.partlyAboveSurface, "从水下上浮进入独立水面泳姿");
        Near(Bounds().center.y, water.SurfaceY, "上浮后稳定在半身高度", .015f);

        Surface(); Step(new PlayerInputData { verticalInput = -1f }); Step(null, 12);
        Check(actor._ActionData.isSurfaceSwimming, "未完全下潜就松开，仍按水面事实悬浮");
        Near(Bounds().center.y, water.SurfaceY, "浅下潜松开后回到水面中心", .015f);

        CenterAt(water.SurfaceY - 1f);
        actor.ChangeInputAsset(new PlayerInputData { verticalInput = 1f }); actor.TickLogic(.02f);
        for (int i = 0; i < 25; i++) physicsStep();
        actor.TickLogic(0f);
        Near(Bounds().center.y, water.SurfaceY, "一次逻辑更新后多物理帧，上浮仍受当前水面约束", .015f);
        Check(actor._ActionData.isSurfaceSwimming, "低逻辑帧率后消费新事实仍进入水面泳姿");
    }

    private static void CheckJumping()
    {
        Surface();
        actor.ChangeInputAsset(new PlayerInputData { jumpPressed = true }); actor.TickLogic(.02f);
        Check(actor._ActionData.NowState == PlayerStateMachine.E_playerState.inAir && actor._ActionData.surfaceJumpInProgress,
            "水面按跳跃切空中并开启跃出边界");
        actor.TickLogic(0f); actor.TickLogic(0f);
        Check(!actor._ActionData.isSwimming && (bool)typeof(CharacterPhysics).GetField("jump", Hidden).GetValue(physics),
            "冲量未消费时连续逻辑更新不会被旧水面事实重捕获");
        physicsStep();
        Check(physics.ReadOnly_PlayersPhysicsData.verticalSpeed > physics.upSpeed * .9f, "跃出复用原跳跃力度");
        Step(null, 6);
        Check(!actor._ActionData.isSwimming && Bounds().min.y > water.SurfaceY, "上升脱离水面后继续原空中行为");
        float fullSpeed = physics.ReadOnly_PlayersPhysicsData.verticalSpeed;
        Step(new PlayerInputData { jumpRelease = true });
        Check(physics.ReadOnly_PlayersPhysicsData.verticalSpeed < fullSpeed * .8f, "达到最小起跳时间后松键沿用短跳");
        for (int i = 0; i < 240 && !actor._ActionData.isSurfaceSwimming; i++) Step(null);
        Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress,
            "回落触水重新捕获水面泳姿并清跃出边界");
        Step(null, 12);
        Near(Bounds().center.y, water.SurfaceY, "跃出回落后恢复半身悬浮", .015f);

        Surface(); Step(new PlayerInputData { jumpPressed = true, jumpRelease = true });
        float initial = physics.ReadOnly_PlayersPhysicsData.verticalSpeed;
        Step(null, 8);
        Check(physics.ReadOnly_PlayersPhysicsData.verticalSpeed < initial * .6f,
            "同一逻辑帧内点按并松开仍保留最小时间后的短跳请求");

        Surface(); actor.ChangeInputAsset(new PlayerInputData { jumpPressed = true }); actor.TickLogic(.02f);
        actor.RemoveInput(); Step(null, 3);
        Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress
            && Mathf.Abs(physics.GetPlanarVelocity().y) < .01f, "切走控制清除未消费的水面跃出");
        Surface(); Step(new PlayerInputData { jumpPressed = true }); Surface();
        Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress,
            "显式位置刷新撤销旧跃出并重采样水面");
    }

    private static bool JumpPending() => (bool)typeof(CharacterPhysics).GetProperty("HasPendingJump", Hidden).GetValue(physics);

    private static void CheckLeapLifecycle(Player controlTarget)
    {
        int first = passed.Count;
        var manager = InputControlMgr.Instance;
        try
        {
            foreach (bool consumed in new[] { false, true })
            foreach (string cancellation in new[] { "撤销输入", "关闭移动许可", "暂停运动", "切换控制目标", "停用角色逻辑" })
            {
                actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking); Surface();
                manager.BindPlayer(null); manager.BindPlayer(actor);
                manager.PlayerInputData.jumpPressed = true;
                manager.PlayerInputData.sprintHeld = true;
                actor.TickLogic(.02f);
                if (consumed) physicsStep();
                Check(JumpPending() != consumed && actor._ActionData.surfaceJumpInProgress
                    && actor.WaterFacts.partlyAboveSurface, cancellation + "夹具：起跳请求" + (consumed ? "已消费且仍触水" : "尚未消费"));
                float speed = physics.GetPlanarVelocity().y;
                Vector2 position = actor.GetComponent<Rigidbody2D>().position;
                State().stamina = 37f; State().oxygen = 40f; State().closedBecauseEmpty = true;
                typeof(PlayerStateMachine).GetField("sprintMustRelease", Hidden).SetValue(State(), true);
                if (cancellation == "撤销输入") actor.RemoveInput();
                else if (cancellation == "关闭移动许可") actor.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, true, true));
                else if (cancellation == "暂停运动") actor.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, false, true));
                else if (cancellation == "切换控制目标")
                {
                    manager.PlayerInputData.jumpPressed = manager.PlayerInputData.interactTapped = true;
                    manager.BindPlayer(controlTarget);
                    Check(manager.Player == controlTarget && !manager.PlayerInputData.jumpPressed
                        && !manager.PlayerInputData.interactTapped, "实际切换目标不转交旧点按（" + consumed + "）");
                }
                else actor.enabled = false;
                Check(!JumpPending() && actor._ActionData.surfaceJumpInProgress == consumed
                    && actor._ActionData.onMove == 0f, cancellation + "只取消未消费请求（" + consumed + "）");
                Check(State().stamina == 37f && State().oxygen == 40f && State().closedBecauseEmpty
                    && (bool)typeof(PlayerStateMachine).GetField("sprintMustRelease", Hidden).GetValue(State()),
                    cancellation + "不刷新资源或疾跑锁存（" + consumed + "）");
                if (cancellation == "暂停运动")
                {
                    for (int i = 0; i < 3; i++) { actor.TickLogic(.02f); physicsStep(); }
                    Check(actor.GetComponent<Rigidbody2D>().position == position && physics.GetPlanarVelocity().y == speed
                        && actor._ActionData.surfaceJumpInProgress == consumed, "运动暂停保持位置、速度及执行阶段（" + consumed + "）");
                    actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
                }
                actor.enabled = true;
                actor.TickLogic(.02f); physicsStep();
                if (consumed)
                {
                    Check(actor._ActionData.NowState == PlayerStateMachine.E_playerState.inAir && actor._ActionData.surfaceJumpInProgress,
                        cancellation + "后已执行跃出仍为空中行为");
                    Near(physics.GetPlanarVelocity().y, speed + physics.gravity * Time.fixedDeltaTime,
                        cancellation + "后仅沿原重力减速");
                    Near(actor.GetComponent<Rigidbody2D>().position.y, position.y + physics.GetPlanarVelocity().y * Time.fixedDeltaTime,
                        cancellation + "后继续上升，未被拉回水面", .01f);
                    for (int i = 0; i < 240 && !actor._ActionData.isSurfaceSwimming; i++)
                    { actor.TickLogic(.02f); physicsStep(); }
                    Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress,
                        cancellation + "后正常回落并释放保护");
                }
                else Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress
                    && Mathf.Abs(physics.GetPlanarVelocity().y) < .01f, cancellation + "后不会补执行已取消的起跳");
                manager.BindPlayer(null);
            }
            actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
            CheckLeapRequeue(manager, controlTarget);
            CheckLeapRefresh();
            CheckLeapFormCommit(controlTarget);
        }
        finally
        {
            manager.BindPlayer(null); actor.enabled = true;
            actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking); Surface();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/EnvironmentValidation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "v10-surface-control-checks.txt"), passed.GetRange(first, passed.Count - first));
        }
    }

    private static void CheckLeapRequeue(InputControlMgr manager, Player controlTarget)
    {
        foreach (string cancellation in new[] { "撤销输入", "关闭移动许可", "暂停运动", "切换控制目标", "停用角色逻辑" })
        {
            actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking); Surface();
            manager.BindPlayer(null); manager.BindPlayer(actor);
            manager.PlayerInputData.jumpPressed = true; actor.TickLogic(.02f); physicsStep();
            float speed = physics.GetPlanarVelocity().y;
            manager.PlayerInputData.jumpPressed = true; actor.TickLogic(.02f);
            Check(JumpPending() && actor._ActionData.surfaceJumpInProgress && actor.WaterFacts.partlyAboveSurface,
                cancellation + "追加跳跃夹具：已执行跃出后另有空中跳跃排队");
            if (cancellation == "撤销输入") actor.RemoveInput();
            else if (cancellation == "关闭移动许可") actor.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, true, true));
            else if (cancellation == "暂停运动") actor.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, false, true));
            else if (cancellation == "切换控制目标") manager.BindPlayer(controlTarget);
            else actor.enabled = false;
            Check(!JumpPending() && actor._ActionData.surfaceJumpInProgress,
                cancellation + "取消后续空中跳跃，保留先前已执行的跃出");
            if (cancellation == "暂停运动") { actor.TickLogic(.02f); physicsStep(); actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking); }
            actor.enabled = true; actor.TickLogic(.02f); physicsStep();
            Check(actor._ActionData.NowState == PlayerStateMachine.E_playerState.inAir,
                cancellation + "取消追加跳跃后不重捕获水面");
            Near(physics.GetPlanarVelocity().y, speed + physics.gravity * Time.fixedDeltaTime,
                cancellation + "取消追加跳跃后只保留原跃出速度");
            manager.BindPlayer(null);
        }
        actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
    }

    private static void CheckLeapRefresh()
    {
        foreach (bool consumed in new[] { false, true })
        {
            Surface(); actor.ChangeInputAsset(new PlayerInputData { jumpPressed = true }); actor.TickLogic(.02f);
            if (consumed) physicsStep();
            Vector2 position = actor.GetComponent<Rigidbody2D>().position;
            State().stamina = 37f; State().oxygen = 40f;
            actor.RefreshRuntime();
            Check(!actor._ActionData.surfaceJumpInProgress && !JumpPending() && physics.GetPlanarVelocity() == Vector2.zero,
                "显式原地刷新重置请求和执行保护（" + consumed + "）");
            Check(actor.GetComponent<Rigidbody2D>().position == position && State().stamina == State().staminaMax
                && State().oxygen == State().oxygenMax, "显式原地刷新仍保持位置并恢复资源（" + consumed + "）");
        }
        Surface(); Step(new PlayerInputData { jumpPressed = true });
        State().stamina = 37f; State().oxygen = 40f;
        float speed = physics.GetPlanarVelocity().y;
        actor.ResetDeathCycle();
        Check(actor._ActionData.surfaceJumpInProgress && physics.GetPlanarVelocity().y == speed
            && State().stamina == 37f && State().oxygen == 40f, "仅重开死亡周期不终止跃出或刷新资源");
        Step(null);
        Near(physics.GetPlanarVelocity().y, speed + physics.gravity * Time.fixedDeltaTime, "重开死亡周期后跃出继续原重力运动");
    }

    private sealed class FlightProbe : IPlayerFlightExecution
    {
        public bool allowed;
        public bool TrySetFlying(bool flying) => allowed;
    }

    private static void CheckLeapFormCommit(Player spirit)
    {
        var execution = new FlightProbe();
        spirit.BindFormRules(null, execution);
        spirit.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, true, true, true));
        var spiritPhysics = spirit.GetComponent<CharacterPhysics>();
        float offset = spirit.GetComponent<BoxCollider2D>().bounds.center.y - spirit.GetComponent<Rigidbody2D>().position.y;
        spirit.RefreshRuntime(new Vector2(start.x - 3f, water.SurfaceY - offset)); spirit.TickLogic(0f);
        spirit.ChangeInputAsset(new PlayerInputData { jumpPressed = true }); spirit.TickLogic(.02f); physicsStep();
        var commit = typeof(Player).GetMethod("TryCommitSpiritForm", Hidden);
        try
        {
            Check(spirit.Identity == PlayerIdentity.Spirit && spirit._ActionData.surfaceJumpInProgress
                && spiritPhysics.GetPlanarVelocity().y > 0f, "精神体形态探针从正在执行的水面跃出开始");
            Check(!(bool)commit.Invoke(spirit, new object[] { true }) && spirit._ActionData.surfaceJumpInProgress,
                "形态执行拒绝时不清正在执行的跃出");
            execution.allowed = true;
            Check((bool)commit.Invoke(spirit, new object[] { true }) && spirit._ActionData.isFlying
                && !spirit._ActionData.surfaceJumpInProgress, "成功形态提交明确重置跃出保护（仅协议探针）");
            Check((bool)commit.Invoke(spirit, new object[] { false }) && !spirit._ActionData.surfaceJumpInProgress,
                "形态返回行走不继承旧跃出保护（仅协议探针）");
        }
        finally { spirit.BindFormRules(null, null); spirit.ApplyCapabilities(PlayerCapabilityPermissions.Standby); }
    }

    private static void CheckSurfaceEffects()
    {
        Surface(); physics.AddSpeedStatus(water.EnvironmentRegistration, Vector2.up * 2f);
        Step(new PlayerInputData { verticalInput = 1f }, 60);
        Near(Bounds().center.y, water.SurfaceY, "向上水流水面处由悬浮动作维持目标高度", .015f);
        Near(physics.ReadOnly_PlayersPhysicsData.phyVSpeed, 2f, "水面动作不删除水流来源或被动速度");
        CenterAt(water.SurfaceY - 1.5f); Step(null, 2);
        Near(physics.GetPlanarVelocity().y, 2f, "返回水下立即保留同一竖直水流");
        physics.RemoveSpeedStatus(water.EnvironmentRegistration);
        Surface(); float oldHeight = water.SurfaceY;
        Check(water.ConfigureWaterLevel(water.GetComponent<BoxCollider2D>().bounds.min.y, oldHeight, .5f, true),
            "水面上涨仍通过原配置入口");
        Step(null, 60);
        Check(water.SurfaceY > oldHeight + .5f && actor._ActionData.isSurfaceSwimming,
            "上涨水面带动半身泳姿，保持独立水面行为");
        Near(Bounds().center.y, water.SurfaceY, "悬浮读取相位零提交后的水面高度", .015f);
        water.ConfigureWaterLevel(water.GetComponent<BoxCollider2D>().bounds.min.y, oldHeight, 0f, false);
        physicsStep(); Surface();
    }

    private static void CheckSurfaceConstraints()
    {
        Surface(); Step(new PlayerInputData { moveInput = 1f, verticalInput = 1f });
        Near(physics.ReadOnly_PlayersPhysicsData.horizontalSpeed, 6f, "水面横向按上不被无效上轴削弱水平速度");
        Surface(); Step(new PlayerInputData { moveInput = 1f, verticalInput = -1f });
        Near(physics.GetPlanarVelocity().magnitude, 6f, "水面斜向下潜沿用双轴归一化");
        Surface();
        float halfHeight = Bounds().extents.y;
        var support = Solid("P53 shallow surface floor", new Vector2(start.x, water.SurfaceY - halfHeight - .25f), new Vector2(4f, .5f));
        support.gameObject.layer = FirstLayer(((SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(physics)).groundLayer);
        try
        {
            Physics2D.SyncTransforms(); Surface();
            Check(actor.NowPhyData.isGrounded && actor._ActionData.isSurfaceSwimming, "浅水贴底时几何着地和水面行为共存");
            actor.ChangeInputAsset(new PlayerInputData { jumpPressed = true }); actor.TickLogic(.02f); actor.TickLogic(0f);
            Check(actor._ActionData.NowState == PlayerStateMachine.E_playerState.inAir,
                "浅水跃出冲量前的旧着地不夺回地面行为");
            physicsStep(); Step(null, 5);
            Check(Bounds().min.y > support.bounds.max.y + .2f && !actor._ActionData.isSwimming,
                "浅水池底保持真实阻挡，跃出仍可起跳");
        }
        finally { Object.DestroyImmediate(support.gameObject); }

        Surface();
        var roof = Solid("P53 surface jump roof", new Vector2(start.x, water.SurfaceY + halfHeight + .26f), new Vector2(4f, .5f));
        roof.gameObject.layer = FirstLayer(((SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(physics)).groundLayer);
        try
        {
            Physics2D.SyncTransforms(); Step(new PlayerInputData { jumpPressed = true }); Step(null, 80);
            Check(Bounds().max.y <= roof.bounds.min.y + .02f, "跃出时原顶壁阻挡保持有效");
            Check(actor._ActionData.isSurfaceSwimming && !actor._ActionData.surfaceJumpInProgress,
                "低顶阻挡跃出后，上升结束仍重新进入水面泳姿");
        }
        finally { Object.DestroyImmediate(roof.gameObject); }

        actor.RefreshRuntime(new Vector2(water.GetComponent<BoxCollider2D>().bounds.max.x + Bounds().extents.x + .2f,
            water.SurfaceY - centerOffset));
        actor.TickLogic(0f); Step(null, 3);
        Check(!actor.WaterFacts.partlyAboveSurface && !actor._ActionData.isSwimming && physics.GetPlanarVelocity().y < 0f,
            "移到水域侧边外退出水面并恢复重力");
        Surface();
    }

    private static int FirstLayer(int mask)
    {
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        throw new InvalidOperationException("Fixture requires a collision layer.");
    }



    private static Collider2D Solid(string name, Vector2 center, Vector2 size)
    {
        var item = new GameObject(name); item.transform.position = center;
        var collider = item.AddComponent<BoxCollider2D>(); collider.size = size; return collider;
    }





    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P53 surface FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float value, float expected, string message, float tolerance = .002f) =>
        Check(Mathf.Abs(value - expected) <= tolerance, message + " (" + value + ")");
}
