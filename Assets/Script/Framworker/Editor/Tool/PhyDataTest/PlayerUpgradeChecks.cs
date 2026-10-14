#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>有限升级的付款边界、共享属性和真实费用消费者；只创建临时夹具。</summary>
public static class PlayerUpgradeChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Object> objects = new List<Object>();
    static readonly List<Scene> scenes = new List<Scene>();
    static readonly List<string> passed = new List<string>();
    static int actorNumber;

    public static void RunForBatch()
    {
        try { Run(); PlayerEffectChecks.Run(); PlayerProgressChecks.Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/P20 验证能力池与三类降费（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 验证升级。");
        passed.Clear(); actorNumber = 0;
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = scheduler.GetValue(null);
        bool previousQuitting = MonoPublicMgr.IsQuitting;
        bool previousPaused = ReadPaused(typeof(BasicEntity));
        bool previousPlatformPaused = ReadPaused(typeof(Taijie));
        SimulationMode2D previousSimulation = Physics2D.simulationMode;
        try
        {
            MonoPublicMgr.IsQuitting = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            if ((MonoPublicMgr)previousScheduler == null)
                scheduler.SetValue(null, NewObject("upgrade temporary scheduler").AddComponent<MonoPublicMgr>());
            CheckCatalog();
            CheckPurchaseTransaction();
            CheckRecordLifetime();
            CheckCharacterConsumers();
            CheckMapConsumer();
            CheckBackpackConsumer();
            CheckFormAndSimulationConsumers();
            CheckThreshold();
            Debug.Log($"Player upgrades: {passed.Count} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] == null) continue;
                if (objects[i] is GameObject item)
                    foreach (BasicEntity entity in item.GetComponentsInChildren<BasicEntity>()) Invoke(entity, "OnDisable");
                Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            foreach (Scene scene in scenes) if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scenes.Clear();
            BasicEntity.SetWorldPaused(previousPaused); Taijie.SetWorldPaused(previousPlatformPaused);
            scheduler.SetValue(null, previousScheduler); MonoPublicMgr.IsQuitting = previousQuitting;
            Physics2D.simulationMode = previousSimulation;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/PlayerUpgradeValidation"));
            Directory.CreateDirectory(folder); File.WriteAllLines(Path.Combine(folder, "unit-checks.txt"), passed);
        }
    }

    static PlayerUpgradeCatalog Catalog()
    {
        PlayerUpgradeCatalog catalog = ScriptableObject.CreateInstance<PlayerUpgradeCatalog>();
        objects.Add(catalog);
        catalog.Definitions = new List<PlayerUpgradeDefinition>
        {
            new PlayerUpgradeDefinition(PlayerUpgradeKind.MovementSpeed, "移动", PlayerUpgradeOperation.Multiply, 1.2f, 3, 2f),
            new PlayerUpgradeDefinition(PlayerUpgradeKind.JumpStrength, "起跳", PlayerUpgradeOperation.Add, 2f, 3, 2f),
            new PlayerUpgradeDefinition(PlayerUpgradeKind.AirJumps, "空中跳", PlayerUpgradeOperation.Add, 1f, 3, 2f),
            new PlayerUpgradeDefinition(PlayerUpgradeKind.MapCost, "地图", PlayerUpgradeOperation.Multiply, .5f, 3, 2f),
            new PlayerUpgradeDefinition(PlayerUpgradeKind.BackpackCost, "入包", PlayerUpgradeOperation.Multiply, .5f, 3, 2f),
            new PlayerUpgradeDefinition(PlayerUpgradeKind.FormCost, "转换", PlayerUpgradeOperation.Multiply, .5f, 3, 2f)
        };
        return catalog;
    }

    static PlayerEffectLedger Ledger(PlayerUpgradeCatalog catalog = null)
    {
        var ledger = new PlayerEffectLedger();
        if (!ledger.Configure(catalog ?? Catalog())) throw new InvalidOperationException("验证夹具升级配置无效。");
        return ledger;
    }

    static void CheckCatalog()
    {
        PlayerUpgradeCatalog catalog = Catalog();
        Check(catalog.Validate(out _), "三项降费有稳定标识，旧运动条目不再使用");
        catalog.Get(PlayerUpgradeKind.MovementSpeed).Step = float.NaN;
        Check(catalog.Validate(out _), "旧运动制作值不影响降费或续关");
        Near(catalog.Get(PlayerUpgradeKind.MapCost).Evaluate(8f, 3), 1f, "降费从基础值按等级重算");
        var additive = new PlayerUpgradeDefinition(PlayerUpgradeKind.FormCost, "定额降费", PlayerUpgradeOperation.Add, -3f, 5, 2f);
        Check(additive.Validate(out _) && additive.Evaluate(5f, 3) == 0f, "定额降费不产生退款");
        catalog.Get(PlayerUpgradeKind.FormCost).Step = 1.1f;
        Check(!catalog.Validate(out _), "费用上涨的制作配置拒绝");
        catalog = Catalog(); catalog.Definitions.RemoveAt(5);
        Check(!catalog.Validate(out _), "缺少三项中的一项拒绝");
        catalog = Catalog(); catalog.Definitions.Add(catalog.Get(PlayerUpgradeKind.MapCost));
        Check(!catalog.Validate(out _), "重复降费标识拒绝");
    }

    static void CheckPurchaseTransaction()
    {
        PlayerEffectLedger ledger = Ledger(); var wallet = new LevelCognition(6f);
        for (int i = 1; i <= 3; i++)
            Check(ledger.TryUpgrade(PlayerUpgradeKind.MapCost, wallet) && ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == i,
                "同一降费可重复购买 " + i);
        Near(wallet.Remaining, 0f, "每次完整扣费，恰好足够也能购买");
        Check(!ledger.TryUpgrade(PlayerUpgradeKind.MapCost, new LevelCognition(100f)), "满级不能继续扣费");
        Check(ledger.CaptureConfirmedUpgradeLevels()[3] == 0, "本关购买尚不写入确认等级");
        var poor = new LevelCognition(1f);
        Check(!ledger.TryUpgrade(PlayerUpgradeKind.FormCost, poor) && poor.Remaining == 1f && ledger.GetUpgradeLevel(PlayerUpgradeKind.FormCost) == 0,
            "余额不足保持等级及钱包");
        foreach (PlayerUpgradeKind kind in new[] { PlayerUpgradeKind.MovementSpeed, PlayerUpgradeKind.JumpStrength, PlayerUpgradeKind.AirJumps })
            Check(!ledger.TryUpgrade(kind, poor) && ledger.GetUpgradeLevel(kind) == 0, "旧运动永久购买关闭 " + kind);
        ledger.Catalog.Get(PlayerUpgradeKind.BackpackCost).Enabled = false;
        Check(!ledger.TryUpgrade(PlayerUpgradeKind.BackpackCost, wallet), "停用降费不能购买");
        Check(!ledger.TryUpgrade((PlayerUpgradeKind)99, poor) && !ledger.TryUpgrade(PlayerUpgradeKind.FormCost, null), "未知能力及空钱包拒绝");
    }

    static void CheckRecordLifetime()
    {
        PlayerEffectLedger ledger = Ledger(); var wallet = new LevelCognition(100f);
        ledger.TryUpgrade(PlayerUpgradeKind.MapCost, wallet);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f);
        Check(ledger.TryGrantAutomaticAbilities(new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 2f)), null),
            "本场自动效果可以锁存");
        ledger.ResetCurrentLevel();
        Check(ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 0 && !ledger.AutomaticResultLocked && ledger.CurrentLevelEffects.Count == 0,
            "失败清本场体能与本关新购");
        ledger.TryUpgrade(PlayerUpgradeKind.BackpackCost, wallet); ledger.CommitLevel(); ledger.CommitLevel();
        Check(ledger.GetUpgradeLevel(PlayerUpgradeKind.BackpackCost) == 1 && ledger.CaptureConfirmedEffects().Length == 0,
            "重复通关只确认降费一次，不保存运动效果");
        int[] legacy = { 3, 4, 5, 1, 2, 3 };
        var oldEffects = new[] { new PlayerEffectRecord(PlayerEffectKind.MovementMultiplier, 5f, PlayerEffectScope.Permanent) };
        Check(ledger.RestoreConfirmed(legacy, oldEffects), "旧六槽存档兼容读取");
        Check(ledger.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 0 && ledger.GetUpgradeLevel(PlayerUpgradeKind.FormCost) == 3
            && ledger.MovementMultiplier == 1f && ledger.CaptureConfirmedEffects().Length == 0, "只保留旧档降费，旧永久运动不会继续加持");
        legacy[3] = -1;
        Check(!ledger.RestoreConfirmed(legacy, oldEffects) && ledger.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 1,
            "损坏降费快照不部分替换有效记录");
    }

    static void CheckCharacterConsumers()
    {
        Player body = Actor("player20.prefab"), spirit = Actor("SpiritPlayer.prefab");
        CharacterPhysics physics = body.GetComponent<CharacterPhysics>();
        var ledger = Ledger(); body.BindEffectLedger(ledger); spirit.BindEffectLedger(ledger);
        float basis = physics.speed;
        Set(physics, "movementSpeedLimits", new PlayerAttributeRange(0f, basis * 1.5f));
        var positive = new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 2f));
        var negative = new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, false, .75f));
        Check(ledger.TryGrantAutomaticAbilities(positive, negative), "同属性正负成对锁存");
        Near(physics.EffectiveMovementSpeed, basis * 1.5f, "先正后负后只截断最终值，中间越上限不会先截断");
        Near(physics.EffectiveFourWaySpeed, physics.BaseFourWaySpeed * 1.5f, "四向速度消费同一移动效果");
        body.BindEffectLedger(ledger); Near(physics.EffectiveMovementSpeed, basis * 1.5f, "重复绑定不重复授予");
        Check(physics.speed == basis && ledger.PositiveAbility.HasValue && ledger.NegativeAbility.HasValue, "基础值不回写且两项生效记录均保留");
        ledger.ClearAutomaticAbilities();
        Check(ledger.TryGrantAutomaticAbilities(new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.JumpStrength, true, 100f)), null), "合法大效果允许超出范围");
        Near(physics.EffectiveJumpSpeed, 100f, "起跳最终上界限制运行参数");
        ledger.ClearAutomaticAbilities();
        Check(ledger.TryGrantAutomaticAbilities(new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, true, 10f)),
            new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, false, -9f))), "跳数先加正面再减负面");
        var fsm = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(body);
        Check(fsm.EffectiveAirJumps == body.BaseAirJumps + 1, "状态机消费合成后的整数跳数");
        fsm.JumpCountToNum(); Check(fsm.jumpCount == fsm.EffectiveAirJumps, "实际补充入口读取本场跳数");
        ledger.ResetCurrentLevel(); Check(fsm.EffectiveAirJumps == body.BaseAirJumps, "失败后跳数恢复基础值");
        Check(!new PlayerAttributeRange(8f, 2f).Validate(4f, false, out _)
            && !new PlayerAttributeRange(0f, 1.5f).Validate(1, true, out _), "反向范围及小数跳数范围拒绝");
        Player highJump = Actor("player20.prefab", 20);
        var highFSM = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(highJump);
        Check(highFSM.EffectiveAirJumps == 20 && highFSM.jumpCount == 20,
            "初次 Awake 使用角色制作范围，允许高于默认十次的合法基础跳数");
    }

    static void CheckMapConsumer()
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); scenes.Add(scene);
        MapCell a = Cell(scene, "before upgrade", 0f), b = Cell(scene, "after upgrade", 4f), c = Cell(scene, "after rollback", 8f);
        var wallet = new LevelCognition(20f); PlayerEffectLedger ledger = Ledger();
        var map = new LevelMapRecord(new[] { a, b, c }, wallet, scene); map.BindEffectLedger(ledger);
        Check(map.SampleNormalMovement(a.Center), "首次点亮进入实际地图占用消费者");
        Near(wallet.Remaining, 12f, "升级前格子按基础八点占用");
        Check(ledger.TryUpgrade(PlayerUpgradeKind.MapCost, wallet), "使用同一认知钱包购买地图降费");
        Near(wallet.Remaining, 10f, "降费购买价格仍为完整两点");
        Check(map.SampleNormalMovement(b.Center), "待确认降费后的新格可以点亮");
        Near(map.ReservedCognition(a), 8f, "既有格子占用不因购买降费重算");
        Near(map.ReservedCognition(b), 4f, "后发生的地图占用即时降费");
        Check(map.TryCancel(a) && map.TryCancel(b), "取消分别释放原来实际占用");
        Near(wallet.Remaining, 18f, "释放只退地图原支付，升级购买费用不退款");
        ledger.ResetCurrentLevel(); Check(map.SampleNormalMovement(c.Center), "回滚后第三格仍可点亮");
        Near(map.ReservedCognition(c), 8f, "回滚实际地图消费者恢复基础价");
        Check(a.CognitionCost == 8f && b.CognitionCost == 8f, "地图降费不修改场景制作价");
    }

    static void CheckBackpackConsumer()
    {
        PlayerEffectLedger ledger = Ledger(); var wallet = new LevelCognition(30f);
        var bag = new CognitionBackpack(5, 6f, 4f, null, new[] { 2f }); bag.BindEffectLedger(ledger);
        BasicPhysicalObject first = Host("first property", new GroundSlowingParameter(.5f));
        var draft = new EnvironmentEditDraft(first, bag);
        Check(draft.TryExtract(false, 0, out _) && draft.TryCommit(wallet, out _), "实际性质抽取整次提交成功");
        Near(wallet.Remaining, 24f, "未升级性质入包按基础六点付费");
        Check(ledger.TryUpgrade(PlayerUpgradeKind.BackpackCost, wallet), "同一钱包买入包降费");
        BasicPhysicalObject second = Host("second property", new GroundJumpMultiplierParameter(1.2f));
        draft = new EnvironmentEditDraft(second, bag);
        Check(draft.TryExtract(false, 0, out _) && draft.TryCommit(wallet, out _), "升级后另一性质进入背包");
        Near(wallet.Remaining, 19f, "后续入包三点，购买仍扣完整两点");
        BasicPhysicalObject third = Host("point source", new GroundSlowingParameter(.5f));
        var attachment = new EnvironmentEditDraft(third, bag);
        int pointId = 0;
        foreach (CognitionBackpackItem item in bag.Items) if (item.IsCalculation) { pointId = item.Id; break; }
        Check(attachment.TryUseItem(pointId, false, 0, false, out _) && attachment.TryCommit(wallet, out _),
            "通过真实草稿确认，将初始计算点免费注入环境");
        Near(wallet.Remaining, 19f, "已有计算点出包注入不收取入包费用");
        draft = new EnvironmentEditDraft(third, bag);
        Check(draft.TryExtractCalculation(false, 0, out _) && draft.TryCommit(wallet, out _), "从环境拆出的独立计算点也整次入包");
        Near(wallet.Remaining, 17f, "计算点基础四点使用同一入包降费");
        var poor = new LevelCognition(1f);
        BasicPhysicalObject refused = Host("insufficient source", new GroundSlowingParameter(.5f));
        draft = new EnvironmentEditDraft(refused, bag); draft.TryExtract(false, 0, out _);
        int count = bag.Items.Count, version = bag.Version;
        Check(!draft.TryCommit(poor, out _) && poor.Remaining == 1f && bag.Items.Count == count
            && bag.Version == version && refused.CopyEnvironmentSlots(false)[0] != null,
            "降费后仍不足时保留环境、背包版本及余额");
    }

    static void CheckFormAndSimulationConsumers()
    {
        Player spirit = Actor("SpiritPlayer.prefab"); PlayerEffectLedger ledger = Ledger();
        var wallet = new LevelCognition(30f);
        var rules = new PlayerLifecycleRules(null, spirit, cognition: wallet, formCost: 6f, simulationCognitionPerSecond: 2f);
        rules.BindEffectLedger(ledger);
        Check(rules.BeginExploration(0f) && ledger.TryUpgrade(PlayerUpgradeKind.FormCost, wallet), "探索中的真实精神体绑定降费记录");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "成功转换进入原统一结算入口");
        Near(wallet.Remaining, 25f, "购买两点、转换三点，待确认转换降费即时生效");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "相同形态请求保持原去重行为");
        Near(wallet.Remaining, 25f, "相同形态不重复计费");
        rules.AdvanceSimulationTime(2f);
        Near(wallet.Remaining, 21f, "模拟计时仍按基础每秒两点收费");
        Check(spirit.RequestSpiritForm(true, PlayerFormChangeReason.Death), "强制转换同样走费用消费者");
        Near(wallet.Remaining, 18f, "死亡强制转换也只收一次降费后的价格");
        ledger.ResetCurrentLevel();
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.ForcedRegion), "撤销升级后可再进入模拟");
        Near(wallet.Remaining, 12f, "撤销后实际转换恢复完整六点");
    }

    static void CheckThreshold()
    {
        float[] ratios = { 0f, .25f, .375f, .5f, .75f, 1f };
        float[] positive = { 0f, 0f, .25f, .5f, 1f, 1f }, negative = { 1f, .5f, .25f, 0f, 0f, 0f };
        for (int i = 0; i < ratios.Length; i++)
        { Near(PlayerAbilityPool.PositiveProbability(ratios[i]), positive[i], "正面概率边界 " + ratios[i]);
          Near(PlayerAbilityPool.NegativeProbability(ratios[i]), negative[i], "负面概率边界 " + ratios[i]); }
        Check(PlayerAbilityPool.Triggers(1f, 1f) && !PlayerAbilityPool.Triggers(0f, 0f), "随机端点确保百分百触发和零概率不触发");
        var pool = ScriptableObject.CreateInstance<PlayerAbilityPool>(); objects.Add(pool);
        pool.Entries = new List<PlayerAbilityOffer> {
            new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, true, 1f, 0f),
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 2f, 1f),
            new PlayerAbilityOffer(PlayerAbilityKind.JumpStrength, true, 3f, 3f),
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, false, .5f) };
        Check(pool.Validate(out _) && pool.Draw(true, 0f).Kind == PlayerAbilityKind.MovementSpeed
            && pool.Draw(true, .25f).Kind == PlayerAbilityKind.JumpStrength && pool.Draw(true, 1f).Kind == PlayerAbilityKind.JumpStrength,
            "正负分组独立加权，零权重不可命中，权重边界及样本一可命中");
        pool.Entries[0].Weight = -1f;
        Check(!pool.Validate(out _), "负权重显式拒绝，不静默改成零"); pool.Entries[0].Weight = 0f;
        pool.Entries[1].Weight = pool.Entries[2].Weight = 0f;
        Check(!pool.Validate(out _), "任一分组总权重零显式拒绝");
        pool.Entries.Clear(); Check(!pool.Validate(out _), "空池显式拒绝");
        var ledger = new PlayerEffectLedger();
        Check(ledger.TryGrantAutomaticAbilities(null, null) && ledger.AutomaticResultLocked
            && !ledger.TryGrantAutomaticAbilities(null, null), "无命中也是锁定结果，不可重新抽取");
    }

    static Player Actor(string prefab, int airJumps = -1)
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing upgrade actor fixture: " + prefab);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave; objects.Add(item);
        item.transform.position = new Vector2(56000f + 200f * actorNumber++, 56000f);
        Physics2D.SyncTransforms(); Player actor = item.GetComponent<Player>();
        if (airJumps >= 0) { Set(actor, "baseAirJumps", airJumps); Set(actor, "airJumpLimits", new PlayerAttributeRange(0f, 25f)); }
        Invoke(actor.GetComponent<CharacterPhysics>(), "Awake"); Invoke(actor, "Awake");
        Invoke(actor.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(actor, "Start");
        Invoke(actor.GetComponent<CharacterPhysics>(), "OnEnable"); actor.ChangeInputAsset(new PlayerInputData());
        return actor;
    }

    static MapCell Cell(Scene scene, string name, float x)
    {
        GameObject item = NewObject("upgrade map " + name); SceneManager.MoveGameObjectToScene(item, scene);
        item.transform.position = new Vector2(x, 0f); MapCell cell = item.AddComponent<MapCell>();
        Set(cell, "width", 4f); Set(cell, "height", 4f); Set(cell, "cognitionCost", 8f); return cell;
    }

    static BasicPhysicalObject Host(string name, EnvironmentParameter property)
    {
        GameObject item = NewObject("upgrade bag " + name); item.SetActive(false);
        item.transform.position = new Vector2(57000f + objects.Count * 20f, 57000f); item.AddComponent<BoxCollider2D>();
        BasicPhysicalObject source = item.AddComponent<BasicPhysicalObject>();
        Set(source, "environmentSlotCapacity", 1); Set(source, "environmentParameters", new List<EnvironmentParameter> { property });
        item.SetActive(true); Physics2D.SyncTransforms(); return source;
    }

    static GameObject NewObject(string name)
    { var item = new GameObject(name) { hideFlags = HideFlags.DontSave }; objects.Add(item); return item; }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    static bool ReadPaused(Type type) => (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Invoke(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method == null) continue; method.Invoke(owner, null); return;
        }
        throw new MissingMethodException(name);
    }
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .002f, message);
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Upgrade FAILED: " + message); passed.Add("PASS " + message); }
}
#endif
