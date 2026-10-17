using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>V_6 定向接口回归。注入固定深水事实及形态探针，不模拟完整穿墙、扣费或阶段流程。</summary>
public static class PlayerDeathCycleChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<Object> fixtures = new List<Object>();
    private static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/P5.3 验证死亡周期（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行死亡周期检查。");
        passed.Clear();
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = scheduler.GetValue(null);
        bool quitting = MonoPublicMgr.IsQuitting;
        try
        {
            MonoPublicMgr.IsQuitting = false;
            if ((MonoPublicMgr)previous == null)
            {
                var item = new GameObject("V6 temporary scheduler"); fixtures.Add(item);
                scheduler.SetValue(null, item.AddComponent<MonoPublicMgr>());
            }
            CheckDirectReset();
            CheckFormReturn();
            CheckRecovery();
            Debug.Log("V6 death cycle: " + passed.Count + " checks passed.");
        }
        finally
        {
            for (int i = fixtures.Count - 1; i >= 0; i--) if (fixtures[i] != null) Object.DestroyImmediate(fixtures[i]);
            fixtures.Clear(); scheduler.SetValue(null, previous); MonoPublicMgr.IsQuitting = quitting;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/EnvironmentValidation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "v6-death-cycle-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static Player Actor()
    {
        var item = new GameObject("V6 submerged spirit"); item.SetActive(false); fixtures.Add(item);
        item.AddComponent<Rigidbody2D>(); item.AddComponent<BoxCollider2D>();
        CharacterPhysics physics = item.AddComponent<CharacterPhysics>();
        var config = ScriptableObject.CreateInstance<SO_CPhysics>(); fixtures.Add(config);
        typeof(BasicEntity).GetField("cPhysics", Private).SetValue(physics, config);
        typeof(CharacterPhysics).GetMethod("Awake", Private).Invoke(physics, null);
        Player player = item.AddComponent<Player>();
        typeof(Player).GetField("identity", Private).SetValue(player, PlayerIdentity.Spirit);
        typeof(Player).GetMethod("Awake", Private).Invoke(player, null);
        var action = (MovementData)typeof(Player).GetField("actionData", Private).GetValue(player);
        State(player).InitData(physics.ReadOnly_GeometryPhysicsData, null, action);
        physics.Init(player._ActionData, State(player).EventSystem);
        typeof(EntityEnvironmentContext).GetField("<WaterFrame>k__BackingField", Private)
            .SetValue(physics.EnvironmentContext, new EntityWaterFrame(true, true));
        return player;
    }

    private static void CheckDirectReset()
    {
        Player player = Actor();
        PlayerStateMachine state = State(player);
        state.oxygen = .1f;
        player.TickLogic(.02f);
        Check(player.PendingDeath.HasValue && state.oxygen == 0f, "首次耗尽发出明确死亡请求");
        uint first = player.PendingDeath.Value.cycle;
        player.TickLogic(.02f);
        Check(player.PendingDeath.Value.cycle == first && !player.TryRequestDeath(PlayerDeathCause.Abyss), "同周期耗氧与深渊不重复请求");
        state.stamina = 37f; state.closedBecauseEmpty = true;
        typeof(PlayerStateMachine).GetField("sprintMustRelease", Private).SetValue(state, true);
        player.ResetDeathCycle();
        Check(!player.PendingDeath.HasValue && player.DeathStatus == PlayerDeathStatus.Handled, "显式重开清旧请求但不提前发新请求");
        Check(state.oxygen == 0f && state.stamina == 37f, "重开死亡周期保持氧气和体力数值");
        Check(state.closedBecauseEmpty && (bool)typeof(PlayerStateMachine).GetField("sprintMustRelease", Private).GetValue(state), "重开耗氧锁存不解除疾跑耗尽禁跑");
        Check(player.WaterFacts.submerged && player.WaterFacts.swimmingAvailable && player._ActionData.isSwimming, "重开保持水事实和游泳行为");
        player.TickLogic(.02f);
        Check(player.PendingDeath.HasValue && player.PendingDeath.Value.cycle > first
            && player.PendingDeath.Value.cause == PlayerDeathCause.OxygenDepleted, "零氧气重开后下次逻辑更新再次请求耗氧死亡");
        uint second = player.PendingDeath.Value.cycle;
        for (int i = 0; i < 10; i++) player.TickLogic(.02f);
        Check(player.PendingDeath.Value.cycle == second && !player.TryRequestDeath(PlayerDeathCause.Abyss), "新周期连续零氧气仍只请求一次");
        player.ResetDeathCycle();
        player.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
        player.TickLogic(.02f);
        Check(!player.PendingDeath.HasValue && state.oxygen == 0f, "重开不绕过资源暂停许可");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        player.TickLogic(0f);
        Check(player.PendingDeath.HasValue && player.PendingDeath.Value.cycle > second, "重新许可时零氧气立即发新周期请求");
    }

    private sealed class FormProbe : IPlayerFormRules, IPlayerFlightExecution
    {
        public int flightCommits;
        public PlayerDeathRequest lastDeath;
        public bool TrySetFlying(bool flying) => true;
        public bool TryChangeForm(Player player, bool flying, PlayerFormChangeReason reason)
        {
            bool committed = (bool)typeof(Player).GetMethod("TryCommitSpiritForm", Private).Invoke(player, new object[] { flying });
            if (committed && flying) { flightCommits++; lastDeath = player.PendingDeath.Value; }
            return committed;
        }
    }

    private static void CheckFormReturn()
    {
        Player spirit = Actor();
        spirit.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, true, true, true));
        var form = new FormProbe(); spirit.BindFormRules(form, form);
        var rules = new PlayerLifecycleRules(null, spirit); rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        spirit.BindDeathReceiver(rules);
        State(spirit).oxygen = .1f;
        spirit.TickLogic(.02f);
        PlayerDeathRequest first = form.lastDeath;
        Check(spirit._ActionData.isFlying && form.flightCommits == 1 && spirit.DeathStatus == PlayerDeathStatus.Handled && !spirit.PendingDeath.HasValue,
            "耗氧归零由实际形态提交契约确认转飞行");
        // 此夹具的 FormProbe 只提交形态；阶段退出由真实规则流程夹具核对。
        Check(State(spirit).oxygen == 0f, "死亡形态提交不补氧");
        spirit.TickLogic(0f);
        Check(form.flightCommits == 1 && State(spirit).oxygen == 0f, "零氧气飞行不再请求死亡");
        Check(spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual) && State(spirit).oxygen == 0f && spirit._ActionData.isSwimming,
            "未恢复氧气切回行走后按水事实游泳");
        spirit.TickLogic(.02f);
        Check(spirit._ActionData.isFlying && form.flightCommits == 2 && form.lastDeath.cycle > first.cycle && spirit.DeathStatus == PlayerDeathStatus.Handled,
            "切回行走后零氧气再次请求并完成新死亡周期");
        rules.ReceiveDeath(first);
        Check(form.flightCommits == 2, "重放已完成旧周期不再次切形态");
        spirit.TickLogic(0f);
        Check(form.flightCommits == 2 && !spirit.PendingDeath.HasValue, "新周期完成后零氧气飞行保持免死");
        spirit.RequestSpiritForm(false, PlayerFormChangeReason.Manual); spirit.TickLogic(0f);
        Check(form.flightCommits == 3 && form.lastDeath.cycle > first.cycle + 1, "连续零氧气形态往返可继续重开周期");
    }

    private static void CheckRecovery()
    {
        var state = new PlayerStateMachine { oxygen = .1f };
        Check(state.TickOxygen(.02f, true, false, true), "恢复回归先耗尽一次");
        Check(!state.TickOxygen(.02f, true, false, true), "未重开且未恢复时保持请求锁存");
        Check(!state.TickOxygen(.1f, false, false, true) && state.oxygen > 0f, "离水正常恢复正氧气");
        Check(state.TickOxygen(1f, true, false, true) && !state.TickOxygen(.02f, true, false, true), "恢复后再次耗尽且同周期仍只发一次");
    }

    private static PlayerStateMachine State(Player player) => (PlayerStateMachine)typeof(Player).GetField("fsm", Private).GetValue(player);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("V6 FAILED: " + message);
        passed.Add(message);
    }
}
