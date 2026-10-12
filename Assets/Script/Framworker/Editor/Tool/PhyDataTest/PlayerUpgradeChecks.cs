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
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/P4 验证升级与三类降费（Edit Mode）")]
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
        Check(catalog.Validate(out _) && catalog.Definitions.Count == 6, "配置恰好包含六个稳定能力标识");
        Near(catalog.Get(PlayerUpgradeKind.MovementSpeed).Evaluate(10f, 3), 17.28f, "固定乘法步长连续三级从基础值重算");
        Near(catalog.Get(PlayerUpgradeKind.JumpStrength).Evaluate(10f, 3), 16f, "固定加法步长连续三级从基础值重算");
        var additiveCost = new PlayerUpgradeDefinition(PlayerUpgradeKind.FormCost, "定额降费", PlayerUpgradeOperation.Add, -3f, 5, 2f);
        Check(additiveCost.Validate(out _), "认知类别允许一条固定减法规则");
        Near(additiveCost.Evaluate(5f, 3), 0f, "定额降费超过基础价后保持零，不退款");
        PlayerUpgradeDefinition jumpCount = catalog.Get(PlayerUpgradeKind.AirJumps);
        jumpCount.Step = .5f;
        Check(!catalog.Validate(out _), "跳跃次数不接受分数步长"); jumpCount.Step = 1f;
        catalog.Definitions[5] = catalog.Definitions[0];
        Check(!catalog.Validate(out _), "重复标识不能冒充缺失的第六能力");
        catalog = Catalog(); catalog.Definitions.RemoveAt(5);
        Check(!catalog.Validate(out _), "少一个固定类别时整份配置拒绝");
        catalog = Catalog(); catalog.Get(PlayerUpgradeKind.FormCost).Step = 1.1f;
        Check(!catalog.Validate(out _), "降费类别拒绝使费用上涨的配置");
        catalog = Catalog(); catalog.Get(PlayerUpgradeKind.MovementSpeed).Cost = float.NaN;
        Check(!catalog.Validate(out _), "非数值价格拒绝整份配置");
        catalog = Catalog(); catalog.Get(PlayerUpgradeKind.MovementSpeed).Step = float.MaxValue;
        Check(!catalog.Validate(out _), "等级上限溢出在购买前拒绝");
    }

    static void CheckPurchaseTransaction()
    {
        PlayerUpgradeCatalog catalog = Catalog(); PlayerEffectLedger ledger = Ledger(catalog);
        var wallet = new LevelCognition(6f);
        for (int i = 1; i <= 3; i++)
            Check(ledger.TryUpgrade(PlayerUpgradeKind.MovementSpeed, wallet)
                && ledger.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == i, $"同关第 {i} 次购买增加一级");
        Near(wallet.Remaining, 0f, "每级扣同一固定价格，恰好足够可以付完");
        Check(!ledger.CanUpgrade(PlayerUpgradeKind.MovementSpeed)
            && !ledger.TryUpgrade(PlayerUpgradeKind.MovementSpeed, new LevelCognition(100f)), "满级拒绝继续购买");
        Check(ledger.CaptureConfirmedUpgradeLevels()[0] == 0, "购买尚不写入已确认等级");
        var poor = new LevelCognition(1f);
        Check(!ledger.TryUpgrade(PlayerUpgradeKind.JumpStrength, poor)
            && ledger.GetUpgradeLevel(PlayerUpgradeKind.JumpStrength) == 0 && poor.Remaining == 1f,
            "余额不足拒绝，不部分扣款或增加等级");
        catalog.Get(PlayerUpgradeKind.JumpStrength).Cost = float.PositiveInfinity;
        Check(!ledger.TryUpgrade(PlayerUpgradeKind.JumpStrength, poor) && poor.Remaining == 1f
            && ledger.GetUpgradeLevel(PlayerUpgradeKind.JumpStrength) == 0, "运行中非法价格保持余额及等级");
        catalog.Get(PlayerUpgradeKind.JumpStrength).Cost = 2f;
        catalog.Get(PlayerUpgradeKind.JumpStrength).Enabled = false;
        Check(!ledger.CanUpgrade(PlayerUpgradeKind.JumpStrength)
            && !ledger.TryUpgrade(PlayerUpgradeKind.JumpStrength, new LevelCognition(100f)), "关闭制作开放项后不能购买");
        Check(!ledger.TryUpgrade((PlayerUpgradeKind)99, poor)
            && !ledger.TryUpgrade(PlayerUpgradeKind.AirJumps, null), "未知类别及缺少钱包拒绝");
        var invalid = Catalog(); invalid.Get(PlayerUpgradeKind.MapCost).Step = 0f;
        Check(!ledger.Configure(invalid) && ledger.Catalog == catalog, "绑定坏配置保留原配置及已有升级");
    }

    static void CheckRecordLifetime()
    {
        PlayerEffectLedger ledger = Ledger(); var wallet = new LevelCognition(100f);
        foreach (PlayerUpgradeDefinition definition in ledger.Catalog.Definitions)
            Check(ledger.TryUpgrade(definition.Kind, wallet), $"{definition.Kind} 购买可立即读取等级");
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.25f, PlayerEffectScope.Permanent);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, .8f);
        Near(ledger.GetUpgradedValue(PlayerUpgradeKind.FormCost, 8f), 4f, "待确认降费立即作用于后续费用");
        ledger.ResetCurrentLevel();
        foreach (PlayerUpgradeDefinition definition in ledger.Catalog.Definitions)
            Check(ledger.GetUpgradeLevel(definition.Kind) == 0, $"失败撤销 {definition.Kind} 本关新增");
        Check(ledger.CurrentLevelEffects.Count == 0 && ledger.ConfirmedPermanentEffects.Count == 0,
            "失败同时撤销普通道具未确认永久与临时负面");
        ledger.TryUpgrade(PlayerUpgradeKind.MovementSpeed, wallet);
        ledger.TryUpgrade(PlayerUpgradeKind.BackpackCost, wallet);
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.25f, PlayerEffectScope.Permanent);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, .8f);
        ledger.CommitLevel(); ledger.CommitLevel();
        Check(ledger.CaptureConfirmedUpgradeLevels()[0] == 1 && ledger.CaptureConfirmedUpgradeLevels()[4] == 1
            && ledger.ConfirmedPermanentEffects.Count == 1 && ledger.CurrentLevelEffects.Count == 0,
            "重复跨关确认不重复升级或叠普通永久效果，临时负面结束");
        int[] levels = ledger.CaptureConfirmedUpgradeLevels(); PlayerEffectRecord[] effects = ledger.CaptureConfirmedEffects();
        PlayerEffectLedger restored = Ledger();
        Check(restored.RestoreConfirmed(levels, effects) && restored.RestoreConfirmed(levels, effects),
            "同一已确认快照可重复重建");
        Near(restored.GetUpgradedValue(PlayerUpgradeKind.MovementSpeed, 10f) * restored.MovementMultiplier, 15f,
            "重建一次等级运算与一次普通永久倍率，没有重复堆叠");
        restored.TryUpgrade(PlayerUpgradeKind.MovementSpeed, wallet); restored.ResetCurrentLevel();
        Check(restored.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 1 && restored.ConfirmedPermanentEffects.Count == 1,
            "后续退出只撤销本关新增，保留历史确认项");
        levels[0] = -1;
        Check(!restored.RestoreConfirmed(levels, effects) && restored.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 1,
            "无效快照不能部分替换现有等级");
        restored.ResetAll();
        Check(restored.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 0 && restored.ConfirmedPermanentEffects.Count == 0
            && restored.CurrentLevelEffects.Count == 0, "清进度清掉确认等级、普通永久及本关新增");
    }

    static void CheckCharacterConsumers()
    {
        Player body = Actor("player20.prefab"), spirit = Actor("SpiritPlayer.prefab");
        CharacterPhysics bodyPhysics = body.GetComponent<CharacterPhysics>();
        CharacterPhysics spiritPhysics = spirit.GetComponent<CharacterPhysics>();
        float baseSpeed = bodyPhysics.speed, baseJump = bodyPhysics.upSpeed;
        PlayerEffectLedger ledger = Ledger(); var wallet = new LevelCognition(100f);
        body.BindEffectLedger(ledger); spirit.BindEffectLedger(ledger);
        ledger.TryUpgrade(PlayerUpgradeKind.MovementSpeed, wallet);
        ledger.TryUpgrade(PlayerUpgradeKind.JumpStrength, wallet);
        ledger.TryUpgrade(PlayerUpgradeKind.AirJumps, wallet);
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.25f, PlayerEffectScope.Permanent);
        Near(bodyPhysics.EffectiveMovementSpeed, baseSpeed * 1.2f * 1.25f, "实体消费等级升级及普通道具倍率");
        Near(spiritPhysics.EffectiveMovementSpeed, spiritPhysics.speed * 1.2f * 1.25f, "精神体从自身基础值消费共享记录");
        Near(bodyPhysics.EffectiveJumpSpeed, baseJump + 2f, "普通起跳消费者读取固定加值");
        var bodyFSM = (PlayerStateMachine)typeof(Player).GetField("fsm", Hidden).GetValue(body);
        Check(bodyFSM.EffectiveAirJumps == body.BaseAirJumps + 1, "原状态机读取升级后的额外空中跳次数");
        bodyFSM.JumpCountToNum();
        Check(bodyFSM.jumpCount == body.BaseAirJumps + 1,
            "原着地补充入口用升级后的次数补齐可用空中跳");
        MovementData action = (MovementData)typeof(Player).GetField("actionData", Hidden).GetValue(body);
        action.nowState = PlayerStateMachine.E_playerState.isOnGround; action.onMove = 1f; action.speedScale = 1.5f;
        Near((float)typeof(CharacterPhysics).GetMethod("HorizontalSpeedCalculation", Hidden).Invoke(bodyPhysics, null),
            baseSpeed * 1.2f * 1.25f * 1.5f, "真实水平动作同时消费升级、普通倍率及原疾跑倍率");
        Invoke(bodyPhysics, "Jump"); Invoke(bodyPhysics, "PhyEventUpdate");
        Near(bodyPhysics.ReadOnly_PlayersPhysicsData.verticalSpeed, baseJump + 2f, "真实起跳消费固定加值一次");
        body.BindEffectLedger(ledger); spirit.BindEffectLedger(ledger);
        Near(bodyPhysics.EffectiveMovementSpeed, baseSpeed * 1.2f * 1.25f, "重复角色绑定不重复加成");
        Check(bodyPhysics.speed == baseSpeed && bodyPhysics.upSpeed == baseJump, "升级与绑定不回写角色基础属性");
        ledger.ResetCurrentLevel();
        Near(bodyPhysics.EffectiveMovementSpeed, baseSpeed, "本关失败实际角色移速回基础值");
        Near(bodyPhysics.EffectiveJumpSpeed, baseJump, "本关失败实际起跳回基础值");
        Check(bodyFSM.EffectiveAirJumps == body.BaseAirJumps, "本关失败空中跳次数回基础值");
        bodyFSM.JumpCountToNum();
        Check(bodyFSM.jumpCount == body.BaseAirJumps,
            "升级回滚后原补充入口不保留额外跳跃次数");
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
        Check(!SceneChangeMgr.RequiresEscapePenalty(10f, 100f), "恰好初始容量百分之十不触发负面");
        Check(SceneChangeMgr.RequiresEscapePenalty(9.999f, 100f), "低于百分之十才触发负面");
        Check(!SceneChangeMgr.RequiresEscapePenalty(10.001f, 100f), "高于百分之十不触发负面");
        Check(SceneChangeMgr.RequiresEscapePenalty(0f, 100f) && SceneChangeMgr.RequiresEscapePenalty(0f, 0f),
            "耗尽及零初始容量统一需要负面确认");
    }

    static Player Actor(string prefab)
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing upgrade actor fixture: " + prefab);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave; objects.Add(item);
        item.transform.position = new Vector2(56000f + 200f * actorNumber++, 56000f);
        Physics2D.SyncTransforms(); Player actor = item.GetComponent<Player>();
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
