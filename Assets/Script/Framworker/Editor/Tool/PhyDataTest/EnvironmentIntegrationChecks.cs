using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 环境接入的可重复 Edit Mode 集成检查。使用实际组件、预制体、Collider2D 查询与实体速度容器。
/// 创建临时对象，结束即销毁，不保存场景/预制体。与只读调试窗口分开，仅由明确的验证命令运行。
/// </summary>
public static class EnvironmentIntegrationChecks
{
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static int group;

    // 检查夹具沿唯一持有者读取内部数值；运行时代码无需暴露可变容器。
    internal static object EffectField(BasicEntity body, string fieldName)
    {
        object state = typeof(BasicEntity).GetField("effectState", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(body);
        return state.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(state);
    }

    [MenuItem("自定义工具/测试工具/验证/EPhy 验证环境接入（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
        group = 0;
        // Edit Mode 使用临时调度器，避免自动单例调用仅 Play Mode 可用的 DontDestroyOnLoad。
        // 只替换原来为空的单例，验证完成后恢复，不修改运行时代码来适配测试。
        FieldInfo schedulerField = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = schedulerField.GetValue(null);
        try
        {
            if ((MonoPublicMgr)previousScheduler == null)
                schedulerField.SetValue(null, NewObject("validation scheduler", Vector3.zero).AddComponent<MonoPublicMgr>());
            CheckGroundLifecycle();
            CheckMotionParticipation();
            CheckMotionInitialization();
            CheckRegions();
            CheckWaterRegions();
            CheckPlayerCapabilities();
            CheckPlayerControlSwitch();
            CheckFourWayBehavior();
            CheckDeathRequestContracts();
            CheckOxygenResources();
            CheckPlayerRefresh();
            CheckWaterLevel();
            CheckRisingWaterLimit();
            CheckSwimmingPresentation();
            CheckCapabilities();
            CheckCapabilityOwnership();
            CheckUniqueEnvironmentHost();
            CheckUnifiedSources();
            CheckSourceValidity();
            CheckPlatformDisplacement();
            CheckContactRoles();
            CheckTriggerContactFilter();
            CheckSlopeFootprintSupport();
            CheckSlopeSideSupport();
            CheckWallSlideBehavior();
            Directory.CreateDirectory(".utmp/EnvironmentValidation");
            File.WriteAllLines(".utmp/EnvironmentValidation/checks.txt", passed);
            Debug.Log($"Environment integration: {passed.Count} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            schedulerField.SetValue(null, previousScheduler);
            Directory.CreateDirectory(".utmp/EnvironmentValidation");
            File.WriteAllLines(".utmp/EnvironmentValidation/checks.txt", passed);
        }
    }

    // Unity -batchmode -executeMethod EnvironmentIntegrationChecks.RunForBatch
    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckPlayerCapabilities()
    {
        var body = new PlayerCapabilityContext(PlayerIdentity.Body);
        var spirit = new PlayerCapabilityContext(PlayerIdentity.Spirit);
        var all = new PlayerCapabilityPermissions(true, true, true, true, true);
        body.ApplyPermissions(all);
        spirit.ApplyPermissions(all);
        Check(!SetFlightForm(body, true), "实体身份拒绝飞行，即使外部误给许可");
        Check(body.ResolveFourWay(new EntityWaterFrame(true, true)) == FourWayMode.Swimming, "实体有效水能力进入游泳");
        Check(SetFlightForm(spirit, true), "精神体获准后可开启飞行形态");
        Check(spirit.ResolveFourWay(new EntityWaterFrame(true, true)) == FourWayMode.Flying, "飞行优先于淹没和游泳");
        Check(spirit.ResolveFourWay(default) == FourWayMode.Flying, "水事实撤销不关闭飞行");
        SetFlightForm(spirit, false);
        Check(spirit.ResolveFourWay(new EntityWaterFrame(true, true)) == FourWayMode.Swimming, "精神体行走形态也可游泳");
        Check(spirit.ResolveFourWay(new EntityWaterFrame(true, false)) == FourWayMode.None, "淹没但游泳关闭时无四向候选");
        spirit.ApplyPermissions(PlayerCapabilityPermissions.Standby);
        Check(!spirit.Permissions.motionUpdates && !spirit.Permissions.resourceUpdates, "待机运动和资源开关独立于控制引用");
    }

    private static void CheckFourWayBehavior()
    {
        var geometry = new GeometryPhysicsData { isGrounded = true };
        var function = new PhysicalFunctionData();
        var movement = new MovementData();
        var input = new PlayerInputData { moveInput = 1f, verticalInput = 1f, sprintHeld = true, jumpPressed = true };
        var fsm = new PlayerStateMachine();
        fsm.InitData(new ReadOnly_GeometryPhysicsData(geometry, function), input, movement);
        int jumps = 0;
        fsm.EventSystem.AddEventListener(PlayerStateMachine.E_playEvent.jump, () => jumps++);
        fsm.stamina = 50f;
        fsm.jbtB = true;
        fsm.Update(input, FourWayMode.Swimming, true, true, 1f);
        Check(movement.nowState == PlayerStateMachine.E_playerState.swimming, "池底着地时游泳行为仍优先");
        Near(new Vector2(movement.onMove, movement.verticalInput).magnitude, 1f, "四向斜向归一化");
        Check(jumps == 0 && !fsm.jbtB && !input.jumpPressed, "入水不执行普通跳跃并清跳跃缓冲");
        Near(movement.speedScale, 1f, "游泳不叠疾跑倍率");
        Near(fsm.stamina, 70f, "游泳按原规则恢复体力");
        fsm.Update(null, FourWayMode.Swimming, true, true, 1f);
        Check(movement.nowState == PlayerStateMachine.E_playerState.swimming, "无输入仍保留水行为");
        Near(movement.onMove, 0f, "失去控制后四向水平主动悬停");
        Near(movement.verticalInput, 0f, "失去控制后四向竖直主动悬停");
        Near(fsm.stamina, 90f, "无输入仍更新获准资源");
        fsm.Update(null, FourWayMode.Swimming, true, false, 1f);
        Near(fsm.stamina, 90f, "关闭资源许可暂停资源更新");
        fsm.Update(input, FourWayMode.None, true, true, 0f);
        Check(movement.nowState == PlayerStateMachine.E_playerState.isOnGround, "离水时按当前几何回着地");
        geometry.isGrounded = false;
        fsm.Update(input, FourWayMode.Swimming, true, true, 0f);
        fsm.Update(input, FourWayMode.None, true, true, 0f);
        Check(movement.nowState == PlayerStateMachine.E_playerState.inAir, "空中离水不恢复旧着地状态");
        fsm.Update(input, FourWayMode.Flying, true, true, 0f);
        Check(new ReadOnly_ActionData(movement).isFlying && !new ReadOnly_ActionData(movement).isSwimming, "飞行与游泳原因互斥");
        fsm.Update(input, FourWayMode.Swimming, false, true, 0f);
        Near(movement.onMove, 0f, "移动许可关闭不改变水事实但屏蔽操作");
    }

    private sealed class RefreshOutcomeProbe : IPlayerRunRefreshRules
    {
        public bool complete;
        public int completions;
        public Player subject;
        public bool TryRestartExploration(Player deadPlayer)
        { subject = deadPlayer; if (complete) completions++; return complete; }
    }

    private sealed class UncommittedFormProbe : IPlayerFormRules
    {
        public bool TryChangeForm(Player subject, bool flying, PlayerFormChangeReason reason) => true;
    }

    private static void CheckDeathRequestContracts()
    {
        Player body = PlayerActor("player20.prefab");
        Player spirit = PlayerActor("SpiritPlayer.prefab");
        var refresh = new RefreshOutcomeProbe();
        var rules = new PlayerLifecycleRules(body, spirit, refresh);
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        Check(body.TryRequestDeath(PlayerDeathCause.OxygenDepleted), "氧气死亡保存明确死者请求");
        PlayerDeathRequest request = body.PendingDeath.Value;
        Check(request.subject == body && request.cause == PlayerDeathCause.OxygenDepleted, "死亡请求不查询当前控制目标替换死者");
        Check(!body.TryRequestDeath(PlayerDeathCause.Abyss), "同周期深渊和氧气只保留一个请求");
        body.BindDeathReceiver(rules);
        Check(body.DeathStatus == PlayerDeathStatus.Pending && body.PendingDeath.HasValue, "刷新服务未完成不能报告死亡处理完成");
        refresh.complete = true;
        body.RetryPendingDeath();
        Check(body.DeathStatus == PlayerDeathStatus.Handled && !body.PendingDeath.HasValue, "结果服务提交成功后确认请求完成");
        Check(refresh.subject == body && refresh.completions == 1, "逃生消费者传递正确死者且只完成一次");
        rules.ReceiveDeath(request);
        Check(refresh.completions == 1, "已完成旧请求重放不再次刷新");
        Check(!body.TryRequestDeath(PlayerDeathCause.Abyss), "完成后未刷新周期仍拒绝同周期重复死亡");
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        spirit.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, true, true, true));
        spirit.BindDeathReceiver(rules);
        Check(spirit.TryRequestDeath(PlayerDeathCause.Abyss), "精神体行走深渊共用统一死亡入口");
        Check(spirit.DeathStatus == PlayerDeathStatus.Pending && rules.IsSimulation, "探索形态依赖缺席保持请求和模拟状态");
        Check(!spirit.RequestSpiritForm(true, PlayerFormChangeReason.Death), "穿墙和扣费模块缺席不伪造飞行成功");
        spirit.BindFormRules(new UncommittedFormProbe(), null);
        Check(!spirit.RequestSpiritForm(true, PlayerFormChangeReason.Death), "形态规则只返回成功但未提交实际形态时仍拒绝确认");
    }

