#if UNITY_EDITOR
using System;
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

/// <summary>性质背包、双列表与整面板草稿的定向检查；临时对象结束即销毁，不保存场景。</summary>
public static class EnvironmentBackpackChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/P14 验证环境背包与整面板确认（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行环境背包检查。");
        passed.Clear();
        FieldInfo scheduler = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = scheduler.GetValue(null);
        bool previousQuitting = MonoPublicMgr.IsQuitting;
        bool previousPaused = (bool)typeof(BasicEntity).GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        SimulationMode2D previousSimulation = Physics2D.simulationMode;
        FieldInfo currentSetup = typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        object previousSetup = currentSetup.GetValue(null);
        var boxRegistry = (List<PhysicalBox>)typeof(LevelBoxContext).GetField("boxes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var previousBoxes = new List<PhysicalBox>(boxRegistry);
        var entityRegistry = (HashSet<BasicEntity>)typeof(BasicEntity).GetField("activeEntities", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var previousEntities = new List<BasicEntity>(entityRegistry);
        Scene previousScene = SceneManager.GetActiveScene();
        Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            if (SceneManager.GetActiveScene() != previewScene && !SceneManager.SetActiveScene(previewScene))
                throw new InvalidOperationException($"无法进入隔离的验证场景：有效={previewScene.IsValid()}，加载={previewScene.isLoaded}，当前={SceneManager.GetActiveScene().name}。");
            MonoPublicMgr.IsQuitting = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            currentSetup.SetValue(null, null);
            boxRegistry.Clear();
            entityRegistry.Clear();
            if ((MonoPublicMgr)previousScheduler == null)
            {
                var temporaryScheduler = new GameObject("P14 temporary scheduler") { hideFlags = HideFlags.HideAndDontSave };
                objects.Add(temporaryScheduler);
                scheduler.SetValue(null, temporaryScheduler.AddComponent<MonoPublicMgr>());
            }
            CheckPreviewAndReassignment();
            CheckWholeCommitAndRejection();
            CheckBothListsAndGeometry();
            CheckCompositeExtractionAndSplit();
            CheckDefaultsAndCopies();
            CheckCalculationEligibility();
            CheckEffectiveAbilityUpdates();
            CheckPausedBoxAndMainWater();
            CheckSolidQueryLayerAndShapes();
            CheckUILayout();
            CheckLinkedUI();
            Debug.Log($"P14 environment backpack: {passed.Count} checks passed.");
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
            boxRegistry.Clear(); boxRegistry.AddRange(previousBoxes);
            entityRegistry.Clear(); foreach (BasicEntity entity in previousEntities) entityRegistry.Add(entity);
            currentSetup.SetValue(null, previousSetup);
            BasicEntity.SetWorldPaused(previousPaused);
            scheduler.SetValue(null, previousScheduler); MonoPublicMgr.IsQuitting = previousQuitting;
            Physics2D.simulationMode = previousSimulation;
            SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(previewScene, true);
            Directory.CreateDirectory(".utmp/P14Validation");
            File.WriteAllLines(".utmp/P14Validation/checks.txt", passed);
        }
    }

    public static void RunForBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("批处理入口只能在 batch editor 中执行。");
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckPreviewAndReassignment()
    {
        BasicPhysicalObject source = Host("preview source", false, 3,
            new GroundSlowingParameter(.4f), new GroundJumpMultiplierParameter(1.2f));
        var bag = new CognitionBackpack(6, 3f, 2f, null, new[] { 2f });
        var draft = new EnvironmentEditDraft(source, bag);
        int id = bag.Items[0].Id, version = source.EnvironmentParameterVersion, bagVersion = bag.Version;
        Check(draft.TryUseItem(id, false, 0, false, out _), "计算点第一次点击在当前槽预览");
        Near(draft.GetSlot(false, 0).Value, .8f, "草稿显示原性质乘计算点后的结果");
        Near(source.CopyEnvironmentSlots(false)[0].Value, .4f, "预览不改变真实来源");
        Check(source.EnvironmentParameterVersion == version && bag.Version == bagVersion && bag.Items.Count == 1,
            "确认前不提交版本、不移出实际背包");
        Check(draft.TryUseItem(id, false, 1, false, out _), "同一个计算点可以改选另一槽");
        Near(draft.GetSlot(false, 0).Value, .4f, "改选后原槽恢复原值");
        Check(draft.GetSlot(false, 0).CalculationPoint == null, "原目标解除计算点占用");
        Near(draft.GetSlot(false, 1).Value, 2.4f, "新槽显示计算结果");
        Check(draft.GetSlot(false, 1).CalculationPoint != null && draft.Items.Count == 1,
            "草稿保留同一计算点内容物直至统一确认");
        var cancelled = new EnvironmentEditDraft(source, bag);
        Near(cancelled.GetSlot(false, 0).Value, .4f, "放弃草稿后新面板读取原配置");
        Check(!cancelled.HasChanges && cancelled.GetSlot(false, 1).CalculationPoint == null,
            "取消不会把旧预览带入新面板");
        var cognition = new LevelCognition(20f);
        Check(draft.TryCommit(cognition, out _), "整面板确认一次提交选定计算结果");
        Near(source.CopyEnvironmentSlots(false)[1].Value, 2.4f, "真实来源在确认后读取新结果");
        Check(bag.Items.Count == 0 && source.CopyEnvironmentSlots(false)[1].CalculationPoint != null,
            "确认后点移出背包并保留环境附加记录");
        Near(cognition.Remaining, 20f, "计算点出包不额外扣费");
        int committedVersion = source.EnvironmentParameterVersion;
        Check(!draft.TryCommit(cognition, out _), "重复确认拒绝同一已提交草稿");
        Check(source.EnvironmentParameterVersion == committedVersion && bag.Items.Count == 0,
            "重复确认不重复改值或产生内容物");
    }

    static void CheckWholeCommitAndRejection()
    {
        BasicPhysicalObject source = Host("fee source", false, 2, new GroundSlowingParameter(.5f));
        var bag = new CognitionBackpack(4, 3f, 2f);
        var draft = new EnvironmentEditDraft(source, bag);
        Check(draft.TryExtract(false, 0, out _), "性质抽取先进入草稿背包");
        Check(draft.GetSlot(false, 0) == null && draft.Items.Count == 1 && bag.Items.Count == 0,
            "抽取预览留下固定空槽，实际背包仍未入包");
        Near(draft.Cost, 3f, "抽取性质只计算一份入包费用");
        var insufficient = new LevelCognition(2f);
        int version = source.EnvironmentParameterVersion, bagVersion = bag.Version;
        Check(!draft.TryCommit(insufficient, out _), "余额不足拒绝整份修改");
        Near(insufficient.Remaining, 2f, "余额不足不部分扣费");
        Check(source.CopyEnvironmentSlots(false)[0] != null && source.EnvironmentParameterVersion == version
            && bag.Items.Count == 0 && bag.Version == bagVersion, "拒绝保留环境、背包及其版本");
        var exact = new LevelCognition(3f);
        Check(draft.TryCommit(exact, out _), "余额恰好足够可确认同一草稿");
        Check(exact.IsExhausted && source.CopyEnvironmentSlots(false)[0] == null && bag.Items.Count == 1,
            "成功只产生一次实际入包并扣至零");
        var reinjection = new EnvironmentEditDraft(source, bag);
        Check(reinjection.TryUseItem(bag.Items[0].Id, false, 0, false, out _), "同种性质可以注入原空槽");
        Near(reinjection.Cost, 0f, "性质出包注入不额外收费");
        Check(reinjection.TryCommit(exact, out _) && bag.Items.Count == 0 && source.CopyEnvironmentSlots(false)[0] != null,
            "余额为零仍可提交零费用注入");

        var full = new CognitionBackpack(1, 3f, 2f, null, new[] { 2f });
        var fullDraft = new EnvironmentEditDraft(source, full);
        Check(!fullDraft.TryExtract(false, 0, out _), "背包满时抽取被拒绝");
        Check(!fullDraft.HasChanges && fullDraft.GetSlot(false, 0) != null && fullDraft.Items.Count == 1,
            "容量拒绝不丢性质或污染草稿");
        var staleBag = new CognitionBackpack(4, 3f, 2f);
        var first = new EnvironmentEditDraft(source, staleBag);
        var second = new EnvironmentEditDraft(source, staleBag);
        Check(first.TryExtract(false, 0, out _) && first.TryCommit(new LevelCognition(10f), out _),
            "第一份草稿可以正常提交");
        var staleCognition = new LevelCognition(10f);
        Check(!second.TryCommit(staleCognition, out _), "来源或背包版本改变后旧草稿拒绝覆盖");
        Near(staleCognition.Remaining, 10f, "过期草稿不扣费");
    }

    static void CheckBothListsAndGeometry()
    {
        BasicPhysicalObject source = Host("dual lists", false, 2,
            new GroundSlowingParameter(.5f), new GroundJumpMultiplierParameter(1.2f),
            new WaterSwimmingParameter(true), new MovementSpeedOffsetParameter(-.3f));
        Check(source.CopyEnvironmentSlots(false).Length == 2 && source.CopyEnvironmentSlots(true).Length == 2,
            "两列表各使用同一槽上限，各自独立计数");
        var bag = new CognitionBackpack(8, 3f, 2f,
            new EnvironmentParameter[] { new TriggerEnvironmentParameter(), new PassiveVelocityParameter(Vector2.right) }, new[] { 2f });
        var draft = new EnvironmentEditDraft(source, bag);
        int calc = FindCalculation(bag), trigger = FindProperty(bag, EnvironmentParameterKind.TriggerEnvironment);
        Check(!draft.CanUseItem(calc, true, 1) && !draft.TryUseItem(calc, true, 1, false, out _),
            "停用列表不高光或接受计算点");
        Check(draft.TryExtract(true, 1, out _), "置灰停用列表仍允许抽取");
        int velocity = FindProperty(bag, EnvironmentParameterKind.PassiveVelocity);
        Check(!draft.CanUseItem(velocity, true, 1) && !draft.TryUseItem(velocity, true, 1, false, out _),
            "停用列表的空槽不可注入");
        Check(draft.TryUseItem(trigger, false, -1, true, out _), "独特槽接受触发器性质预览");
        Check(draft.IsTriggerEnvironment && !source.IsTriggerEnvironment && !source.GetComponent<Collider2D>().isTrigger,
            "切换草稿仅改变面板生效列表，不提前改真实几何");
        Check(draft.CanUseItem(velocity, true, 1) && draft.TryUseItem(velocity, true, 1, false, out _),
            "预览切换后触发器空槽接受兼容性质");
        Check(!draft.CanUseItem(calc, false, 0) && !draft.TryUseItem(calc, false, 0, false, out _),
            "原碰撞器列表保留内容且变为不可计算");
        var cognition = new LevelCognition(20f);
        Check(draft.TryCommit(cognition, out _) && source.IsTriggerEnvironment
            && source.GetComponent<Collider2D>().isTrigger, "确认同时提交双列表和唯一碰撞器形态");
        Check(source.CopyEnvironmentSlots(false)[0] != null && source.CopyEnvironmentSlots(false)[1] != null,
            "切成触发器保留原碰撞器全部槽内容");
        Check(source.CopyEnvironmentSlots(true)[0].Kind == EnvironmentParameterKind.WaterSwimming
            && source.CopyEnvironmentSlots(true)[1].Kind == EnvironmentParameterKind.PassiveVelocity,
            "触发器列表独立抽取注入，没有占用碰撞器槽数");
        Near(cognition.Remaining, 17f, "本面板只有抽取性质入包产生费用");
        var solid = new EnvironmentEditDraft(source, bag);
        Check(solid.TryExtractTrigger(out _) && !solid.IsTriggerEnvironment, "独特槽只能通过抽取性质切回实心");
        Check(solid.TryCommit(cognition, out _) && !source.IsTriggerEnvironment
            && !source.GetComponent<Collider2D>().isTrigger, "确认抽取恢复实心并保存触发器列表");
        Near(cognition.Remaining, 14f, "触发器性质成功入包收费一次");
        Check(source.CopyEnvironmentSlots(true)[0] != null && source.CopyEnvironmentSlots(true)[1] != null,
            "恢复实心不清空停用触发器列表");
    }

    static void CheckCompositeExtractionAndSplit()
    {
        BasicPhysicalObject source = Host("composite", false, 2, new WallSlideMultiplierParameter(.4f));
        var bag = new CognitionBackpack(5, 3f, 2f, null, new[] { 2f });
        var attach = new EnvironmentEditDraft(source, bag);
        Check(attach.TryUseItem(bag.Items[0].Id, false, 0, false, out _)
            && attach.TryCommit(new LevelCognition(20f), out _), "复合性质夹具经真实点预览与确认形成");
        var extract = new EnvironmentEditDraft(source, bag);
        Check(extract.TryExtract(false, 0, out _), "带计算点性质可整体抽取");
        Near(extract.Cost, 3f, "整体抽取只计性质入包费用");
        Check(extract.Items.Count == 1 && extract.Items[0].Property.CalculationPoint != null,
            "整体抽取保持一个内容物与附加点记录");
        var cognition = new LevelCognition(20f);
        Check(extract.TryCommit(cognition, out _) && bag.Items.Count == 1,
            "整体抽取确认不会另生成计算点");
        int composite = bag.Items[0].Id;
        Near(bag.Items[0].Property.Value, .8f, "背包显示带点性质的有效结果");
        var split = new EnvironmentEditDraft(source, bag);
        Check(split.TrySplitCalculation(composite, out _), "背包性质槽可以预览抽出计算点");
        Check(split.Items.Count == 2 && split.Items[0].Property.CalculationPoint == null,
            "拆点草稿分成性质和一个计算点");
        Near(split.Items[0].Property.Value, .4f, "拆点恢复性质原值，不重复乘结果");
        Near(split.Cost, 2f, "拆点只对新入包的计算点收费");
        Check(bag.Items.Count == 1 && bag.Items[0].Property.CalculationPoint != null,
            "拆点预览保持真实复合内容物完整");
        Check(split.TryCommit(cognition, out _), "整面板确认拆点");
        Check(bag.Items.Count == 2 && bag.Items[0].Property.CalculationPoint == null && FindCalculation(bag) >= 0,
            "成功拆点只加入一个点，保留性质身份");
        Near(cognition.Remaining, 15f, "性质整体抽取与拆点各自仅扣一次费用");
        var noPoint = new EnvironmentEditDraft(source, bag);
        Check(!noPoint.TrySplitCalculation(composite, out _), "已无附加点的性质不再允许拆点");

        var tightNature = new GroundSlowingParameter(.5f);
        tightNature.TryAttachCalculation(new EnvironmentCalculationPoint(2f));
        var tight = new CognitionBackpack(1, 3f, 2f, new[] { tightNature });
        var tightDraft = new EnvironmentEditDraft(source, tight);
        Check(!tightDraft.TrySplitCalculation(tight.Items[0].Id, out _) && tightDraft.Items[0].Property.CalculationPoint != null,
            "拆点容量不足时完整保留原复合性质");
    }

    static void CheckDefaultsAndCopies()
    {
        BasicPhysicalObject source = Host("defaults", false, 2, new GroundSlowingParameter(.5f), new WaterSwimmingParameter(true));
        EnvironmentRegistration identity = source.EnvironmentRegistration;
        var snapshot = source.CopyEnvironmentSlots(false);
        Check(snapshot[0].TryAttachCalculation(new EnvironmentCalculationPoint(2f)), "外部读口获得独立可修改副本");
        Near(source.CopyEnvironmentSlots(false)[0].Value, .5f, "修改副本不覆盖来源真实槽");
        var bag = new CognitionBackpack(5, 3f, 2f, new[] { new TriggerEnvironmentParameter() });
        var draft = new EnvironmentEditDraft(source, bag);
        Check(draft.TryExtract(false, 0, out _) && draft.TryExtract(true, 0, out _)
            && draft.TryUseItem(bag.Items[0].Id, false, -1, true, out _)
            && draft.TryCommit(new LevelCognition(20f), out _), "默认恢复夹具已抽走两列表内容并切触发器");
        source.gameObject.SetActive(false); source.gameObject.SetActive(true);
        Check(source.IsTriggerEnvironment && source.CopyEnvironmentSlots(false)[0] == null,
            "启停不把已提交修改恢复或覆盖默认");
        source.RestoreEnvironmentDefaults();
        Check(!source.IsTriggerEnvironment && !source.GetComponent<Collider2D>().isTrigger,
            "重刷新恢复原默认碰撞形态");
        Check(source.CopyEnvironmentSlots(false)[0] != null && source.CopyEnvironmentSlots(true)[0] != null,
            "重刷新恢复已抽走的双列表性质");
        Near(source.CopyEnvironmentSlots(false)[0].Value, .5f, "重刷新恢复制作原值");
        Check(ReferenceEquals(identity, source.EnvironmentRegistration), "抽取、切换与恢复保持唯一来源身份");
        int version = source.EnvironmentParameterVersion;
        source.RestoreEnvironmentDefaults();
        Check(source.EnvironmentParameterVersion == version, "重复默认恢复幂等，不重复增加版本");
    }

    static void CheckCalculationEligibility()
    {
        var point = new EnvironmentCalculationPoint(2f);
        var water = new WaterSwimmingParameter(true);
        var trigger = new TriggerEnvironmentParameter();
        Check(!water.SupportsCalculation && !water.TryAttachCalculation(point), "水四向提供能力不能附加计算点");
        Check(!trigger.SupportsCalculation && !trigger.TryAttachCalculation(point), "独特触发器性质不能参与计算");
        var wall = new WallSlideMultiplierParameter(.4f);
        Check(wall.SupportsCalculation && wall.TryAttachCalculation(point), "墙滑能力的固定系数允许计算");
        Near(wall.Value, .8f, "墙滑计算消费原系数");
        Check(!wall.TryAttachCalculation(new EnvironmentCalculationPoint(3f)), "一项性质拒绝第二个计算点");
        Check(ReferenceEquals(wall.RemoveCalculation(), point) && wall.CalculationPoint == null, "拆出点保持原内容物身份");
        Near(wall.Value, .4f, "拆点回滚性质原值");
        var movement = new MovementSpeedOffsetParameter(-.3f);
        Check(movement.TryAttachCalculation(point), "主动速度修饰允许负原值和非负计算点");
        Near(movement.Value, -.6f, "计算点乘负修饰偏移");
        var passive = new PassiveVelocityParameter(new Vector2(-2f, 3f));
        Check(passive.TryAttachCalculation(point), "二维被动速度允许附加计算点");
        Near((passive.EffectiveVelocity - new Vector2(-4f, 6f)).sqrMagnitude, 0f,
            "被动速度两轴同倍缩放并保持方向");
    }

    static void CheckEffectiveAbilityUpdates()
    {
        BasicPhysicalObject source = Host("same source effects", true, 3,
            new WaterSwimmingParameter(true), new MovementSpeedOffsetParameter(-.3f),
            new PassiveVelocityParameter(new Vector2(2f, 3f)));
        Collider2D collider = source.GetComponent<Collider2D>();
        var receiverObject = new GameObject("P14 environment receiver") { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(receiverObject); receiverObject.AddComponent<Rigidbody2D>(); receiverObject.AddComponent<BoxCollider2D>();
        var receiver = receiverObject.AddComponent<EnvironmentParameterCheckReceiver>();
        receiver.InitializeForChecks();
        Rigidbody2D body = receiverObject.GetComponent<Rigidbody2D>(); body.velocity = new Vector2(7f, -8f);
        var overlaps = new List<Collider2D> { collider };
        receiver.EnvironmentContext.RefreshEnvironment(null, overlaps, source.transform.position);
        var movement = (Dictionary<EnvironmentRegistration, float>)EnvironmentIntegrationChecks.EffectField(receiver, "movementModifiers");
        var passive = (Dictionary<EnvironmentRegistration, Vector2>)EnvironmentIntegrationChecks.EffectField(receiver, "stateVelocities");
        EnvironmentRegistration identity = source.EnvironmentRegistration;
        Check(movement.Count == 1 && passive.Count == 1 && receiver.EnvironmentContext.DebugSourceCount == 1,
            "同一宿主两个持续效果沿原关系入口各登记一次");
        Check(receiver.EnvironmentContext.WaterFrame.submerged && receiver.EnvironmentContext.WaterFrame.swimmingAvailable,
            "普通触发器列表的水性质提供同源淹没与四向能力");
        var trigger = source.CopyEnvironmentSlots(true); trigger[1] = null;
        Check(source.TryCommitEnvironmentEdit(source.CopyEnvironmentSlots(false), trigger, true, out _),
            "同来源可以移除主动速度性质");
        Check(movement.Count == 1 && passive.Count == 1, "确认只写环境来源，不同步改接收者效果容器");
        receiver.EnvironmentContext.RefreshEnvironment(null, overlaps, source.transform.position);
        Check(movement.Count == 0 && passive.Count == 1 && receiver.EnvironmentContext.DebugSourceCount == 1,
            "下次正常采样只撤销已移除项，保留同源被动速度");
        Near((body.velocity - new Vector2(7f, -8f)).sqrMagnitude, 0f, "单项撤销保留刚体真实动量");
        trigger = source.CopyEnvironmentSlots(true);
        Check(trigger[2].TryAttachCalculation(new EnvironmentCalculationPoint(2f))
            && source.TryCommitEnvironmentEdit(source.CopyEnvironmentSlots(false), trigger, true, out _),
            "同来源被动速度可提交附加计算结果");
        receiver.EnvironmentContext.RefreshEnvironment(null, overlaps, source.transform.position);
        Near((passive[identity] - new Vector2(4f, 6f)).sqrMagnitude, 0f,
            "未离开区域的原 Binding 更新两轴参数");
        Check(passive.Count == 1 && ReferenceEquals(identity, source.EnvironmentRegistration),
            "改值不复制持续效果或来源身份");
        trigger = source.CopyEnvironmentSlots(true); trigger[0] = null; trigger[2] = null;
        Check(source.TryCommitEnvironmentEdit(source.CopyEnvironmentSlots(false), trigger, true, out _),
            "同来源可移除剩余水能力和速度");
        receiver.EnvironmentContext.RefreshEnvironment(null, overlaps, source.transform.position);
        Check(passive.Count == 0 && movement.Count == 0 && receiver.EnvironmentContext.DebugSourceCount == 0,
            "所有持续项失效时原关系不残留登记");
        Check(!receiver.EnvironmentContext.WaterFrame.submerged && !receiver.EnvironmentContext.WaterFrame.swimmingAvailable
            && EnvironmentCapabilities.Find<IWaterRegion>(collider) == null, "水性质抽走即不再提供淹没或游泳事实");
        source.RestoreEnvironmentDefaults();
        receiver.EnvironmentContext.RefreshEnvironment(null, overlaps, source.transform.position);
        Check(movement.Count == 1 && passive.Count == 1 && receiver.EnvironmentContext.WaterFrame.swimmingAvailable,
            "重刷新后仍命中的来源重新登记默认项");
        Near(movement[identity], -.3f, "原 Binding 恢复默认主动速度修饰");
        Near((passive[identity] - new Vector2(2f, 3f)).sqrMagnitude, 0f, "原 Binding 恢复默认被动速度");
        var solid = source.CopyEnvironmentSlots(false); solid[0] = new WallSlideMultiplierParameter(.4f);
        Check(source.TryCommitEnvironmentEdit(solid, source.CopyEnvironmentSlots(true), false, out _),
            "切实心生效碰撞器墙性质");
        receiver.EnvironmentContext.RefreshEnvironment(collider, null, source.transform.position);
        Check(movement.Count == 0 && passive.Count == 0 && EnvironmentCapabilities.Find<IWallSlideSurface>(collider) != null,
            "切实心撤销触发器项并提供当前墙能力");
        IWallSlideSurface wall = EnvironmentCapabilities.Find<IWallSlideSurface>(collider);
        solid = source.CopyEnvironmentSlots(false); solid[0] = null;
        Check(source.TryCommitEnvironmentEdit(solid, source.CopyEnvironmentSlots(true), false, out _)
            && EnvironmentCapabilities.Find<IWallSlideSurface>(collider) == null && !EnvironmentCapabilities.IsActive(wall),
            "抽走墙滑项同时让查询和已取得的墙视图失效");
    }

    static void CheckPausedBoxAndMainWater()
    {
        BasicPhysicalObject source = Host("paused box region", true, 3, new WaterSwimmingParameter(true));
        source.GetComponent<BoxCollider2D>().size = new Vector2(8f, 8f);
        GameObject boxAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/box.prefab");
        if (boxAsset == null) boxAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/box.prefab");
        Check(boxAsset != null, "暂停箱提示检查使用实际箱子预设");
        GameObject boxObject = Object.Instantiate(boxAsset); boxObject.hideFlags = HideFlags.HideAndDontSave;
        objects.Add(boxObject); boxObject.transform.position = source.transform.position; Physics2D.SyncTransforms();
        PhysicalBox box = boxObject.GetComponent<PhysicalBox>(); Invoke(box, "Awake"); Invoke(box, "OnEnable");
        BasicEntity.SetWorldPaused(true); Physics2D.SyncTransforms();
        Check(!box.GetComponent<Rigidbody2D>().simulated && !box.IsMotionParticipating, "真实世界暂停使箱子退出原生模拟");
        Check(LevelBoxContext.CountPushableOverlaps(source) == 1, "提示按登记及真实位置查到触发器内暂停箱子");
        typeof(PhysicalBox).GetField("electrified", Hidden).SetValue(box, true);
        Check(LevelBoxContext.CountPushableOverlaps(source) == 1, "仍可被推动的带电箱也计入栏目提示");
        typeof(PhysicalBox).GetField("stored", Hidden).SetValue(box, true);
        Check(LevelBoxContext.CountPushableOverlaps(source) == 0, "收纳箱不计作当前可推动实体");
        typeof(PhysicalBox).GetField("stored", Hidden).SetValue(box, false);
        boxObject.transform.position += Vector3.right * 20f;
        box.GetComponent<Rigidbody2D>().position = boxObject.transform.position; Physics2D.SyncTransforms();
        Check(LevelBoxContext.CountPushableOverlaps(source) == 0, "触发器外的暂停箱子不被提示");
        boxObject.transform.position = source.transform.position;
        box.GetComponent<Rigidbody2D>().position = boxObject.transform.position; Physics2D.SyncTransforms();
        var draft = new EnvironmentEditDraft(source, new CognitionBackpack(3, 3f, 2f));
        Check(draft.TryExtractTrigger(out _) && draft.TryCommit(new LevelCognition(10f), out _),
            "箱子提示仅说明范围占用，不禁止恢复实心");

        var waterObject = new GameObject("P14 rising main water") { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(waterObject); waterObject.AddComponent<BoxCollider2D>().isTrigger = true;
        WaterRegion water = waterObject.AddComponent<WaterRegion>();
        var setupObject = new GameObject("P14 temporary level setup") { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(setupObject); SceneLevelSetup setup = setupObject.AddComponent<SceneLevelSetup>();
        typeof(SceneLevelSetup).GetField("risingWater", Hidden).SetValue(setup, water);
        typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, setup);
        Check(!water.AllowsEnvironmentEditing && source.AllowsEnvironmentEditing,
            "本关指定主水体单独排除编辑，普通环境仍开放");
        var mainSolid = water.CopyEnvironmentSlots(false); var mainTrigger = water.CopyEnvironmentSlots(true);
        Check(!water.ValidateEnvironmentEdit(mainSolid, mainTrigger, false, out _)
            && !water.TryCommitEnvironmentEdit(mainSolid, mainTrigger, false, out _) && water.GetComponent<Collider2D>().isTrigger,
            "主水体来源提交入口也拒绝编辑或几何切换");
        MethodInfo editable = typeof(EnvironmentEditView).GetMethod("EditableSource", BindingFlags.Static | BindingFlags.NonPublic);
        Check(!(bool)editable.Invoke(null, new object[] { water }), "主水体同时从蓝框与点选共用资格入口排除");
    }

    static void CheckSolidQueryLayerAndShapes()
    {
        BasicPhysicalObject source = Host("native query layer", true, 2, new WaterSwimmingParameter(true));
        source.gameObject.layer = 0;
        Collider2D collider = source.GetComponent<Collider2D>();
        EnvironmentRegistration identity = source.EnvironmentRegistration;
        var solid = source.CopyEnvironmentSlots(false); solid[0] = new GroundSlowingParameter(.5f);
        solid[1] = new WallSlideMultiplierParameter(.4f);
        Check(source.TryCommitEnvironmentEdit(solid, source.CopyEnvironmentSlots(true), false, out _),
            "默认触发器层可以确认切换为实心环境");
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(LayerMask.GetMask("Ground", "Wall"));
        var hits = new List<RaycastHit2D>();
        Physics2D.BoxCast((Vector2)source.transform.position + Vector2.up * 3f, new Vector2(.5f, .1f), 0f,
            Vector2.down, filter, hits, 6f);
        Check(hits.Exists(hit => hit.collider == collider), "转换后原脚下／墙查询层实际命中同一个实心碰撞器");
        Check(ReferenceEquals(EnvironmentCapabilities.FindHost(collider), source)
            && EnvironmentCapabilities.Find<IWallSlideSurface>(collider) != null,
            "转换后的原生查询仍解析同一来源与当前墙能力");
        source.RestoreEnvironmentDefaults();
        Check(source.gameObject.layer == 0 && collider.isTrigger && ReferenceEquals(identity, source.EnvironmentRegistration),
            "重刷新恢复制作层和触发器形态，来源身份保持");
        source.TryGetEnvironmentParameter(0, out EnvironmentParameter water);
        Check(water != null && !water.SupportsCalculation, "旧枚举读口仍读取当前触发器列表");
        var surface = Host("legacy copy read", false, 2, new GroundSlowingParameter(.5f));
        int version = surface.EnvironmentParameterVersion;
        surface.TryGetEnvironmentParameter(0, out EnvironmentParameter read);
        Check(read.TryAttachCalculation(new EnvironmentCalculationPoint(2f)), "旧枚举读口提供独立性质副本");
        Near(surface.CopyEnvironmentSlots(false)[0].Value, .5f, "旧读口修改副本不会绕过宿主提交");
        Check(surface.EnvironmentParameterVersion == version, "旧读口副本修改不改变来源版本");

        var edgeObject = new GameObject("P14 unsupported edge") { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(edgeObject); edgeObject.AddComponent<EdgeCollider2D>();
        BasicPhysicalObject edge = edgeObject.AddComponent<BasicPhysicalObject>();
        Check(!edge.ValidateEnvironmentEdit(edge.CopyEnvironmentSlots(false), edge.CopyEnvironmentSlots(true), true, out _),
            "无面积Edge几何不开放实心／触发器完整编辑");
    }

    static void CheckUILayout()
    {
        var panel = new GameObject("P14 isolated gameplay layout", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(panel);
        RectTransform root = (RectTransform)panel.transform; root.sizeDelta = new Vector2(1920f, 1080f);
        var areaObject = new GameObject("EnvironmentEditArea", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        areaObject.transform.SetParent(root, false);
        var oldInput = new GameObject("EnvironmentValueInput", typeof(RectTransform), typeof(InputField));
        oldInput.transform.SetParent(areaObject.transform, false);
        EnvironmentEditLayout.Ensure(areaObject.transform, root);
        RectTransform area = (RectTransform)areaObject.transform;
        RectTransform bag = (RectTransform)root.Find("CognitionBackpackArea");
        Check(area.anchorMin == new Vector2(.5f, 0f) && area.pivot == new Vector2(.5f, 0f)
            && area.sizeDelta == new Vector2(720f, 430f), "环境面板保持中下方并有独立滚动区域");
        Check(bag.anchorMin == Vector2.one && bag.pivot == Vector2.one && bag.sizeDelta == new Vector2(360f, 414f),
            "认知背包固定右侧，可与环境面板同时显示");
        Check(!oldInput.activeSelf && !oldInput.GetComponent<InputField>().interactable,
            "旧数字改值控件隐藏并禁用，不产生第二编辑入口");
        Check(area.Find("EnvironmentEditorContent/EnvironmentAbilityScroll").GetComponent<ScrollRect>() != null
            && bag.Find("CognitionBackpackScroll").GetComponent<ScrollRect>() != null,
            "环境性质列表与侧栏背包各有独立滚动控件");
        Check(area.Find("EnvironmentEditorContent/ConfirmEnvironmentButton").GetComponent<Button>() != null
            && area.Find("EnvironmentEditorContent/CancelEnvironmentButton").GetComponent<Button>() != null
            && bag.Find("SplitBackpackCalculationButton").GetComponent<Button>() != null,
            "统一确认／取消与背包固定拆点按钮有实际控件");
        int children = root.childCount, areaChildren = area.childCount;
        EnvironmentEditLayout.Ensure(area, root);
        Check(root.childCount == children && area.childCount == areaChildren,
            "重复启用布局不复制环境面板、背包或按钮");
    }

    static void CheckLinkedUI()
    {
        FieldInfo sceneSingleton = typeof(BaseMgr<SceneChangeMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo stageSingleton = typeof(BaseMgr<LevelStageMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo inputSingleton = typeof(BaseAutoMonoMgr<InputControlMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousSceneManager = sceneSingleton.GetValue(null), previousStageManager = stageSingleton.GetValue(null), previousInput = inputSingleton.GetValue(null);
        float previousTimeScale = Time.timeScale;
        int firstObject = objects.Count;
        try
        {
            var sceneManager = new SceneChangeMgr(); sceneSingleton.SetValue(null, sceneManager);
            var stageManager = new LevelStageMgr(); stageSingleton.SetValue(null, stageManager);
            var inputObject = new GameObject("P14 isolated input holder") { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(inputObject); var input = inputObject.AddComponent<InputControlMgr>(); inputSingleton.SetValue(null, input);
            GameObject spiritAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/Player/SpiritPlayer.prefab");
            Check(spiritAsset != null, "联动UI夹具使用实际精神体预设");
            GameObject spiritObject = Object.Instantiate(spiritAsset); spiritObject.hideFlags = HideFlags.HideAndDontSave;
            objects.Add(spiritObject); spiritObject.transform.position = new Vector2(47000f, 47000f);
            Player spirit = spiritObject.GetComponent<Player>();
            Invoke(spirit.GetComponent<CharacterPhysics>(), "Awake"); Invoke(spirit, "Awake");
            Invoke(spirit.GetComponentInChildren<PresentationLayer>(), "Awake"); Invoke(spirit, "Start");
            Invoke(spirit.GetComponent<CharacterPhysics>(), "OnEnable");
            spirit.ChangeInputAsset(new PlayerInputData());
            typeof(InputControlMgr).GetField("player", Hidden).SetValue(input, spirit);
            var rules = new PlayerLifecycleRules(null, spirit, cognition: new LevelCognition(100f));
            var setupObject = new GameObject("P14 UI level setup") { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(setupObject); var setup = setupObject.AddComponent<SceneLevelSetup>();
            Check(FindProperty(setup.CreateCognitionBackpack(), EnvironmentParameterKind.TriggerEnvironment) < 0,
                "默认初始背包不提供触发器性质，玩家须从普通触发器取得");
            var composite = new GroundJumpMultiplierParameter(.5f); composite.TryAttachCalculation(new EnvironmentCalculationPoint(2f));
            typeof(SceneLevelSetup).GetField("initialCognitionProperties", Hidden).SetValue(setup,
                new List<EnvironmentParameter> { composite });
            typeof(SceneLevelSetup).GetField("initialCognitionCalculations", Hidden).SetValue(setup, new[] { 2f, .2f });
            typeof(SceneLevelSetup).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, setup);
            Check(rules.BindCognitionBackpack(setup) && rules.BeginExploration(), "实际阶段规则与制作背包开放漫游编辑");
            typeof(SceneChangeMgr).GetField("spirit", Hidden).SetValue(sceneManager, spirit);
            typeof(SceneChangeMgr).GetField("levelRules", Hidden).SetValue(sceneManager, rules);
            typeof(LevelStageMgr).GetField("rules", Hidden).SetValue(stageManager, rules);
            Time.timeScale = 1f;
            BasicPhysicalObject source = Host("UI linked source", false, 3, new GroundSlowingParameter(.4f), new WaterSwimmingParameter(true));
            var panelObject = new GameObject("P14 actual UI controls", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(panelObject); panelObject.AddComponent<GameplayPanel>();
            var areaObject = new GameObject("EnvironmentEditArea", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            areaObject.transform.SetParent(panelObject.transform, false);
            var view = areaObject.AddComponent<EnvironmentEditView>(); Invoke(view, "Awake");
            PrepareUIAction(view, true); view.SelectEnvironment(source);
            Check(view.DisplayedDraft != null && rules.ActiveEnvironmentEdit == view.DisplayedDraft,
                "真实视图只编辑规则持有的同一草稿");
            Transform editor = view.transform.Find("EnvironmentEditorContent"), bag = panelObject.transform.Find("CognitionBackpackArea");
            Click(view, editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_1").GetComponent<Button>());
            Check(view.IsBackpackOpen && !view.IsBackpackReadOnly && bag.gameObject.activeSelf && editor.gameObject.activeSelf,
                "点击空槽同时显示中下环境面板与右侧联动背包");
            int compositeId = rules.CognitionBackpack.Items[0].Id;
            Transform bagContent = bag.Find("CognitionBackpackScroll/Viewport/Content");
            Button splitSelection = bagContent.Find("SelectBackpackSplitItem_" + compositeId).GetComponent<Button>();
            Check(splitSelection.interactable, "兼容空槽旁的附加点选择区域可独立选择拆点");
            int beforeCount = view.DisplayedDraft.Items.Count;
            Click(view, splitSelection);
            Check(view.DisplayedDraft.GetSlot(false, 1) == null && view.DisplayedDraft.Items.Count == beforeCount,
                "点击附加点区域不误注入或移出兼容性质");
            Button splitButton = bag.Find("SplitBackpackCalculationButton").GetComponent<Button>();
            Check(splitButton.interactable, "选择背包带点性质后固定拆点按钮启用");
            Click(view, splitButton);
            Check(view.DisplayedDraft.Items.Count == beforeCount + 1 && rules.CognitionBackpack.Items[0].Property.CalculationPoint != null,
                "实际拆点按钮只改变预览，正式背包仍完整");
            Click(view, editor.Find("CancelEnvironmentButton").GetComponent<Button>(), true);
            Check(view.DisplayedDraft == null && rules.ActiveEnvironmentEdit == null && !bag.gameObject.activeSelf,
                "实际取消按钮关闭联动面板并释放规则草稿");

            PrepareUIAction(view, true); view.SelectEnvironment(source);
            Click(view, editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_0").GetComponent<Button>());
            CognitionBackpackItem point = null;
            foreach (var candidate in rules.CognitionBackpack.Items) if (candidate.IsCalculation) { point = candidate; break; }
            Click(view, bag.Find("CognitionBackpackScroll/Viewport/Content/CognitionBackpackItem_" + point.Id).GetComponent<Button>());
            Near(view.DisplayedDraft.GetSlot(false, 0).Value, .8f, "实际背包按钮展示计算结果");
            Near(source.CopyEnvironmentSlots(false)[0].Value, .4f, "实际背包按钮没有提前提交来源");
            Check(bag.Find("BackpackCapacityText").GetComponent<Text>().text.Contains("确认后"),
                "背包展示统一确认后的容量占用");
            PrepareUIAction(view); view.OpenReadOnlyBackpack();
            Check(view.IsBackpackReadOnly && view.DisplayedDraft != null, "独立打开背包保留同一预览但只读");
            foreach (Button button in bag.Find("CognitionBackpackScroll/Viewport/Content").GetComponentsInChildren<Button>())
                Check(!button.interactable, "独立只读背包正文与附加点区域均不可点击");
            Check(!splitButton.interactable, "独立只读背包固定拆点按钮不可点击");
            view.SelectBackpackPropertyForSplit(compositeId); view.SplitSelectedCalculation();
            Check(view.DisplayedDraft.Items.Count == beforeCount, "独立只读模式直接调用选择／拆点也拒绝修改");
            PrepareUIAction(view); view.SelectAbilitySlot(0);
            Click(view, editor.Find("ConfirmEnvironmentButton").GetComponent<Button>(), true);
            Near(source.CopyEnvironmentSlots(false)[0].Value, .8f, "真实统一确认按钮经规则提交来源结果");
            Check(view.DisplayedDraft == null && rules.ActiveEnvironmentEdit == null,
                "统一确认后视图与规则都结束本次草稿");

            PrepareUIAction(view, true); view.SelectEnvironment(source);
            PrepareUIAction(view, true); view.SetViewedList(true);
            Check(!editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_0").GetComponent<Button>().interactable
                && editor.Find("EnvironmentAbilityScroll/Viewport/Content/ExtractEnvironmentAbility_0").GetComponent<Button>().interactable,
                "真实置灰列表正文禁用，右侧抽取区域仍启用");

            Button geometry = editor.Find("EnvironmentGeometrySlot").GetComponent<Button>();
            float cognitionBeforeGeometry = rules.Cognition.Remaining;
            PointerClick(view, geometry);
            Check(view.IsBackpackOpen && !view.IsBackpackReadOnly && bag.gameObject.activeSelf,
                "真实独特槽指针点击打开联动背包，即使尚未取得触发器性质");
            Check(geometry.GetComponent<Image>().color == EnvironmentEditLayout.Highlight
                && geometry.GetComponentInChildren<Text>().text.Contains("待注入"),
                "独特槽持续高光并显示待注入，不把选中目标误写成已注入");
            Button waitingConfirm = editor.Find("ConfirmEnvironmentButton").GetComponent<Button>();
            Check(!waitingConfirm.interactable && waitingConfirm.GetComponentInChildren<Text>().text == "待注入",
                "只选独特目标槽时确认区明确显示待注入而非无反馈禁用");
            string missingHint = editor.Find("EnvironmentDraftHintText").GetComponent<Text>().text;
            Check(missingHint.Contains("暂无触发器性质") && missingHint.Contains("抽取并确认"),
                "背包无触发器性质时明确提示先从普通触发器抽取并确认");
            Check(FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment) < 0
                && !view.DisplayedDraft.HasChanges, "缺少性质时不凭空发放内容物或产生草稿修改");
            foreach (CognitionBackpackItem item in view.DisplayedDraft.Items)
                if (item.IsCalculation)
                    Check(!bagContent.Find("CognitionBackpackItem_" + item.Id).GetComponent<Button>().interactable,
                        "独特触发器性质槽不高光或启用普通计算点");
            PrepareUIAction(view); view.OpenReadOnlyBackpack();
            Check(view.IsBackpackReadOnly, "独特槽选中后主动打开背包仍转只读");
            PointerClick(view, geometry);
            Check(!view.IsBackpackReadOnly && geometry.GetComponent<Image>().color == EnvironmentEditLayout.Highlight,
                "再次指针点击独特槽从只读背包恢复联动目标");
            PrepareUIAction(view, true); view.SetViewedList(false);
            Click(view, editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_1").GetComponent<Button>());
            Check(geometry.GetComponent<Image>().color == EnvironmentEditLayout.Normal,
                "改选普通能力槽解除独特槽高光");
            Click(view, editor.Find("CancelEnvironmentButton").GetComponent<Button>(), true);

            BasicPhysicalObject ordinaryTrigger = Host("UI ordinary trigger", true, 3, new PassiveVelocityParameter(Vector2.right));
            PrepareUIAction(view, true); view.SelectEnvironment(ordinaryTrigger);
            PointerClick(view, editor.Find("ExtractTriggerNatureButton").GetComponent<Button>());
            Check(!view.DisplayedDraft.IsTriggerEnvironment && ordinaryTrigger.IsTriggerEnvironment
                && ordinaryTrigger.GetComponent<Collider2D>().isTrigger,
                "普通触发器右侧抽取按钮只预览恢复实心");
            Check(FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment) < 0,
                "抽取预览在统一确认前尚未加入真实背包");
            Near(view.DisplayedDraft.Cost, 1f, "普通触发器性质预览按配置计算一份入包费用");
            Click(view, editor.Find("ConfirmEnvironmentButton").GetComponent<Button>(), true);
            int triggerId = FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment);
            Check(triggerId >= 0 && !ordinaryTrigger.IsTriggerEnvironment
                && !ordinaryTrigger.GetComponent<Collider2D>().isTrigger,
                "统一确认普通触发器抽取后取得可转移的独特性质");
            Near(rules.Cognition.Remaining, cognitionBeforeGeometry - 1f, "确认抽取独特性质只扣一次入包费用");

            PrepareUIAction(view, true); view.SelectEnvironment(source);
            PointerClick(view, geometry);
            Button triggerItem = bagContent.Find("CognitionBackpackItem_" + triggerId).GetComponent<Button>();
            Check(triggerItem.interactable && triggerItem.GetComponent<Image>().color == EnvironmentEditLayout.Highlight,
                "抽取确认后再次选择实心环境，背包触发器性质正确高光且可点击");
            Check(triggerItem.GetComponentInChildren<Text>().text.Contains("点击注入")
                && bag.Find("BackpackModeText").GetComponent<Text>().text.Contains("点击高光"),
                "高光背包项及背包标题说明下一步是点击注入");
            Check(editor.Find("EnvironmentDraftHintText").GetComponent<Text>().text.Contains("高光"),
                "取得触发器性质后提示切换为选择高光项");
            Click(view, triggerItem);
            Check(view.DisplayedDraft.IsTriggerEnvironment && !source.IsTriggerEnvironment
                && !source.GetComponent<Collider2D>().isTrigger,
                "高光背包性质点击只预览实心环境转触发器");
            Check(!geometry.interactable && editor.Find("ExtractTriggerNatureButton").GetComponent<Button>().interactable,
                "注入预览后独特槽改为已注入，右侧抽取按钮可用");
            Check(geometry.GetComponentInChildren<Text>().text.Contains("待确认")
                && editor.Find("ConfirmEnvironmentButton").GetComponent<Button>().interactable
                && bag.Find("BackpackModeText").GetComponent<Text>().text.Contains("请确认"),
                "点击高光项后两面板改为待确认提示且统一确认启用");
            Check(!editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_0").GetComponent<Button>().interactable,
                "触发器注入预览时原碰撞器列表立即停用");
            Near(view.DisplayedDraft.Cost, 0f, "触发器性质注入出包不增加费用");
            Click(view, editor.Find("CancelEnvironmentButton").GetComponent<Button>(), true);
            Check(!source.IsTriggerEnvironment && FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment) == triggerId,
                "取消触发器注入保留原实心几何及同一个背包性质");

            PrepareUIAction(view, true); view.SelectEnvironment(source);
            PointerClick(view, geometry);
            Click(view, bagContent.Find("CognitionBackpackItem_" + triggerId).GetComponent<Button>());
            Click(view, editor.Find("ConfirmEnvironmentButton").GetComponent<Button>(), true);
            Check(source.IsTriggerEnvironment && source.GetComponent<Collider2D>().isTrigger
                && FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment) < 0,
                "统一确认注入后真实环境转触发器且性质移出背包");
            Near(rules.Cognition.Remaining, cognitionBeforeGeometry - 1f, "抽取再注入全流程仅抽取入包扣费");
            PrepareUIAction(view, true); view.SelectEnvironment(source);
            Invoke(view, "OnDisable");
            Check(view.DisplayedDraft == null && rules.ActiveEnvironmentEdit == null && !bag.gameObject.activeSelf,
                "视图停用不留下可确认草稿或背包侧栏");
            CheckSavedUIPointerPath(source, rules);
        }
        finally
        {
            for (int i = objects.Count - 1; i >= firstObject; i--)
            {
                if (objects[i] == null) continue;
                foreach (EnvironmentEditView view in objects[i].GetComponentsInChildren<EnvironmentEditView>(true))
                {
                    Transform panel = view.GetComponentInParent<GameplayPanel>().transform;
                    Transform bag = panel.Find("CognitionBackpackArea"), opener = panel.Find("OpenCognitionBackpackButton");
                    if (bag != null) Object.DestroyImmediate(bag.gameObject);
                    if (opener != null) Object.DestroyImmediate(opener.gameObject);
                }
                foreach (BasicEntity entity in objects[i].GetComponentsInChildren<BasicEntity>()) Invoke(entity, "OnDisable");
                Object.DestroyImmediate(objects[i]);
            }
            objects.RemoveRange(firstObject, objects.Count - firstObject);
            sceneSingleton.SetValue(null, previousSceneManager); stageSingleton.SetValue(null, previousStageManager);
            inputSingleton.SetValue(null, previousInput); Time.timeScale = previousTimeScale;
        }
    }

    // Edit Mode 调用真实运行时按钮前，测试侧立即清理旧行；不改变生产UI的延迟销毁方式。
    static void PrepareUIAction(EnvironmentEditView view, bool slots = false)
    {
        Transform bag = view.GetComponentInParent<GameplayPanel>().transform.Find("CognitionBackpackArea/CognitionBackpackScroll/Viewport/Content");
        while (bag != null && bag.childCount > 0) Object.DestroyImmediate(bag.GetChild(0).gameObject);
        if (!slots) return;
        Transform content = view.transform.Find("EnvironmentEditorContent/EnvironmentAbilityScroll/Viewport/Content");
        while (content != null && content.childCount > 0) Object.DestroyImmediate(content.GetChild(0).gameObject);
    }
    static void Click(EnvironmentEditView view, Button button, bool slots = false)
    {
        Button.ButtonClickedEvent click = button.onClick;
        PrepareUIAction(view, slots);
        click.Invoke();
    }
    static void PointerClick(EnvironmentEditView view, Button button)
    {
        Check(button.IsActive() && button.interactable, "指针点击目标是当前启用的真实控件：" + button.name);
        PrepareUIAction(view);
        button.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
    }

    static void CheckSavedUIPointerPath(BasicPhysicalObject donor, PlayerLifecycleRules rules)
    {
        EnvironmentEditDraft extraction = rules.BeginEnvironmentEdit(donor);
        Check(extraction != null && extraction.TryExtractTrigger(out _) && rules.ConfirmEnvironmentEdit(extraction, out _),
            "实际预设点击夹具先从已确认触发器取得独特性质");
        GameObject canvasObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NecessaryAssets/Canvas.prefab"));
        GameObject cameraObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NecessaryAssets/UICamera.prefab"));
        canvasObject.hideFlags = cameraObject.hideFlags = HideFlags.HideAndDontSave;
        objects.Add(canvasObject); objects.Add(cameraObject);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false; camera.transform.position = new Vector3(80000f, 80000f, -200f);
        var target = new RenderTexture(1920, 1080, 16) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            camera.targetTexture = target;
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.worldCamera = camera;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            GameObject panelObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/UIPanel/GameplayPanel.prefab"),
                canvasObject.transform.Find("Middle"), false);
            panelObject.hideFlags = HideFlags.HideAndDontSave;
            GameplayPanel panel = panelObject.GetComponent<GameplayPanel>(); Invoke(panel, "Awake");
            EnvironmentEditView view = panelObject.GetComponentInChildren<EnvironmentEditView>(true); Invoke(view, "Awake");
            panelObject.transform.Find("ActionArea").gameObject.SetActive(true); view.gameObject.SetActive(true);
            GameObject wallObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/SceneObj/wall.prefab"));
            wallObject.hideFlags = HideFlags.HideAndDontSave; objects.Add(wallObject);
            wallObject.transform.position = new Vector2(81000f, 81000f);
            Wall wall = wallObject.GetComponent<Wall>();
            PrepareUIAction(view, true); view.SelectEnvironment(wall);
            Transform editor = view.transform.Find("EnvironmentEditorContent");
            Button geometry = editor.Find("EnvironmentGeometrySlot").GetComponent<Button>();
            Canvas.ForceUpdateCanvases(); camera.Render();
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            CheckPointerHit(raycaster, geometry, "实际保存面板的独特槽是最上层点击命中目标");
            PointerClick(view, geometry);
            int triggerId = FindProperty(rules.CognitionBackpack, EnvironmentParameterKind.TriggerEnvironment);
            Button trigger = panelObject.transform.Find("CognitionBackpackArea/CognitionBackpackScroll/Viewport/Content/CognitionBackpackItem_" + triggerId).GetComponent<Button>();
            Canvas.ForceUpdateCanvases(); camera.Render();
            CheckPointerHit(raycaster, trigger, "实际保存面板的背包触发器性质是最上层点击命中目标");
            Check(trigger.IsInteractable(), "实际保存面板的高光触发器性质允许点击");
            // Edit Mode 立即清理其它旧行，保留被点按钮本身以通过真实指针事件。
            Transform content = trigger.transform.parent;
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i) != trigger.transform) Object.DestroyImmediate(content.GetChild(i).gameObject);
            trigger.transform.SetParent(panelObject.transform, true);
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(trigger.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(trigger.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(trigger.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Object.DestroyImmediate(trigger.gameObject);
            Check(view.DisplayedDraft.IsTriggerEnvironment && view.DisplayedDraft.HasChanges,
                "实际保存面板完整指针事件将触发器性质注入墙体草稿");
            Button confirm = editor.Find("ConfirmEnvironmentButton").GetComponent<Button>();
            Check(confirm.IsInteractable(), "实际墙体注入预览后统一确认按钮启用");
            Canvas.ForceUpdateCanvases(); camera.Render();
            CheckPointerHit(raycaster, confirm, "实际保存面板的统一确认按钮是最上层点击命中目标");
            Button.ButtonClickedEvent confirmation = confirm.onClick;
            PrepareUIAction(view, true); confirmation.Invoke();
            Check(wall.IsTriggerEnvironment && wall.GetComponent<Collider2D>().isTrigger,
                "实际墙体预设在面板确认后切换为触发器");
        }
        finally { camera.targetTexture = null; Object.DestroyImmediate(target); }
    }

    static void CheckPointerHit(GraphicRaycaster raycaster, Button button, string message)
    {
        RectTransform rect = (RectTransform)button.transform;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        raycaster.Raycast(new PointerEventData(null) { position = position }, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            message + " [命中=" + (hits.Count == 0 ? "无" : hits[0].gameObject.name) + "]");
    }

    static BasicPhysicalObject Host(string name, bool trigger, int capacity, params EnvironmentParameter[] values)
    {
        var item = new GameObject("P14 " + name) { hideFlags = HideFlags.HideAndDontSave };
        objects.Add(item); item.SetActive(false);
        item.transform.position = new Vector2(43000f + objects.Count * 20f, 43000f);
        var collider = item.AddComponent<BoxCollider2D>(); collider.isTrigger = trigger;
        var source = item.AddComponent<BasicPhysicalObject>();
        typeof(BasicPhysicalObject).GetField("environmentSlotCapacity", Hidden).SetValue(source, capacity);
        typeof(BasicPhysicalObject).GetField("environmentParameters", Hidden)
            .SetValue(source, new List<EnvironmentParameter>(values));
        item.SetActive(true); Physics2D.SyncTransforms();
        return source;
    }

    static int FindCalculation(CognitionBackpack bag)
    {
        foreach (var item in bag.Items) if (item.IsCalculation) return item.Id;
        return -1;
    }
    static int FindProperty(CognitionBackpack bag, EnvironmentParameterKind kind)
    {
        foreach (var item in bag.Items) if (item.Property != null && item.Property.Kind == kind) return item.Id;
        return -1;
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
    static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < .0001f, message + $" [{actual} == {expected}]");
    static void Invoke(object instance, string method)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo found = type.GetMethod(method, Hidden | BindingFlags.DeclaredOnly);
            if (found == null) continue;
            found.Invoke(instance, null); return;
        }
        throw new MissingMethodException(method);
    }
}
#endif
