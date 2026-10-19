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
        deadline = EditorApplication.timeSinceStartup + 360;
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
        Check(order != null && order.Length >= 2, "真实关序提供教学和探索关");
        SceneChangeMgr host = SceneChangeMgr.Instance;
        PlayerUpgradeCatalog catalog = Catalog();
        Check(host.PlayerEffects.Configure(catalog), "宿主绑定三项降费配置");
        host.PlayerEffects.ResetAll();
        Check(DataAndInitMgr.Instance.SavePlayerProgress(0, host.PlayerEffects), "夹具使用独立进度从教学开始");
        host.EnterSavedLevel(); while (!Ready(0)) yield return null;
        Check(SceneLevelSetup.Current.Teaching && Rules().Cognition.Remaining == 0f && !host.StartEscape()
            && !host.EscapeSelectionPending && !host.PlayerEffects.AutomaticResultLocked,
            "教学实际零认知，跳过两页结算与能力池");
        Check(host.PlayerEffects.TryUpgrade(PlayerUpgradeKind.MapCost, new LevelCognition(10f)), "布置教学通关前尚未确认的降费");
        host.EnterNextLevel();
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[3] == 0, "加载受理尚不确认降费");
        while (!Ready(1)) yield return null;
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[3] == 1, "下一关角色及规则就绪后确认降费");
        var restored = new PlayerEffectLedger(); restored.Configure(catalog);
        Check(DataAndInitMgr.Instance.LoadPlayerProgress(restored, out int savedIndex) && savedIndex == 1
            && restored.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 1, "正式确认点保存三项降费等级");
        Check(host.ReturnToMenu(), "确认后可返回菜单"); while (!AtMenu()) yield return null;
        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        Check(host.PlayerEffects.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 1, "续关重建等级一次");

        host.PlayerEffects.ResetAll(); Check(DataAndInitMgr.Instance.SavePlayerProgress(1, host.PlayerEffects), "正式保存清空准备夹具等级");
        SceneLevelSetup setup = SceneLevelSetup.Current;
        Player body = Actor("body"), spirit = Actor("spirit");
        PlayerLifecycleRules rules = Rules();
        PhysicalBox box = ConfigureScene(setup, body, spirit); Vector2 boxHome = box.DebugActualPosition;
        box.RefreshMotionState(boxHome + Vector2.right * 3f);
        catalog.Get(PlayerUpgradeKind.BackpackCost).Enabled = false;
        PlayerAbilityPool pool = Resources.Load<PlayerAbilityPool>(PlayerAbilityPool.ResourceName);
        pool.Entries = new List<PlayerAbilityOffer> {
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 2f),
            new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, false, .5f) };
        ReduceTo(rules.Cognition, setup.InitialCognition * .375f + 2f);
        Animator spiritAnimator = spirit.GetComponentInChildren<PresentationLayer>().GetComponent<Animator>();
        while (!spiritAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Flight")) yield return null;
        int preparationAnimation = spiritAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        Vector3 preparationPosition = spirit.transform.position;
        Capture("p27-before-preparation.png");
        Check(host.StartEscape() && host.EscapeSelectionPending && !host.IsEscapeHandoff,
            "主动结束先进入一级菜单，尚未开始消散");
        while (EscapeRoot() == null || !EscapeRoot().gameObject.activeInHierarchy) yield return null;
        Check(EscapeRoot().Find("Choices/Tabs") == null && !EscapeRoot().Find("AbilityFog").gameObject.activeSelf,
            "一级保留纸样，无编号页签且不显示迷雾");
        while (!NextButton().interactable) yield return null;
        Check(spiritAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == preparationAnimation
            && spirit.transform.position == preparationPosition, "一级菜单保留进入准备前的飞行动作与位置");
        RefreshView(); CheckLayout();
        Check(host.EscapeOffers.Count == 3 && host.EscapeOffers[0].Kind == PlayerUpgradeKind.MapCost
            && host.EscapeOffers[1].Kind == PlayerUpgradeKind.BackpackCost && host.EscapeOffers[2].Kind == PlayerUpgradeKind.FormCost,
            "一级固定列全三项，停用项仍显示");
        Check(!ChoiceButton(1).interactable && StateText(1).text == "未开放", "停用项给出明确状态");
        int version = host.EscapePreparationVersion;
        Check(!host.StartEscape() && host.EscapePreparationVersion == version, "重复结束不重开准备");
        Text hint = EscapeRoot().Find("Choices/CostPage/Content/PurchaseHintText").GetComponent<Text>();
        string authoredHint = hint.text; hint.text = "人工修改的静态说明";
        RefreshView(); Check(hint.text == "人工修改的静态说明", "运行时不覆盖预设静态说明"); hint.text = authoredHint;
        float before = rules.Cognition.Remaining;
        Click(ChoiceButton(0)); RefreshView(); Click(ChoiceButton(0)); RefreshView();
        Check(host.PlayerEffects.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 2 && !ChoiceButton(0).interactable
            && StateText(0).text == "已满级", "同一按钮重复购买两次，满级后仍保留卡片");
        Near(rules.Cognition.Remaining, before - 2f, "重复购买逐次扣完整价格");
        Check(!host.CompleteEscapePreparation(), "一级菜单不能直接交接");
        Capture("p20-cost-page.png");
        SeedResult(true, true);
        Click(NextButton()); RefreshView();
        CanvasGroup reveal = EscapeRoot().Find("Choices").GetComponent<CanvasGroup>();
        Check(reveal.alpha == 0f && !reveal.interactable && !BeginButton().interactable,
            "二级先铺雾，文字透明且确认按钮锁住");
        Check(!EscapeRoot().Find("Choices").GetComponent<Image>().enabled
            && EscapeRoot().Find("AbilityFog").gameObject.activeSelf && EscapeRoot().GetComponent<Image>().raycastTarget,
            "二级关闭纸底并开启雾面，整屏底层继续阻挡穿透");
        BeginButton().onClick.Invoke();
        Check(!host.IsEscapeHandoff && host.EscapeSelectionPending, "显字前程序化点击同样不能提前交接");
        float fogHalfTime = Time.unscaledTime + EscapeRoot().GetComponent<EscapeChoiceView>().fogExpandSeconds * .5f;
        while (Time.unscaledTime < fogHalfTime) yield return null;
        Check(reveal.alpha == 0f && !BeginButton().interactable, "中心扩散中仍未显字");
        Check(spiritAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == preparationAnimation
            && spirit.transform.position == preparationPosition, "二级迷雾展开时精神体仍保持原样");
        Check(host.EscapeAutomaticReady && host.EscapeSelectionPending && !host.IsEscapeHandoff,
            "下一步锁存购买后余额结果并展示二级菜单");
        Check(host.PlayerEffects.PositiveAbility.HasValue && host.PlayerEffects.NegativeAbility.HasValue,
            "购买后百分之三十七点五认知可以独立同时触发正负");
        Check(EscapeRoot().Find("Choices/AbilityPage").gameObject.activeSelf
            && EscapeRoot().Find("Choices/AbilityPage/PositiveResult").gameObject.activeSelf
            && EscapeRoot().Find("Choices/AbilityPage/NegativeResult").gameObject.activeSelf, "同属性正负均显示生效");
        Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed, body.GetComponent<CharacterPhysics>().speed,
            "先乘正面二再乘负面零点五，合成后不回写基础值");
        int lockedVersion = host.EscapePreparationVersion;
        Check(host.ToggleGameMenu() && !host.NextEscapeStep() && !host.PurchaseEscapeAbility(2), "暂停期间不能更改结算");
        while (reveal.alpha < 1f) yield return null;
        Check(Time.timeScale == 0f && reveal.alpha == 1f && !BeginButton().interactable,
            "暂停时按真实时间完成显字，但确认按钮保持禁用");
        Check(host.ToggleGameMenu() && host.EscapePreparationVersion == lockedVersion, "关闭菜单保留同次结果");
        // 菜单显隐由原UI队列处理；下一次人工点击在返回后的帧验证。
        yield return null;
        RefreshView();
        while (!BeginButton().interactable) { yield return null; RefreshView(); }
        Check(spiritAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == preparationAnimation
            && spirit.transform.position == preparationPosition, "二级显字完成及暂停返回仍保留原动作与位置");
        Check(!ChoiceButton(2).interactable && StateText(2).text == "购买已关闭" && !host.PurchaseEscapeAbility(2),
            "没有回页页签，原购买入口仍封闭，不改变已抽取概率");
        Check(!host.NextEscapeStep(), "已确定结果不可再抽");
        Check(reveal.alpha == 1f && host.EscapePreparationVersion == lockedVersion, "暂停返回不重播迷雾或重新抽取");
        Capture("p20-both-effects.png");
        Click(BeginButton());
        Check(!host.IsEscapeHandoff && !BeginButton().interactable, "确认后先淡出并阻止重复点击");
        while (!host.IsEscapeHandoff) yield return null;
        Check(spiritAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"), "正式确认后才由原动作切入消散动画");
        Check(host.IsEscapeHandoff && !host.BeginEscapeFromAbilities(), "开始逃生沿原消散且不能重复确认");
        while (host.IsEscapeHandoff || Actor("spirit") != null) yield return null;
        Check(rules.Stage == PlayerGameplayStage.Escape && InputControlMgr.Instance.Player == body && !WorldPaused(),
            "动画及镜头完成后开放原实体和世界");
        Near(box.DebugActualPosition.x, boxHome.x, "开始逃生沿原默认复位箱子现场");
        Check(host.PlayerEffects.CaptureConfirmedUpgradeLevels()[3] == 0, "进入逃生不提前确认本关降费");
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "原死亡入口受理失败");
        while (Rules() == rules || host.IsFailureRecovering) yield return null;
        Check(host.PlayerEffects.GetUpgradeLevel(PlayerUpgradeKind.MapCost) == 0 && !host.PlayerEffects.AutomaticResultLocked
            && host.EscapeOffers.Count == 0, "整关失败清购买及本场正负结果");
        Near(body.GetComponent<CharacterPhysics>().EffectiveMovementSpeed, body.GetComponent<CharacterPhysics>().speed, "失败后基础体能恢复");

        rules = Rules(); setup = SceneLevelSetup.Current;
        ReduceTo(rules.Cognition, setup.InitialCognition * .375f);
        Check(host.StartEscape(), "第二次准备使用新流程"); SeedResult(false, false);
        Check(host.NextEscapeStep(), "正负都未触发仍正常进入二级页"); yield return null; RefreshView();
        Check(host.EscapeAutomaticReady && !host.PlayerEffects.PositiveAbility.HasValue && !host.PlayerEffects.NegativeAbility.HasValue
            && EscapeRoot().Find("Choices/AbilityPage/EmptyResultText").gameObject.activeInHierarchy
            && EscapeRoot().Find("Choices/AbilityPage/EmptyResultText").GetComponent<TMPro.TMP_Text>().text == "当前无任何效果生效",
            "无命中明确显示当前无任何效果生效");
        while (!BeginButton().interactable) yield return null;
        Capture("p20-no-effects.png");
        Check(host.BeginEscapeFromAbilities(), "无命中也必须由二级页开始逃生");
        while (host.IsEscapeHandoff) yield return null;
        Check(host.ReturnToMenu(), "无效果流程结束后退出"); while (!AtMenu()) yield return null;

        host.EnterSavedLevel(); while (!Ready(1)) yield return null;
        rules = Rules(); rules.Cognition.TryPayAbilityCost(rules.Cognition.Remaining);
        typeof(SceneChangeMgr).GetMethod("AdvanceLevelCognition", Hidden).Invoke(host, null);
        Check(host.EscapeSelectionPending && !host.EscapeAutomaticReady && host.EscapeOffers.Count == 3,
            "认知耗尽也先打开列全降费的一级页");
        Check(host.NextEscapeStep() && !host.PlayerEffects.PositiveAbility.HasValue && host.PlayerEffects.NegativeAbility.HasValue,
            "零余额必定只触发负面，不因购买跳过二级页");
        Check(host.BeginEscapeFromAbilities() && host.IsEscapeHandoff && host.ReturnToMenu(), "交接未完成可以退出作废本次效果");
        while (!AtMenu()) yield return null;
        Check(!host.PlayerEffects.AutomaticResultLocked && host.PlayerEffects.CurrentLevelEffects.Count == 0, "退出清本场体能记录");
    }

    static void SeedResult(bool positive, bool negative)
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            UnityEngine.Random.InitState(seed);
            bool p = UnityEngine.Random.value < .25f, n = UnityEngine.Random.value < .25f;
            if (p == positive && n == negative) { UnityEngine.Random.InitState(seed); return; }
        }
        throw new InvalidOperationException("找不到固定夹具随机种子。");
    }
    static void Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        GameMenuInputChecks.Capture(name);
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/PlayerUpgradeValidation"));
        Directory.CreateDirectory(folder); File.Copy(".utmp/GameUIValidation/" + name, Path.Combine(folder, name), true);
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
    static readonly string[] Rows = { "MapCostRow", "BackpackCostRow", "FormCostRow" };
    static Button ChoiceButton(int index) => EscapeRoot().Find("Choices/CostPage/Content/" + Rows[index] + "/" + Rows[index] + "PurchaseButton").GetComponent<Button>();
    static Text StateText(int index) => EscapeRoot().Find("Choices/CostPage/Content/" + Rows[index] + "/" + Rows[index] + "StateText").GetComponent<Text>();
    static Button BeginButton() => EscapeRoot().Find("Choices/BeginEscapeButton").GetComponent<Button>();
    static Button NextButton() => EscapeRoot().Find("Choices/NextEscapeButton").GetComponent<Button>();
    static void RefreshView() => typeof(EscapeChoiceView).GetMethod("Refresh", Hidden)
        .Invoke(EscapeRoot().GetComponent<EscapeChoiceView>(), null);
    static void Click(Button button)
    {
        Check(button != null && button.gameObject.activeInHierarchy && button.interactable,
            $"真实按钮当前可交互 {button.name} (active={button.gameObject.activeInHierarchy}, interactable={button.interactable}, timeScale={Time.timeScale})");
        ExecuteEvents.Execute<IPointerClickHandler>(button.gameObject,
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    static void CheckLayout()
    {
        Transform root = EscapeRoot();
        ScrollRect scroll = root.Find("Choices/CostPage").GetComponent<ScrollRect>();
        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Check(scroll.content.rect.height > scroll.viewport.rect.height && !scroll.horizontal, "完整卡片超出视口时可纵向滚动");
        Check(root.GetComponent<Image>().raycastTarget && scroll.viewport.GetComponent<RectMask2D>() != null, "遮罩阻挡后方点击且视口裁剪");
        RectTransform positive = (RectTransform)root.Find("Choices/AbilityPage/PositiveResult"), negative = (RectTransform)root.Find("Choices/AbilityPage/NegativeResult");
        Check(positive.anchoredPosition.y > negative.anchoredPosition.y, "预设正面排在负面上方");
        Check(root.Find("Choices/NextEscapeButton").parent != scroll.content && root.Find("Choices/BeginEscapeButton").parent != scroll.content,
            "下一步与开始逃生固定在滚动区域外");
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