    private static void CheckOxygenResources()
    {
        var fsm = new PlayerStateMachine();
        Check(!fsm.TickOxygen(1f, true, false, true), "淹没先扣氧而非立即死亡");
        Near(fsm.oxygen, 90f, "氧气按时间扣一次");
        fsm.TickOxygen(1f, true, false, false);
        Near(fsm.oxygen, 90f, "待机许可暂停氧气更新");
        fsm.TickOxygen(1f, false, false, true);
        Near(fsm.oxygen, 100f, "离水逐渐恢复并限制上限");
        fsm.oxygen = 50f;
        fsm.TickOxygen(1f, true, true, true);
        Near(fsm.oxygen, 70f, "飞行淹没不耗氧并沿资源参数恢复");
        fsm.oxygen = 10f;
        Check(fsm.TickOxygen(1f, true, false, true), "氧气归零只发一次死亡信号");
        int repeated = 0;
        for (int i = 0; i < 120; i++) if (fsm.TickOxygen(1f / 60f, true, false, true)) repeated++;
        Check(repeated == 0 && fsm.oxygen == 0f, "持续淹没两秒不重复死亡且不低于零");
        fsm.TickOxygen(1f, false, false, true);
        Check(fsm.TickOxygen(2f, true, false, true), "恢复正值后可以开始下一次耗尽周期");
        var fast = new PlayerStateMachine();
        var slow = new PlayerStateMachine();
        for (int i = 0; i < 60; i++) fast.TickOxygen(1f / 60f, true, false, true);
        for (int i = 0; i < 5; i++) slow.TickOxygen(.2f, true, false, true);
        Check(Mathf.Abs(fast.oxygen - slow.oxygen) < .001f, "氧气不同逻辑帧率按累计时间一致");
        var split = new PlayerStateMachine();
        split.TickOxygen(.02f, true, false, true);
        split.TickOxygen(.48f, true, false, true);
        Near(split.oxygen, 95f, "入离水按所消费快照对应的逻辑时间扣减");
    }

    private static void CheckPlayerRefresh()
    {
        Player player = PlayerActor("player20.prefab");
        CharacterPhysics physics = player.GetComponent<CharacterPhysics>();
        Physics2D.SyncTransforms();
        Bounds bounds = player.GetComponent<BoxCollider2D>().bounds;
        WaterWithEffects water = Prefab<WaterWithEffects>("WaterPoolWithEffects.prefab", new Vector3(bounds.center.x, bounds.max.y, 0f));
        Physics2D.SyncTransforms();
        physics.ResampleEnvironment();
        Check(player.WaterFacts.submerged, $"刷新用例出生几何已完全淹没 [body={bounds}, water={water.GetComponent<Collider2D>().bounds}]");
        player.ApplyCapabilities(new PlayerCapabilityPermissions(false, true, false, false, true));
        physics.ResampleEnvironment();
        Check(player.WaterFacts.submerged, "仅暂停运动时水事实仍可采样");
        player.TickLogic(.1f);
        Near(player.ResourceFacts.oxygen, 99f, "暂停运动但许可资源更新时仍耗氧");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        Vector2 origin = player.transform.position;
        var fsm = (PlayerStateMachine)typeof(Player).GetField("fsm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        fsm.staminaMax = 150f;
        fsm.stamina = fsm.oxygen = 0f;
        fsm.jbtB = true;
        player.TryRequestDeath(PlayerDeathCause.OxygenDepleted);
        uint firstCycle = player.PendingDeath.Value.cycle;
        player.GetComponent<Rigidbody2D>().velocity = new Vector2(10f, 20f);
        physics.AddTimeSpeed(1f, 30f);
        player.RefreshRuntime();
        Check((Vector2)player.transform.position == origin, "不指定位置的轻刷新保留精神体/角色坐标");
        Check(player.WaterFacts.submerged && player._ActionData.isSwimming, "深水刷新重采样而非永久伪装离水");
        Near(player.ResourceFacts.oxygen, 100f, "刷新恢复氧气");
        Near(player.ResourceFacts.stamina, 150f, "刷新资源保留当前生效上限");
        Check(!fsm.jbtB && !player.PendingDeath.HasValue, "刷新清旧跳跃缓冲和死亡周期");
        Check(player.GetComponent<Rigidbody2D>().velocity == Vector2.zero && physics.GetPlanarVelocity() == Vector2.zero, "刷新清原生和自研速度缓存");
        Check(physics.EnvironmentContext.DebugSourceCount == 1, "轻刷新保留仍命中的水效果关系");
        player.TryRequestDeath(PlayerDeathCause.Abyss);
        Check(player.PendingDeath.Value.cycle > firstCycle, "刷新后可以开始新死亡周期");
        player.RefreshRuntime(origin + Vector2.up * 20f, true);
        Check(!player.WaterFacts.submerged && physics.EnvironmentContext.DebugSourceCount == 0, "带位置与显式撤销的刷新按新几何退出旧水效果");
    }

    private static void CheckWaterLevel()
    {
        WaterRegion water = Prefab<WaterRegion>("WaterPool.prefab", NextPosition());
        MethodInfo phase = typeof(WaterRegion).GetMethod("AdvanceWaterLevel", BindingFlags.Instance | BindingFlags.NonPublic);
        Physics2D.SyncTransforms();
        Bounds original = water.GetComponent<BoxCollider2D>().bounds;
        phase.Invoke(water, null);
        Physics2D.SyncTransforms();
        Check(water.GetComponent<BoxCollider2D>().bounds == original, "未配置普通池保持静态");
        float bottom = original.min.y;
        float surface = original.max.y;
        Check(water.ConfigureWaterLevel(bottom, surface, 2f, false), "规则层可配置水位和上涨许可");
        phase.Invoke(water, null);
        Physics2D.SyncTransforms();
        Near(water.SurfaceY, surface, "关闭许可水面不涨");
        water.ConfigureWaterLevel(bottom, surface, 2f, true);
        for (int i = 0; i < 5; i++) phase.Invoke(water, null);
        Physics2D.SyncTransforms();
        Bounds risen = water.GetComponent<BoxCollider2D>().bounds;
        Check(Mathf.Abs(risen.max.y - (surface + 10f * Time.fixedDeltaTime)) < .003f, "相位零按许可和速率上涨");
        Near(risen.min.y, bottom, "上涨固定水底");
        Check(Mathf.Abs(water.GetComponent<SpriteRenderer>().bounds.max.y - risen.max.y) < .003f, "显示水面与判定范围一致");
        Check(!water.ConfigureWaterLevel(bottom, bottom, 1f, true), "拒绝空水域而不猜测几何");
        water.ConfigureWaterLevel(bottom, risen.max.y, 2f, false);
        phase.Invoke(water, null);
        Physics2D.SyncTransforms();
        Check(Mathf.Abs(water.SurfaceY - risen.max.y) < .003f, "撤销上涨许可保留当前水位");
    }

    private static void CheckRisingWaterLimit()
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        WaterWithEffects water = Prefab<WaterWithEffects>("WaterPoolWithEffects.prefab", NextPosition());
        typeof(WaterWithEffects).GetField("flowVelocity", hidden).SetValue(water, new Vector2(2f, 0f));
        Physics2D.SyncTransforms();
        BoxCollider2D shape = water.GetComponent<BoxCollider2D>();
        Bounds original = shape.bounds;
        SceneLevelSetup setup = NewObject("capped rising water setup", NextPosition()).AddComponent<SceneLevelSetup>();
        typeof(SceneLevelSetup).GetField("risingWater", hidden).SetValue(setup, water);
        typeof(SceneLevelSetup).GetField("waterRiseSpeed", hidden).SetValue(setup, 7f);
        Check(!setup.PrepareRisingWater(out string missing) && !string.IsNullOrEmpty(missing),
            "主水缺最高点明确拒绝，不自行搜索场景或改水位");
        Check(shape.bounds == original, "最高点配置拒绝保持制作水域不变");
        Transform mark = NewObject("maximum water surface", new Vector3(original.center.x + 50f, original.max.y - 1f, 4f)).transform;
        typeof(SceneLevelSetup).GetField("risingWaterMaximum", hidden).SetValue(setup, mark);
        Check(!setup.PrepareRisingWater(out string below) && below.Contains("初始水面"),
            "最高点低于初始水面拒绝，不抬高标记或替换上限");
        mark.position = new Vector3(original.center.x + 50f, original.max.y + .08f, 4f);
        mark.SetParent(water.transform, true);
        Check(!setup.ValidateRisingWaterConfiguration(out _), "最高点挂在上涨水下拒绝，避免随水漂移");
        mark.SetParent(null, true);
        Check(!water.CanPrepareRun(float.NaN, mark.position.y)
            && !water.CanPrepareRun(-1f, mark.position.y)
            && !water.CanPrepareRun(7f, float.PositiveInfinity), "非法水速与最高水面拒绝");
        Check(setup.PrepareRisingWater(out _), "独立最高点按世界 Y 准备带效果主水，X／Z 不限制水位");
        float maximum = mark.position.y;
        mark.position += Vector3.up * 20f;
        MethodInfo phase = typeof(WaterRegion).GetMethod("AdvanceWaterLevel", hidden);
        phase.Invoke(water, null);
        Near(water.SurfaceY, original.max.y, "准备主水尚未放开上涨许可");
        water.SetRisingAllowed(true);
        phase.Invoke(water, null);
        Near(water.SurfaceY, maximum, "单步越过最高点只到水面上限，不跟随运行中标记移动");
        for (int i = 0; i < 20; i++) phase.Invoke(water, null);
        Near(water.SurfaceY, maximum, "到顶持续运行不越过最高水面");
        Near(shape.bounds.min.y, original.min.y, "限高上涨保留原水底");
        Near(shape.bounds.size.x, original.size.x, "限高上涨保留原水宽");
        Near(water.GetComponent<SpriteRenderer>().bounds.max.y, maximum, "到顶显示水面与触发范围一致");
        Check(!water.ConfigureWaterLevel(original.min.y, maximum + 1f, 7f, true),
            "已有主水上限时外部超高配置拒绝，不能绕过最高点");
        Near(water.SurfaceY, maximum, "超高配置拒绝保持当前水面");
        water.RestoreRunDefaults();
        Near(water.SurfaceY, original.max.y, "失败恢复初始水面");
        Check(!water.RisingAllowed && water.SwimmingEnabled, "复位停止上涨而保留游泳");
        phase.Invoke(water, null);
        Near(water.SurfaceY, original.max.y, "恢复漫游时水位不补涨");
        water.SetRisingAllowed(true);
        phase.Invoke(water, null);
        Near(water.SurfaceY, maximum, "恢复后再次上涨沿已提交上限停止");

        FieldInfo current = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = current.GetValue(null);
        MethodInfo editable = typeof(EnvironmentEditView).GetMethod("EditableSource", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            current.SetValue(null, setup);
            Check(!water.AllowsEnvironmentEditing && !(bool)editable.Invoke(null, new object[] { water }),
                "指定带效果主水拒绝玩家编辑与选择框");
            EnvironmentCheckBody body = Body(water.transform.position);
            Refresh(body);
            Check(body.EnvironmentContext.DebugSourceCount == 1 && body.DebugMovementModifierCount == 1
                && body.DebugStateVelocityCount == 1, "主水不可编辑仍提供减速与水流绑定");
            Near(water.StateVelocity.x, 2f, "上涨与复位保留宿主水流参数");
            Check(water.MovementSpeedOffset < 0f, "上涨与复位保留制作水中减速");
            WaterRegion ordinary = Prefab<WaterRegion>("WaterPool.prefab", NextPosition());
            Check(ordinary.AllowsEnvironmentEditing && (bool)editable.Invoke(null, new object[] { ordinary }),
                "关联主水不锁定本关其它普通水池");
        }
        finally { current.SetValue(null, previous); }
        mark.position = new Vector3(mark.position.x, original.max.y, mark.position.z);
        Check(setup.PrepareRisingWater(out _), "最高点等于初始水面可配置为不再上涨");
        water.SetRisingAllowed(true); phase.Invoke(water, null);
        Near(water.SurfaceY, original.max.y, "零上涨空间不越界");
    }

