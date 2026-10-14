#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>在真实关卡、角色预设与 Animator 中核对死亡等待、退出作废和探索交接；不保存夹具场景。</summary>
public static class DeathAnimationFlowChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();
    static readonly List<Object> fixtures = new List<Object>();
    static string progressDirectory, previousProgressDirectory, runtimeError;
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
        progressDirectory = Path.GetFullPath(Path.Combine(".utmp/DeathExitValidation", "Save-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(progressDirectory);
        DataAndInitMgr.debugPlayerProgressDirectory = progressDirectory;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity", OpenSceneMode.Single);
        deadline = EditorApplication.timeSinceStartup + 180;
        checks = Run();
        Application.logMessageReceived += RecordError;
        EditorApplication.update += Advance;
        EditorApplication.isPlaying = true;
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
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("死亡／退场流程验收超时。");
            if (checks.MoveNext()) return;
        }
        catch (Exception error) { failure = error; Debug.LogException(error); }
        finished = true;
        EditorApplication.isPlaying = false;
    }

    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        order = DataAndInitMgr.Instance.LoadLevelOrder().scenes;
        Check(order != null && order.Length >= 2, "实际顺序包含教学及探索关");
        Enter(1);
        while (!Ready(1)) yield return null;
        Player body = Actor("body"), spirit = Actor("spirit");
        SceneLevelSetup setup = SceneLevelSetup.Current;
        PlayerLifecycleRules rules = Rules();
        CheckNativePlayback(body);
        CheckNativePlayback(spirit);
        PhysicalBox box = ConfigureFloor(setup, body, spirit);
        Vector2 boxHome = box.DebugActualPosition;
        box.RefreshMotionState(boxHome + Vector2.right * 3f);
        Vector2 boxScene = box.DebugActualPosition;
        Check(SceneChangeMgr.Instance.SetSimulation(true, PlayerFormChangeReason.ForcedRegion), "真实精神体进入模拟");
        float cognition = rules.Cognition.Remaining;
        uint simulationPlayback = Playback(spirit);
        Check(spirit.TryRequestDeath(PlayerDeathCause.Abyss), "模拟遇险同步受理退出试验");
        PlayerDeathRequest death = CurrentDeath(spirit, PlayerDeathCause.Abyss);
        Check(!rules.IsSimulation && !rules.DeathPending && !spirit.PendingDeath.HasValue
            && spirit.ResolveFourWayAbility() == FourWayMode.Flying, "当次调用立即回漫游，不挂死亡等待");
        Check(Playback(spirit) == simulationPlayback, "模拟遇险不启动消散动画");
        Near(rules.Cognition.Remaining, cognition - setup.FormChangeCost, "立即转换只收一次原费用");
        Check(!spirit.TryRequestDeath(PlayerDeathCause.Electrocuted), "同周期电击不追加请求");
        rules.AdvanceSimulationTime(4f);
        Near(rules.Cognition.Remaining, cognition - setup.FormChangeCost, "回漫游后停止模拟计费");
        Check(body.Permissions.motionUpdates && !body.Permissions.movement && WorldPaused(), "待命实体不陪同冻结，世界按漫游暂停");
        Near(box.DebugActualPosition, boxScene, "模拟退出保留现场");
        Check(rules.ReceiveDeath(death) == PlayerDeathStatus.Handled, "旧完成周期重放直接去重");
        Near(rules.Cognition.Remaining, cognition - setup.FormChangeCost, "旧周期重放不重复扣费");

        Check(SceneChangeMgr.Instance.SetSimulation(true, PlayerFormChangeReason.ForcedRegion), "同一精神体恢复行走并重开死亡周期");
        cognition = rules.Cognition.Remaining;
        Check(SceneChangeMgr.Instance.ToggleGameMenu(), "模拟暂停菜单可打开");
        Presentation(spirit).enabled = false;
        Check(spirit.TryRequestDeath(PlayerDeathCause.Electrocuted), "暂停中模拟遇险仍立即回漫游");
        Check(!rules.DeathPending && !spirit.PendingDeath.HasValue, "模拟退出不依赖启用的死亡动画组件");
        Presentation(spirit).enabled = true;
        PlayerDeathRequest resetDeath = CurrentDeath(spirit, PlayerDeathCause.Electrocuted);
        Check(resetDeath.cycle > death.cycle, "重复死亡使用递增周期");
        Check(SceneChangeMgr.Instance.GameMenuOpen && Time.timeScale == 0f && !rules.IsSimulation,
            "同步转换保留菜单暂停");
        SceneChangeMgr.Instance.ToggleGameMenu();
        Check(WorldPaused() && !rules.IsSimulation, "关闭菜单后按漫游保持世界暂停");
        Near(rules.Cognition.Remaining, cognition - setup.FormChangeCost, "再次遇险只扣一次转换费");
        spirit.RefreshRuntime(setup.SpiritPoint);
        Check(!rules.DeathPending && rules.ReceiveDeath(resetDeath) == PlayerDeathStatus.Handled
            && Playback(spirit) == simulationPlayback, "刷新后旧同步请求不重播或重复转换");
        Near(rules.Cognition.Remaining, cognition - setup.FormChangeCost, "刷新后的旧请求不再扣费");

        Vector2 bodyBefore = body.GetComponent<CharacterPhysics>().DebugActualPosition;
        box.RefreshMotionState(boxScene);
        InputControlMgr.Instance.SnapCameraToPlayer();
        Check(SceneChangeMgr.Instance.StartEscape(), "现有购买准备入口保持现场并暂停");
        Near(box.DebugActualPosition, boxScene, "准备面板未提前复位现场");
        Near(body.GetComponent<CharacterPhysics>().DebugActualPosition, bodyBefore, "准备面板未提前搬移实体");
        cognition = rules.Cognition.Remaining;
        float upgradeCost = SceneChangeMgr.Instance.EscapeOffers[0].Cost;
        Check(SceneChangeMgr.Instance.PurchaseEscapeAbility(0)
            && SceneChangeMgr.Instance.EscapeSelectionPending && !SceneChangeMgr.Instance.IsEscapeHandoff,
            "购买升级后保持准备，下一步前不受理精神体消散");
        Check((SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities()), "下一步最终确认后受理精神体消散");
        float paid = rules.Cognition.Remaining;
        float waterBefore = setup.RisingWater.SurfaceY;
        Near(paid, cognition - upgradeCost, "购买只收原能力价，不额外收转换费");
        Check(SceneChangeMgr.Instance.IsEscapeHandoff && !SceneChangeMgr.Instance.EscapeSelectionPending
            && !spirit.PendingDeath.HasValue && InputControlMgr.Instance.Player == null,
            "退场隐藏已确认选择且不发送死亡请求");
        Check(!SceneChangeMgr.Instance.PurchaseEscapeAbility(0) && !SceneChangeMgr.Instance.CompleteEscapePreparation(),
            "退场重复确认不重播或重复购买");
        Near(rules.Cognition.Remaining, paid, "重复确认保持费用");
        Check(SceneChangeMgr.Instance.ToggleGameMenu(), "消散及镜头交接可以打开暂停菜单");
        bool sawCamera = false;
        double handoffDeadline = EditorApplication.timeSinceStartup + 18;
        while (rules.Stage != PlayerGameplayStage.Escape)
        {
            if (EditorApplication.timeSinceStartup > handoffDeadline)
                throw new TimeoutException(HandoffDiagnostic(spirit));
            Require(WorldPaused() && InputControlMgr.Instance.Player == null && !body.Permissions.movement,
                "消散和镜头交接结束前不开放世界或实体操作");
            Require(!setup.RisingWater.RisingAllowed && setup.RisingWater.SurfaceY == waterBefore,
                "消散和镜头交接前主水没有上涨许可或推进");
            if (Presentation(spirit).GetDeathPlaybackStatus(Playback(spirit)) == PresentationLayer.DeathPlaybackStatus.Playing)
                Require((box.DebugActualPosition - boxScene).magnitude < .002f, "消散完成前现场保持");
            else if ((box.DebugActualPosition - boxHome).magnitude < .002f)
                sawCamera = true;
            yield return null;
        }
        Check(sawCamera, "消散完成后观察到复位及镜头交接区间");
        Check(SceneChangeMgr.Instance.GameMenuOpen && Time.timeScale == 0f && !InputControlMgr.Instance.InputEnabled
            && WorldPaused(), "交接在暂停菜单中结束仍不开放操作和世界");
        SceneChangeMgr.Instance.ToggleGameMenu();
        Check(InputControlMgr.Instance.Player == body && InputControlMgr.Instance.InputEnabled
            && !WorldPaused() && body.Permissions.movement, "关闭菜单后唯一实体控制和世界恢复");
        Check(setup.RisingWater.RisingAllowed, "交接完成才授予主水上涨许可");
        Near(box.DebugActualPosition, boxHome, "退场后原轻刷新恢复现场默认");
        double riseUntil = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < riseUntil) yield return null;
        Check(setup.RisingWater.SurfaceY > waterBefore, "正式逃生实际推进主水");
        Near(rules.Cognition.Remaining, paid, "交接结束仍没有死亡转换费");
        // 在空中验证真实惯性与碰撞，不用改角色预设或另造物理执行器。
        CharacterPhysics character = body.GetComponent<CharacterPhysics>();
        body.RefreshRuntime(new Vector2(8000f, 8003f));
        Rigidbody2D corpse = body.GetComponent<Rigidbody2D>();
        corpse.velocity = new Vector2(2f, -1f);
        Check(body.TryRequestDeath(PlayerDeathCause.OxygenDepleted), "逃生死亡受理实体 Death");
        PlayerDeathRequest cancelledDeath = body.PendingDeath.Value;
        uint cancelledPlayback = Playback(body);
        Check(corpse.bodyType == RigidbodyType2D.Dynamic && corpse.simulated
            && Passive(character) && corpse.velocity.x > 1f, "死亡保留动态刚体及已有惯性，关闭角色驱动");
        Check(!body.Permissions.movement && body.Permissions.motionUpdates && !body.Permissions.resourceUpdates
            && !InputControlMgr.Instance.InputEnabled && !WorldPaused(), "死亡停操作及角色资源，保留世界运动");
        body.RefreshRuntime(new Vector2(8000f, 8003f));
        body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        InputControlMgr.Instance.InputOpenOrClose(true);
        Check(!Passive(character) && !rules.DeathPending && rules.ReceiveDeath(cancelledDeath) == PlayerDeathStatus.Rejected
            && Presentation(body).GetDeathPlaybackStatus(cancelledPlayback) == PresentationLayer.DeathPlaybackStatus.Invalid,
            "显式刷新恢复角色执行并作废旧动画结果");
        box.RefreshMotionState(new Vector2(8010f, 8006f));
        Vector2 fallingBody = character.DebugActualPosition, fallingBox = box.DebugActualPosition;
        corpse.velocity = Vector2.down;
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "刷新后重开新的实体死亡播放");
        PlayerDeathRequest escapeDeath = body.PendingDeath.Value;
        Check(escapeDeath.cycle > cancelledDeath.cycle && !body.TryRequestDeath(PlayerDeathCause.Electrocuted),
            "实体新周期递增，连续危险请求不重播");
        float stamina = body.ResourceFacts.stamina, oxygen = body.ResourceFacts.oxygen;
        float deathSurface = setup.RisingWater.SurfaceY;
        Check(!SceneChangeMgr.Instance.IsFailureRecovering && rules.Stage == PlayerGameplayStage.Escape
            && !WorldPaused(), "实体动画结束前不启动渐暗或复位，世界继续");
        double fallingUntil = EditorApplication.timeSinceStartup + .25;
        while (EditorApplication.timeSinceStartup < fallingUntil) yield return null;
        Check(character.DebugActualPosition.y < fallingBody.y - .05f && box.DebugActualPosition.y < fallingBox.y - .05f,
            "死亡期间实体与箱子实际继续下落");
        Check(setup.RisingWater.SurfaceY > deathSurface, "死亡期间主水继续上涨");
        Near(body.ResourceFacts.stamina, stamina, "死亡不推进角色体力");
        Near(body.ResourceFacts.oxygen, oxygen, "死亡不推进角色氧气");
        Check(SceneChangeMgr.Instance.ToggleGameMenu(), "实体死亡可打开暂停菜单");
        Vector2 pausedBody = character.DebugActualPosition;
        float pausedWater = setup.RisingWater.SurfaceY;
        double pausedUntil = EditorApplication.timeSinceStartup + .2;
        while (EditorApplication.timeSinceStartup < pausedUntil) yield return null;
        Near(character.DebugActualPosition, pausedBody, "菜单暂停停止死者物理运动");
        Near(setup.RisingWater.SurfaceY, pausedWater, "菜单暂停停止主水");
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && !WorldPaused() && Time.timeScale > 0f
            && !InputControlMgr.Instance.InputEnabled, "死亡中关闭菜单恢复世界但保持角色输入关闭");
        bool sawFailure = false;
        bool sawCollision = false;
        float failureSurface = 0f;
        while (Rules() == rules || SceneChangeMgr.Instance.IsFailureRecovering)
        {
            if (body.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime < 1f
                && Rules() == rules)
                Require(!SceneChangeMgr.Instance.IsFailureRecovering, "非完整实体播放不会提前失败刷新");
            sawCollision |= Rules() == rules && !SceneChangeMgr.Instance.IsFailureRecovering
                && corpse.position.y < fallingBody.y - .1f && corpse.velocity.y > -.05f;
            if (SceneChangeMgr.Instance.IsFailureRecovering && Rules() == rules)
            {
                if (!sawFailure) failureSurface = setup.RisingWater.SurfaceY;
                Require(WorldPaused() && Time.timeScale == 0f && setup.RisingWater.SurfaceY == failureSurface,
                    "动画结束后的失败渐变仍暂停世界和涨水");
            }
            sawFailure |= SceneChangeMgr.Instance.IsFailureRecovering;
            yield return null;
        }
        Check(sawFailure && Actor("body") == body && Actor("spirit") != null
            && Rules().Stage == PlayerGameplayStage.Exploration, "实体完整播放后走既有失败渐暗并重建探索");
        Check(sawCollision && !Passive(character), "死亡可落地碰撞，失败复位后退出被动运动");
        Check(rules.ReceiveDeath(escapeDeath) == PlayerDeathStatus.Handled, "失败后的旧规则记录同周期已处理");

        rules = Rules();
        box.RefreshMotionState(boxScene);
        cognition = rules.Cognition.Remaining;
        Check(SceneChangeMgr.Instance.StartEscape()
            && (SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities()),
            "后续面板可复用同一最终确认入口并打开体能页确认");
        while (rules.Stage != PlayerGameplayStage.Escape) yield return null;
        Near(box.DebugActualPosition.x, boxHome.x, "消散后恢复箱子现场默认");
        Near(rules.Cognition.Remaining, cognition, "无购买交接没有额外转换费用");
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "第二次实体死亡重开独立播放周期");
        Check(SceneChangeMgr.Instance.ToggleGameMenu(), "第二次死亡中打开菜单");
        while (!SceneChangeMgr.Instance.IsFailureRecovering && Rules() == rules) yield return null;
        Check(SceneChangeMgr.Instance.GameMenuOpen && SceneChangeMgr.Instance.IsFailureRecovering,
            "暂停中死亡动画完成并进入失败渐变");
        Check(SceneChangeMgr.Instance.ToggleGameMenu() && Time.timeScale == 0f && WorldPaused(),
            "失败渐变中关闭菜单不提前恢复世界");
        while (Rules() == rules || SceneChangeMgr.Instance.IsFailureRecovering) yield return null;

        rules = Rules();
        Check(SceneChangeMgr.Instance.StartEscape()
            && (SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities()),
            "退出回归进入实体逃生");
        while (rules.Stage != PlayerGameplayStage.Escape) yield return null;
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "退出回归建立未完成实体死亡");
        PlayerDeathRequest exitedDeath = body.PendingDeath.Value;
        cognition = rules.Cognition.Remaining;
        Check(SceneChangeMgr.Instance.ReturnToMenu(), "死亡动画未结束可退出菜单");
        while (!MenuReady()) yield return null;
        Check(rules.ReceiveDeath(exitedDeath) == PlayerDeathStatus.Rejected && InputControlMgr.Instance.Player == null
            && !WorldPaused(), "退出作废死亡等待，不恢复旧操作或世界暂停");
        Near(rules.Cognition.Remaining, cognition, "退出的迟到动画不结算转换费");

        Enter(1);
        while (!Ready(1)) yield return null;
        Rules().Cognition.TryPayAbilityCost(Rules().Cognition.Remaining);
        Check(SceneChangeMgr.Instance.StartEscape() && SceneChangeMgr.Instance.NextEscapeStep()
            && SceneChangeMgr.Instance.BeginEscapeFromAbilities() && SceneChangeMgr.Instance.IsEscapeHandoff,
            "低预算路径等待消散，不提前切逃生阶段");
        rules = Rules(); cognition = rules.Cognition.Remaining;
        Check(SceneChangeMgr.Instance.ReturnToMenu(), "消散交接中途退出");
        while (!MenuReady()) yield return null;
        double lateUntil = EditorApplication.timeSinceStartup + 2.2;
        while (EditorApplication.timeSinceStartup < lateUntil) yield return null;
        Check(!SceneChangeMgr.Instance.IsEscapeHandoff && InputControlMgr.Instance.Player == null
            && Actor("spirit") == null && !WorldPaused(), "等待旧动画时长后交接仍作废，没有迟到恢复");
        Near(rules.Cognition.Remaining, cognition, "作废探索交接不追加扣费");

        Enter(1);
        while (!Ready(1)) yield return null;
        rules = Rules(); body = Actor("body");
        Check(SceneChangeMgr.Instance.StartEscape()
            && (SceneChangeMgr.Instance.NextEscapeStep() && SceneChangeMgr.Instance.BeginEscapeFromAbilities()),
            "换关回归进入实体逃生");
        while (rules.Stage != PlayerGameplayStage.Escape) yield return null;
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss), "换关回归建立未完成实体死亡");
        PlayerDeathRequest changedDeath = body.PendingDeath.Value;
        cognition = rules.Cognition.Remaining;
        Enter(0);
        Check(rules.ReceiveDeath(changedDeath) == PlayerDeathStatus.Rejected, "换关受理即作废旧死亡，不等淡出完成");
        while (!Ready(0)) yield return null;
        Near(rules.Cognition.Remaining, cognition, "换关淡出期间没有迟到转换扣费");
        body = Actor("body"); setup = SceneLevelSetup.Current; rules = Rules();
        int setupId = setup.GetInstanceID(), bodyId = body.GetInstanceID();
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss) && !Loading(), "教学先受理实体动画，不立即重载");
        Check(SceneChangeMgr.Instance.ToggleGameMenu(), "教学死亡可暂停");
        while (SceneLevelSetup.Current != null && SceneLevelSetup.Current.GetInstanceID() == setupId)
        {
            if (body.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                Require(!Loading(), "教学完整动画前不开始场景加载");
            yield return null;
        }
        while (!Ready(0)) yield return null;
        Check(Actor("body").GetInstanceID() == bodyId && Actor("spirit") == null
            && Rules() != rules && !Actor("body").PendingDeath.HasValue && !Passive(Actor("body").GetComponent<CharacterPhysics>()),
            "教学完整播放后沿原加载重用实体，并退出被动运动");
        SceneChangeMgr.Instance.ReturnToMenu();
        while (!MenuReady()) yield return null;
    }

    static void CheckNativePlayback(Player actor)
    {
        PresentationLayer presentation = Presentation(actor);
        Animator animator = actor.GetComponentInChildren<Animator>();
        Check(presentation.ValidateDeathPlayback(out _), actor.Identity + " 制作配置有效");
        AnimatorUpdateMode originalMode = animator.updateMode;
        AnimatorCullingMode originalCulling = animator.cullingMode;
        Check(presentation.TryBeginDeathPlayback(out uint first), actor.Identity + " 共用入口实际进入 Death");
        typeof(PresentationLayer).GetMethod("Update", Hidden).Invoke(presentation, null);
        Check(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash("Base Layer.Death"),
            actor.Identity + " 常规动作不会覆盖 Death");
        Check(animator.updateMode == AnimatorUpdateMode.UnscaledTime && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            actor.Identity + " 非缩放且离屏仍推进播放");
        animator.Update(.05f);
        Check(presentation.GetDeathPlaybackStatus(first) == PresentationLayer.DeathPlaybackStatus.Playing, "未播完不能完成");
        animator.Update(3f);
        Check(presentation.GetDeathPlaybackStatus(first) == PresentationLayer.DeathPlaybackStatus.Completed, "原生 normalizedTime 报告完成");
        presentation.CancelDeathPlayback();
        Check(animator.updateMode == originalMode && animator.cullingMode == originalCulling, "释放后恢复 Animator 更新与裁剪设置");
        Check(presentation.TryBeginDeathPlayback(out uint second) && second != first
            && presentation.GetDeathPlaybackStatus(first) == PresentationLayer.DeathPlaybackStatus.Invalid, "旧播放身份不能完成新播放");
        presentation.enabled = false;
        Check(presentation.GetDeathPlaybackStatus(second) == PresentationLayer.DeathPlaybackStatus.Invalid, "禁用表现组件作废等待");
        presentation.enabled = true;
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        AnimationClip original = Array.Find(controller.animationClips, clip => clip.name == "Death");
        AnimationClip looping = Object.Instantiate(original); fixtures.Add(looping); looping.name = "Death";
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(looping);
        settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(looping, settings);
        var overrides = new AnimatorOverrideController(controller); fixtures.Add(overrides); overrides[original] = looping;
        animator.runtimeAnimatorController = overrides;
        Check(!presentation.ValidateDeathPlayback(out _), "循环 Death 配置明确拒绝");
        looping.name = "WrongClip";
        Check(!presentation.ValidateDeathPlayback(out _), "缺少同名 Death 素材明确拒绝");
        animator.runtimeAnimatorController = controller;
        animator.Rebind(); animator.Update(0f);
    }

    static PhysicalBox ConfigureFloor(SceneLevelSetup setup, Player body, Player spirit)
    {
        var item = new GameObject("Death validation floor"); fixtures.Add(item);
        SO_CPhysics config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden)
            .GetValue(spirit.GetComponent<CharacterPhysics>());
        for (int i = 0; i < 32; i++) if ((config.groundLayer.value & (1 << i)) != 0) { item.layer = i; break; }
        item.transform.position = new Vector3(8000f, 7999.5f, 0f);
        item.AddComponent<BoxCollider2D>().size = new Vector2(100f, 1f);
        item.AddComponent<BaseGround>();
        GameObject bodyMark = new GameObject("Death body mark"), spiritMark = new GameObject("Death spirit mark");
        fixtures.Add(bodyMark); fixtures.Add(spiritMark);
        bodyMark.transform.position = new Vector3(8000f, 8003f, setup.BodyPose.z);
        spiritMark.transform.position = new Vector3(8020f, 8003f, setup.SpiritPose.z);
        Set(setup, "bodySpawn", bodyMark.transform); Set(setup, "spiritSpawn", spiritMark.transform);
        body.RefreshRuntime(setup.BodyPoint); spirit.RefreshRuntime(setup.SpiritPoint);
        GameObject boxObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/box.prefab"),
            new Vector3(8010f, 8001f, 0f), Quaternion.identity);
        fixtures.Add(boxObject);
        PhysicalBox box = boxObject.GetComponent<PhysicalBox>();
        box.RefreshMotionState(new Vector2(8010f, 8001f));
        Physics2D.SyncTransforms();
        var upgrades = ScriptableObject.CreateInstance<PlayerUpgradeCatalog>(); fixtures.Add(upgrades);
        foreach (PlayerUpgradeDefinition definition in upgrades.Definitions)
            definition.Enabled = definition.Kind == PlayerUpgradeKind.MapCost;
        Check(SceneChangeMgr.Instance.PlayerEffects.Configure(upgrades), "死亡回归使用独立单项升级配置，不改制作资产");
        var waterObject = new GameObject("Death validation rising water"); fixtures.Add(waterObject);
        waterObject.transform.position = new Vector3(8010f, 7995f, 0f);
        BoxCollider2D waterCollider = waterObject.AddComponent<BoxCollider2D>();
        waterCollider.size = new Vector2(100f, 4f); waterCollider.isTrigger = true;
        WaterRegion water = waterObject.AddComponent<WaterRegion>();
        var maximum = new GameObject("Death validation maximum surface"); fixtures.Add(maximum);
        maximum.transform.position = new Vector3(8010f, 8030f, 0f);
        Set(setup, "risingWater", water); Set(setup, "risingWaterMaximum", maximum.transform);
        Set(setup, "waterRiseSpeed", 1f);
        Physics2D.SyncTransforms();
        Check(setup.PrepareRisingWater(out _), "真实主水几何与最高水面准备完成");
        return box;
    }

    static void Enter(int index) { DataAndInitMgr.Instance.SaveLevelIndex(index); SceneChangeMgr.Instance.EnterSavedLevel(); }
    static string HandoffDiagnostic(Player departing)
    {
        MainCamera camera = (MainCamera)typeof(InputControlMgr).GetField("mainCamera", Hidden).GetValue(InputControlMgr.Instance);
        return $"交接超时：status={Presentation(departing).GetDeathPlaybackStatus(Playback(departing))}, "
            + $"state={Rules().Stage}, handoff={SceneChangeMgr.Instance.IsEscapeHandoff}, cameraReady={InputControlMgr.Instance.TransitionViewComplete}, "
            + $"position={camera.transform.position}, target={typeof(MainCamera).GetField("toPoint", Hidden).GetValue(camera)}, "
            + $"portrait={typeof(MainCamera).GetField("showingPortrait", Hidden).GetValue(camera)}, delta={Time.unscaledDeltaTime}";
    }
    static bool MenuReady() => !Loading() && SceneManager.GetActiveScene().name == "BeginScenes"
        && SceneLevelSetup.Current == null && Actor("body") == null;
    static bool Ready(int index) => SceneLevelSetup.Current != null && SceneLevelSetup.Current.gameObject.scene.name == order[index]
        && InputControlMgr.Instance.Player != null && InputControlMgr.Instance.InputEnabled && !Loading();
    static bool Loading() => (bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(SceneChangeMgr.Instance);
    static Player Actor(string field) => (Player)typeof(SceneChangeMgr).GetField(field, Hidden).GetValue(SceneChangeMgr.Instance);
    static PlayerLifecycleRules Rules() => (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
    static PresentationLayer Presentation(Player actor) => actor.GetComponentInChildren<PresentationLayer>();
    static uint Playback(Player actor) => (uint)typeof(PresentationLayer).GetField("deathPlaybackSequence", Hidden).GetValue(Presentation(actor));
    static PlayerDeathRequest CurrentDeath(Player actor, PlayerDeathCause cause) =>
        new PlayerDeathRequest(actor, cause, (uint)typeof(Player).GetField("deathCycle", Hidden).GetValue(actor));
    static bool Passive(CharacterPhysics actor) => (bool)typeof(CharacterPhysics).GetProperty("IsPassiveMotion", Hidden).GetValue(actor);
    static bool WorldPaused() => (bool)typeof(BasicEntity).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    static void Near(float actual, float expected, string label) => Check(Mathf.Abs(actual - expected) < .002f, label);
    static void Near(Vector2 actual, Vector2 expected, string label) => Check((actual - expected).magnitude < .002f, label);
    static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
    static void Check(bool condition, string label)
    {
        Require(condition, label); passed.Add("PASS " + label);
        Directory.CreateDirectory(".utmp/DeathExitValidation");
        File.WriteAllLines(".utmp/DeathExitValidation/progress.txt", passed);
    }
    static void RecordError(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError ??= message + "\n" + trace; }
    static void Finish()
    {
        EditorApplication.update -= Advance; Application.logMessageReceived -= RecordError;
        foreach (Object item in fixtures) if (item != null) Object.DestroyImmediate(item);
        fixtures.Clear();
        try
        {
            DataAndInitMgr.Instance.ClearLevelProgress();
            if (Directory.Exists(progressDirectory)) Directory.Delete(progressDirectory);
        }
        finally { DataAndInitMgr.debugPlayerProgressDirectory = previousProgressDirectory; }
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        Directory.CreateDirectory(".utmp/DeathExitValidation");
        passed.Add("RESULT: " + (failure == null ? "PASS" : "FAIL " + failure.Message));
        File.WriteAllLines(".utmp/DeathExitValidation/flow-checks.txt", passed);
        Debug.Log("Death/exit flow: " + passed.Count + "; success=" + (failure == null));
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
