#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>真实组装、准备付款、负面确认与原动画交接；原进度文件逐字节恢复。</summary>
public static class PlayerUpgradeFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();
    static readonly List<Object> fixtures = new List<Object>();
    static string runtimeError, previousProgressDirectory, testProgressDirectory;
    static bool expectInterruptedPlayback, sawInterruptedPlayback;
    static bool previousOptionsEnabled, finished;
    static EnterPlayModeOptions previousOptions;
    static Exception failure;
    static double deadline;
    static IEnumerator checks;
    static string[] order;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("要求独立 batch editor。");
        passed.Clear(); fixtures.Clear(); runtimeError = null; failure = null; finished = false;
        expectInterruptedPlayback = sawInterruptedPlayback = false;
        previousProgressDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        testProgressDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../.utmp/PlayerUpgradeValidation/Progress-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(testProgressDirectory);
        DataAndInitMgr.debugPlayerProgressDirectory = testProgressDirectory;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 240;
        checks = Run(); Application.logMessageReceived += RecordError;
        EditorApplication.update += Advance; EditorApplication.isPlaying = true;
    }

    static void Advance()
    {
        if (finished)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
            return;
        }
        if (!Application.isPlaying) return;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("升级与准备实际流程超时。");
            if (checks.MoveNext()) return;
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        finished = true; EditorApplication.isPlaying = false;
    }

    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        order = DataAndInitMgr.Instance.LoadLevelOrder().scenes;
        Check(order != null && order.Length >= 2, "真实关序提供教学及探索关");
        SceneChangeMgr host = SceneChangeMgr.Instance;
        PlayerUpgradeCatalog catalog = Catalog();
        Check(host.PlayerEffects.Configure(catalog), "场景宿主使用同一有效升级配置");
        host.PlayerEffects.ResetAll();
        Check(DataAndInitMgr.Instance.SavePlayerProgress(0, host.PlayerEffects), "夹具从清空能力的教学关开始");
        host.EnterSavedLevel(); while (!Ready(0)) yield return null;
        Check(SceneLevelSetup.Current.Teaching, "第一关真实教学组装完成");
        var purchaseWallet = new LevelCognition(10f);
        Check(host.PlayerEffects.TryUpgrade(PlayerUpgradeKind.MovementSpeed, purchaseWallet)
            && host.PlayerEffects.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.25f, PlayerEffectScope.Permanent),
            "统一共享记录包含待确认等级及普通道具永久效果");
        host.EnterNextLevel();
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[0] == 0
            && host.PlayerEffects.ConfirmedPermanentEffects.Count == 0, "下一关加载受理时尚不确认等级及普通永久项");
        while (!Ready(1)) yield return null;
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[0] == 1
            && host.PlayerEffects.ConfirmedPermanentEffects.Count == 1, "下一关角色及规则就绪后一起确认等级和普通永久项");
        var reconstructed = new PlayerEffectLedger(); reconstructed.Configure(catalog);
        Check(DataAndInitMgr.Instance.LoadPlayerProgress(reconstructed, out int savedIndex) && savedIndex == 1
            && reconstructed.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 1
            && reconstructed.ConfirmedPermanentEffects.Count == 1, "实际确认点将关序、等级及普通效果写入同一存档");
        float expectedMovement = Actor("body").GetComponent<CharacterPhysics>().speed * 1.2f * 1.25f;
        Near(Actor("body").GetComponent<CharacterPhysics>().EffectiveMovementSpeed, expectedMovement, "跨关后的真实角色消费一次升级及普通效果");
        Check(host.ReturnToMenu(), "已确认能力的本局可返回菜单");
        while (!AtMenu()) yield return null;
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        Near(Actor("body").GetComponent<CharacterPhysics>().EffectiveMovementSpeed, expectedMovement,
            "重新续关从存档重建，没有重复叠加等级或普通效果");

        host.PlayerEffects.ResetAll();
        Check(DataAndInitMgr.Instance.SavePlayerProgress(1, host.PlayerEffects), "准备流程夹具清空已确认能力");
        SceneLevelSetup setup = SceneLevelSetup.Current;
        Player body = Actor("body"), spirit = Actor("spirit");
        PlayerLifecycleRules rules = Rules();
        PhysicalBox box = ConfigureScene(setup, body, spirit);
        Vector2 boxHome = box.DebugActualPosition;
        box.RefreshMotionState(boxHome + Vector2.right * 3f);
        Vector2 boxMoved = box.DebugActualPosition;
        BasicPhysicalObject edited = ConfigureEditable();
        Check(host.EditEnvironmentParameter(edited, 0, .6f), "已提交环境参数走原探索编辑入口");
        catalog.Get(PlayerUpgradeKind.AirJumps).Enabled = false;
        catalog.Get(PlayerUpgradeKind.BackpackCost).Enabled = false;
        Check(host.PlayerEffects.TryUpgrade(PlayerUpgradeKind.JumpStrength, purchaseWallet)
            && host.PlayerEffects.TryUpgrade(PlayerUpgradeKind.JumpStrength, purchaseWallet), "候选前将起跳升至配置上限");
        Check(host.StartEscape(), "主动按钮受理一次准备");
        Check(host.EscapeSelectionPending && !host.IsEscapeHandoff && rules.Stage == PlayerGameplayStage.Exploration,
            "进入准备停止探索但尚不进入消散交接");
        Check(InputControlMgr.Instance.Player == null && WorldPaused() && !spirit.Permissions.movement,
            "准备暂停角色及原世界运动");
        Check(host.EscapeOffers.Count == 3, "三个可用能力抽取三项，满级及停用项不占名额");
        var chosen = new PlayerUpgradeKind[3]; var unique = new HashSet<PlayerUpgradeKind>();
        for (int i = 0; i < chosen.Length; i++)
        {
            chosen[i] = host.EscapeOffers[i].Kind;
            Check(unique.Add(chosen[i]) && chosen[i] != PlayerUpgradeKind.JumpStrength
                && chosen[i] != PlayerUpgradeKind.AirJumps && chosen[i] != PlayerUpgradeKind.BackpackCost,
                "三项不重复且排除满级和关闭项 " + i);
        }
        while (EscapeRoot() == null || !EscapeRoot().gameObject.activeInHierarchy) yield return null;
        RefreshView(); CheckLayout();
        int moveIndex = Array.IndexOf(chosen, PlayerUpgradeKind.MovementSpeed);
        CharacterPhysics bodyPhysics = body.GetComponent<CharacterPhysics>();
        float movementBase = bodyPhysics.speed, overflowBalance = rules.Cognition.Remaining;
        bodyPhysics.speed = float.MaxValue;
        Check(!host.PurchaseEscapeAbility(moveIndex) && rules.Cognition.Remaining == overflowBalance
            && host.PlayerEffects.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 0,
            "真实角色基础值导致升级溢出时付款和等级都拒绝");
        bodyPhysics.speed = movementBase;
        float fourWayBase = bodyPhysics.BaseFourWaySpeed;
        Set(bodyPhysics, "fourWaySpeed", float.MaxValue);
        bool fourWayRejected = !host.PurchaseEscapeAbility(moveIndex);
        Set(bodyPhysics, "fourWaySpeed", fourWayBase);
        Check(fourWayRejected && rules.Cognition.Remaining == overflowBalance
            && host.PlayerEffects.GetUpgradeLevel(PlayerUpgradeKind.MovementSpeed) == 0 && bodyPhysics.speed == movementBase,
            "水平基础速度正常但四向基础速度单独溢出时仍不扣款或升级");
        int version = host.EscapePreparationVersion;
        Check(!host.StartEscape() && host.EscapePreparationVersion == version, "重复开始不能重新抽候选");
        float beforeBuy = rules.Cognition.Remaining;
        Button firstChoice = ChoiceButton(0);
        Click(firstChoice); RefreshView();
        Check(host.PlayerEffects.GetUpgradeLevel(chosen[0]) == 1 && host.EscapeSelectionPending,
            "真实升级按钮购买一级后留在面板");
        Click(firstChoice); RefreshView();
        Check(host.PlayerEffects.GetUpgradeLevel(chosen[0]) == 2, "同一卡真实按钮可在本次准备连续购买第二级");
        Near(rules.Cognition.Remaining, beforeBuy - 2f, "连续购买各付完整固定价格");
        Check(host.EscapeSelectionPending && !host.IsEscapeHandoff && !host.PurchaseEscapeAbility(0),
            "满级之后仍在准备，第三次拒绝");
        Check(OffersEqual(chosen) && host.EscapePreparationVersion == version, "满级卡保留本次位置，不自动补位或刷新");
        Check(!firstChoice.interactable && firstChoice.GetComponentInChildren<Text>().text.Contains("已满级"),
            "满级卡继续显示明确状态并关闭按钮");
        float beforePreserve = rules.Cognition.Remaining;
        Click(EscapeRoot().Find("Choices/Viewport/Content/PreserveSceneButton").GetComponent<Button>()); RefreshView();
        Check(host.PreserveEscapeScene, "真实保留现场按钮单独购买");
        Near(rules.Cognition.Remaining, beforePreserve - setup.PreserveSceneCost, "保留费完整支付，不经过降费能力");
        Check(!host.PurchasePreserveScene(), "保留现场只能付款一次");
        Near(box.DebugActualPosition.x, boxMoved.x, "准备购买期间尚未复位箱子现场");
        Check(host.ToggleGameMenu() && Time.timeScale == 0f && WorldPaused(), "准备内菜单暂停沿原入口");
        Check(!host.PurchaseEscapeAbility(1) && !host.NextEscapeStep(), "菜单暂停期间购买与下一步拒绝");
        Check(host.ToggleGameMenu() && OffersEqual(chosen) && host.EscapePreparationVersion == version,
            "关闭菜单恢复同一次固定候选");
        Check(!host.CompleteEscapePreparation(true), "未经下一步确认不能直接调用最终交接");
        Click(NextButton());
        Check(host.IsEscapeHandoff && !host.EscapeNegativePending, "真实下一步按钮才进入原精神体消散");
        Check(!host.NextEscapeStep() && !host.PurchaseEscapeAbility(1), "交接期间不再次确认或购买");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Check(rules.Stage == PlayerGameplayStage.Escape && InputControlMgr.Instance.Player == body
            && InputControlMgr.Instance.InputEnabled && !WorldPaused(), "原镜头交接完成后才开放实体及世界");
        Near(box.DebugActualPosition.x, boxMoved.x, "付费保留分支沿原交接保留箱子位置");
        Near(edited.CopyEnvironmentSlots(false)[0].Value, .6f, "付费保留分支保留已提交环境参数");
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[(int)chosen[0]] == 0,
            "进入本关逃生不提前确认本关购买");
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "真实逃生死亡受理原失败流程");
        while (Rules() == rules || host.IsFailureRecovering) yield return null;
        Check(Rules().Stage == PlayerGameplayStage.Exploration && Actor("body") == body && Actor("spirit") != null,
            "死亡演出及全黑复位后重用实体并重建探索精神体");
        foreach (PlayerUpgradeDefinition item in catalog.Definitions)
            Check(host.PlayerEffects.GetUpgradeLevel(item.Kind) == 0, "失败撤销本关等级 " + item.Kind);
        Check(!host.PreserveEscapeScene && host.EscapeOffers.Count == 0, "失败清本次购买现场选择及候选");
        Near(edited.CopyEnvironmentSlots(false)[0].Value, .4f, "整关失败恢复环境制作默认");

        rules = Rules(); spirit = Actor("spirit");
        Check(host.EditEnvironmentParameter(edited, 0, .7f), "新探索可重新提交环境参数");
        box.RefreshMotionState(boxHome + Vector2.right * 4f);
        Check(host.StartEscape(), "失败后可再次打开准备");
        ReduceTo(rules.Cognition, setup.InitialCognition * .1f);
        Check(host.NextEscapeStep() && host.IsEscapeHandoff && !host.EscapeNegativePending,
            "真实余额恰好百分之十直接交接，不抽负面");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Near(box.DebugActualPosition.x, boxHome.x, "未购买保留分支沿原交接复位箱子");
        Near(edited.CopyEnvironmentSlots(false)[0].Value, .7f, "未保留现场仍保留已提交环境参数");
        Check(host.ReturnToMenu(), "第二次逃生可退出"); while (!AtMenu()) yield return null;
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;

        setup = SceneLevelSetup.Current; rules = Rules(); body = Actor("body"); spirit = Actor("spirit");
        ConfigureScene(setup, body, spirit);
        Check(host.StartEscape(), "低预算夹具先主动进入购买准备");
        while (EscapeRoot() == null || !EscapeRoot().gameObject.activeInHierarchy) yield return null;
        PlayerUpgradeKind pendingKind = host.EscapeOffers[0].Kind;
        Check(host.PurchaseEscapeAbility(0), "低预算前仍可买一个本关待确认升级");
        ReduceTo(rules.Cognition, setup.InitialCognition * .1f - .01f);
        Check(!host.EscapeNegativePending && !host.IsEscapeHandoff, "购买及余额减少不会中途触发负面");
        Check(host.NextEscapeStep() && host.EscapeNegativePending && !host.IsEscapeHandoff,
            "低于阈值在下一步统一进入负面确认");
        RefreshView();
        Check(EscapeRoot().Find("Choices/Viewport/Content/PenaltyText").gameObject.activeSelf
            && !ChoiceButton(0).gameObject.activeSelf && NextButton().GetComponentInChildren<Text>().text.Contains("确认负面"),
            "同一面板切负面说明，隐藏购买并显示最终确认");
        EscapePenaltyOffer locked = host.SelectedEscapePenalty;
        Check(locked != null && !host.NextEscapeStep() && ReferenceEquals(locked, host.SelectedEscapePenalty),
            "重复下一步保留一个已锁定负面，不重新抽取");
        Check(host.PlayerEffects.CurrentLevelEffects.Count == 0 && !host.PurchaseEscapeAbility(0)
            && !host.PurchasePreserveScene(), "负面确认前不施加效果且购买已封闭");
        Click(NextButton());
        Check(host.IsEscapeHandoff, "真实负面确认按钮进入同一原交接");
        Check(!host.ConfirmEscapePenalty() && host.PlayerEffects.CurrentLevelEffects.Count == 1,
            "确认一次只施加一个本局负面");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Near(host.PlayerEffects.MovementMultiplier, .8f, "锁定移动负面实际生效");
        Check(host.ReturnToMenu(), "本关升级及负面退出沿原清理"); while (!AtMenu()) yield return null;
        Check(host.PlayerEffects.GetUpgradeLevel(pendingKind) == 0 && host.PlayerEffects.CurrentLevelEffects.Count == 0,
            "退出撤销本关升级和负面，不保存阶段");
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;

        setup = SceneLevelSetup.Current; rules = Rules(); ConfigureScene(setup, Actor("body"), Actor("spirit"));
        rules.Cognition.TryPayAbilityCost(rules.Cognition.Remaining);
        Check(host.StartEscape() && host.EscapeNegativePending && host.EscapeOffers.Count == 0 && !host.IsEscapeHandoff,
            "探索耗尽直接负面确认，跳过正面购买");
        locked = host.SelectedEscapePenalty;
        Check(!host.StartEscape() && !host.NextEscapeStep() && ReferenceEquals(locked, host.SelectedEscapePenalty),
            "耗尽准备重复调用仍锁定同一负面");
        Check(host.ReturnToMenu(), "负面尚未确认可以退出"); while (!AtMenu()) yield return null;
        Check(host.PlayerEffects.CurrentLevelEffects.Count == 0 && !host.EscapeNegativePending && !host.IsEscapeHandoff,
            "退出未确认负面不领取、不留下交接");
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        setup = SceneLevelSetup.Current; rules = Rules(); ConfigureScene(setup, Actor("body"), Actor("spirit"));
        Check(host.StartEscape() && host.PurchaseEscapeAbility(0), "最后夹具建立本关待确认升级");
        Check(host.NextEscapeStep() && host.IsEscapeHandoff, "最后夹具进入尚未完成的消散");
        Check(host.ReturnToMenu(), "消散交接尚未结束可退出"); while (!AtMenu()) yield return null;
        double until = EditorApplication.timeSinceStartup + 2.2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        Check(!host.IsEscapeHandoff && Actor("body") == null && Actor("spirit") == null
            && InputControlMgr.Instance.Player == null && !WorldPaused(), "迟到动画不会恢复已退出的角色、世界或控制权");
        foreach (PlayerUpgradeDefinition item in catalog.Definitions)
            Check(host.PlayerEffects.GetUpgradeLevel(item.Kind) == 0, "作废交接撤销本关等级 " + item.Kind);
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        setup = SceneLevelSetup.Current; ConfigureScene(setup, Actor("body"), Actor("spirit"));
        Check(host.StartEscape() && host.PurchaseEscapeAbility(0) && host.NextEscapeStep() && host.IsEscapeHandoff,
            "动画打断夹具建立已受理但尚未完成交接");
        expectInterruptedPlayback = true;
        Actor("spirit").GetComponentInChildren<PresentationLayer>().enabled = false;
        while (!AtMenu()) yield return null;
        expectInterruptedPlayback = false;
        Check(sawInterruptedPlayback && !host.IsEscapeHandoff && host.PlayerEffects.CurrentLevelEffects.Count == 0,
            "消散被打断明确报错并返回菜单，不遗留输入锁或本关效果");
        foreach (PlayerUpgradeDefinition item in catalog.Definitions)
            Check(host.PlayerEffects.GetUpgradeLevel(item.Kind) == 0, "动画打断撤销本关等级 " + item.Kind);

        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        setup = SceneLevelSetup.Current; body = Actor("body"); spirit = Actor("spirit"); rules = Rules();
        ConfigureScene(setup, body, spirit);
        WaterRegion water = ConfigureWater(setup);
        Set(setup, "escapePenalties", new[] { new EscapePenaltyOffer(EscapePenaltyKind.WaterRise, 2f) });
        Check(host.StartEscape(), "本关只有上涨水负面时可进入准备");
        ReduceTo(rules.Cognition, setup.InitialCognition * .1f - .01f);
        Check(host.NextEscapeStep() && host.EscapeNegativePending
            && host.SelectedEscapePenalty.Kind == EscapePenaltyKind.WaterRise && !water.RisingAllowed,
            "上涨水负面锁定后仍等待确认，准备期间不涨水");
        Check(host.ConfirmEscapePenalty(), "上涨水负面确认接同一最终交接");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Check(water.RisingAllowed && host.PlayerEffects.CurrentLevelEffects.Count == 0,
            "水速负面只调整本关水，不伪造角色永久或临时效果");
        float surface = water.SurfaceY;
        typeof(WaterRegion).GetMethod("AdvanceWaterLevel", Hidden).Invoke(water, null);
        Near(water.SurfaceY - surface, 2f * Time.fixedDeltaTime, "真实水物理步按基础水速乘负面二倍上涨");
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "上涨水负面下真实死亡进入原失败流程");
        while (Rules() == rules || host.IsFailureRecovering) yield return null;
        Check(!water.RisingAllowed && WorldPaused(), "失败恢复探索后主水再次暂停");
        Near((float)typeof(WaterRegion).GetField("riseSpeed", Hidden).GetValue(water), 1f, "失败恢复主水制作基础速度，负面不跨局");
        Near(water.SurfaceY, 8094f, "失败恢复主水制作默认水面");

        foreach (PlayerUpgradeDefinition item in catalog.Definitions) item.Enabled = false;
        Check(host.StartEscape() && host.EscapeOffers.Count == 0 && !host.EscapeNegativePending,
            "人工关闭全部能力后仍能进入空候选准备");
        while (EscapeRoot() == null || !EscapeRoot().gameObject.activeInHierarchy) yield return null;
        RefreshView();
        Check(EscapeRoot().Find("Choices/Viewport/Content/EmptyPoolText").gameObject.activeSelf
            && NextButton().interactable, "空候选面板说明原因且下一步可用");
        Check(host.NextEscapeStep(), "全部关闭时允许不购买直接下一步");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Check(Rules().Stage == PlayerGameplayStage.Escape, "空候选仍通过原交接进入逃生");
        surface = water.SurfaceY;
        typeof(WaterRegion).GetMethod("AdvanceWaterLevel", Hidden).Invoke(water, null);
        Near(water.SurfaceY - surface, Time.fixedDeltaTime, "下一次正常逃生的实际水步恢复基础速度，不叠前局负面");
        Check(host.ReturnToMenu(), "空候选回归结束本局"); while (!AtMenu()) yield return null;
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        setup = SceneLevelSetup.Current; ConfigureScene(setup, Actor("body"), Actor("spirit"));
        foreach (PlayerUpgradeDefinition item in catalog.Definitions) { item.Enabled = true; item.MaxLevel = 0; }
        Check(host.StartEscape() && host.EscapeOffers.Count == 0, "全部已达零级上限时不抽任何能力");
        while (EscapeRoot() == null || !EscapeRoot().gameObject.activeInHierarchy) yield return null;
        Check(host.NextEscapeStep(), "全满级时也能跳过购买继续");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Check(Rules().Stage == PlayerGameplayStage.Escape, "全满级空候选成功进入实体逃生");
        Check(host.ReturnToMenu(), "全满级回归正常返回菜单"); while (!AtMenu()) yield return null;
    }

    static PlayerUpgradeCatalog Catalog()
    {
        PlayerUpgradeCatalog catalog = ScriptableObject.CreateInstance<PlayerUpgradeCatalog>(); fixtures.Add(catalog);
        foreach (PlayerUpgradeDefinition item in catalog.Definitions) { item.MaxLevel = 2; item.Cost = 1f; }
        return catalog;
    }

    static PhysicalBox ConfigureScene(SceneLevelSetup setup, Player body, Player spirit)
    {
        GameObject floor = NewObject("upgrade validation floor");
        SO_CPhysics config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(spirit.GetComponent<CharacterPhysics>());
        for (int i = 0; i < 32; i++) if ((config.groundLayer.value & (1 << i)) != 0) { floor.layer = i; break; }
        floor.transform.position = new Vector3(8100f, 8099.5f, 0f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(100f, 1f); floor.AddComponent<BaseGround>();
        GameObject bodyMark = NewObject("upgrade body mark"), spiritMark = NewObject("upgrade spirit mark");
        bodyMark.transform.position = new Vector3(8100f, 8103f, setup.BodyPose.z);
        spiritMark.transform.position = new Vector3(8120f, 8103f, setup.SpiritPose.z);
        Set(setup, "bodySpawn", bodyMark.transform); Set(setup, "spiritSpawn", spiritMark.transform);
        Set(setup, "preserveSceneCost", 7f);
        Set(setup, "escapePenalties", new[] { new EscapePenaltyOffer(EscapePenaltyKind.Movement, .8f) });
        body.RefreshRuntime(setup.BodyPoint); spirit.RefreshRuntime(setup.SpiritPoint);
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/box.prefab");
        GameObject item = Object.Instantiate(asset, new Vector3(8110f, 8101f, 0f), Quaternion.identity); fixtures.Add(item);
        PhysicalBox box = item.GetComponent<PhysicalBox>(); box.RefreshMotionState(new Vector2(8110f, 8101f));
        Physics2D.SyncTransforms(); return box;
    }

    static BasicPhysicalObject ConfigureEditable()
    {
        GameObject item = NewObject("upgrade retained environment"); item.SetActive(false);
        item.transform.position = new Vector2(8130f, 8100f); item.AddComponent<BoxCollider2D>();
        BasicPhysicalObject source = item.AddComponent<BasicPhysicalObject>();
        Set(source, "environmentSlotCapacity", 1);
        Set(source, "environmentParameters", new List<EnvironmentParameter> { new GroundSlowingParameter(.4f) });
        item.SetActive(true); Physics2D.SyncTransforms(); return source;
    }

    static WaterRegion ConfigureWater(SceneLevelSetup setup)
    {
        GameObject item = NewObject("upgrade validation rising water");
        item.transform.position = new Vector3(8110f, 8092f, 0f);
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>(); collider.size = new Vector2(100f, 4f); collider.isTrigger = true;
        WaterRegion water = item.AddComponent<WaterRegion>();
        GameObject maximum = NewObject("upgrade validation water limit"); maximum.transform.position = new Vector3(8110f, 8140f, 0f);
        Set(setup, "risingWater", water); Set(setup, "risingWaterMaximum", maximum.transform); Set(setup, "waterRiseSpeed", 1f);
        Physics2D.SyncTransforms(); Check(setup.PrepareRisingWater(out _), "实际矩形主水及独立上限配置准备完成");
        return water;
    }

    static void ReduceTo(LevelCognition cognition, float target)
    { Check(cognition.Remaining >= target && cognition.TryPayAbilityCost(cognition.Remaining - target), "夹具将共享余额降到指定边界"); }
    static bool OffersEqual(PlayerUpgradeKind[] previous)
    {
        if (SceneChangeMgr.Instance.EscapeOffers.Count != previous.Length) return false;
        for (int i = 0; i < previous.Length; i++) if (SceneChangeMgr.Instance.EscapeOffers[i].Kind != previous[i]) return false;
        return true;
    }
    static bool Ready(int index) => SceneLevelSetup.Current != null && SceneLevelSetup.Current.gameObject.scene.name == order[index]
        && InputControlMgr.Instance.Player != null && InputControlMgr.Instance.InputEnabled && !Loading();
    static bool AtMenu() => SceneManager.GetActiveScene().name == "BeginScenes" && SceneLevelSetup.Current == null
        && Actor("body") == null && Actor("spirit") == null && !Loading();
    static bool Loading() => (bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance);
    static Player Actor(string field) => (Player)typeof(SceneChangeMgr).GetField(field, Hidden).GetValue(SceneChangeMgr.Instance);
    static PlayerLifecycleRules Rules() => (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
    static bool WorldPaused() => (bool)typeof(BasicEntity).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static Transform EscapeRoot() => Object.FindObjectOfType<GameplayPanel>()?.transform.Find("EscapeChoiceArea");
    static Button ChoiceButton(int index) => EscapeRoot().Find("Choices/Viewport/Content/EscapeChoice" + index).GetComponent<Button>();
    static Button NextButton() => EscapeRoot().Find("Choices/NextEscapeButton").GetComponent<Button>();
    static void RefreshView() => typeof(EscapeChoiceView).GetMethod("Refresh", Hidden)
        .Invoke(EscapeRoot().GetComponent<EscapeChoiceView>(), null);
    static void Click(Button button)
    {
        Check(button != null && button.gameObject.activeInHierarchy && button.interactable, "真实按钮当前可交互 " + button.name);
        ExecuteEvents.Execute<IPointerClickHandler>(button.gameObject,
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    static void CheckLayout()
    {
        Transform root = EscapeRoot();
        ScrollRect scroll = root.Find("Choices").GetComponent<ScrollRect>();
        Check(scroll != null && scroll.content != null && scroll.viewport != null && !scroll.horizontal,
            "准备面板配置实际纵向滚动容器");
        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "三项完整卡片超出视口时产生滚动内容");
        Check(root.Find("Choices/Viewport/Content/PreserveSceneButton").GetSiblingIndex()
            < ChoiceButton(0).transform.GetSiblingIndex(), "升级卡放在保留现场和费用说明之后");
        Check(root.Find("Choices/NextEscapeButton").parent != scroll.content, "最终下一步固定在滚动内容之外");
        Check(root.GetComponent<Image>().raycastTarget && scroll.viewport.GetComponent<RectMask2D>() != null,
            "准备遮罩阻挡后方点击，超出视口卡片裁剪");
        Check(ChoiceButton(0).GetComponentInChildren<Text>().text.Contains("等级")
            && ChoiceButton(0).GetComponentInChildren<Text>().text.Contains("认知"), "实际候选卡显示等级与固定购买价格");
    }
    static GameObject NewObject(string name) { var item = new GameObject(name); fixtures.Add(item); return item; }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .003f, message);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Upgrade flow FAILED: " + message);
        passed.Add("PASS " + message); WriteReport("flow-progress.txt");
    }
    static void RecordError(string message, string trace, LogType type)
    {
        if (expectInterruptedPlayback && message == "精神体退场动画被打断，返回菜单并撤销本关准备。")
        { sawInterruptedPlayback = true; return; }
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError ??= message + "\n" + trace;
    }
    static void WriteReport(string name)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/PlayerUpgradeValidation"));
        Directory.CreateDirectory(folder); File.WriteAllLines(Path.Combine(folder, name), passed);
    }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        foreach (Object item in fixtures) if (item != null) Object.DestroyImmediate(item);
        fixtures.Clear();
        DataAndInitMgr.debugPlayerProgressDirectory = previousProgressDirectory;
        foreach (string name in new[] { "LevelProgress.txt", "PlayerProgress.json", "PlayerProgress.json.tmp" })
        {
            string path = Path.Combine(testProgressDirectory, name);
            if (File.Exists(path)) File.Delete(path);
        }
        if (Directory.Exists(testProgressDirectory)) Directory.Delete(testProgressDirectory);
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled; EditorSettings.enterPlayModeOptions = previousOptions;
        WriteReport("flow-checks.txt");
        Debug.Log($"Player upgrades flow Play Mode: {passed.Count} checks passed; success={failure == null}");
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