    private static void CheckSwimmingPresentation()
    {
        Player player = PlayerActor("player20.prefab");
        PresentationLayer presentation = player.GetComponentInChildren<PresentationLayer>();
        var fsm = (PlayerStateMachine)typeof(Player).GetField("fsm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        var physics = player.GetComponent<CharacterPhysics>();
        var geometry = (GeometryPhysicsData)typeof(BasicEntity).GetField("nowGemetry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(physics);
        var speed = (PlayerPhysicsData)typeof(BasicEntity).GetField("playerPhysicsData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(physics);
        var input = new PlayerInputData { verticalInput = 1f };
        MethodInfo update = typeof(PresentationLayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo animation = typeof(PresentationLayer).GetField("currentAnimation", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo trigger = typeof(PresentationLayer).GetField("activeTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
        Animator animator = presentation.GetComponent<Animator>();
        animator.Rebind();
        animator.Update(0f);
        geometry.isGrounded = true;
        fsm.Update(input, FourWayMode.Swimming, true, true, 0f);
        speed.verticalSpeed = 6f;
        update.Invoke(presentation, null);
        Check(animation.GetValue(presentation).ToString() == "Swim", "水底几何着地仍播放独立游泳状态");
        Check(trigger.GetValue(presentation).ToString() == "None", "游泳上浮不启动跳跃触发");
        speed.verticalSpeed = 12f;
        update.Invoke(presentation, null);
        Check(animation.GetValue(presentation).ToString() == "Swim" && trigger.GetValue(presentation).ToString() == "None", "游泳竖直速度增加不误判跳跃冲量");
        fsm.Update(input, FourWayMode.None, true, true, 0f);
        update.Invoke(presentation, null);
        Check(animation.GetValue(presentation).ToString() == "Idle" && trigger.GetValue(presentation).ToString() == "None", "水底退出游泳不误播旧落地动作");
        fsm.Update(input, FourWayMode.Flying, true, true, 0f);
        update.Invoke(presentation, null);
        Check(animation.GetValue(presentation).ToString() == "Flight", "飞行原因拥有独立表现接入口");
        Check(animator.HasState(0, Animator.StringToHash("Base Layer.Swim")), "控制器已有独立游泳状态");
    }

    private static Player PlayerActor(string prefab)
    {
        Player player = Prefab<Player>(prefab, NextPosition());
        typeof(CharacterPhysics).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player.GetComponent<CharacterPhysics>(), null);
        typeof(Player).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
        typeof(PresentationLayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player.GetComponentInChildren<PresentationLayer>(), null);
        typeof(Player).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
        return player;
    }

    private static void CheckPlayerControlSwitch()
    {
        Player body = PlayerActor("player20.prefab");
        Player spirit = PlayerActor("SpiritPlayer.prefab");
        Check(body.Identity == PlayerIdentity.Body && spirit.Identity == PlayerIdentity.Spirit, "双预设共用脚本并持有固定身份");
        InputControlMgr manager = NewObject("control switch", NextPosition()).AddComponent<InputControlMgr>();
        typeof(InputControlMgr).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
        Check(manager.Player == null, "创建玩家不主动抢控制权");
        manager.BindPlayer(body);
        PlayerInputData input = manager.PlayerInputData;
        input.moveInput = 1f;
        input.jumpPressed = input.jumpRelease = input.interactTapped = input.interactLongPressed = input.sprintReleased = true;
        typeof(Player).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(body, null);
        CharacterPhysics physics = body.GetComponent<CharacterPhysics>();
        typeof(CharacterPhysics).GetMethod("Jump", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(physics, null);
        typeof(InputControlMgr).GetField("interactHolding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, true);
        input.jumpPressed = input.jumpRelease = input.interactTapped = input.interactLongPressed = input.sprintReleased = true;
        manager.BindPlayer(spirit);
        Check(manager.Player == spirit, "外部选择目标不受创建顺序影响");
        Near(body._ActionData.onMove, 0f, "切换立即清旧角色移动输出");
        Check(!(bool)typeof(CharacterPhysics).GetField("jump", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(physics), "切换清物理待消费跳跃");
        Check(!input.jumpPressed && !input.jumpRelease && !input.interactTapped && !input.interactLongPressed && !input.sprintReleased, "新目标不继承旧点按和释放历史");
        Check(!(bool)typeof(InputControlMgr).GetField("interactHolding", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager), "切换中止交互长按计时");
        manager.UnbindPlayer(body);
        Check(manager.Player == spirit, "旧目标不能误解绑新目标");
        Disable(spirit);
        Check(manager.Player == null, "禁用当前目标安全解除绑定");
        Vector3 position = body.transform.position;
        Check(position.x > 10000f, "角色初始化保留外部出生位置");
        body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        Check(!body.GetComponent<Rigidbody2D>().simulated && !body._ActionData.motionUpdates, "待机角色暂停原生及自研运动");
        body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        Check(body.GetComponent<Rigidbody2D>().simulated, "运动许可重新开启恢复参与");
    }

    private static bool SetFlightForm(PlayerCapabilityContext context, bool flying) =>
        (bool)typeof(PlayerCapabilityContext).GetMethod("SetFlightForm", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(context, new object[] { flying });

    private static void CheckWaterRegions()
    {
        Vector3 position = NextPosition();
        WaterRegion water = Prefab<WaterRegion>("WaterPool.prefab", position);
        Collider2D region = water.GetComponent<Collider2D>();
        EnvironmentCheckBody body = Body(position);
        var regions = new List<Collider2D> { region, region };
        Physics2D.SyncTransforms();
        EntityEnvironmentContext context = body.EnvironmentContext;
        Vector2 inside = position;
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.submerged && context.WaterFrame.swimmingAvailable, "水预设提供淹没及游泳");
        Check(context.DebugSourceCount == 0, "纯水采样不伪造持续效果绑定");
        water.SetSwimmingEnabled(false);
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.submerged && !context.WaterFrame.swimmingAvailable, "重叠内关游泳仍保留淹没");
        water.SetSwimmingEnabled(true);
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.swimmingAvailable, "重叠内重新开游泳立即生效");
        context.RefreshEnvironment(null, regions, inside + Vector2.up * 3f);
        Check(!context.WaterFrame.submerged && !context.WaterFrame.swimmingAvailable, "脚入水但上缘在水外不算淹没");
        WaterRegion second = Prefab<WaterRegion>("WaterPool.prefab", position + Vector3.right * 12f);
        Collider2D secondRegion = second.GetComponent<Collider2D>();
        Physics2D.SyncTransforms();
        regions.Add(secondRegion);
        water.SetSwimmingEnabled(false);
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.submerged && !context.WaterFrame.swimmingAvailable, "不同水源不得拼接淹没和游泳条件");
        second.transform.position = position;
        Physics2D.SyncTransforms();
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.submerged && context.WaterFrame.swimmingAvailable, "多水源按任一满足完整条件汇总");
        second.enabled = false;
        context.RefreshEnvironment(null, regions, inside);
        Check(context.WaterFrame.submerged && !context.WaterFrame.swimmingAvailable, "禁用一水源不清掉另一水源淹没");
        water.enabled = false;
        context.RefreshEnvironment(null, regions, inside);
        Check(!context.WaterFrame.submerged, "全部水宿主禁用后撤销事实");
        water.enabled = true;
        region.isTrigger = false;
        context.RefreshEnvironment(null, regions, inside);
        Check(!context.WaterFrame.submerged, "实心几何不提供水区域事实");
        region.isTrigger = true;
        region.enabled = false;
        context.RefreshEnvironment(null, regions, inside);
        Check(!context.WaterFrame.submerged, "禁用水碰撞体不保留淹没");
        region.enabled = true;
        context.RefreshEnvironment(null, regions, inside);
        context.ReleaseAll();
        Check(!context.WaterFrame.submerged, "实体释放关系同时清水快照");
        context.RefreshEnvironment(null, regions);
        Check(!context.WaterFrame.submerged, "没有采样点不沿用旧水事实");
        ForceField field = NewObject("pure force", position).AddComponent<ForceField>();
        Collider2D fieldRegion = field.gameObject.AddComponent<BoxCollider2D>();
        fieldRegion.isTrigger = true;
        context.RefreshEnvironment(null, new List<Collider2D> { fieldRegion }, inside);
        Check(!context.WaterFrame.submerged, "纯力场不自动成为水");
        ForceField oldPond = Prefab<ForceField>("Pond.prefab", position);
        context.RefreshEnvironment(null, new List<Collider2D> { oldPond.GetComponent<Collider2D>() }, inside);
        Check(!context.WaterFrame.submerged, "旧 Pond 保持力场身份");
        Object.DestroyImmediate(water.gameObject);
        context.RefreshEnvironment(null, regions, inside);
        Check(!context.WaterFrame.submerged, "销毁水源后安全撤销事实");
    }

    private static void CheckGroundLifecycle()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Collider2D ground = ice.GetComponent<Collider2D>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        body.RefreshSupport(ground);
        Check(body.DebugEnvironmentSourceCount == 1 && body.DebugMovementModifierCount == 1
            && body.DebugStateVelocityCount == 1 && body.DebugDynamicForceCount == 1, "冰面环境三类能力登记到同一来源");
        Near(body.Frame.slowingMultiplier, .5f, "冰面保留阻力倍率");
        Near(body.Frame.jumpHeightOffset, -.5f, "冰面保留跳跃加成");
        body.RefreshSupport(ground);
        Check(body.DebugEnvironmentSourceCount == 1 && body.DebugDynamicForceCount == 1, "重复支撑采样不重复登记来源");
        Rigidbody2D rigidbody = body.GetComponent<Rigidbody2D>();
        rigidbody.velocity = Vector2.right * 4f;
        body.RefreshSupport(null);
        Check(body.DebugEnvironmentSourceCount == 0 && body.DebugMovementModifierCount == 0
            && body.DebugStateVelocityCount == 0 && body.DebugDynamicForceCount == 0, "离地撤销来源和环境力");
        Check(rigidbody.velocity == Vector2.right * 4f, "撤销环境来源不抹除原生惯性");
        body.RefreshSupport(ground);
        Check(body.DebugEnvironmentSourceCount == 1, "重新进入恢复原来源");
        Disable(ice);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && body.DebugDynamicForceCount == 0, "源端禁用立即解除来源和施力");
        body.RefreshSupport(ground);
        Near(body.Frame.slowingMultiplier, 1f, "禁用组件仍有 Collider 时不读取表面效果");
        Check(body.EnvironmentContext.DebugSourceCount == 0, "禁用来源不能被支撑查询重新登记");
        ice.enabled = true;
        body.RefreshSupport(ground);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "原地重新启用冰面恢复登记");
        Disable(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "实体禁用统一解除所有来源");
        body.enabled = true;
        body.RefreshSupport(ground);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "实体原地启用恢复地面作用");
        ice.EnvironmentRegistration.ReleaseAll();
        ice.EnvironmentRegistration.ReleaseAll();
        body.EnvironmentContext.ReleaseAll();
        Check(body.EnvironmentContext.DebugSourceCount == 0, "双方重复清理幂等");
    }

