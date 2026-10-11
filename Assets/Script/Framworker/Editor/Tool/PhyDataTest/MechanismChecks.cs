#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>P15 定向回归：实际预设、真实接触和同一原生模拟；不保存关卡或修改物理配置资产。</summary>
public static class MechanismChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<string> passed = new List<string>();
    static readonly MethodInfo sidePush = typeof(BasicEntity).Assembly.GetType("EntitySidePush")
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
    static float nextX = 20000f;

    [MenuItem("自定义工具/测试工具/验证/P15 验证小车机关与固定带电（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行。");
        passed.Clear();
        SimulationMode2D mode = Physics2D.simulationMode;
        bool worldPause = Paused(typeof(BasicEntity)), platformPause = Paused(typeof(Taijie)), platePause = Paused(typeof(MechanismPressurePlate));
        FieldInfo managerField = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object manager = managerField.GetValue(null);
        bool quitting = MonoPublicMgr.IsQuitting;
        try
        {
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)manager == null) managerField.SetValue(null, New("P15 scheduler", Vector2.zero).AddComponent<MonoPublicMgr>());
            Physics2D.simulationMode = SimulationMode2D.Script;
            PlayerLifecycleRules.SetWorldPaused(false);
            CheckPrefabs();
            CheckCart(1); CheckCart(-1);
            CheckPressure(false); CheckPressure(true);
            CheckPushAcrossPressure(false); CheckPushAcrossPressure(true);
            CheckTargetAndRider();
            CheckCrush();
            CheckElectricity();
            CheckTrialAndConfiguration();
            Debug.Log($"P15 mechanisms: {passed.Count} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            BasicEntity.SetWorldPaused(worldPause); Taijie.SetWorldPaused(platformPause); MechanismPressurePlate.SetWorldPaused(platePause);
            managerField.SetValue(null, manager); MonoPublicMgr.IsQuitting = quitting; Physics2D.simulationMode = mode;
            Directory.CreateDirectory(".utmp/P15Validation");
            File.WriteAllLines(".utmp/P15Validation/checks.txt", passed);
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
            if (!Application.isBatchMode) throw new InvalidOperationException("组合回归只用于新开的 batch editor。");
            // 背包回归需附加隔离场景；batch 的无标题默认场景不允许附加，先打开已保存入口。
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            Run(); BoxInteractionChecks.Run(); EnvironmentIntegrationChecks.Run();
            SpiritFlightChecks.Run(); TrialRefreshChecks.Run(); EnvironmentBackpackChecks.Run(); ScenePropInteractionChecks.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckPrefabs()
    {
        foreach (string name in new[] { "MechanismCart", "MechanismPlatform", "MechanismDoor", "PressurePlate", "ElectrifiedPressurePlate", "ElectrifiedLowFrictionGround" })
        {
            GameObject item = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + name + ".prefab");
            Check(item != null && item.GetComponent<Collider2D>() != null, name + " 预设可直接加载且有明确几何");
            Check(item.layer != Player.PlayerLayer, name + " 使用世界渲染层");
        }
        GameObject platform = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "MechanismPlatform.prefab");
        GameObject door = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "MechanismDoor.prefab");
        Check(platform.GetComponent<PlatformEffector2D>().useOneWay && platform.GetComponent<Collider2D>().usedByEffector,
            "可跳上移动平台保留单向效应器");
        Check(door.GetComponent<PlatformEffector2D>() == null && !door.GetComponent<Collider2D>().usedByEffector,
            "实心机关门不带平台效应器且四面阻挡");
        GameObject electricPlate = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "ElectrifiedPressurePlate.prefab");
        Check(electricPlate.GetComponent<FixedElectricHazard>() == null, "带电压感的含义是只认带电箱，本身不自动电击");
        GameObject cart = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + "MechanismCart.prefab");
        Check(cart.GetComponent<PhysicalBox>() == null && cart.GetComponent<MechanismCart>().ValidateConfiguration(out _),
            "小车不成为可收纳箱，方向／探针／碰撞排除配置有效");
    }

    static void CheckCart(int direction)
    {
        float x = Place(); Ground(x);
        MechanismCart cart = Prefab<MechanismCart>("MechanismCart", new Vector2(x, 101f), false);
        Set(cart, "direction", (MechanismCart.Direction)direction); Initialize(cart);
        StandOn(cart, x, 100f);
        PhysicalBox box = Prefab<PhysicalBox>("box", new Vector2(x + direction * 2f, 101f));
        StandOn(box, x + direction * (cart.BodyBounds.extents.x + box.BodyBounds.extents.x + .015f), 100f);
        Player player = PlayerAt(new Vector2(x - direction * .3f, 101f));
        MoveCenter(player.GetComponent<CharacterPhysics>(), cart.BodyBounds.center);
        Vector2 playerStart = player.GetComponent<CharacterPhysics>().DebugActualPosition;
        Check(cart.Interact(new PlayerInteractRequest(player, PlayerInteractKind.Tap, false)) == PlayerInteractOutcome.Activated && cart.IsOn,
            $"{direction} 方向小车点按开启");
        Check(cart.Interact(new PlayerInteractRequest(player, PlayerInteractKind.LongPress, false)) == PlayerInteractOutcome.Rejected && cart.IsOn,
            "长按不切开关或收纳小车");
        Vector2 start = cart.DebugActualPosition, boxStart = box.DebugActualPosition;
        for (int i = 0; i < 35; i++) Step(cart, box, player.GetComponent<CharacterPhysics>());
        Check((cart.DebugActualPosition.x - start.x) * direction > .8f && (box.DebugActualPosition.x - boxStart.x) * direction > .8f,
            $"{direction} 方向小车经现有驱动真实推箱");
        Check(Mathf.Abs(player.GetComponent<CharacterPhysics>().DebugActualPosition.x - playerStart.x) < .08f,
            "车穿过玩家，侧推和原生碰撞均不推走玩家");
        MoveCenter(player.GetComponent<CharacterPhysics>(), cart.BodyBounds.center);
        float velocity = cart.GetPlanarVelocity().x;
        Check(cart.Interact(new PlayerInteractRequest(player, PlayerInteractKind.Tap, false)) == PlayerInteractOutcome.Activated && !cart.IsOn,
            "再次点按关闭小车");
        Near(cart.GetPlanarVelocity().x, velocity, .001f, "关闭当刻保留刚体惯性");
        Step(cart, player.GetComponent<CharacterPhysics>());
        Check(cart.GetPlanarVelocity().x * direction > .1f, "关闭后原有物理继续消费惯性，不立即清零");
        Vector2 position = cart.DebugActualPosition, momentum = cart.GetPlanarVelocity();
        PlayerLifecycleRules.SetWorldPaused(true);
        for (int i = 0; i < 5; i++) Step(cart);
        Near(cart.DebugActualPosition, position, .001f, "漫游暂停小车位置");
        Near(cart.GetPlanarVelocity(), momentum, .001f, "漫游暂停保留小车速度");
        PlayerLifecycleRules.SetWorldPaused(false);
        Step(cart);
        Check((cart.DebugActualPosition.x - position.x) * direction > 0f, "模拟恢复小车惯性");
        cart.RestoreDefault();
        Check(!cart.IsOn && cart.GetPlanarVelocity() == Vector2.zero, "小车默认复位清运行驱动与运动");
    }

    static void CheckPressure(bool electric)
    {
        float x = Place(); Ground(x);
        MechanismPressurePlate plate = Prefab<MechanismPressurePlate>(electric ? "ElectrifiedPressurePlate" : "PressurePlate", new Vector2(x, 100f));
        Taijie target = (Taijie)plate.TargetComponent;
        Invoke(target, "OnEnable"); Invoke(plate, "OnEnable");
        float top = 100f;
        PhysicalBox first = Prefab<PhysicalBox>("box", new Vector2(x - .8f, 102f));
        PhysicalBox second = Prefab<PhysicalBox>("box", new Vector2(x + .8f, 102f));
        StandOn(first, x - .8f, top); StandOn(second, x + .8f, top);
        if (electric) { Set(first, "electrified", true); Set(second, "electrified", true); }
        for (int i = 0; i < 6; i++) { Step(first, second); Invoke(plate, "SamplePressure"); }
        Check(plate.GetComponent<Collider2D>().isTrigger && plate.IsPressed && plate.OccupantCount == 2,
            electric ? "带电压感触发区聚合两个脚底进入的带电箱" : "普通压感触发区聚合两个脚底进入的箱子");
        Vector3 home = target.transform.position;
        Set(target, "nowtime", AudioSettings.dspTime - .75d); Invoke(target, "FixFun");
        Check(target.transform.position.y > home.y + 1f, "压住通过统一目标接口启动缓动");
        first.SetMotionParticipation(false); Invoke(plate, "SamplePressure");
        Check(plate.IsPressed && plate.OccupantCount == 1, "一个对象退出后仍由剩余对象压住");
        second.RefreshMotionState(second.DebugActualPosition + Vector2.right * 12f);
        Vector3 reverse = target.transform.position;
        Invoke(plate, "SamplePressure");
        Check(!plate.IsPressed && plate.OccupantCount == 0, "最后一个离开立即释放，不保留旧原生接触");
        Near(target.transform.position, reverse, .001f, "中途释放反向不瞬移");
        Set(target, "nowtime", AudioSettings.dspTime - 2d); Invoke(target, "FixFun");
        Near(target.transform.position, home, .001f, "释放缓动返回首次位置");
        first.SetMotionParticipation(true); first.RefreshMotionState(new Vector2(x, top + first.BodyBounds.extents.y + .005f));
        if (electric) Set(first, "electrified", false);
        for (int i = 0; i < 4; i++) { Step(first); Invoke(plate, "SamplePressure"); }
        Check(plate.IsPressed == !electric, electric ? "带电压感拒绝普通箱" : "普通压感仍接受普通箱");
        if (!electric)
        {
            Check(first.Interact(new PlayerInteractRequest(null, PlayerInteractKind.LongPress, true)) == PlayerInteractOutcome.Stored,
                "压住的普通箱经现有入口实际收纳");
            Invoke(plate, "SamplePressure");
            Check(!plate.IsPressed, "收纳关闭碰撞与原生参与后释放压感");
        }
        first.SetMotionParticipation(false);
        Player player = PlayerAt(new Vector2(x, top + 2f));
        StandOn(player.GetComponent<CharacterPhysics>(), x, top);
        for (int i = 0; i < 5; i++) { Step(player.GetComponent<CharacterPhysics>()); Invoke(plate, "SamplePressure"); }
        Check(plate.IsPressed == !electric, electric ? "带电压感拒绝行走玩家" : "普通压感接受行走玩家");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Standby); Invoke(plate, "SamplePressure");
        Check(!plate.IsPressed, "待命角色不压住机关");
        MechanismCart cart = Prefab<MechanismCart>("MechanismCart", new Vector2(x, 102f));
        StandOn(cart, x, top);
        for (int i = 0; i < 5; i++) { Step(cart); Invoke(plate, "SamplePressure"); }
        Check(plate.IsPressed == !electric, electric ? "带电压感拒绝小车" : "普通压感接受小车");
        PlayerLifecycleRules.SetWorldPaused(true);
        bool pressed = plate.IsPressed;
        cart.SetMotionParticipation(false); Invoke(plate, "SamplePressure");
        Check(plate.IsPressed == pressed, "漫游暂停保留压感现场");
        PlayerLifecycleRules.SetWorldPaused(false); Invoke(plate, "SamplePressure");
        Check(!plate.IsPressed, "恢复后重新核对已消失的压住来源");
        plate.enabled = false; Invoke(plate, "OnDisable");
    }

    static void CheckPushAcrossPressure(bool electric)
    {
        float x = Place(); Ground(x);
        MechanismPressurePlate plate = Prefab<MechanismPressurePlate>(electric ? "ElectrifiedPressurePlate" : "PressurePlate", new Vector2(x, 100f));
        Invoke(plate, "OnEnable"); Invoke(plate.TargetComponent, "OnEnable");
        MechanismCart cart = Prefab<MechanismCart>("MechanismCart", new Vector2(x - 4f, 101f));
        PhysicalBox box = Prefab<PhysicalBox>("box", new Vector2(x - 2.5f, 101f));
        StandOn(cart, x - 4f, 100f);
        StandOn(box, cart.BodyBounds.max.x + box.BodyBounds.extents.x + .015f, 100f);
        Set(box, "electrified", electric); Set(cart, "isOn", true);
        bool seenPressed = false;
        for (int i = 0; i < 100; i++)
        {
            Step(cart, box); Invoke(plate, "SamplePressure");
            if (plate.IsPressed) seenPressed = true;
            // 第一个进入到离开都在原地面上推进，不需要把箱子抬到板顶。
            if (box.BodyBounds.min.x > plate.GetComponent<Collider2D>().bounds.max.x + .2f) break;
        }
        Check(seenPressed, electric ? "小车沿平地推带电箱进入电气压感区域并激活" : "小车沿平地推普通箱进入压感区域并激活");
        Check(box.BodyBounds.min.x > plate.GetComponent<Collider2D>().bounds.max.x,
            "触发器压力板保持原凸起显示高度，箱子仍能连续推过");
        Near(box.BodyBounds.min.y, 100f, .04f, "推过压力板不抬箱，仍由平地支撑");
        cart.SetMotionParticipation(false); box.SetMotionParticipation(false); Invoke(plate, "SamplePressure");
        Check(!plate.IsPressed, "全部退出触发区后释放目标");
        box.SetMotionParticipation(true); MoveCenter(box, new Vector2(x, 100f));
        Invoke(plate, "SamplePressure"); Check(!plate.IsPressed, "箱子腰部侧擦触发区但脚底在下方，不压住机关");
        MoveCenter(box, new Vector2(x, 100f - box.BodyBounds.extents.y));
        Invoke(plate, "SamplePressure"); Check(!plate.IsPressed, "从压力板下方穿过不压住机关");
        plate.enabled = false; Invoke(plate, "OnDisable");
    }

    static void CheckTargetAndRider()
    {
        float x = Place();
        Taijie target = Prefab<Taijie>("MechanismPlatform", new Vector2(x, 100f)); Invoke(target, "OnEnable");
        Player player = PlayerAt(new Vector2(x, 102f));
        CharacterPhysics rider = player.GetComponent<CharacterPhysics>();
        StandOn(rider, x, target.GetComponent<Collider2D>().bounds.max.y);
        Set(target, "mechanismOffset", new Vector2(2f, -2f));
        Vector2 riderStart = rider.DebugActualPosition;
        Vector3 home = target.transform.position;
        Check(target.TrySetMechanismActive(true), "统一接口接受有效终点和时长");
        for (int i = 1; i <= 40; i++)
        {
            Set(target, "nowtime", AudioSettings.dspTime - i * Time.fixedDeltaTime);
            Invoke(target, "FixFun"); Step(rider);
        }
        Vector2 platformDelta = target.transform.position - home;
        Check(platformDelta.x > .5f && platformDelta.y < -.5f && rider.DebugIsGrounded, "移动平台水平加下降时持续支撑角色");
        Near(rider.DebugActualPosition - riderStart, platformDelta, .07f, "平台承载位移只消费一次");
        PlayerLifecycleRules.SetWorldPaused(true);
        Vector3 frozen = target.transform.position;
        FieldInfo clock = typeof(Taijie).GetField("nowtime", Hidden), pause = typeof(Taijie).GetField("pausedAt", Hidden);
        clock.SetValue(target, (double)clock.GetValue(target) - 10d); pause.SetValue(target, (double)pause.GetValue(target) - 10d);
        Invoke(target, "FixFun");
        Near(target.transform.position, frozen, .001f, "平台暂停无位移"); Check(target.Delta == Vector2.zero, "平台暂停无携带贡献");
        PlayerLifecycleRules.SetWorldPaused(false); Invoke(target, "FixFun");
        Near(target.transform.position, frozen, .04f, "恢复不追赶暂停期间的十秒");
        target.TrySetMechanismActive(false);
        Near(target.transform.position, frozen, .04f, "返回命令沿实际位置反向");
        Set(target, "nowtime", AudioSettings.dspTime - 2d); Invoke(target, "FixFun");
        Near(target.transform.position, home, .001f, "返回终点正确");
        target.RestoreMechanismDefaults();
        Check(target.Delta == Vector2.zero, "显式复位清平台 Delta");
    }

    static void CheckElectricity()
    {
        float x = Place();
        BaseGround ground = Prefab<BaseGround>("ElectrifiedLowFrictionGround", new Vector2(x, 100f));
        FixedElectricHazard hazard = ground.GetComponent<FixedElectricHazard>(); Invoke(hazard, "OnEnable");
        Player player = PlayerAt(new Vector2(x, 102f));
        CharacterPhysics body = player.GetComponent<CharacterPhysics>();
        var receiver = new Receiver(); player.BindDeathReceiver(receiver);
        StandOn(body, x, ground.GetComponent<Collider2D>().bounds.max.y);
        Invoke(player, "UpdateBoxRelations"); Invoke(player, "UpdateBoxRelations");
        Check(receiver.Count == 1 && receiver.Cause == PlayerDeathCause.Electrocuted, "固定实心顶面接触经统一死亡入口且持续接触去重");
        MoveCenter(body, new Vector2(x + 12f, 102f)); Invoke(player, "UpdateBoxRelations"); player.ResetDeathCycle();
        MoveCenter(body, new Vector2(x + ground.GetComponent<Collider2D>().bounds.extents.x + body.BodyBounds.extents.x + .01f, 100f));
        Invoke(player, "UpdateBoxRelations"); Check(receiver.Count == 2, "固定实心侧面接触电击");
        MoveCenter(body, new Vector2(x + 12f, 102f)); Invoke(player, "UpdateBoxRelations"); player.ResetDeathCycle();
        MoveCenter(body, new Vector2(x, ground.GetComponent<Collider2D>().bounds.min.y - body.BodyBounds.extents.y - .01f));
        Invoke(player, "UpdateBoxRelations"); Check(receiver.Count == 3, "固定实心底面接触电击");
        MoveCenter(body, new Vector2(x + 12f, 102f)); Invoke(player, "UpdateBoxRelations"); player.ResetDeathCycle();
        PhysicalBox box = Prefab<PhysicalBox>("box", new Vector2(x, 102f));
        StandOn(box, x, ground.GetComponent<Collider2D>().bounds.max.y);
        StandOn(body, x, box.BodyBounds.max.y);
        Check(!box.IsElectrified && !ElectricContact.Touches(player, .03f), "普通箱隔开危险面，不导电到箱顶玩家");
        Set(box, "electrified", true);
        Invoke(player, "UpdateBoxRelations"); Check(receiver.Count == 4, "带电箱与环境共用相同电击入口");
        Set(box, "electrified", false); box.SetMotionParticipation(false);
        MoveCenter(body, new Vector2(x + 12f, 102f)); Invoke(player, "UpdateBoxRelations"); player.ResetDeathCycle();
        int slots = ground.EnvironmentSlotCapacity;
        var bag = new CognitionBackpack(20, 0f, 0f, new EnvironmentParameter[] { new TriggerEnvironmentParameter() });
        var draft = new EnvironmentEditDraft(ground, bag);
        Check(draft.TryUseItem(bag.Items[0].Id, false, -1, true, out _) && draft.TryCommit(new LevelCognition(100f), out _), "通过真实草稿确认把带电地面切成触发器");
        Check(ground.IsTriggerEnvironment && ground.EnvironmentSlotCapacity == slots && ground.GetComponent<FixedElectricHazard>() == hazard,
            "几何切换保留独立带电组件，不占能力槽");
        MoveCenter(body, ground.GetComponent<Collider2D>().bounds.center);
        Invoke(player, "UpdateBoxRelations"); Check(receiver.Count == 5, "触发器真实重叠同样电击");
        MoveCenter(body, new Vector2(x + 12f, 102f)); Invoke(player, "UpdateBoxRelations"); player.ResetDeathCycle();
        // Box2D 形状含接触皮肤；选取真实 Distance 为正、又小于实心电击容差的位置。
        MoveCenter(body, new Vector2(ground.GetComponent<Collider2D>().bounds.max.x + body.BodyBounds.extents.x + .025f, 100f));
        ColliderDistance2D outside = ground.GetComponent<Collider2D>().Distance(body.GetComponent<Collider2D>());
        Check(outside.distance > 0f && outside.distance < .03f && !ElectricContact.Touches(player, .03f),
            "触发器外的近距离不借实心容差电击");
        draft = new EnvironmentEditDraft(ground, bag);
        Check(draft.TryExtractTrigger(out _) && draft.TryCommit(new LevelCognition(100f), out _), "抽走几何性质可切回实心");
        Check(!ground.IsTriggerEnvironment && hazard.ValidateConfiguration(out _) && bag.Items.Count == 1
            && bag.Items[0].Property.Kind == EnvironmentParameterKind.TriggerEnvironment, "背包只取得几何性质，固定电不进入抽取或计算");
        ground.RestoreEnvironmentDefaults();
        Near(ground.SlowingEffect, .05f, .001f, "默认低摩擦样例保留可编辑消退倍率");
        GameObject area = New("P15 panel area", Vector2.zero); area.AddComponent<RectTransform>();
        GameObject panel = New("P15 panel", Vector2.zero); panel.AddComponent<RectTransform>(); area.transform.SetParent(panel.transform);
        EnvironmentEditLayout.Ensure(area.transform, panel.transform);
        Check(area.transform.Find("EnvironmentEditorContent/EnvironmentFixedFeatureText") != null,
            "环境面板只读固定特性行独立于可抽取槽列表");
    }

    static void CheckTrialAndConfiguration()
    {
        float x = Place(); Ground(x);
        Player spirit = PlayerAt(new Vector2(x, 102f), true);
        var rules = new PlayerLifecycleRules(null, spirit, cognition: new LevelCognition(100f), simulationCognitionPerSecond: 0f);
        Check(rules.BeginExploration() && spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "真实漫游切模拟规则就绪");
        MechanismCart cart = Prefab<MechanismCart>("MechanismCart", new Vector2(x + 8f, 101f));
        Vector2 cartHome = cart.DebugActualPosition;
        Taijie target = Prefab<Taijie>("MechanismDoor", new Vector2(x + 20f, 102f)); Invoke(target, "OnEnable");
        Vector3 targetHome = target.transform.position;
        MechanismPressurePlate plate = Prefab<MechanismPressurePlate>("PressurePlate", new Vector2(x + 30f, 100f));
        Invoke(plate, "OnEnable");
        Set(plate, "target", target);
        Set(cart, "isOn", true); cart.RefreshMotionState(cartHome + Vector2.right * 3f);
        target.TrySetMechanismActive(true); Set(target, "nowtime", AudioSettings.dspTime - .75d); Invoke(target, "FixFun");
        Vector3 retained = target.transform.position;
        Check(spirit.TryRequestDeath(PlayerDeathCause.Crushed) && spirit._ActionData.isFlying && !rules.IsSimulation,
            "模拟夹击死亡经已有规则切回漫游，暂停世界");
        Check(cart.IsOn && cart.DebugActualPosition.x == cartHome.x + 3f && target.transform.position == retained, "模式切换保留小车开关位置与目标现场");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion) && rules.TryResetTrial(new Vector2(x, 102f)), "真实试验复位沿全关默认恢复入口");
        Check(!cart.IsOn && cart.GetPlanarVelocity() == Vector2.zero, "试验复位恢复车开关和速度");
        Near(cart.DebugActualPosition, cartHome, .001f, "试验复位恢复车初始位置");
        Near(target.transform.position, targetHome, .001f, "试验复位恢复门初始位置");
        Check(!plate.IsPressed && target.Delta == Vector2.zero, "试验复位清压感和平台贡献");
        Set(cart, "speed", float.NaN); Check(!cart.ValidateConfiguration(out string cartError) && cartError.Contains(cart.name), "非法小车速度显式指出对象");
        Set(target, "mechanismTravelTime", 0f); Check(!target.ValidateMechanismConfiguration(out _), "零缓动时长拒绝，不改成默认值");
        Set(target, "mechanismTravelTime", 1.5f); Set(target, "mechanismOffset", new Vector2(float.PositiveInfinity, 0f));
        Check(!target.ValidateMechanismConfiguration(out _), "无限终点拒绝，不替换位置");
        Set(target, "mechanismOffset", Vector2.up * 3f);
        MechanismPressurePlate duplicate = Prefab<MechanismPressurePlate>("PressurePlate", new Vector2(x + 40f, 100f));
        Set(duplicate, "target", target);
        Check(!plate.ValidateConfiguration(out string conflict) && conflict.Contains(duplicate.name)
            && !duplicate.ValidateConfiguration(out _), "重复控制关联两端都拒绝，不按顺序抢占");
        Set(duplicate, "target", null); Check(!duplicate.ValidateConfiguration(out _), "缺失关联拒绝，不自动寻找其它目标");
        PlayerLifecycleRules.SetWorldPaused(false);
    }

    static void CheckCrush()
    {
        float x = Place();
        Taijie door = Prefab<Taijie>("MechanismDoor", new Vector2(x - 2f, 100f)); Invoke(door, "OnEnable");
        Set(door, "mechanismOffset", new Vector2(1.55f, 0f));
        GameObject wall = New("P15 fixed wall", new Vector2(x + 5.3f, 100f)); wall.layer = LayerMask.NameToLayer("Ground");
        BoxCollider2D wallShape = wall.AddComponent<BoxCollider2D>(); wallShape.size = new Vector2(10f, 8f); wall.AddComponent<Wall>();
        Player player = PlayerAt(new Vector2(x, 100f));
        CharacterPhysics body = player.GetComponent<CharacterPhysics>();
        MoveCenter(body, new Vector2(x - .23f, 100f));
        var receiver = new Receiver(); player.BindDeathReceiver(receiver);
        door.TrySetMechanismActive(true);
        Invoke(player, "CheckMechanismCrush");
        Check(receiver.Count == 0, "普通窄缝接触墙但未嵌门不死亡");
        Set(door, "nowtime", AudioSettings.dspTime - 2d); Invoke(door, "FixFun");
        Step(body);
        ColliderDistance2D doorDistance = body.GetComponent<Collider2D>().Distance(door.GetComponent<Collider2D>());
        ColliderDistance2D wallDistance = body.GetComponent<Collider2D>().Distance(wallShape);
        Check(doorDistance.isOverlapped && wallDistance.isOverlapped
            && (doorDistance.distance < -.03f || wallDistance.distance < -.03f),
            $"门相位 0 推进后一次原生模拟仍同时嵌门墙 [{doorDistance.distance}/{wallDistance.distance}]");
        Invoke(player, "CheckMechanismCrush");
        Check(receiver.Count == 1 && receiver.Cause == PlayerDeathCause.Crushed, "模拟后夹击沿现有死亡入口提交");
        player.ResetDeathCycle(); Invoke(player, "CheckMechanismCrush");
        Check(receiver.Count == 1, "持续夹击只成功发一次请求");
        MoveCenter(body, new Vector2(x - 20f, 100f)); Invoke(player, "CheckMechanismCrush"); player.ResetDeathCycle();
        MoveCenter(body, new Vector2(x - .1f, 100f));
        PlatformEffector2D effector = door.gameObject.AddComponent<PlatformEffector2D>(); effector.useOneWay = true;
        door.GetComponent<Collider2D>().usedByEffector = true; Physics2D.SyncTransforms();
        Invoke(player, "CheckMechanismCrush"); Check(receiver.Count == 1, "带效应器的可跳台阶不作为实心夹门");
        door.GetComponent<Collider2D>().usedByEffector = false; Object.DestroyImmediate(effector);
        player.ApplyCapabilities(PlayerCapabilityPermissions.Standby); Invoke(player, "CheckMechanismCrush");
        Check(receiver.Count == 1, "探索待命实体夹击免死");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        PlayerLifecycleRules.SetWorldPaused(true); Invoke(player, "CheckMechanismCrush");
        Check(receiver.Count == 1, "漫游暂停不结算门夹击");
        PlayerLifecycleRules.SetWorldPaused(false);
        door.RestoreMechanismDefaults(); Physics2D.SyncTransforms();
        Invoke(player, "CheckMechanismCrush"); Check(receiver.Count == 1, "默认复位移开门，清除持续夹击现场");
        wallShape.enabled = false; door.TrySetMechanismActive(true);
        Set(door, "nowtime", AudioSettings.dspTime - 2d); Invoke(door, "FixFun"); Physics2D.SyncTransforms();
        Invoke(player, "CheckMechanismCrush"); Check(receiver.Count == 1, "只有门内重叠而无固定墙不判夹击");
        float half = body.BodyBounds.extents.x;
        wallShape.enabled = true; wall.transform.position = new Vector2(x + half - .005f + 5f, 100f);
        door.transform.position = new Vector2(x - half - .5f + .005f, 100f);
        MoveCenter(body, new Vector2(x, 100f));
        doorDistance = body.GetComponent<Collider2D>().Distance(door.GetComponent<Collider2D>());
        wallDistance = body.GetComponent<Collider2D>().Distance(wallShape);
        Check(doorDistance.isOverlapped && wallDistance.isOverlapped
            && doorDistance.distance >= -.03f && wallDistance.distance >= -.03f, "正常双侧浅接触夹具未超过接触容差");
        Invoke(player, "CheckMechanismCrush"); Check(receiver.Count == 1, "正常窄缝双侧浅接触不误判死亡");
    }

    static float Place() { nextX += 100f; return nextX; }
    static GameObject New(string name, Vector2 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave }; item.transform.position = position; objects.Add(item); return item;
    }
    static void Ground(float x)
    {
        GameObject ground = New("P15 ground", new Vector2(x, 99.5f)); ground.layer = LayerMask.NameToLayer("Ground");
        ground.AddComponent<BoxCollider2D>().size = new Vector2(80f, 1f); ground.AddComponent<BaseGround>(); Physics2D.SyncTransforms();
    }
    static T Prefab<T>(string name, Vector2 position, bool initialize = true) where T : Component
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(MechanismPrefabBuilder.Folder + name + ".prefab");
        if (asset == null) throw new InvalidOperationException(name);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave; item.transform.position = position; objects.Add(item);
        T result = item.GetComponent<T>();
        if (result is BasicEntity entity && initialize) Initialize(entity);
        Physics2D.SyncTransforms(); return result;
    }
    static void Initialize(BasicEntity entity) { Invoke(entity, "Awake"); Invoke(entity, "OnEnable"); Physics2D.SyncTransforms(); }
    static Player PlayerAt(Vector2 position, bool spirit = false)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/" + (spirit ? "SpiritPlayer" : "player20") + ".prefab");
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave; item.transform.position = position; objects.Add(item);
        CharacterPhysics physics = item.GetComponent<CharacterPhysics>(); Initialize(physics);
        Player player = item.GetComponent<Player>(); Invoke(player, "Awake");
        Invoke(item.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(player, "Start");
        typeof(Player).GetMethod("UsePlayerLayer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { item });
        player.ChangeInputAsset(new PlayerInputData()); return player;
    }
    static void StandOn(BasicEntity entity, float x, float top) => MoveCenter(entity, new Vector2(x, top + entity.BodyBounds.extents.y + .005f));
    static void MoveCenter(BasicEntity entity, Vector2 center)
    {
        entity.RefreshMotionState(entity.DebugActualPosition + center - (Vector2)entity.BodyBounds.center); Physics2D.SyncTransforms();
    }
    static void Step(params BasicEntity[] actors)
    {
        Physics2D.SyncTransforms();
        foreach (BasicEntity entity in actors) Invoke(entity, "GeometricQueryPhase");
        foreach (BasicEntity entity in actors) Invoke(entity, "PhyFunUpdate");
        foreach (BasicEntity entity in actors) Invoke(entity, "SpeedCalculation");
        sidePush.Invoke(null, null); Physics2D.Simulate(Time.fixedDeltaTime);
        foreach (BasicEntity entity in actors) Invoke(entity, "DisplacementCorrection");
    }
    static bool Paused(Type type) => (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Set(object item, string field, object value)
    {
        for (Type type = item.GetType(); type != null; type = type.BaseType)
        { FieldInfo member = type.GetField(field, Hidden | BindingFlags.DeclaredOnly); if (member != null) { member.SetValue(item, value); return; } }
        throw new MissingFieldException(field);
    }
    static void Invoke(object item, string method)
    {
        for (Type type = item.GetType(); type != null; type = type.BaseType)
        { MethodInfo member = type.GetMethod(method, Hidden | BindingFlags.DeclaredOnly); if (member != null) { member.Invoke(item, null); return; } }
        throw new MissingMethodException(method);
    }
    static void Near(float actual, float expected, float tolerance, string text) => Check(Mathf.Abs(actual - expected) <= tolerance, text + $" [{actual}/{expected}]");
    static void Near(Vector2 actual, Vector2 expected, float tolerance, string text) => Check((actual - expected).magnitude <= tolerance, text + $" [{actual}/{expected}]");
    static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException("FAILED: " + text); passed.Add(text); }
    sealed class Receiver : IPlayerDeathReceiver
    {
        public int Count; public PlayerDeathCause Cause;
        public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request) { Count++; Cause = request.cause; return PlayerDeathStatus.Handled; }
    }
}
#endif
