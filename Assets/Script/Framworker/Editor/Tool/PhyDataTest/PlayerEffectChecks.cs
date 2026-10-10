using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>P4 最小效果记录与实际角色消费检查；只创建临时角色，不保存场景或预设。</summary>
public static class PlayerEffectChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static int group;

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/P4 验证最小效果记录（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear(); group = 0;
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
                var item = NewObject("P4 temporary scheduler");
                scheduler.SetValue(null, item.AddComponent<MonoPublicMgr>());
            }
            CheckRecordLifetime();
            CheckSharedCharacterValues();
            CheckActualConsumption();
            Debug.Log($"Player effects: {passed.Count} checks passed.");
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
            Directory.CreateDirectory(".utmp/PlayerEffectValidation");
            File.WriteAllLines(".utmp/PlayerEffectValidation/checks.txt", passed);
        }
    }

    private static void CheckRecordLifetime()
    {
        var ledger = new PlayerEffectLedger();
        Near(ledger.MovementMultiplier, 1f, "空记录不改变基础水平速度");
        Near(ledger.JumpMultiplier, 1f, "空记录不改变基础普通跳跃");
        Check(ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f, PlayerEffectScope.Permanent)
            && ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f)
            && ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, .8f), "有限永久、正面和负面效果立即领取");
        Near(ledger.MovementMultiplier, .96f, "同类有限倍率按有效记录相乘");
        Near(ledger.JumpMultiplier, 1.2f, "不同类型分开汇总");
        Check(ledger.CurrentLevelEffects.Count == 3 && ledger.ConfirmedPermanentEffects.Count == 0,
            "新增永久先属于本关新增，不提前成为已确认永久");
        ledger.ResetCurrentLevel();
        Check(ledger.CurrentLevelEffects.Count == 0 && ledger.ConfirmedPermanentEffects.Count == 0,
            "失败清所有新增，包含未确认永久");
        Near(ledger.MovementMultiplier, 1f, "失败撤销本局正负面水平倍率");
        Near(ledger.JumpMultiplier, 1f, "失败撤销新增跳跃倍率");

        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f, PlayerEffectScope.Permanent);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f);
        ledger.CommitLevel();
        Check(ledger.CurrentLevelEffects.Count == 0 && ledger.ConfirmedPermanentEffects.Count == 1,
            "通关结束本局项，仅移动新增永久记录");
        Near(ledger.MovementMultiplier, 1.2f, "通关确认不重复施加永久效果");
        Near(ledger.JumpMultiplier, 1f, "通关结束本局跳跃效果");
        ledger.CommitLevel();
        Check(ledger.ConfirmedPermanentEffects.Count == 1 && ledger.MovementMultiplier == 1.2f,
            "重复确认不增加记录或再次施加");
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, .8f);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f, PlayerEffectScope.Permanent);
        ledger.ResetCurrentLevel();
        Check(ledger.ConfirmedPermanentEffects.Count == 1 && ledger.CurrentLevelEffects.Count == 0,
            "下一关失败保留此前已确认永久");
        Near(ledger.MovementMultiplier, 1.2f, "此前已确认倍率失败后继续生效");
        Near(ledger.JumpMultiplier, 1f, "本关未确认永久被失败撤销");
        Check(!ledger.GrantEffect((PlayerEffectKind)99, 1f)
            && !ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1f, (PlayerEffectScope)99)
            && !ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, -1f)
            && !ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, float.NaN)
            && !ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, float.PositiveInfinity),
            "未知种类、范围和非法倍率拒绝，不产生部分记录");
        Check(ledger.CurrentLevelEffects.Count == 0 && ledger.ConfirmedPermanentEffects.Count == 1,
            "拒绝不改变记录边界");
    }

    private static void CheckSharedCharacterValues()
    {
        Player body = Actor("player20.prefab");
        Player spirit = Actor("SpiritPlayer.prefab");
        var ledger = new PlayerEffectLedger();
        CharacterPhysics bodyPhysics = body.GetComponent<CharacterPhysics>();
        CharacterPhysics spiritPhysics = spirit.GetComponent<CharacterPhysics>();
        float bodyBase = bodyPhysics.speed, spiritBase = spiritPhysics.speed;
        float bodyJump = bodyPhysics.upSpeed, spiritJump = spiritPhysics.upSpeed;
        body.BindEffectLedger(ledger); spirit.BindEffectLedger(ledger);
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f, PlayerEffectScope.Permanent);
        Near(bodyPhysics.EffectiveMovementSpeed, bodyBase * 1.2f, "本体从自己的基础值消费共享水平倍率");
        Near(spiritPhysics.EffectiveMovementSpeed, spiritBase * 1.2f, "精神体共读同份新增记录");
        Near(bodyPhysics.EffectiveJumpSpeed, bodyJump * 1.2f, "本体读取有效普通跳跃值");
        Near(spiritPhysics.EffectiveJumpSpeed, spiritJump * 1.2f, "精神体从自身基础跳跃值重算");
        body.BindEffectLedger(ledger); body.BindEffectLedger(ledger); spirit.BindEffectLedger(ledger);
        Near(bodyPhysics.EffectiveMovementSpeed, bodyBase * 1.2f, "重复绑定不累加水平效果");
        Near(spiritPhysics.EffectiveJumpSpeed, spiritJump * 1.2f, "重复绑定不再次施加跳跃效果");
        Check(bodyPhysics.speed == bodyBase && bodyPhysics.upSpeed == bodyJump
            && spiritPhysics.speed == spiritBase && spiritPhysics.upSpeed == spiritJump,
            "绑定和领取不改角色制作基础配置");
        ledger.CommitLevel();
        Near(bodyPhysics.EffectiveMovementSpeed, bodyBase, "本局效果通关结束，无需逐角色减回数值");
        Near(spiritPhysics.EffectiveJumpSpeed, spiritJump * 1.2f, "确认永久后共享角色不重复施加");
        ledger.ResetCurrentLevel();
        Near(bodyPhysics.EffectiveJumpSpeed, bodyJump * 1.2f, "后续失败保留已确认永久的有效值");
        body.BindEffectLedger(null);
        Near(bodyPhysics.EffectiveJumpSpeed, bodyJump, "解绑回自己的基础值，不回写另一角色记录");
        Near(spiritPhysics.EffectiveJumpSpeed, spiritJump * 1.2f, "单角色解绑不清共用记录");
    }

    private static void CheckActualConsumption()
    {
        Player body = Actor("player20.prefab");
        CharacterPhysics physics = body.GetComponent<CharacterPhysics>();
        MovementData action = (MovementData)typeof(Player).GetField("actionData", Hidden).GetValue(body);
        var ledger = new PlayerEffectLedger();
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f);
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.2f);
        body.BindEffectLedger(ledger);
        action.nowState = PlayerStateMachine.E_playerState.isOnGround;
        action.onMove = 1f; action.speedScale = 2f;
        Near(Horizontal(physics), physics.speed * 1.2f * 2f, "实际水平消费叠行为疾跑倍率，保持乘法语义");
        Near(physics.Mobility, physics.speed * 1.2f, "环境算法只读有效基础移动能力");

        var ground = NewObject("jump response").AddComponent<BaseGround>();
        Collider2D collider = ground.gameObject.AddComponent<BoxCollider2D>();
        typeof(BaseGround).GetField("jumpHeightNum", Hidden).SetValue(ground, .25f);
        physics.EnvironmentContext.RefreshEnvironment(collider, null);
        int sequence = physics.ReadOnly_PlayersPhysicsData.jumpSequence;
        Invoke(physics, "Jump"); Invoke(physics, "PhyEventUpdate");
        Near(physics.ReadOnly_PlayersPhysicsData.verticalSpeed, physics.upSpeed * 1.2f * 1.25f,
            "实际普通起跳叠脚下加成，只消费一次");
        Check(physics.ReadOnly_PlayersPhysicsData.jumpSequence == sequence + 1, "效果不伪造第二次起跳");
        Invoke(physics, "PhyEventUpdate");
        Check(physics.ReadOnly_PlayersPhysicsData.jumpSequence == sequence + 1, "重复消费相位不重新施加起跳");
        Invoke(physics, "WallJump"); Invoke(physics, "PhyEventUpdate");
        Near(physics.ReadOnly_PlayersPhysicsData.verticalSpeed, physics.upSpeed,
            "普通跳跃效果不偷改墙跳速度");

        var source = NewObject("passive flow source").AddComponent<BasicPhysicalObject>();
        physics.AddSpeedStatus(source.EnvironmentRegistration, new Vector2(3f, 2f));
        var passive = (Dictionary<EnvironmentRegistration, Vector2>)EnvironmentIntegrationChecks.EffectField(physics, "stateVelocities");
        ledger.ResetCurrentLevel();
        Check(passive[source.EnvironmentRegistration] == new Vector2(3f, 2f), "效果回滚不修改环境持续水流参数");
        Near(Horizontal(physics), physics.speed * 2f, "回滚后实际主动消费回基础值，疾跑倍率仍保留");

        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, .8f);
        action.nowState = PlayerStateMachine.E_playerState.swimming;
        action.onMove = 1f; action.verticalInput = -1f;
        float fourWay = (float)typeof(CharacterPhysics).GetField("fourWaySpeed", Hidden).GetValue(physics);
        Near(Horizontal(physics), fourWay * .8f, "游泳水平消费移动倍率");
        PlayerPhysicsData data = (PlayerPhysicsData)typeof(BasicEntity).GetField("playerPhysicsData", Hidden).GetValue(physics);
        data.nowPhyNum = .2f;
        Invoke(physics, "VerticalTransmission");
        Near(data.verticalSpeed, -fourWay * .8f * 1.2f, "游泳竖直同时消费移动倍率，环境修饰仍只乘一次");
        action.nowState = PlayerStateMachine.E_playerState.flying;
        Vector2 flight = (Vector2)typeof(CharacterPhysics).GetProperty("CollisionFreeVelocity", Hidden).GetValue(physics);
        Near(flight.x, fourWay * .8f / Mathf.Sqrt(2f), "飞行水平沿归一方向消费移动倍率");
        Near(flight.y, -fourWay * .8f / Mathf.Sqrt(2f), "飞行竖直沿同一归一方向消费移动倍率");
    }

    private static Player Actor(string prefab)
    {
        PlayerLifecycleRules.SetWorldPaused(false);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing P4 fixture: " + prefab);
        GameObject item = Object.Instantiate(asset); item.hideFlags = HideFlags.DontSave;
        item.transform.position = new Vector2(40000f + 200f * group++, 40000f);
        objects.Add(item); Physics2D.SyncTransforms();
        Player player = item.GetComponent<Player>();
        Invoke(player.GetComponent<CharacterPhysics>(), "Awake"); Invoke(player, "Awake");
        Invoke(player.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(player, "Start");
        Invoke(player.GetComponent<CharacterPhysics>(), "OnEnable");
        player.ChangeInputAsset(new PlayerInputData());
        return player;
    }
    private static GameObject NewObject(string name)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        objects.Add(item); return item;
    }
    private static float Horizontal(CharacterPhysics physics) =>
        (float)typeof(CharacterPhysics).GetMethod("HorizontalSpeedCalculation", Hidden).Invoke(physics, null);
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
        if (!condition) throw new InvalidOperationException("P4 FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < .002f, message + $" [{actual} == {expected}]");
}