    private static void CheckMotionParticipation()
    {
        EnvironmentCheckBody body = Body(NextPosition());
        Rigidbody2D rigidbody = body.GetComponent<Rigidbody2D>();
        rigidbody.bodyType = RigidbodyType2D.Dynamic;
        rigidbody.gravityScale = 0f;
        rigidbody.freezeRotation = true;
        var source = new EnvironmentCheckSource();
        body.AddSpeedStatus(source.EnvironmentRegistration, new Vector2(1f, 2f));
        rigidbody.velocity = new Vector2(3f, -2f);
        SimulationMode2D previousSimulation = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        try
        {
            body.SetMotionParticipation(false);
            Vector2 position = rigidbody.position;
            Check(!body.IsMotionParticipating && !rigidbody.simulated, "普通实体暂停关闭原生模拟");
            for (int i = 0; i < 12; i++) Physics2D.Simulate(Time.fixedDeltaTime);
            Check(rigidbody.position == position, "暂停连续物理步保持现场坐标");
            Check(body.DebugStateVelocityCount == 1, "暂停保留环境来源");
            body.SetMotionParticipation(true);
            Check(body.IsMotionParticipating && rigidbody.simulated, "恢复重新参加统一物理模拟");
            Physics2D.Simulate(Time.fixedDeltaTime);
            Check((rigidbody.position - position).magnitude < .1f, "恢复没有额外旧位移或速度突增");
            body.SetMotionParticipation(false);
            position = rigidbody.position;
            for (int i = 0; i < 12; i++) Physics2D.Simulate(Time.fixedDeltaTime);
            Check(rigidbody.position == position, "已有速度的实体暂停后仍固定位置");
            body.SetMotionParticipation(true);
            body.RefreshMotionState(position);
            Check(rigidbody.velocity == Vector2.zero, "显式运动刷新清实际刚体速度");
            Check(body.DebugStateVelocityCount == 1, "显式运动刷新保留未要求撤销的环境来源");
            body.GetComponent<BoxCollider2D>().enabled = false;
            InvokePhysics(body, "SpeedCalculation");
            Check(!body.IsMotionParticipating && !rigidbody.simulated, "关闭碰撞器后退出原生运动");
            body.GetComponent<BoxCollider2D>().enabled = true;
            body.SetMotionParticipation(true);
        }
        finally { Physics2D.simulationMode = previousSimulation; body.SetMotionParticipation(false); }

        Player player = PlayerActor("player20.prefab");
        CharacterPhysics character = player.GetComponent<CharacterPhysics>();
        player.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        character.SetMotionParticipation(true);
        Check(!character.IsMotionParticipating && !character.GetComponent<Rigidbody2D>().simulated,
            "实体参与入口不能越过玩家既有运动许可");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        Check(character.IsMotionParticipating && character.GetComponent<Rigidbody2D>().simulated,
            "原玩家权限入口可以恢复统一运动");
    }

