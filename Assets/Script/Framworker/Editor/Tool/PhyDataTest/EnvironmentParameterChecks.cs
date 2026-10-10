using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>实际来源与 Context 的配置检查；临时对象不保存场景或预设，不启动播放。</summary>
public static class EnvironmentParameterChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/EPhy 验证环境参数与默认恢复（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
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
                var item = new GameObject("K7 temporary scheduler") { hideFlags = HideFlags.DontSave };
                objects.Add(item); scheduler.SetValue(null, item.AddComponent<MonoPublicMgr>());
            }
            CheckLegacyAndDefaults();
            CheckExplicitAndInstances();
            CheckConfigurationRejection();
            CheckSamplingAndExistingBinding();
            CheckActualPrefab();
            CheckRoamingRuleGate();
            CheckMultipleSurfaceParameters();
            CheckJumpAndWallConsumption();
            CheckLegacySurfaceCompatibility();
            Debug.Log($"Environment parameters: {passed.Count} checks passed.");
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
            Directory.CreateDirectory(".utmp/EnvironmentParameterValidation");
            File.WriteAllLines(".utmp/EnvironmentParameterValidation/checks.txt", passed);
        }
    }

    private static void CheckLegacyAndDefaults()
    {
        BaseGround ground = Host<BaseGround>("legacy ground");
        SetLegacy(ground, .3f);
        Check(ground.EnvironmentParameterCount == 2, "未迁移地面从旧字段提供消退与普通起跳两项");
        Near(ground.SlowingEffect, .3f, "未编辑时保留旧制作倍率");
        EnvironmentRegistration identity = ground.EnvironmentRegistration;
        Check(ground.TrySetEnvironmentParameter(0, .8f), "来源接受有效改值");
        Near(ground.SlowingEffect, .8f, "生效值从原来源读取");
        ground.TryGetEnvironmentParameter(0, out EnvironmentParameter parameter);
        Near(parameter.DefaultValue, .3f, "编辑不改变制作默认");
        Near((float)typeof(BaseGround).GetField("slowingEffect", Hidden).GetValue(ground), .3f,
            "编辑不回写旧序列化字段");
        int changedVersion = ground.EnvironmentParameterVersion;
        Check(ground.TrySetEnvironmentParameter(0, .8f) && ground.EnvironmentParameterVersion == changedVersion,
            "同值编辑不增加配置版本");
        Check(!ground.TrySetEnvironmentParameter(0, -1f)
            && !ground.TrySetEnvironmentParameter(0, float.NaN)
            && !ground.TrySetEnvironmentParameter(0, float.PositiveInfinity)
            && !ground.TrySetEnvironmentParameter(ground.EnvironmentParameterCount, 2f), "非法值或索引拒绝修改");
        Check(ground.EnvironmentParameterVersion == changedVersion, "拒绝没有版本或部分修改");
        ground.enabled = false;
        Check(!ground.TrySetEnvironmentParameter(0, 2f), "禁用来源拒绝编辑");
        ground.enabled = true;
        Check(ReferenceEquals(identity, ground.EnvironmentRegistration), "启停保持同一来源身份");
        Near(ground.SlowingEffect, .8f, "启停保留生效配置");
        ground.RestoreEnvironmentDefaults();
        Near(ground.SlowingEffect, .3f, "恢复默认使用首次编辑前的制作值");
        int restoredVersion = ground.EnvironmentParameterVersion;
        ground.RestoreEnvironmentDefaults();
        Check(ground.EnvironmentParameterVersion == restoredVersion, "重复默认恢复不增加版本");

        BaseGround untouched = Host<BaseGround>("unread legacy default");
        SetLegacy(untouched, .6f);
        untouched.RestoreEnvironmentDefaults();
        Check(untouched.TrySetEnvironmentParameter(0, 0f), "零倍率是有效参数");
        untouched.RestoreEnvironmentDefaults();
        Near(untouched.SlowingEffect, .6f, "读取或编辑前调用恢复也能捕获原默认");
    }

    private static void CheckExplicitAndInstances()
    {
        BaseGround ground = Host<BaseGround>("explicit source");
        SetLegacy(ground, .3f);
        SetParameters(ground, new GroundSlowingParameter(1.6f));
        Near(ground.SlowingEffect, 1.6f, "显式项优先，旧字段不成为第二份生效真值");
        var clone = Object.Instantiate(ground.gameObject);
        clone.hideFlags = HideFlags.DontSave;
        objects.Add(clone);
        BaseGround second = clone.GetComponent<BaseGround>();
        Check(ground.TrySetEnvironmentParameter(0, 2f), "显式项接受局内覆盖");
        Near(second.SlowingEffect, 1.6f, "实例编辑不改变同配置的另一实例");
        ground.RestoreEnvironmentDefaults();
        Near(ground.SlowingEffect, 1.6f, "显式项恢复自己的制作默认");
        Check(ground.TryGetEnvironmentParameter(0, out EnvironmentParameter parameter)
            && parameter.ContactCondition == "脚下支撑", "触发条件固定，不能改成区域或侧碰");

        var shared = new GroundSlowingParameter(.4f);
        var firstShared = Host<BaseGround>("first shared config");
        var secondShared = Host<BaseGround>("second shared config");
        SetParameters(firstShared, shared);
        SetParameters(secondShared, shared);
        Check(firstShared.TrySetEnvironmentParameter(0, .9f), "共享制作项仍可单独编辑来源");
        Near(secondShared.SlowingEffect, .4f, "两宿主意外共用制作项也不共用运行时值");
        Near(shared.Value, .4f, "制作列表项没有被运行时编辑改变");
    }

    private static void CheckConfigurationRejection()
    {
        var duplicate = Host<BaseGround>("duplicate parameter");
        SetParameters(duplicate, new GroundSlowingParameter(.2f), new GroundSlowingParameter(.8f));
        Check(!duplicate.ValidateEnvironmentParameters(out string error) && error.Contains("重复"),
            "重复项拒绝整份配置并指出错误");
        var invalid = Host<BaseGround>("invalid parameter");
        SetParameters(invalid, new GroundSlowingParameter(float.NegativeInfinity));
        Check(!invalid.ValidateEnvironmentParameters(out _), "非法制作参数拒绝配置");
        var missing = Host<BaseGround>("null parameter");
        SetParameters(missing, new EnvironmentParameter[] { null });
        Check(!missing.ValidateEnvironmentParameters(out _), "空配置项拒绝配置");
    }

    private static void CheckSamplingAndExistingBinding()
    {
        BasicPhysicalObject simple = Host<BasicPhysicalObject>("composed ground");
        SetParameters(simple, new GroundSlowingParameter(.7f));
        Collider2D simpleCollider = simple.gameObject.AddComponent<BoxCollider2D>();
        EnvironmentParameterCheckReceiver receiver = Receiver();
        receiver.EnvironmentContext.RefreshEnvironment(simpleCollider, null);
        Near(receiver.EnvironmentContext.Frame.slowingMultiplier, .7f, "普通唯一宿主的列表接入脚下采样");
        receiver.EnvironmentContext.RefreshEnvironment(null, new List<Collider2D> { simpleCollider });
        Near(receiver.EnvironmentContext.Frame.slowingMultiplier, 1f, "仅区域命中不触发地面参数");
        Check(receiver.EnvironmentContext.DebugSourceCount == 0, "纯采样项不建立虚假持续 Binding");

        IceGround ice = Host<IceGround>("editable ice");
        SetLegacy(ice, .5f);
        Collider2D iceCollider = ice.gameObject.AddComponent<BoxCollider2D>();
        receiver.EnvironmentContext.RefreshEnvironment(iceCollider, null);
        Dictionary<IDynamicAddForce, ForceData> forces = Forces(receiver);
        Near(forces[ice].recoverySpeed, .5f * ice.Odex, "冰面自由箱参数从有效倍率初始化");
        Rigidbody2D rigidbody = receiver.GetComponent<Rigidbody2D>();
        rigidbody.velocity = new Vector2(4f, 2f);
        Check(ice.TrySetEnvironmentParameter(0, .2f), "暂停期间只改变来源参数");
        Near(forces[ice].recoverySpeed, .5f * ice.Odex, "改值瞬间不主动回写接收者参数");
        Check(rigidbody.velocity == new Vector2(4f, 2f), "来源编辑不改变刚体真实动量");
        receiver.EnvironmentContext.RefreshEnvironment(iceCollider, null);
        Near(forces[ice].recoverySpeed, .2f * ice.Odex, "正常相位采样更新仍命中的旧 Binding 参数");
        Check(receiver.EnvironmentContext.DebugSourceCount == 1 && forces.Count == 1,
            "更新保持一份来源关系与动态参数，不要求离开重进");
        Check(rigidbody.velocity == new Vector2(4f, 2f), "Binding 更新不清真实动量");
        ice.RestoreEnvironmentDefaults();
        receiver.EnvironmentContext.RefreshEnvironment(iceCollider, null);
        Near(forces[ice].recoverySpeed, .5f * ice.Odex, "重恢复后的既有 Binding 使用默认参数");
        receiver.EnvironmentContext.RefreshEnvironment(null, null);
        Check(forces.Count == 0 && receiver.EnvironmentContext.DebugSourceCount == 0, "离开沿原关系入口正常撤销");
    }

    private static void CheckActualPrefab()
    {
        const string path = "Assets/MyObjAssets/gameObject/Pf/SceneObj/ICE.prefab";
        byte[] before = File.ReadAllBytes(path);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(prefab != null, "实际冰面预设存在");
        var instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.DontSave;
        objects.Add(instance);
        IceGround ice = instance.GetComponent<IceGround>();
        float configured = (float)typeof(BaseGround).GetField("slowingEffect", Hidden).GetValue(ice);
        Near(ice.SlowingEffect, configured, "实际未迁移预设读取原序列化倍率");
        Check(ice.TrySetEnvironmentParameter(0, configured + .4f), "实际旧预设无需迁移即可编辑");
        ice.RestoreEnvironmentDefaults();
        Near(ice.SlowingEffect, configured, "实际旧预设恢复默认");
        Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)),
            "验证不改实际预设字节");
    }

    private static void CheckRoamingRuleGate()
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
        if (asset == null) throw new InvalidOperationException("Missing K7 fixture: SpiritPlayer.prefab");
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave;
        item.transform.position = new Vector2(35000f, 35000f);
        objects.Add(item); Physics2D.SyncTransforms();
        Player spirit = item.GetComponent<Player>();
        Invoke(spirit.GetComponent<CharacterPhysics>(), "Awake"); Invoke(spirit, "Awake");
        Invoke(spirit.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(spirit, "Start");
        Invoke(spirit.GetComponent<CharacterPhysics>(), "OnEnable");
        spirit.ChangeInputAsset(new PlayerInputData());
        var rules = new PlayerLifecycleRules(null, spirit);
        var ground = Host<BaseGround>("roaming editable ground");
        SetLegacy(ground, .4f);
        Check(rules.BeginExploration() && rules.CanEditEnvironment, "真实探索漫游开放环境编辑资格");
        Check(rules.TryEditEnvironmentParameter(ground, 0, .8f), "漫游通过规则修改本关来源");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "真实切换到模拟行走");
        int version = ground.EnvironmentParameterVersion;
        Check(!rules.CanEditEnvironment && !rules.TryEditEnvironmentParameter(ground, 0, 1f),
            "探索模拟拒绝环境编辑");
        Check(ground.EnvironmentParameterVersion == version && ground.SlowingEffect == .8f,
            "资格拒绝没有生效值或版本改变");
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Check(!rules.CanEditEnvironment, "仅阶段模式标记不冒充精神体真实飞行资格");
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        Check(!rules.CanEditEnvironment && !rules.TryEditEnvironmentParameter(ground, 0, 1f), "逃生拒绝环境编辑");
        rules.ConfigureStage(PlayerGameplayStage.Tutorial);
        Check(!rules.CanEditEnvironment && !rules.TryEditEnvironmentParameter(ground, 0, 1f), "教学拒绝环境编辑");
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Check(spirit.RequestSpiritForm(true, PlayerFormChangeReason.ForcedRegion) && rules.CanEditEnvironment,
            "返回真实漫游才重新开放编辑");
        Check(rules.TryEditEnvironmentParameter(ground, 0, 1.2f), "返回漫游后同一来源可继续编辑");
        Check(!rules.TryEditEnvironmentParameter(null, 0, 1f), "规则拒绝无来源编辑");
        ground.RestoreEnvironmentDefaults();
        Near(ground.SlowingEffect, .4f, "经过模式切换与规则编辑仍恢复原默认");
    }

    private static void CheckMultipleSurfaceParameters()
    {
        var host = Host<BasicPhysicalObject>("three surface parameters");
        SetParameters(host, new GroundSlowingParameter(.4f), new GroundJumpMultiplierParameter(1.3f),
            new WallSlideMultiplierParameter(.6f));
        Collider2D collider = host.gameObject.AddComponent<BoxCollider2D>();
        Check(host.ValidateEnvironmentParameters(out _) && host.EnvironmentParameterCount == 3,
            "同一来源允许三类项共存，每类各一项");
        var receiver = Receiver();
        receiver.EnvironmentContext.RefreshEnvironment(collider, null);
        Near(receiver.EnvironmentContext.Frame.slowingMultiplier, .4f, "脚下只采样同源消退项");
        Near(receiver.EnvironmentContext.Frame.jumpHeightOffset, .3f, "脚下将普通起跳倍率转换为原加成");
        Check(receiver.EnvironmentContext.DebugSourceCount == 0, "三类表面项不伪造持续 Binding");
        IWallSlideSurface wall = EnvironmentCapabilities.Find<IWallSlideSurface>(collider);
        Check(wall != null && EnvironmentCapabilities.IsActive(wall), "普通唯一宿主的墙项提供有效墙能力");
        Near(wall.WallSlideMultiplier, .6f, "墙查询只解析墙滑项，不拿脚下倍率替代");
        Check(ReferenceEquals(wall, EnvironmentCapabilities.Find<IWallSlideSurface>(collider)),
            "墙能力视图缓存，反复查询不建立新来源");
        EnvironmentRegistration identity = host.EnvironmentRegistration;
        receiver.EnvironmentContext.RefreshEnvironment(null, null);
        Near(receiver.EnvironmentContext.Frame.slowingMultiplier, 1f, "仅侧面取墙能力不注入脚下消退项");
        Near(receiver.EnvironmentContext.Frame.jumpHeightOffset, 0f, "仅侧面取墙能力不注入普通起跳项");
        receiver.EnvironmentContext.RefreshEnvironment(null, new List<Collider2D> { collider });
        Near(receiver.EnvironmentContext.Frame.jumpHeightOffset, 0f, "区域重叠不注入普通起跳项");

        Check(host.TrySetEnvironmentParameter(ParameterIndex(host, EnvironmentParameterKind.GroundJumpMultiplier), 1.6f),
            "既有 index 编辑口可独立改普通起跳项");
        Near(wall.WallSlideMultiplier, .6f, "改普通起跳不改变墙滑");
        receiver.EnvironmentContext.RefreshEnvironment(collider, null);
        Near(receiver.EnvironmentContext.Frame.slowingMultiplier, .4f, "改普通起跳不改变消退");
        Near(receiver.EnvironmentContext.Frame.jumpHeightOffset, .6f, "正常支撑采样读取已修改的普通起跳项");
        Check(host.TrySetEnvironmentParameter(ParameterIndex(host, EnvironmentParameterKind.WallSlideMultiplier), .2f),
            "同来源可独立修改墙滑项");
        Near(wall.WallSlideMultiplier, .2f, "现有墙能力视图读取来源生效值");
        host.RestoreEnvironmentDefaults();
        receiver.EnvironmentContext.RefreshEnvironment(collider, null);
        Near(receiver.EnvironmentContext.Frame.jumpHeightOffset, .3f, "重恢复还原普通起跳默认");
        Near(wall.WallSlideMultiplier, .6f, "重恢复还原墙滑默认");
        Check(ReferenceEquals(identity, host.EnvironmentRegistration), "多项编辑恢复保持原来源身份");
        host.enabled = false;
        Check(!EnvironmentCapabilities.IsActive(wall) && EnvironmentCapabilities.Find<IWallSlideSurface>(collider) == null,
            "来源禁用使墙视图及查询同时失效");
        host.enabled = true;
        Check(EnvironmentCapabilities.IsActive(wall), "原来源恢复启用后原墙视图恢复有效");
        Object.DestroyImmediate(host);
        Check(!EnvironmentCapabilities.IsActive(wall), "来源销毁后墙视图安全失效");

        var groundOnly = Host<BasicPhysicalObject>("ground response only");
        SetParameters(groundOnly, new GroundJumpMultiplierParameter(1.2f));
        Collider2D groundOnlyCollider = groundOnly.gameObject.AddComponent<BoxCollider2D>();
        Check(EnvironmentCapabilities.Find<IWallSlideSurface>(groundOnlyCollider) == null,
            "普通宿主只有脚下项时不自动拥有墙滑能力");
        var duplicate = Host<BasicPhysicalObject>("duplicated wall response");
        SetParameters(duplicate, new GroundSlowingParameter(.4f), new WallSlideMultiplierParameter(.5f),
            new WallSlideMultiplierParameter(.8f));
        Check(!duplicate.ValidateEnvironmentParameters(out string error) && error.Contains("重复"),
            "不同类项可共存，重复同类墙项仍拒绝整份配置");
    }

    private static void CheckJumpAndWallConsumption()
    {
        var host = Host<BasicPhysicalObject>("jump and wall consumption source");
        SetParameters(host, new GroundJumpMultiplierParameter(1.4f), new WallSlideMultiplierParameter(.3f));
        Collider2D collider = host.gameObject.AddComponent<BoxCollider2D>();
        var item = new GameObject("surface response character") { hideFlags = HideFlags.DontSave };
        objects.Add(item);
        item.AddComponent<Rigidbody2D>(); item.AddComponent<BoxCollider2D>();
        var slider = item.AddComponent<EnvironmentCheckSlider>();
        var action = new MovementData { nowState = PlayerStateMachine.E_playerState.isOnGround, onMove = 1f };
        var events = new LocalEventSystem<PlayerStateMachine.E_playEvent>();
        var function = new PhysicalFunctionData { canRightWall = EnvironmentCapabilities.Find<IWallSlideSurface>(collider) };
        slider.Bind(new ReadOnly_ActionData(action), function, events);
        slider.EnvironmentContext.RefreshEnvironment(collider, null);
        slider.FallAt(0f);
        Near(slider.Vertical, 0f, "进入脚下来源不自动执行跳跃");
        events.EventTigger(PlayerStateMachine.E_playEvent.jump);
        slider.ConsumeEvents();
        Near(slider.Vertical, slider.upSpeed * 1.4f, "普通起跳真实消费脚下已采样倍率");
        int sequence = slider.ReadOnly_PlayersPhysicsData.jumpSequence;
        slider.ConsumeEvents();
        Check(slider.ReadOnly_PlayersPhysicsData.jumpSequence == sequence, "重复运动阶段不重新触发跳跃");
        events.EventTigger(PlayerStateMachine.E_playEvent.wallJump);
        slider.ConsumeEvents();
        Near(slider.Vertical, slider.upSpeed, "脚下普通起跳项不影响墙跳");

        action.nowState = PlayerStateMachine.E_playerState.inAir;
        slider.FallAt(-10f); slider.Limit();
        Near(slider.Vertical, -10f, "普通侧面命中不因有墙项自动墙滑限速");
        action.nowState = PlayerStateMachine.E_playerState.onWallSliding;
        slider.FallAt(5f); slider.Limit();
        Near(slider.Vertical, 5f, "墙滑上升不消费下落响应项");
        slider.FallAt(-10f); slider.Limit();
        Near(slider.Vertical, -.6f, "实际墙滑下落才消费普通宿主的墙项");
        host.TrySetEnvironmentParameter(ParameterIndex(host, EnvironmentParameterKind.WallSlideMultiplier), .5f);
        slider.FallAt(-10f); slider.Limit();
        Near(slider.Vertical, -1f, "编辑来源后正常墙滑消费同一视图的新值");
        host.enabled = false;
        slider.FallAt(-10f); slider.Limit();
        Near(slider.Vertical, -10f, "旧墙视图的来源失效后不继续限速");
        host.enabled = true;
        host.TrySetEnvironmentParameter(ParameterIndex(host, EnvironmentParameterKind.GroundJumpMultiplier), 0f);
        action.nowState = PlayerStateMachine.E_playerState.isOnGround;
        slider.EnvironmentContext.RefreshEnvironment(collider, null);
        events.EventTigger(PlayerStateMachine.E_playEvent.jump); slider.ConsumeEvents();
        Near(slider.Vertical, 0f, "零普通起跳倍率保留旧加成负一的含义");
    }

    private static void CheckLegacySurfaceCompatibility()
    {
        BaseGround ground = Host<BaseGround>("legacy jump compatibility");
        SetLegacy(ground, .3f);
        typeof(BaseGround).GetField("jumpHeightNum", Hidden).SetValue(ground, -.5f);
        SetParameters(ground, new WallSlideMultiplierParameter(.7f));
        Check(ground.EnvironmentParameterCount == 3, "新增墙项后旧地面消退和普通起跳缺项仍补原值");
        Near(ground.SlowingEffect, .3f, "新增其它类型不覆盖旧消退倍率");
        Near(ground.JumpHeightNum, -.5f, "旧负起跳加成按原含义转换为非负倍率");
        ground.TrySetEnvironmentParameter(ParameterIndex(ground, EnvironmentParameterKind.GroundJumpMultiplier), 1.2f);
        Near(ground.JumpHeightNum, .2f, "旧地面的普通起跳也通过生效项读取");
        ground.RestoreEnvironmentDefaults();
        Near(ground.JumpHeightNum, -.5f, "恢复旧地面普通起跳默认");
        Near((float)typeof(BaseGround).GetField("jumpHeightNum", Hidden).GetValue(ground), -.5f,
            "普通起跳编辑不回写旧序列化字段");

        Wall wall = Host<Wall>("legacy wall compatibility");
        typeof(Wall).GetField("wallFriction", Hidden).SetValue(wall, .8f);
        SetParameters(wall, new GroundJumpMultiplierParameter(1.3f));
        Collider2D collider = wall.gameObject.AddComponent<BoxCollider2D>();
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(collider), wall),
            "旧墙接口身份保持，新增其它类型不换环境来源");
        Near(wall.WallSlideMultiplier, .8f, "旧墙未配置墙项时保留原墙滑倍率");
        wall.TrySetEnvironmentParameter(ParameterIndex(wall, EnvironmentParameterKind.WallSlideMultiplier), .4f);
        Near(wall.WallSlideMultiplier, .4f, "原墙接口消费原来源的运行时墙项");
        wall.RestoreEnvironmentDefaults();
        Near(wall.WallSlideMultiplier, .8f, "原墙恢复制作默认，不靠重新启用抓取");
        Near((float)typeof(Wall).GetField("wallFriction", Hidden).GetValue(wall), .8f,
            "墙项编辑不回写原墙摩擦字段");
        BaseGround minimumJump = Host<BaseGround>("legacy minimum jump");
        typeof(BaseGround).GetField("jumpHeightNum", Hidden).SetValue(minimumJump, -1f);
        Near(minimumJump.JumpHeightNum, -1f, "旧起跳加成下界负一仍合法");
    }

    private static int ParameterIndex(BasicPhysicalObject host, EnvironmentParameterKind kind)
    {
        for (int i = 0; i < host.EnvironmentParameterCount; i++)
            if (host.TryGetEnvironmentParameter(i, out EnvironmentParameter parameter) && parameter.Kind == kind) return i;
        throw new InvalidOperationException("Missing environment parameter: " + kind);
    }

    private static T Host<T>(string name) where T : BasicPhysicalObject
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        objects.Add(item);
        return item.AddComponent<T>();
    }
    private static void SetLegacy(BaseGround host, float value) =>
        typeof(BaseGround).GetField("slowingEffect", Hidden).SetValue(host, value);
    private static void SetParameters(BasicPhysicalObject host, params EnvironmentParameter[] values) =>
        typeof(BasicPhysicalObject).GetField("environmentParameters", Hidden)
            .SetValue(host, new List<EnvironmentParameter>(values));
    private static EnvironmentParameterCheckReceiver Receiver()
    {
        var item = new GameObject("environment receiver") { hideFlags = HideFlags.DontSave };
        objects.Add(item);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        var receiver = item.AddComponent<EnvironmentParameterCheckReceiver>();
        receiver.InitializeForChecks();
        return receiver;
    }
    private static Dictionary<IDynamicAddForce, ForceData> Forces(BasicEntity receiver) =>
        (Dictionary<IDynamicAddForce, ForceData>)EnvironmentIntegrationChecks.EffectField(receiver, "dynamicForces");
    private static bool ReadPaused(Type type) =>
        (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
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
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < .0001f, message + $" [{actual} == {expected}]");
}

// 复用实际实体的环境与效果容器，只跳过场景探针配置和公共相位注册。
public sealed class EnvironmentParameterCheckReceiver : BasicEntity
{
    protected override void Awake() => InitializeForChecks();
    protected override void OnEnable() { }
    protected override void OnDisable() => EnvironmentContext.ReleaseAll();
    protected override void GeometricQuery() { }
    public void InitializeForChecks()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        nowGemetry = new GeometryPhysicsData();
        playerPhysicsData = new PlayerPhysicsData();
    }
}