    private static void CheckMotionInitialization()
    {
        Player player = Prefab<Player>("player20.prefab", NextPosition());
        CharacterPhysics character = player.GetComponent<CharacterPhysics>();
        typeof(CharacterPhysics).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(character, null);
        Rigidbody2D rigidbody = character.GetComponent<Rigidbody2D>();
        Check(!character.IsMotionReady && character.IsMotionPermitted, "动作未注入表示未就绪而非规则禁止运动");
        Check(!character.IsMotionParticipating && !rigidbody.simulated, "未就绪时暂停自研与原生运动");
        InvokePhysics(character, "SpeedCalculation");
        InvokePhysics(character, "DisplacementCorrection");
        Near(character.ReadOnly_PlayersPhysicsData.verticalSpeed, 0f, "未注入动作的相位不积分重力或读取动作");
        character.SetMotionParticipation(false);
        var action = new MovementData { motionUpdates = false };
        character.Init(new ReadOnly_ActionData(action), new LocalEventSystem<PlayerStateMachine.E_playEvent>());
        Check(character.IsMotionReady && !character.IsMotionPermitted && !rigidbody.simulated,
            "动作注入完成就绪但不越过此前暂停或动作许可");
        character.SetMotionParticipation(true);
        Check(!character.IsMotionPermitted && !character.IsMotionParticipating,
            "组装后参与开关仍不能越过角色规则许可");
        action.motionUpdates = true;
        character.SetMotionParticipation(true);
        Check(character.IsMotionReady && character.IsMotionPermitted && character.IsMotionParticipating && rigidbody.simulated,
            "就绪与许可同时满足才开启双端运动");
        character.SetMotionParticipation(false);
    }

    private static void InvokePhysics(object target, string method)
    {
        Type owner = target is BasicEntity ? typeof(BasicEntity) : target.GetType();
        owner.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void CheckRegions()
    {
        Vector3 position = NextPosition();
        ForceField field = Prefab<ForceField>("F1.prefab", position);
        BoxCollider2D source = field.GetComponent<BoxCollider2D>();
        source.isTrigger = true;
        source.size = new Vector2(10, 10);
        EnvironmentCheckBody body = Body(position);
        Collider2D first = body.GetComponent<Collider2D>();
        GameObject child = NewObject("child collider", position + Vector3.right);
        child.transform.SetParent(body.transform, true);
        Collider2D second = child.AddComponent<BoxCollider2D>();
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).dynamicForceCount == 1,
            "复合碰撞体区域接入只登记一次");
        Check(EnvironmentCapabilities.FindEntity(second) == body, "子 Collider 仍正确识别动态实体身份");
        first.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && body.DebugDynamicForceCount == 0,
            "主碰撞体禁用后解除区域作用");
        first.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "Collider 重启后恢复区域作用");

        Collider2D source2 = field.gameObject.AddComponent<BoxCollider2D>();
        source2.isTrigger = true;
        source.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "多个源 Collider 取并集");
        source2.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "全部源 Collider 禁用后解除区域作用");
        source.enabled = true;
        Refresh(body);
        Disable(field);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && body.DebugDynamicForceCount == 0,
            "力场禁用同时停止动态施力（旧遗漏修复）");
        field.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "力场原地启用无需新的 Enter 回调");
        Disable(body);
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "区域查询排除禁用接收者");
        body.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "实体原地启用恢复区域作用");

        Physics2D.IgnoreCollision(source, first, true);
        try
        {
            Refresh(body);
            Check(body.EnvironmentContext.DebugSourceCount == 0, "区域查询尊重 Collider 忽略关系");
        }
        finally { Physics2D.IgnoreCollision(source, first, false); }
        Refresh(body);
        ForceField overlapping = Prefab<ForceField>("Pond.prefab", position);
        overlapping.GetComponent<Collider2D>().isTrigger = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 2, "Pond 实际 ForceField 组件与另一环境独立叠加");
        Disable(field);
        Check(body.EnvironmentContext.DebugSourceCount == 1 && body.DebugDynamicForceCount == 1,
            "退出一个环境不撤销另一个环境");
        Object.DestroyImmediate(overlapping.gameObject);
        body.RefreshSupport(null);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 0,
            "来源销毁后的无效绑定被实体清理");
        Check(body.DebugDynamicForceCount == 0, "来源销毁后撤销原生运动的环境施力");
    }

    private static void CheckCapabilities()
    {
        Vector3 position = NextPosition();
        EnvironmentCheckBody body = Body(position);
        GameObject surface = NewObject("capabilities only", position);
        Collider2D collider = surface.AddComponent<BoxCollider2D>();
        body.RefreshSupport(collider);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "普通无脚本地面无需效果登记");
        Near(body.Frame.slowingMultiplier, 1, "普通地面默认阻力倍率");
        Near(body.Frame.jumpHeightOffset, 0, "普通地面默认跳跃加成");
        EnvironmentCheckSurface platform = surface.AddComponent<EnvironmentCheckSurface>();
        body.RefreshSupport(collider);
        Check(body.Frame.platformDelta == platform.Delta && body.EnvironmentContext.DebugSourceCount == 1,
            $"单一环境宿主提供平台能力并建立一份来源关系 [{body.Frame.platformDelta} / {platform.Delta}, sources={body.EnvironmentContext.DebugSourceCount}]");
        platform.enabled = false;
        body.RefreshSupport(collider);
        Check(body.Frame.platformDelta == Vector2.zero, "禁用平台能力不再提供位移");
        body.RefreshSupport(null);
        Check(body.Frame.platformDelta == Vector2.zero, "离地清除平台采样");

        platform.enabled = true;
        body.RefreshSupport(collider);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 0 &&
            PhysicsEditorEntityObservation.GetSnapshot(body).dynamicForceCount == 0, "同一宿主可单独提供持续速度能力");
        body.envImpact = .25f;
        body.RunForces();
        Check(body.PassiveVelocity == platform.StateVelocity,
            $"同一环境宿主提供的持续速度不乘质量影响系数 [{body.PassiveVelocity} / {platform.StateVelocity}]");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(collider), platform),
            "同一环境宿主可以提供墙滑能力");
        var function = new PhysicalFunctionData { canLeftWall = platform, canRightWall = platform };
        var read = new ReadOnly_GeometryPhysicsData(new GeometryPhysicsData(), function);
        Check(read.canLeftWall && read.canRightWall, "逻辑层读取左右墙滑能力布尔事实");
        platform.enabled = false;
        Check(!read.canLeftWall && !read.canRightWall, "左右墙滑接口引用能识别 Unity 组件禁用");
        Object.DestroyImmediate(platform);
        Check(!read.canLeftWall && !read.canRightWall, "左右墙滑接口引用能识别 Unity 对象销毁");
    }

    private static void CheckCapabilityOwnership()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Near(ice.MovementSpeedOffset, -.5f, "冰面预制体保留移速修饰");
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        body.RefreshSupport(ice.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1,
            "冰面声明的移速和持续速度仍会登记");

        IceGround iceBelt = Prefab<IceGround>("ICE 1.prefab", position);
        Check(iceBelt.StateVelocity == new Vector2(3, 0), "ICE 1 预制体保留持续速度");
        body.RefreshSupport(iceBelt.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1, "ICE 1 持续速度仍会登记");

        ForceField pond = Prefab<ForceField>("Pond.prefab", position);
        Near(pond.MovementSpeedOffset, -.4f, "Pond 预制体保留移速修饰");
        pond.GetComponent<Collider2D>().isTrigger = true;
        EnvironmentCheckBody swimmer = Body(position);
        Refresh(swimmer);
        Check(PhysicsEditorEntityObservation.GetSnapshot(swimmer).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(swimmer).stateVelocityCount == 0,
            "力场登记移速修饰且不再隐式登记持续速度");

        Taijie platform = Prefab<Taijie>("Taijie.prefab", NextPosition());
        Near(platform.MovementSpeedOffset, 1f, "台阶预制体保留移速修饰");
        EnvironmentCheckBody rider = Body(platform.transform.position + Vector3.up);
        rider.RefreshSupport(platform.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(rider).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(rider).stateVelocityCount == 0,
            "台阶登记移速修饰且不登记持续速度");

        Vector3 wallPosition = NextPosition();
        Wall wall = Prefab<Wall>("wall.prefab", wallPosition);
        EnvironmentCheckBody onWall = Body(wallPosition + Vector3.up);
        onWall.RefreshSupport(wall.GetComponent<Collider2D>());
        Check(onWall.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(onWall).movementModifierCount == 0
            && PhysicsEditorEntityObservation.GetSnapshot(onWall).stateVelocityCount == 0, "纯墙不建立绑定，也不登记移速或持续速度");
        Near(onWall.Frame.slowingMultiplier, 1f, "纯墙无地面响应时脚下采样阻力为默认");
        Near(onWall.Frame.jumpHeightOffset, 0f, "纯墙无地面响应时脚下采样起跳为默认");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(wall.GetComponent<Collider2D>()), wall),
            "无绑定时墙滑能力仍可按碰撞体查询");
    }

    // 同一组件同时声明墙滑、移速和地面响应。脚下只登记持续效果并采样地面；侧面只取墙滑；区域只登记移速。
    private static void CheckContactRoles()
    {
        Vector3 position = NextPosition();
        GameObject host = NewObject("contact roles", position);
        BoxCollider2D collider = host.AddComponent<BoxCollider2D>();
        EnvironmentCheckContact contact = host.AddComponent<EnvironmentCheckContact>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);

        body.RefreshSupport(collider);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1 && body.EnvironmentContext.DebugSourceCount == 1,
            "脚下登记同宿主的移速修饰");
        Near(body.Frame.slowingMultiplier, contact.SlowingEffect, "脚下采样同宿主的地面阻力");
        Near(body.Frame.jumpHeightOffset, contact.JumpHeightNum, "脚下采样同宿主的起跳加成");

        body.RefreshSupport(null);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 0,
            "只查询侧面墙滑时不登记移速");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(collider), contact),
            "侧面命中可解析墙滑能力");

        collider.isTrigger = true;
        body.transform.position = position;
        Refresh(body);
        var speeds = (Dictionary<EnvironmentRegistration, float>)EffectField(body, "movementModifiers");
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1
            && speeds.TryGetValue(contact.EnvironmentRegistration, out float offset)
            && Mathf.Abs(offset - contact.MovementSpeedOffset) < .0001f,
            "区域登记移速修饰，不登记墙滑乘数");
        Near(body.Frame.slowingMultiplier, 1f, "区域不采样脚下阻力");
        Near(body.Frame.jumpHeightOffset, 0f, "区域不采样起跳加成");
    }

    // 触发体与实心表面同时重叠探测盒时，接触查询必须返回实心体。区域重叠规则不在这里改变。
    private static void CheckTriggerContactFilter()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.boxCastH = new Vector2(.3f, .12f);
        config.boxCastV = new Vector2(.2f, .4f);
        try
        {
            Collider2D ground = Solid(origin + new Vector3(0, -.5f, 0), Vector2.one);
            TriggerVeil(origin + new Vector3(0, .25f, 0), new Vector2(3f, 1.5f));
            Collider2D wall = Solid(origin + new Vector3(29.5f, 0, 0), new Vector2(1f, 2f));
            TriggerVeil(origin + new Vector3(30.2f, 0, 0), new Vector2(2f, 3f));
            Collider2D ceiling = Solid(origin + new Vector3(60, .5f, 0), Vector2.one);
            TriggerVeil(origin + new Vector3(60, -.25f, 0), new Vector2(3f, 1.5f));
            Transform groundProbe = NewObject("ground probe", origin + new Vector3(0, .04f, 0)).transform;
            Transform leftProbe = NewObject("left probe", origin + new Vector3(30.05f, 0, 0)).transform;
            Transform rightProbe = NewObject("right probe", origin + new Vector3(90, 0, 0)).transform;
            Transform upProbe = NewObject("up probe", origin + new Vector3(60, -.04f, 0)).transform;
            Physics2D.SyncTransforms();

            EnvironmentCheckCharacter character = ProbeCharacter(origin + new Vector3(0, 20, 0), config,
                groundProbe, leftProbe, rightProbe);
            character.Query();
            Check(character.Ground == ground && character.Grounded, "角色脚下过滤 Trigger 后命中实心表面");
            Check(character.Left == wall && character.OnLeft, "角色侧面过滤 Trigger 后命中实心墙");

            EnvironmentCheckBox box = ProbeBox(origin + new Vector3(0, 40, 0), config,
                groundProbe, leftProbe, rightProbe, upProbe);
            box.Query();
            Check(box.Ground == ground && box.Grounded, "箱体脚下过滤 Trigger 后命中实心表面");
            Check(box.Left == wall && box.OnLeft, "箱体侧面过滤 Trigger 后命中实心墙");
            Check(box.Top, "箱体顶头过滤 Trigger 后仍发现实心表面");
            // 墙与地面同层、同时落入脚下探针：首个侧面命中不能抹掉支撑。
            Vector3 corner = origin + Vector3.right * 120f;
            Solid(corner + new Vector3(.22f, .5f, 0), new Vector2(.2f, 1f));
            Collider2D cornerFloor = Solid(corner + new Vector3(0, -.5f, 0), Vector2.one);
            Transform cornerProbe = NewObject("corner ground probe", corner + new Vector3(0, .04f, 0)).transform;
            Physics2D.SyncTransforms();
            character.Apply(config, cornerProbe, leftProbe, rightProbe);
            character.Query();
            Check(character.Ground == cornerFloor && character.Grounded, "墙角角色保留有效地面");
            box.Apply(config, cornerProbe, leftProbe, rightProbe, upProbe);
            box.Query();
            Check(box.Ground == cornerFloor && box.Grounded, "墙角箱体保留有效地面");
            cornerProbe.position += Vector3.up * .035f;
            Physics2D.SyncTransforms();
            character.Query();
            box.Query();
            Check(character.Ground == cornerFloor && box.Ground == cornerFloor,
                "墙角微小离地间隙仍保持支撑");
            ceiling.gameObject.SetActive(false);
            Physics2D.SyncTransforms();
            box.Query();
            Check(!box.Top, "箱体顶头只有 Trigger 时不记为接触");
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    // F_6.2_6-6：箱体前缘贴坡而中心脚探针悬空时，着地事实须与完整箱体的坡面接触一致。
    private static void CheckSlopeFootprintSupport()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.boxCastH = new Vector2(.75f, .1f);
        config.boxCastV = new Vector2(.02f, 1.6f);
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            float halfWidth = .76f;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * halfWidth + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            Transform foot = NewObject("slope foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("slope side", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, config, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.y < .99f,
                "箱体前缘贴坡时仍得到真实坡面支撑法线");
            for (int step = 0; step < 6; step++)
            {
                Vector3 alongSlope = new Vector3(.1f, Mathf.Tan(slope) * .1f, 0f);
                box.transform.position += alongSlope;
                foot.position += alongSlope;
                Physics2D.SyncTransforms();
                box.Query();
                Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.y < .99f,
                    $"箱体沿坡第 {step + 1} 段持续保留真实支撑");
            }
        }
        finally { Object.DestroyImmediate(config); }
    }

    // F_6.2_6-8：左上坡前缘碰到冰面右侧时，脚下不能把冰面的侧面当成顶面支撑。
    private static void CheckSlopeSideSupport()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.maxClimbAngle = 45f;
        config.boxCastH = new Vector2(.75f, .1f);
        config.boxCastV = new Vector2(.02f, 1.6f);
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * .76f + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, -30f);
            Collider2D iceSide = Solid(origin + new Vector3(-2.758f, .165f, 0f),
                new Vector2(4f, .37f));
            Transform foot = NewObject("ice side foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("ice side probe", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, config, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.x > .2f,
                $"冰面侧面接触不替换坡面支撑 [{box.Ground?.name} / {iceSide.name} / {box.DebugGroundNormal}]");
        }
        finally { Object.DestroyImmediate(config); }
    }

    // F_6.2_7-5：冰面侧挡裁掉箱体位移后，接触对须把推动者退到不重叠处，且不登记无效施力。

    // F_6.2_6-7：沿坡的基础 Y 若撞上坡顶平台底面，不能仍把该段上升交给引擎反复挤回。

    // 调用真实贴墙状态和角色限速，不使用把 VerticalTransmission 盖空的接触夹具。
    private static void CheckWallSlideBehavior()
    {
        Vector3 position = NextPosition();
        EnvironmentCheckSlide left = Slide(position, .5f);
        EnvironmentCheckSlide right = Slide(position + Vector3.right * 3f, 2f);
        var geometry = new GeometryPhysicsData();
        var function = new PhysicalFunctionData { canLeftWall = left, canRightWall = right };
        var read = new ReadOnly_GeometryPhysicsData(geometry, function);
        var input = new PlayerInputData();
        var action = new MovementData();
        var fsm = new PlayerStateMachine();
        fsm.InitData(read, input, action);
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);

        input.moveInput = 0f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.inAir && action.onMove == 0f,
            "松手退出贴墙并写回零方向");

        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        function.canRightWall = null;
        input.moveInput = 1f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.inAir, "朝向没有墙滑能力的一侧则退出");

        function.canRightWall = right;
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = 1f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.onWallSliding && action.onMove == 1f,
            "朝向有墙滑能力的一侧保持贴墙");

        EnvironmentCheckSlider slider = NewObject("wall slide body", position + Vector3.up * 5f).AddComponent<EnvironmentCheckSlider>();
        slider.Bind(new ReadOnly_ActionData(action), function, fsm.EventSystem);
        action.nowState = PlayerStateMachine.E_playerState.onWallSliding;
        action.onMove = -1f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -1f, "朝左按左墙乘数限速");
        action.onMove = 1f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -4f, "朝右按右墙乘数限速");
        right.enabled = false;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -10f, "朝向一侧能力失效时不限速");
        right.enabled = true;
        action.onMove = 0f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -10f, "松手不选择墙滑乘数");

        slider.TouchBothWalls();
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = 1f;
        input.jumpPressed = true;
        fsm.Update(input);
        slider.ConsumeEvents();
        Near(slider.LatestImpulse(), -30f, "双侧接触朝右墙跳时冲量向左");
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = -1f;
        input.jumpPressed = true;
        fsm.Update(input);
        slider.ConsumeEvents();
        Near(slider.LatestImpulse(), 30f, "双侧接触朝左墙跳时冲量向右");
    }

    private static EnvironmentCheckSlide Slide(Vector3 position, float multiplier)
    {
        EnvironmentCheckSlide slide = NewObject("slide side", position).AddComponent<EnvironmentCheckSlide>();
        slide.WallSlideMultiplier = multiplier;
        return slide;
    }

    private static Collider2D Solid(Vector3 position, Vector2 size)
    {
        GameObject item = NewObject("solid contact", position);
        item.layer = 6;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
        collider.size = size;
        return collider;
    }

    private static void TriggerVeil(Vector3 position, Vector2 size)
    {
        GameObject item = NewObject("trigger veil", position);
        item.layer = 6;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = true;
    }

    private static EnvironmentCheckCharacter ProbeCharacter(Vector3 position, SO_CPhysics config,
        Transform ground, Transform left, Transform right)
    {
        GameObject item = NewObject("character probe", position);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        EnvironmentCheckCharacter probe = item.AddComponent<EnvironmentCheckCharacter>();
        probe.Apply(config, ground, left, right);
        return probe;
    }

    private static EnvironmentCheckBox ProbeBox(Vector3 position, SO_CPhysics config,
        Transform ground, Transform left, Transform right, Transform up)
    {
        GameObject item = NewObject("box probe", position);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        EnvironmentCheckBox probe = item.AddComponent<EnvironmentCheckBox>();
        probe.Apply(config, ground, left, right, up);
        return probe;
    }

    private static void CheckUniqueEnvironmentHost()
    {
        GameObject host = NewObject("unique environment host", NextPosition());
        host.SetActive(false);
        IceGround ice = host.AddComponent<IceGround>();
        ice.enabled = false;
        Debug.Log("Unique environment host check: the next two AddComponent errors are expected rejections.");
        Check(host.AddComponent<IceGround>() == null, "同类环境入口重复挂载被 Unity 拒绝");
        Check(host.AddComponent<ForceField>() == null, "不同子类不能绕过同一环境入口约束");
        // 验证实际配置结果；原生拒绝消息在 batch 日志中，不依赖托管日志回调计数。
        var remaining = host.GetComponents<BasicPhysicalObject>();
        Check(remaining.Length == 1 && remaining[0] == ice && !ice.enabled,
            "禁用入口仍占身份，重复请求不新增或替换原组件");

        BoxCollider2D collider = host.AddComponent<BoxCollider2D>();
        Check(EnvironmentCapabilities.FindHost(collider) == null, "层级失活对象不提供环境来源");
        host.SetActive(true);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "禁用的唯一宿主不提供环境来源");
        ice.enabled = true;
        Check(ReferenceEquals(EnvironmentCapabilities.FindHost(collider), ice), "启用宿主通过来源接口被发现");
        collider.enabled = false;
        Check(EnvironmentCapabilities.FindHost(collider) == null, "禁用 Collider 不提供环境来源");
        collider.enabled = true;
        host.SetActive(false);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "Collider 所属物体失活时不提供环境来源");
        host.SetActive(true);
        Check(ReferenceEquals(EnvironmentCapabilities.FindHost(collider), ice), "重新启用后恢复同一来源身份");
        UnityEngine.Object.DestroyImmediate(ice);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "销毁唯一宿主后不提供环境来源");
    }

    // 注入已经采集的几何事实，专门核对关系连续性；实际 overlap 查询由 CheckRegions 覆盖。
    private static void CheckUnifiedSources()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Collider2D collider = ice.GetComponent<Collider2D>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        EntityEnvironmentContext context = body.EnvironmentContext;
        var hits = new List<Collider2D> { collider, collider };
        var bindings = (System.Collections.IDictionary)typeof(EntityEnvironmentContext)
            .GetField("bindings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
        context.RefreshEnvironment(collider, null);
        object binding = bindings[ice.EnvironmentRegistration];
        Rigidbody2D rigidbody = body.GetComponent<Rigidbody2D>();
        rigidbody.velocity = Vector2.right * 4f;
        context.RefreshEnvironment(null, hits);
        Check(ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]), "脚下转区域保留原绑定，不中途退出再进入");
        Check(rigidbody.velocity == Vector2.right * 4f, "接入方式转换保留原生惯性");
        Near(body.Frame.slowingMultiplier, 1, "仅区域命中不采样脚下阻力");
        context.RefreshEnvironment(collider, hits);
        Check(context.DebugSourceCount == 1 && ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]),
            "脚下与重复区域事实同时命中仍只有原绑定");
        context.RefreshEnvironment(collider, null);
        Check(ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]), "区域消失但脚下仍在时保持绑定");
        context.RefreshEnvironment(null, null);
        Check(context.DebugSourceCount == 0 && body.DebugDynamicForceCount == 0,
            "完整事实消失才解除原来源和环境力");
        Check(rigidbody.velocity == Vector2.right * 4f, "完整退出环境不清原生惯性");
        context.RefreshEnvironment(collider, hits);
        context.ReleaseAll();
        var sources = (HashSet<EnvironmentRegistration>)typeof(EntityEnvironmentContext)
            .GetField("currentSources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
        Check(context.DebugSourceCount == 0 && sources.Count == 0 && body.Frame.platformDelta == Vector2.zero,
            "实体释放时清空绑定、事实缓存与采样结果");
    }

    private static void CheckSourceValidity()
    {
        var plain = new EnvironmentCheckSource();
        Check(plain.EnvironmentRegistration.IsActive, "纯 C# 来源可提供注册表有效性");
        plain.IsEnvironmentActive = false;
        Check(!plain.EnvironmentRegistration.IsActive, "注册表跟随来源接口的有效性变化");
        BasicPhysicalObject host = NewObject("source validity", NextPosition()).AddComponent<BasicPhysicalObject>();
        EnvironmentRegistration registration = host.EnvironmentRegistration;
        Check(registration.IsActive, "Unity 宿主启用时来源有效");
        host.enabled = false;
        Check(!registration.IsActive, "Unity 宿主禁用时来源无效");
        host.enabled = true;
        host.gameObject.SetActive(false);
        Check(!registration.IsActive, "Unity 宿主层级失活时来源无效");
        host.gameObject.SetActive(true);
        Check(registration.IsActive, "Unity 宿主重新激活恢复来源有效性");
        Object.DestroyImmediate(host);
        Check(!registration.IsActive, "接口持有已销毁宿主时安全返回无效");
    }

    private static void CheckPlatformDisplacement()
    {
        Vector3 target = NextPosition();
        Taijie platform = Prefab<Taijie>("Taijie.prefab", target);
        platform.transform.position = target + Vector3.up * 5f;
        platform.ToMovePoint(target);
        Vector2 expected = target - platform.transform.position;
        // 固定为已到达终点的时刻，避免真实等待造成测试不稳定。
        typeof(Taijie).GetField("nowtime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(platform, AudioSettings.dspTime - 10d);
        MethodInfo tick = typeof(Taijie).GetMethod("FixFun", BindingFlags.Instance | BindingFlags.NonPublic);
        tick.Invoke(platform, null);
        Check(platform.Delta == expected, "实际平台记录到达终点的本帧位移");
        EnvironmentCheckBody rider = Body(target + Vector3.up * 3);
        rider.RefreshSupport(platform.GetComponent<Collider2D>());
        Check(rider.Frame.platformDelta == expected, "实际平台位移经接口进入脚下采样");
        tick.Invoke(platform, null);
        Check(platform.Delta == Vector2.zero, "平台停止后下一帧位移清零");
        platform.ToMovePoint(target);
        typeof(Taijie).GetField("nowtime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(platform, AudioSettings.dspTime - 10d);
        tick.Invoke(platform, null);
        Disable(platform);
        Check(platform.Delta == Vector2.zero && rider.EnvironmentContext.DebugSourceCount == 0,
            "平台禁用时位移清零并立即解除骑手绑定");
    }

    private static Vector3 NextPosition() => new Vector3(10000 + 100 * group++, 10000, 0);
    private static GameObject NewObject(string name, Vector3 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position;
        objects.Add(item);
        return item;
    }
    private static T Prefab<T>(string name, Vector3 position) where T : Component
    {
        string folder = name == "player20.prefab" || name == "SpiritPlayer.prefab" ? "Player/" : "SceneObj/";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/" + folder + name);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/" + name);
        if (asset == null) throw new InvalidOperationException("Missing fixture prefab: " + name);
        GameObject item = Object.Instantiate(asset);
        item.hideFlags = HideFlags.DontSave;
        item.transform.position = position;
        objects.Add(item);
        return item.GetComponent<T>();
    }
    private static EnvironmentCheckBody Body(Vector3 position)
    {
        GameObject item = NewObject("environment receiver", position);
        // 原生执行已把 Kinematic 解释为锁定实体；速度能力夹具应像正常实体使用 Dynamic。
        Rigidbody2D rigidbody = item.AddComponent<Rigidbody2D>();
        rigidbody.bodyType = RigidbodyType2D.Dynamic;
        rigidbody.gravityScale = 0f;
        item.AddComponent<BoxCollider2D>();
        var body = item.AddComponent<EnvironmentCheckBody>();
        body.InitializeForChecks();
        return body;
    }
    private static void Refresh(EnvironmentCheckBody body)
    {
        Physics2D.SyncTransforms();
        body.RefreshRegions();
    }
    private static void Disable(Behaviour component)
    {
        component.enabled = false;
        // Edit Mode 不依赖 Unity 自动派发普通 MonoBehaviour 消息；显式验证真实的框架清理入口。
        // 若 Unity 已派发，本次重复调用同时验证幂等性。
        component.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < .0001f, message + $" [{actual} == {expected}]");
}

// 验证夹具：计算和环境清理使用实际 BasicEntity；仅跳过场景探针配置与公共时序注册。
public sealed class EnvironmentCheckBody : BasicEntity, ICanMove
{
    public float Direction = 1;
    public float MovingDirection => Direction;
    public float Mobility => 15;
    public EntityEnvironmentFrame Frame => EnvironmentFrame;
    public Vector2 PassiveVelocity => new Vector2(playerPhysicsData.phyHSpeed, playerPhysicsData.phyVSpeed);
    protected override void Awake() => InitializeForChecks();
    protected override void OnEnable() { }
    protected override void GeometricQuery() { }
    public void InitializeForChecks()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        nowGemetry = new GeometryPhysicsData();
        playerPhysicsData = new PlayerPhysicsData();
    }
    public void RefreshSupport(Collider2D collider)
    {
        nowGemetry.groundCollider = collider;
        nowGemetry.isGrounded = collider != null;
        self_resistanceCoefficient = collider != null ? 20 : 1;
        CollectRegionOverlapFacts();
        base.PhyFunUpdate();
    }

    public void RefreshRegions()
    {
        CollectRegionOverlapFacts();
        base.PhyFunUpdate();
    }
    public void RunForces() => typeof(BasicEntity).GetMethod("SpeedCalculation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, null);

}

public sealed class EnvironmentCheckContact : BasicPhysicalObject, IMovementSpeedModifier, IWallSlideSurface, IGroundResponse
{
    public float MovementSpeedOffset => -.25f;
    public float WallSlideMultiplier => .4f;
    public float SlowingEffect => 2f;
    public float JumpHeightNum => .3f;
}

public sealed class EnvironmentCheckCharacter : BasePhysicsEntity
{
    protected override void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        Init();
    }
    protected override void OnEnable() { }
    protected override float HorizontalSpeedCalculation() => 0f;
    protected override void PhyEventUpdate() { }
    protected override void VerticalTransmission() { }
    public void Apply(SO_CPhysics config, Transform ground, Transform left, Transform right)
    {
        // Edit Mode 添加组件时 Awake 可能尚未执行；查询前补齐几何容器。
        if (nowGemetry == null)
        {
            rb = GetComponent<Rigidbody2D>();
            boxCollider = GetComponent<BoxCollider2D>();
            playerPhysicsData = new PlayerPhysicsData();
            nowGemetry = new GeometryPhysicsData();
            Init();
        }
        cPhysics = config;
        groundV = ground;
        leftV = left;
        rightV = right;
    }
    public void Query() => GeometricQuery();
    public Collider2D Ground => nowGemetry.groundCollider;
    public bool Grounded => nowGemetry.isGrounded;
    public Collider2D Left => nowGemetry.leftWallCollider;
    public bool OnLeft => nowGemetry.onLeftWall;

}

public sealed class EnvironmentCheckBox : PhysicalBox
{
    protected override void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
    }
    protected override void OnEnable() { }
    public void Apply(SO_CPhysics config, Transform ground, Transform left, Transform right, Transform up)
    {
        // Edit Mode 添加组件时 Awake 可能尚未执行；查询前补齐几何容器。
        if (nowGemetry == null)
        {
            BindEffectReceiver();
            rb = GetComponent<Rigidbody2D>();
            boxCollider = GetComponent<BoxCollider2D>();
            playerPhysicsData = new PlayerPhysicsData();
            nowGemetry = new GeometryPhysicsData();
        }
        cPhysics = config;
        groundV = ground;
        leftV = left;
        rightV = right;
        upV = up;
    }
    public void Query() => GeometricQuery();
    public Collider2D Ground => nowGemetry.groundCollider;
    public bool Grounded => nowGemetry.isGrounded;
    public Collider2D Left => nowGemetry.leftWallCollider;
    public bool OnLeft => nowGemetry.onLeftWall;
    public bool Top => nowGemetry.istop;

}

public sealed class EnvironmentCheckSlide : BasicPhysicalObject, IWallSlideSurface
{
    public float WallSlideMultiplier { get; set; }
}

public sealed class EnvironmentCheckSlider : CharacterPhysics
{
    protected override void Awake() => EnsureReady();
    protected override void OnEnable() { }
    public void Bind(ReadOnly_ActionData action, PhysicalFunctionData function, LocalEventSystem<PlayerStateMachine.E_playEvent> events)
    {
        EnsureReady();
        playphyFunData = function;
        Init(action, events);
    }
    public void FallAt(float vertical) => playerPhysicsData.verticalSpeed = vertical;
    public void Limit() => VerticalTransmission();
    public void ConsumeEvents() => PhyEventUpdate();
    public float Vertical => playerPhysicsData.verticalSpeed;
    public void TouchBothWalls()
    {
        nowGemetry.onLeftWall = true;
        nowGemetry.onRightWall = true;
    }
    public float LatestImpulse()
    {
        var forces = (List<SpeedStackData>)EnvironmentIntegrationChecks.EffectField(this, "timedSpeeds");
        return forces[forces.Count - 1].hspeed;
    }
    void EnsureReady()
    {
        BindEffectReceiver();
        if (playerPhysicsData != null) return;
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        Init();
    }
}

public sealed class EnvironmentCheckSurface : BasicPhysicalObject, IPlatformMotion, IWallSlideSurface, IStateVelocitySource
{
    [SerializeField] Vector2 phySpeed = new Vector2(3, 2);
    public Vector2 StateVelocity => phySpeed;
    public Vector2 Delta => new Vector2(.3f, -.2f);
    public float WallSlideMultiplier => .5f;
}
// 无 Unity 宿主的来源夹具，只验证注册表的来源协议边界。
public sealed class EnvironmentCheckSource : IEnvironmentSource
{
    public bool IsEnvironmentActive { get; set; } = true;
    public EnvironmentRegistration EnvironmentRegistration { get; }
    public EnvironmentCheckSource() => EnvironmentRegistration = new EnvironmentRegistration(this);
}
