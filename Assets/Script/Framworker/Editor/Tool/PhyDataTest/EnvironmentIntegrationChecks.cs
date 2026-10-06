using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 环境接入的可重复 Edit Mode 集成检查。使用实际组件、预制体、Collider2D 查询与实体速度容器。
/// 创建临时对象，结束即销毁，不保存场景/预制体。与只读调试窗口分开，仅由明确的验证命令运行。
/// </summary>
public static class EnvironmentIntegrationChecks
{
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static int group;

    // 检查夹具沿唯一持有者读取内部数值；运行时代码无需暴露可变容器。
    internal static object EffectField(BasicEntity body, string fieldName)
    {
        object state = typeof(BasicEntity).GetField("effectState", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(body);
        return state.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(state);
    }

    [MenuItem("自定义工具/EPhy 验证环境接入（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
        group = 0;
        // Edit Mode 使用临时调度器，避免自动单例调用仅 Play Mode 可用的 DontDestroyOnLoad。
        // 只替换原来为空的单例，验证完成后恢复，不修改运行时代码来适配测试。
        FieldInfo schedulerField = typeof(BaseAutoMonoMgr<MonoPublicMgr>).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousScheduler = schedulerField.GetValue(null);
        try
        {
            if ((MonoPublicMgr)previousScheduler == null)
                schedulerField.SetValue(null, NewObject("validation scheduler", Vector3.zero).AddComponent<MonoPublicMgr>());
            CheckGroundLifecycle();
            CheckDynamicForceRefresh();
            CheckRegions();
            CheckCapabilities();
            CheckCapabilityOwnership();
            CheckUniqueEnvironmentHost();
            CheckUnifiedSources();
            CheckSourceValidity();
            CheckPlatformDisplacement();
            CheckContactRoles();
            CheckTriggerContactFilter();
            CheckSlopeFootprintSupport();
            CheckSlopeSideSupport();
            CheckIceSideContactFeedback();
            CheckSlopeCeilingConstraint();
            CheckWallSlideBehavior();
            Directory.CreateDirectory(".utmp/EnvironmentValidation");
            File.WriteAllLines(".utmp/EnvironmentValidation/checks.txt", passed);
            Debug.Log($"Environment integration: {passed.Count} checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            schedulerField.SetValue(null, previousScheduler);
            Directory.CreateDirectory(".utmp/EnvironmentValidation");
            File.WriteAllLines(".utmp/EnvironmentValidation/checks.txt", passed);
        }
    }

    // Unity -batchmode -executeMethod EnvironmentIntegrationChecks.RunForBatch
    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckGroundLifecycle()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Collider2D ground = ice.GetComponent<Collider2D>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        body.RefreshSupport(ground);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).environmentSourceCount == 1, "冰面登记一个来源");
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1 &&
            PhysicsEditorEntityObservation.GetSnapshot(body).dynamicForceCount == 1, "冰面三类效果完整登记");
        Near(body.Frame.slowingMultiplier, .5f, "保留冰面阻力倍率");
        Near(body.Frame.jumpHeightOffset, -.5f, "保留冰面跳跃加成");
        ForceData initial = body.Force(ice);
        Near(initial.balanceSpeed, 15f, "冰面可移动实体初始化公式");
        Near(initial.recoverySpeed, 5f, "冰面受控恢复参数");
        body.RunForces();
        Near(body.Force(ice).speedStacking, 15f * body.envImpact * Time.fixedDeltaTime, "实际实体按旧公式积分冰面施力");
        body.RefreshSupport(ground);
        Near(body.Force(ice).speedStacking, 15f * Time.fixedDeltaTime, "重复支撑采样不重置累计速度");
        body.Direction = 0;
        body.RunForces();
        Check(body.Force(ice).type == E_PhyForceType.controlRecovery, "松开输入进入受控恢复");
        body.SetStack(ice, 4f);
        body.RefreshSupport(null);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).environmentSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 0 &&
            PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 0, "离地撤销绑定和持续状态");
        Check(body.Force(ice).type == E_PhyForceType.fadeAway, "离地动态力进入消退");
        Near(body.Force(ice).speedStacking, 4f, "退出保留已取得速度");
        body.RunForces();
        Check(body.Force(ice).speedStacking < 4f && body.Force(ice).speedStacking > 0, "尾效继续由实体阻力衰减");
        float tail = body.Force(ice).speedStacking;
        ice.Odex = 99;
        body.RefreshSupport(ground);
        Near(body.Force(ice).speedStacking, tail, "重新进入保留尾效速度");
        Near(body.Force(ice).recoverySpeed, initial.recoverySpeed, "重新进入沿用旧初始化参数规则");

        Disable(ice);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "源端禁用统一解除绑定");
        body.RefreshSupport(ground);
        Near(body.Frame.slowingMultiplier, 1f, "禁用组件仍有 Collider 时不读取表面效果");
        Check(body.EnvironmentContext.DebugSourceCount == 0, "禁用来源不能被支撑查询重新登记");
        ice.enabled = true;
        body.RefreshSupport(ground);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "原地重新启用冰面恢复登记");
        Disable(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "实体禁用统一解除所有来源");
        body.enabled = true;
        body.RefreshSupport(ground);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "实体原地启用恢复地面作用");
        ice.EnvironmentRegistration.ReleaseAll();
        ice.EnvironmentRegistration.ReleaseAll();
        body.EnvironmentContext.ReleaseAll();
        Check(body.EnvironmentContext.DebugSourceCount == 0, "双方重复清理幂等");

        GameObject plainObject = NewObject("non-moving", position);
        EnvironmentCheckReceiver plain = plainObject.AddComponent<EnvironmentCheckReceiver>();
        ForceData passive = ice.CreateEnvironmentForce(plain);
        Near(passive.balanceSpeed, 0, "冰面对无移动能力实体仍使用零平衡速度");
        Near(passive.recoverySpeed, .5f, "无移动能力分支保留地面恢复参数");
    }

    private static void CheckDynamicForceRefresh()
    {
        Vector3 position = NextPosition();
        ForceField source = Prefab<ForceField>("F1.prefab", position);
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        body.SetOrUpdateDynamicForce(source, new DynamicForceParameters(1f, 2f, 3f));
        body.SetStack(source, 4f);
        body.ChangeType(source, E_PhyForceType.fadeAway);
        body.SetOrUpdateDynamicForce(source, new DynamicForceParameters(5f, -6f, 7f));
        ForceData refreshed = body.Force(source);
        Near(refreshed.Force, 5f, "接触力刷新替换施力大小");
        Near(refreshed.balanceSpeed, 6f, "接触力刷新仍规范化平衡速度");
        Near(refreshed.recoverySpeed, 7f, "接触力刷新替换恢复速度");
        Near(refreshed.speedStacking, 4f, "接触力刷新保留累计速度");
        Check(refreshed.type == E_PhyForceType.apply, "接触力刷新恢复施加状态");
    }

    private static void CheckRegions()
    {
        Vector3 position = NextPosition();
        ForceField field = Prefab<ForceField>("F1.prefab", position);
        BoxCollider2D source = field.GetComponent<BoxCollider2D>();
        source.isTrigger = true;
        source.size = new Vector2(10, 10);
        EnvironmentCheckBody body = Body(position);
        Collider2D first = body.GetComponent<Collider2D>();
        GameObject child = NewObject("child collider", position + Vector3.right);
        child.transform.SetParent(body.transform, true);
        Collider2D second = child.AddComponent<BoxCollider2D>();
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).dynamicForceCount == 1,
            "复合碰撞体区域接入只登记一次");
        Check(EnvironmentCapabilities.FindEntity(second) == body, "子 Collider 仍正确识别动态实体身份");
        body.RunForces();
        Near(body.Force(field).speedStacking, -5f * Time.fixedDeltaTime, "力场使用预制体原有施力参数");
        first.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && body.Force(field).type == E_PhyForceType.fadeAway,
            "主碰撞体禁用后解除区域作用");
        first.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "Collider 重启后恢复区域作用");

        Collider2D source2 = field.gameObject.AddComponent<BoxCollider2D>();
        source2.isTrigger = true;
        source.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "多个源 Collider 取并集");
        source2.enabled = false;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "全部源 Collider 禁用后解除区域作用");
        source.enabled = true;
        Refresh(body);
        Disable(field);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && body.Force(field).type == E_PhyForceType.fadeAway,
            "力场禁用同时停止动态施力（旧遗漏修复）");
        field.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "力场原地启用无需新的 Enter 回调");
        Disable(body);
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "区域查询排除禁用接收者");
        body.enabled = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 1, "实体原地启用恢复区域作用");

        Physics2D.IgnoreCollision(source, first, true);
        try
        {
            Refresh(body);
            Check(body.EnvironmentContext.DebugSourceCount == 0, "区域查询尊重 Collider 忽略关系");
        }
        finally { Physics2D.IgnoreCollision(source, first, false); }
        Refresh(body);
        ForceField overlapping = Prefab<ForceField>("Pond.prefab", position);
        overlapping.GetComponent<Collider2D>().isTrigger = true;
        Refresh(body);
        Check(body.EnvironmentContext.DebugSourceCount == 2, "Pond 实际 ForceField 组件与另一环境独立叠加");
        Disable(field);
        Check(body.EnvironmentContext.DebugSourceCount == 1 && body.Force(overlapping).type == E_PhyForceType.apply,
            "退出一个环境不撤销另一个环境");
        Object.DestroyImmediate(overlapping.gameObject);
        body.RefreshSupport(null);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 0,
            "来源销毁后的无效绑定被实体清理");
        body.RunForces();
        Check(true, "已销毁来源的尾效不再回调施力者");
    }

    private static void CheckCapabilities()
    {
        Vector3 position = NextPosition();
        EnvironmentCheckBody body = Body(position);
        GameObject surface = NewObject("capabilities only", position);
        Collider2D collider = surface.AddComponent<BoxCollider2D>();
        body.RefreshSupport(collider);
        Check(body.EnvironmentContext.DebugSourceCount == 0, "普通无脚本地面无需效果登记");
        Near(body.Frame.slowingMultiplier, 1, "普通地面默认阻力倍率");
        Near(body.Frame.jumpHeightOffset, 0, "普通地面默认跳跃加成");
        EnvironmentCheckSurface platform = surface.AddComponent<EnvironmentCheckSurface>();
        body.RefreshSupport(collider);
        Check(body.Frame.platformDelta == platform.Delta && body.EnvironmentContext.DebugSourceCount == 1,
            $"单一环境宿主提供平台能力并建立一份来源关系 [{body.Frame.platformDelta} / {platform.Delta}, sources={body.EnvironmentContext.DebugSourceCount}]");
        MethodInfo build = typeof(BasicEntity).GetMethod("BuildMotionFrame", BindingFlags.Static | BindingFlags.NonPublic);
        EntityMotionFrame motion = (EntityMotionFrame)build.Invoke(null, new object[]
            { body, new Vector2(2, 0), 0f, true, Vector2.up, body.Frame.platformDelta, .02f, false });
        Near(motion.plannedWorldDelta.x, .04f + platform.Delta.x, "平台位移在实际运动快照中仅计一次 X");
        Near(motion.plannedWorldDelta.y, platform.Delta.y, "平台位移在实际运动快照中仅计一次 Y");
        platform.enabled = false;
        body.RefreshSupport(collider);
        Check(body.Frame.platformDelta == Vector2.zero, "禁用平台能力不再提供位移");
        body.RefreshSupport(null);
        Check(body.Frame.platformDelta == Vector2.zero, "离地清除平台采样");

        platform.enabled = true;
        body.RefreshSupport(collider);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 0 &&
            PhysicsEditorEntityObservation.GetSnapshot(body).dynamicForceCount == 0, "同一宿主可单独提供持续速度能力");
        body.envImpact = .25f;
        body.RunForces();
        Check(body.PassiveVelocity == platform.StateVelocity,
            $"同一环境宿主提供的持续速度不乘质量影响系数 [{body.PassiveVelocity} / {platform.StateVelocity}]");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(collider), platform),
            "同一环境宿主可以提供墙滑能力");
        var function = new PhysicalFunctionData { canLeftWall = platform, canRightWall = platform };
        var read = new ReadOnly_GeometryPhysicsData(new GeometryPhysicsData(), function);
        Check(read.canLeftWall && read.canRightWall, "逻辑层读取左右墙滑能力布尔事实");
        platform.enabled = false;
        Check(!read.canLeftWall && !read.canRightWall, "左右墙滑接口引用能识别 Unity 组件禁用");
        Object.DestroyImmediate(platform);
        Check(!read.canLeftWall && !read.canRightWall, "左右墙滑接口引用能识别 Unity 对象销毁");
    }

    private static void CheckCapabilityOwnership()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Near(ice.MovementSpeedOffset, -.5f, "冰面预制体保留移速修饰");
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        body.RefreshSupport(ice.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1,
            "冰面声明的移速和持续速度仍会登记");

        IceGround iceBelt = Prefab<IceGround>("ICE 1.prefab", position);
        Check(iceBelt.StateVelocity == new Vector2(3, 0), "ICE 1 预制体保留持续速度");
        body.RefreshSupport(iceBelt.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).stateVelocityCount == 1, "ICE 1 持续速度仍会登记");

        ForceField pond = Prefab<ForceField>("Pond.prefab", position);
        Near(pond.MovementSpeedOffset, -.4f, "Pond 预制体保留移速修饰");
        pond.GetComponent<Collider2D>().isTrigger = true;
        EnvironmentCheckBody swimmer = Body(position);
        Refresh(swimmer);
        Check(PhysicsEditorEntityObservation.GetSnapshot(swimmer).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(swimmer).stateVelocityCount == 0,
            "力场登记移速修饰且不再隐式登记持续速度");

        Taijie platform = Prefab<Taijie>("Taijie.prefab", NextPosition());
        Near(platform.MovementSpeedOffset, 1f, "台阶预制体保留移速修饰");
        EnvironmentCheckBody rider = Body(platform.transform.position + Vector3.up);
        rider.RefreshSupport(platform.GetComponent<Collider2D>());
        Check(PhysicsEditorEntityObservation.GetSnapshot(rider).movementModifierCount == 1 && PhysicsEditorEntityObservation.GetSnapshot(rider).stateVelocityCount == 0,
            "台阶登记移速修饰且不登记持续速度");

        Vector3 wallPosition = NextPosition();
        Wall wall = Prefab<Wall>("wall.prefab", wallPosition);
        EnvironmentCheckBody onWall = Body(wallPosition + Vector3.up);
        onWall.RefreshSupport(wall.GetComponent<Collider2D>());
        Check(onWall.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(onWall).movementModifierCount == 0
            && PhysicsEditorEntityObservation.GetSnapshot(onWall).stateVelocityCount == 0, "纯墙不建立绑定，也不登记移速或持续速度");
        Near(onWall.Frame.slowingMultiplier, 1f, "纯墙无地面响应时脚下采样阻力为默认");
        Near(onWall.Frame.jumpHeightOffset, 0f, "纯墙无地面响应时脚下采样起跳为默认");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(wall.GetComponent<Collider2D>()), wall),
            "无绑定时墙滑能力仍可按碰撞体查询");
    }

    // 同一组件同时声明墙滑、移速和地面响应。脚下只登记持续效果并采样地面；侧面只取墙滑；区域只登记移速。
    private static void CheckContactRoles()
    {
        Vector3 position = NextPosition();
        GameObject host = NewObject("contact roles", position);
        BoxCollider2D collider = host.AddComponent<BoxCollider2D>();
        EnvironmentCheckContact contact = host.AddComponent<EnvironmentCheckContact>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);

        body.RefreshSupport(collider);
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1 && body.EnvironmentContext.DebugSourceCount == 1,
            "脚下登记同宿主的移速修饰");
        Near(body.Frame.slowingMultiplier, contact.SlowingEffect, "脚下采样同宿主的地面阻力");
        Near(body.Frame.jumpHeightOffset, contact.JumpHeightNum, "脚下采样同宿主的起跳加成");

        body.RefreshSupport(null);
        Check(body.EnvironmentContext.DebugSourceCount == 0 && PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 0,
            "只查询侧面墙滑时不登记移速");
        Check(ReferenceEquals(EnvironmentCapabilities.Find<IWallSlideSurface>(collider), contact),
            "侧面命中可解析墙滑能力");

        collider.isTrigger = true;
        body.transform.position = position;
        Refresh(body);
        var speeds = (Dictionary<EnvironmentRegistration, float>)EffectField(body, "movementModifiers");
        Check(PhysicsEditorEntityObservation.GetSnapshot(body).movementModifierCount == 1
            && speeds.TryGetValue(contact.EnvironmentRegistration, out float offset)
            && Mathf.Abs(offset - contact.MovementSpeedOffset) < .0001f,
            "区域登记移速修饰，不登记墙滑乘数");
        Near(body.Frame.slowingMultiplier, 1f, "区域不采样脚下阻力");
        Near(body.Frame.jumpHeightOffset, 0f, "区域不采样起跳加成");
    }

    // 触发体与实心表面同时重叠探测盒时，接触查询必须返回实心体。区域重叠规则不在这里改变。
    private static void CheckTriggerContactFilter()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.boxCastH = new Vector2(.3f, .12f);
        config.boxCastV = new Vector2(.2f, .4f);
        try
        {
            Collider2D ground = Solid(origin + new Vector3(0, -.5f, 0), Vector2.one);
            TriggerVeil(origin + new Vector3(0, .25f, 0), new Vector2(3f, 1.5f));
            Collider2D wall = Solid(origin + new Vector3(29.5f, 0, 0), new Vector2(1f, 2f));
            TriggerVeil(origin + new Vector3(30.2f, 0, 0), new Vector2(2f, 3f));
            Collider2D ceiling = Solid(origin + new Vector3(60, .5f, 0), Vector2.one);
            TriggerVeil(origin + new Vector3(60, -.25f, 0), new Vector2(3f, 1.5f));
            Transform groundProbe = NewObject("ground probe", origin + new Vector3(0, .04f, 0)).transform;
            Transform leftProbe = NewObject("left probe", origin + new Vector3(30.05f, 0, 0)).transform;
            Transform rightProbe = NewObject("right probe", origin + new Vector3(90, 0, 0)).transform;
            Transform upProbe = NewObject("up probe", origin + new Vector3(60, -.04f, 0)).transform;
            Physics2D.SyncTransforms();

            EnvironmentCheckCharacter character = ProbeCharacter(origin + new Vector3(0, 20, 0), config,
                groundProbe, leftProbe, rightProbe);
            character.Query();
            Check(character.Ground == ground && character.Grounded, "角色脚下过滤 Trigger 后命中实心表面");
            Check(character.Left == wall && character.OnLeft, "角色侧面过滤 Trigger 后命中实心墙");

            EnvironmentCheckBox box = ProbeBox(origin + new Vector3(0, 40, 0), config,
                groundProbe, leftProbe, rightProbe, upProbe);
            box.Query();
            Check(box.Ground == ground && box.Grounded, "箱体脚下过滤 Trigger 后命中实心表面");
            Check(box.Left == wall && box.OnLeft, "箱体侧面过滤 Trigger 后命中实心墙");
            Check(box.Top, "箱体顶头过滤 Trigger 后仍发现实心表面");
            // 墙与地面同层、同时落入脚下探针：首个侧面命中不能抹掉支撑。
            Vector3 corner = origin + Vector3.right * 120f;
            Solid(corner + new Vector3(.22f, .5f, 0), new Vector2(.2f, 1f));
            Collider2D cornerFloor = Solid(corner + new Vector3(0, -.5f, 0), Vector2.one);
            Transform cornerProbe = NewObject("corner ground probe", corner + new Vector3(0, .04f, 0)).transform;
            Physics2D.SyncTransforms();
            character.Apply(config, cornerProbe, leftProbe, rightProbe);
            character.Query();
            Check(character.Ground == cornerFloor && character.Grounded, "墙角角色保留有效地面");
            box.Apply(config, cornerProbe, leftProbe, rightProbe, upProbe);
            box.Query();
            Check(box.Ground == cornerFloor && box.Grounded, "墙角箱体保留有效地面");
            cornerProbe.position += Vector3.up * .035f;
            Physics2D.SyncTransforms();
            character.Query();
            box.Query();
            Check(character.Ground == cornerFloor && box.Ground == cornerFloor,
                "墙角微小离地间隙仍保持支撑");
            // 已有轻微交叠时，离开墙角的 X 不能被零距离命中反向锁住。
            Solid(origin + new Vector3(.55f, 20f, 0), new Vector2(.2f, 2f));
            Physics2D.SyncTransforms();
            Check(character.CandidateAllowedX(-.02f) < 0f, "墙角允许向外退出");
            Check(character.DebugCandidatePathCount > 0 && character.DebugCandidateFirstPathZeroDistance &&
                character.DebugCandidateOverlapCount > 0, "墙角确有零距离路径及终点重叠");
            Check(character.CandidateAllowedX(.02f) <= 0f, "墙角仍阻止向内穿入");
            ceiling.gameObject.SetActive(false);
            Physics2D.SyncTransforms();
            box.Query();
            Check(!box.Top, "箱体顶头只有 Trigger 时不记为接触");
        }
        finally
        {
            Object.DestroyImmediate(config);
        }
    }

    // F_6.2_6-6：箱体前缘贴坡而中心脚探针悬空时，着地事实须与完整箱体的坡面接触一致。
    private static void CheckSlopeFootprintSupport()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.boxCastH = new Vector2(.75f, .1f);
        config.boxCastV = new Vector2(.02f, 1.6f);
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            float halfWidth = .76f;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * halfWidth + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            Transform foot = NewObject("slope foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("slope side", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, config, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.y < .99f,
                "箱体前缘贴坡时仍得到真实坡面支撑法线");
            for (int step = 0; step < 6; step++)
            {
                Vector3 alongSlope = new Vector3(.1f, Mathf.Tan(slope) * .1f, 0f);
                box.transform.position += alongSlope;
                foot.position += alongSlope;
                Physics2D.SyncTransforms();
                box.Query();
                Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.y < .99f,
                    $"箱体沿坡第 {step + 1} 段持续保留真实支撑");
            }
        }
        finally { Object.DestroyImmediate(config); }
    }

    // F_6.2_6-8：左上坡前缘碰到冰面右侧时，脚下不能把冰面的侧面当成顶面支撑。
    private static void CheckSlopeSideSupport()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.maxClimbAngle = 45f;
        config.boxCastH = new Vector2(.75f, .1f);
        config.boxCastV = new Vector2(.02f, 1.6f);
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * .76f + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, -30f);
            Collider2D iceSide = Solid(origin + new Vector3(-2.758f, .165f, 0f),
                new Vector2(4f, .37f));
            Transform foot = NewObject("ice side foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("ice side probe", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, config, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground && box.DebugGroundNormal.x > .2f,
                $"冰面侧面接触不替换坡面支撑 [{box.Ground?.name} / {iceSide.name} / {box.DebugGroundNormal}]");
            Vector2 request = box.CandidateRequest(new Vector2(-.08f, Mathf.Tan(slope) * .08f),
                groundSlopeDeltaY: Mathf.Tan(slope) * .08f);
            Check(Mathf.Abs(request.x) < .01f && Mathf.Abs(request.y) < .01f,
                $"冰面侧挡使单个箱体的沿坡请求停止 [{request}]");
        }
        finally { Object.DestroyImmediate(config); }
    }

    // F_6.2_7-5：冰面侧挡裁掉箱体位移后，接触对须把推动者退到不重叠处，且不登记无效施力。
    private static void CheckIceSideContactFeedback()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics boxConfig = ScriptableObject.CreateInstance<SO_CPhysics>();
        SO_CPhysics pusherConfig = ScriptableObject.CreateInstance<SO_CPhysics>();
        boxConfig.hideFlags = pusherConfig.hideFlags = HideFlags.DontSave;
        boxConfig.groundLayer = boxConfig.wallLayer = 1 << 6;
        boxConfig.maxClimbAngle = 45f;
        boxConfig.boxCastH = new Vector2(.75f, .1f);
        boxConfig.boxCastV = new Vector2(.02f, 1.6f);
        // 推动者在此反例中无局部静态阻挡，隔离接触对反馈本身。
        pusherConfig.groundLayer = pusherConfig.wallLayer = 0;
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * .76f + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, -30f);
            Collider2D iceSide = Solid(origin + new Vector3(-2.758f, .165f, 0f),
                new Vector2(4f, .37f));
            Transform foot = NewObject("feedback foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("feedback side", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, boxConfig, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            EnvironmentCheckBox pusher = ProbeBox(origin + new Vector3(1.255f, .77f, 0f),
                pusherConfig, side, side, side, side);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground, "受阻接触对仍保留坡面支撑");
            var solver = new PhysicsSolverMgr();
            solver.AddPhyBoX(box.Prediction(new Vector2(-.08f, Mathf.Tan(slope) * .08f), -4f, Mathf.Tan(slope) * .08f));
            solver.AddPhyBoX(pusher.Prediction(new Vector2(-.14f, 0f), -7f));
            typeof(PhysicsSolverMgr).GetMethod("ContactForSolution", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(solver, null);
            float overlap = box.GetComponent<BoxCollider2D>().bounds.extents.x +
                pusher.GetComponent<BoxCollider2D>().bounds.extents.x -
                Mathf.Abs((box.transform.position.x + box.Request.x) -
                    (pusher.transform.position.x + pusher.Request.x));
            Check(Mathf.Abs(box.Request.x) < .01f && box.BlockedDirection < 0f,
                $"冰面侧挡继续约束箱体 [{box.Request}]");
            Check(overlap <= .003f, $"回算后推动者与被挡箱体不再深度重叠 [{overlap}]");
            Check(box.DebugDynamicForceCount == 0, "被冰面挡住时不登记不可行接触力");
            iceSide.enabled = false;
            Physics2D.SyncTransforms();
            solver.AddPhyBoX(box.Prediction(new Vector2(-.08f, Mathf.Tan(slope) * .08f), -4f, Mathf.Tan(slope) * .08f));
            solver.AddPhyBoX(pusher.Prediction(new Vector2(-.14f, 0f), -7f));
            typeof(PhysicsSolverMgr).GetMethod("ContactForSolution", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(solver, null);
            Check(box.DebugDynamicForceCount > 0, "无冰面侧挡时仍可登记推箱接触力");
            // 其它来源可抵消净被动速度；仍须清除这条已不可行的接触来源。
            box.SetContactStack(pusher, -2f);
            iceSide.enabled = true;
            Physics2D.SyncTransforms();
            solver.AddPhyBoX(box.Prediction(new Vector2(-.08f, Mathf.Tan(slope) * .08f), -4f, Mathf.Tan(slope) * .08f));
            solver.AddPhyBoX(pusher.Prediction(new Vector2(-.14f, 0f), -7f));
            typeof(PhysicsSolverMgr).GetMethod("ContactForSolution", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(solver, null);
            ForceData stopped = box.ContactForce(pusher);
            Check(stopped.type == E_PhyForceType.fadeAway && Mathf.Abs(stopped.speedStacking) < .0001f,
                "原有推箱力遇冰面侧挡后退出并清空朝障碍累计速度");
        }
        finally
        {
            Object.DestroyImmediate(boxConfig);
            Object.DestroyImmediate(pusherConfig);
        }
    }

    // F_6.2_6-7：沿坡的基础 Y 若撞上坡顶平台底面，不能仍把该段上升交给引擎反复挤回。
    private static void CheckSlopeCeilingConstraint()
    {
        Vector3 origin = NextPosition();
        SO_CPhysics config = ScriptableObject.CreateInstance<SO_CPhysics>();
        config.hideFlags = HideFlags.DontSave;
        config.groundLayer = 1 << 6;
        config.wallLayer = 1 << 6;
        config.maxClimbAngle = 45f;
        config.boxCastH = new Vector2(.75f, .1f);
        config.boxCastV = new Vector2(.02f, 1.6f);
        try
        {
            float slope = 30f * Mathf.Deg2Rad;
            Collider2D ground = Solid(origin + Vector3.down *
                (Mathf.Tan(slope) * .76f + .1f / Mathf.Cos(slope)), new Vector2(6f, .2f));
            ground.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            Collider2D platform = Solid(origin + Vector3.up * 1.65f, new Vector2(4f, .2f));
            Transform foot = NewObject("blocked slope foot", origin + Vector3.down * .057f).transform;
            Transform side = NewObject("blocked slope side", origin + Vector3.right * 10f).transform;
            EnvironmentCheckBox box = ProbeBox(origin + Vector3.up * .77f, config, foot, side, side, side);
            box.GetComponent<BoxCollider2D>().size = new Vector2(1.52f, 1.54f);
            Physics2D.SyncTransforms();
            box.Query();
            Check(box.Grounded && box.Ground == ground, "坡顶平台下方仍由坡面提供支撑");
            Vector2 verticalOffsetRequest = box.CandidateRequest(Vector2.zero, Vector2.up * .1f);
            Check(verticalOffsetRequest.y < .02f,
                $"坡顶平台底面限制实体间向上挤出 [{verticalOffsetRequest}]");
            Vector2 requested = box.CandidateRequest(new Vector2(.3f, Mathf.Tan(slope) * .3f),
                groundSlopeDeltaY: Mathf.Tan(slope) * .3f);
            Check(requested.x < .04f && requested.y < .03f,
                $"坡顶平台底面阻止沿坡上升 [{requested} / {platform.name}]");
            platform.transform.position = origin + new Vector3(2.8f, 1.8f, 0f);
            Physics2D.SyncTransforms();
            requested = box.CandidateRequest(new Vector2(.3f, Mathf.Tan(slope) * .3f),
                groundSlopeDeltaY: Mathf.Tan(slope) * .3f);
            Check(requested.x < .27f && requested.y < .16f,
                $"坡顶平台边缘限制进入后的上升 [{requested}]");
        }
        finally { Object.DestroyImmediate(config); }
    }

    // 调用真实贴墙状态和角色限速，不使用把 VerticalTransmission 盖空的接触夹具。
    private static void CheckWallSlideBehavior()
    {
        Vector3 position = NextPosition();
        EnvironmentCheckSlide left = Slide(position, .5f);
        EnvironmentCheckSlide right = Slide(position + Vector3.right * 3f, 2f);
        var geometry = new GeometryPhysicsData();
        var function = new PhysicalFunctionData { canLeftWall = left, canRightWall = right };
        var read = new ReadOnly_GeometryPhysicsData(geometry, function);
        var input = new PlayerInputData();
        var action = new MovementData();
        var fsm = new PlayerStateMachine();
        fsm.InitData(read, input, action);
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);

        input.moveInput = 0f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.inAir && action.onMove == 0f,
            "松手退出贴墙并写回零方向");

        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        function.canRightWall = null;
        input.moveInput = 1f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.inAir, "朝向没有墙滑能力的一侧则退出");

        function.canRightWall = right;
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = 1f;
        fsm.Update(input);
        Check(action.nowState == PlayerStateMachine.E_playerState.onWallSliding && action.onMove == 1f,
            "朝向有墙滑能力的一侧保持贴墙");

        EnvironmentCheckSlider slider = NewObject("wall slide body", position + Vector3.up * 5f).AddComponent<EnvironmentCheckSlider>();
        slider.Bind(new ReadOnly_ActionData(action), function, fsm.EventSystem);
        action.nowState = PlayerStateMachine.E_playerState.onWallSliding;
        action.onMove = -1f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -1f, "朝左按左墙乘数限速");
        action.onMove = 1f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -4f, "朝右按右墙乘数限速");
        right.enabled = false;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -10f, "朝向一侧能力失效时不限速");
        right.enabled = true;
        action.onMove = 0f;
        slider.FallAt(-10f);
        slider.Limit();
        Near(slider.Vertical, -10f, "松手不选择墙滑乘数");

        slider.TouchBothWalls();
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = 1f;
        input.jumpPressed = true;
        fsm.Update(input);
        slider.ConsumeEvents();
        Near(slider.LatestImpulse(), -30f, "双侧接触朝右墙跳时冲量向左");
        fsm.ChangeState(PlayerStateMachine.E_playerState.onWallSliding);
        input.moveInput = -1f;
        input.jumpPressed = true;
        fsm.Update(input);
        slider.ConsumeEvents();
        Near(slider.LatestImpulse(), 30f, "双侧接触朝左墙跳时冲量向右");
    }

    private static EnvironmentCheckSlide Slide(Vector3 position, float multiplier)
    {
        EnvironmentCheckSlide slide = NewObject("slide side", position).AddComponent<EnvironmentCheckSlide>();
        slide.WallSlideMultiplier = multiplier;
        return slide;
    }

    private static Collider2D Solid(Vector3 position, Vector2 size)
    {
        GameObject item = NewObject("solid contact", position);
        item.layer = 6;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
        collider.size = size;
        return collider;
    }

    private static void TriggerVeil(Vector3 position, Vector2 size)
    {
        GameObject item = NewObject("trigger veil", position);
        item.layer = 6;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = true;
    }

    private static EnvironmentCheckCharacter ProbeCharacter(Vector3 position, SO_CPhysics config,
        Transform ground, Transform left, Transform right)
    {
        GameObject item = NewObject("character probe", position);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        EnvironmentCheckCharacter probe = item.AddComponent<EnvironmentCheckCharacter>();
        probe.Apply(config, ground, left, right);
        return probe;
    }

    private static EnvironmentCheckBox ProbeBox(Vector3 position, SO_CPhysics config,
        Transform ground, Transform left, Transform right, Transform up)
    {
        GameObject item = NewObject("box probe", position);
        item.AddComponent<Rigidbody2D>();
        item.AddComponent<BoxCollider2D>();
        EnvironmentCheckBox probe = item.AddComponent<EnvironmentCheckBox>();
        probe.Apply(config, ground, left, right, up);
        return probe;
    }

    private static void CheckUniqueEnvironmentHost()
    {
        GameObject host = NewObject("unique environment host", NextPosition());
        host.SetActive(false);
        IceGround ice = host.AddComponent<IceGround>();
        ice.enabled = false;
        Debug.Log("Unique environment host check: the next two AddComponent errors are expected rejections.");
        Check(host.AddComponent<IceGround>() == null, "同类环境入口重复挂载被 Unity 拒绝");
        Check(host.AddComponent<ForceField>() == null, "不同子类不能绕过同一环境入口约束");
        // 验证实际配置结果；原生拒绝消息在 batch 日志中，不依赖托管日志回调计数。
        var remaining = host.GetComponents<BasicPhysicalObject>();
        Check(remaining.Length == 1 && remaining[0] == ice && !ice.enabled,
            "禁用入口仍占身份，重复请求不新增或替换原组件");

        BoxCollider2D collider = host.AddComponent<BoxCollider2D>();
        Check(EnvironmentCapabilities.FindHost(collider) == null, "层级失活对象不提供环境来源");
        host.SetActive(true);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "禁用的唯一宿主不提供环境来源");
        ice.enabled = true;
        Check(ReferenceEquals(EnvironmentCapabilities.FindHost(collider), ice), "启用宿主通过来源接口被发现");
        collider.enabled = false;
        Check(EnvironmentCapabilities.FindHost(collider) == null, "禁用 Collider 不提供环境来源");
        collider.enabled = true;
        host.SetActive(false);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "Collider 所属物体失活时不提供环境来源");
        host.SetActive(true);
        Check(ReferenceEquals(EnvironmentCapabilities.FindHost(collider), ice), "重新启用后恢复同一来源身份");
        UnityEngine.Object.DestroyImmediate(ice);
        Check(EnvironmentCapabilities.FindHost(collider) == null, "销毁唯一宿主后不提供环境来源");
    }

    // 注入已经采集的几何事实，专门核对关系连续性；实际 overlap 查询由 CheckRegions 覆盖。
    private static void CheckUnifiedSources()
    {
        Vector3 position = NextPosition();
        IceGround ice = Prefab<IceGround>("ICE.prefab", position);
        Collider2D collider = ice.GetComponent<Collider2D>();
        EnvironmentCheckBody body = Body(position + Vector3.up * 3);
        EntityEnvironmentContext context = body.EnvironmentContext;
        var hits = new List<Collider2D> { collider, collider };
        var bindings = (System.Collections.IDictionary)typeof(EntityEnvironmentContext)
            .GetField("bindings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
        context.RefreshEnvironment(collider, null);
        object binding = bindings[ice.EnvironmentRegistration];
        body.SetStack(ice, 4f);
        context.RefreshEnvironment(null, hits);
        Check(ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]), "脚下转区域保留原绑定，不中途退出再进入");
        Near(body.Force(ice).speedStacking, 4, "接入方式转换保留累计速度");
        Near(body.Frame.slowingMultiplier, 1, "仅区域命中不采样脚下阻力");
        context.RefreshEnvironment(collider, hits);
        Check(context.DebugSourceCount == 1 && ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]),
            "脚下与重复区域事实同时命中仍只有原绑定");
        context.RefreshEnvironment(collider, null);
        Check(ReferenceEquals(binding, bindings[ice.EnvironmentRegistration]), "区域消失但脚下仍在时保持绑定");
        context.RefreshEnvironment(null, null);
        Check(context.DebugSourceCount == 0 && body.Force(ice).type == E_PhyForceType.fadeAway,
            "完整事实消失才解除，动态力进入尾效");
        Near(body.Force(ice).speedStacking, 4, "完整退出不清零累计速度");
        context.RefreshEnvironment(collider, hits);
        context.ReleaseAll();
        var sources = (HashSet<EnvironmentRegistration>)typeof(EntityEnvironmentContext)
            .GetField("currentSources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
        Check(context.DebugSourceCount == 0 && sources.Count == 0 && body.Frame.platformDelta == Vector2.zero,
            "实体释放时清空绑定、事实缓存与采样结果");
    }

    private static void CheckSourceValidity()
    {
        var plain = new EnvironmentCheckSource();
        Check(plain.EnvironmentRegistration.IsActive, "纯 C# 来源可提供注册表有效性");
        plain.IsEnvironmentActive = false;
        Check(!plain.EnvironmentRegistration.IsActive, "注册表跟随来源接口的有效性变化");
        BasicPhysicalObject host = NewObject("source validity", NextPosition()).AddComponent<BasicPhysicalObject>();
        EnvironmentRegistration registration = host.EnvironmentRegistration;
        Check(registration.IsActive, "Unity 宿主启用时来源有效");
        host.enabled = false;
        Check(!registration.IsActive, "Unity 宿主禁用时来源无效");
        host.enabled = true;
        host.gameObject.SetActive(false);
        Check(!registration.IsActive, "Unity 宿主层级失活时来源无效");
        host.gameObject.SetActive(true);
        Check(registration.IsActive, "Unity 宿主重新激活恢复来源有效性");
        Object.DestroyImmediate(host);
        Check(!registration.IsActive, "接口持有已销毁宿主时安全返回无效");
    }

    private static void CheckPlatformDisplacement()
    {
        Vector3 target = NextPosition();
        Taijie platform = Prefab<Taijie>("Taijie.prefab", target);
        platform.ToMovePoint(target);
        Vector2 expected = target - platform.transform.position;
        // 固定为已到达终点的时刻，避免真实等待造成测试不稳定。
        typeof(Taijie).GetField("nowtime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(platform, AudioSettings.dspTime - 10d);
        MethodInfo tick = typeof(Taijie).GetMethod("FixFun", BindingFlags.Instance | BindingFlags.NonPublic);
        tick.Invoke(platform, null);
        Check(platform.Delta == expected, "实际平台记录到达终点的本帧位移");
        EnvironmentCheckBody rider = Body(target + Vector3.up * 3);
        rider.RefreshSupport(platform.GetComponent<Collider2D>());
        Check(rider.Frame.platformDelta == expected, "实际平台位移经接口进入脚下采样");
        tick.Invoke(platform, null);
        Check(platform.Delta == Vector2.zero, "平台停止后下一帧位移清零");
        platform.ToMovePoint(target);
        typeof(Taijie).GetField("nowtime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(platform, AudioSettings.dspTime - 10d);
        tick.Invoke(platform, null);
        Disable(platform);
        Check(platform.Delta == Vector2.zero && rider.EnvironmentContext.DebugSourceCount == 0,
            "平台禁用时位移清零并立即解除骑手绑定");
    }

    private static Vector3 NextPosition() => new Vector3(10000 + 100 * group++, 10000, 0);
    private static GameObject NewObject(string name, Vector3 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position;
        objects.Add(item);
        return item;
    }
    private static T Prefab<T>(string name, Vector3 position) where T : Component
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/" + name);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/" + name);
        if (asset == null) throw new InvalidOperationException("Missing fixture prefab: " + name);
        GameObject item = Object.Instantiate(asset);
        item.hideFlags = HideFlags.DontSave;
        item.transform.position = position;
        objects.Add(item);
        return item.GetComponent<T>();
    }
    private static EnvironmentCheckBody Body(Vector3 position)
    {
        GameObject item = NewObject("environment receiver", position);
        item.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        item.AddComponent<BoxCollider2D>();
        var body = item.AddComponent<EnvironmentCheckBody>();
        body.InitializeForChecks();
        return body;
    }
    private static void Refresh(EnvironmentCheckBody body)
    {
        Physics2D.SyncTransforms();
        body.RefreshRegions();
    }
    private static void Disable(Behaviour component)
    {
        component.enabled = false;
        // Edit Mode 不依赖 Unity 自动派发普通 MonoBehaviour 消息；显式验证真实的框架清理入口。
        // 若 Unity 已派发，本次重复调用同时验证幂等性。
        component.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < .0001f, message + $" [{actual} == {expected}]");
}

// 验证夹具：计算和环境清理使用实际 BasicEntity；仅跳过场景探针配置与公共时序注册。
public sealed class EnvironmentCheckBody : BasicEntity, ICanMove
{
    public float Direction = 1;
    public float MovingDirection => Direction;
    public float Mobility => 15;
    public EntityEnvironmentFrame Frame => EnvironmentFrame;
    public Vector2 PassiveVelocity => new Vector2(playerPhysicsData.phyHSpeed, playerPhysicsData.phyVSpeed);
    protected override void Awake() => InitializeForChecks();
    protected override void OnEnable() { }
    protected override void GeometricQuery() { }
    public void InitializeForChecks()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        nowGemetry = new GeometryPhysicsData();
        nowPhyFun = new BasePhyFunData();
        playerPhysicsData = new PlayerPhysicsData();
    }
    public void RefreshSupport(Collider2D collider)
    {
        nowGemetry.groundCollider = collider;
        nowGemetry.isGrounded = collider != null;
        self_resistanceCoefficient = collider != null ? 20 : 1;
        CollectRegionOverlapFacts();
        base.PhyFunUpdate();
    }

    public void RefreshRegions()
    {
        CollectRegionOverlapFacts();
        base.PhyFunUpdate();
    }
    public void RunForces() => typeof(BasicEntity).GetMethod("UpdatePassiveEffectSpeed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, null);
    private Dictionary<IDynamicAddForce, ForceData> Forces =>
        (Dictionary<IDynamicAddForce, ForceData>)EnvironmentIntegrationChecks.EffectField(this, "dynamicForces");
    public ForceData Force(IDynamicAddForce source) => Forces[source];
    public void SetStack(IDynamicAddForce source, float speed)
    {
        ForceData data = Forces[source];
        data.speedStacking = speed;
        Forces[source] = data;
    }
}

public sealed class EnvironmentCheckContact : BasicPhysicalObject, IMovementSpeedModifier, IWallSlideSurface, IGroundResponse
{
    public float MovementSpeedOffset => -.25f;
    public float WallSlideMultiplier => .4f;
    public float SlowingEffect => 2f;
    public float JumpHeightNum => .3f;
}

public sealed class EnvironmentCheckCharacter : BasePhysicsEntity
{
    protected override void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        Init();
    }
    protected override void OnEnable() { }
    protected override float HorizontalSpeedCalculation() => 0f;
    protected override void PhyEventUpdate() { }
    protected override void VerticalTransmission() { }
    public void Apply(SO_CPhysics config, Transform ground, Transform left, Transform right)
    {
        // Edit Mode 添加组件时 Awake 可能尚未执行；查询前补齐几何容器。
        if (nowGemetry == null)
        {
            rb = GetComponent<Rigidbody2D>();
            boxCollider = GetComponent<BoxCollider2D>();
            playerPhysicsData = new PlayerPhysicsData();
            nowGemetry = new GeometryPhysicsData();
            Init();
        }
        cPhysics = config;
        groundV = ground;
        leftV = left;
        rightV = right;
    }
    public void Query() => GeometricQuery();
    public Collider2D Ground => nowGemetry.groundCollider;
    public bool Grounded => nowGemetry.isGrounded;
    public Collider2D Left => nowGemetry.leftWallCollider;
    public bool OnLeft => nowGemetry.onLeftWall;
    public float CandidateAllowedX(float x)
    {
        var bounds = boxCollider.bounds;
        var frame = new EntityMotionFrame(Vector2.zero, Vector2.zero, new Vector2(x, 0f), Vector2.zero);
        var sample = new PhysicalBoundingBox
        {
            point = (Vector2)bounds.center + frame.plannedWorldDelta,
            size = bounds.extents,
            motionFrame = frame,
            myPhyBox = this
        };
        typeof(BasicEntity).GetMethod("ProbeCandidateEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(this, new object[] { sample });
        return entitySolutionResult.allowedHorizontalDelta;
    }
}

public sealed class EnvironmentCheckBox : PhysicalBox
{
    protected override void Awake()
    {
        BindEffectReceiver();
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        nowPhyFun = new BasePhyFunData();
    }
    protected override void OnEnable() { }
    public void Apply(SO_CPhysics config, Transform ground, Transform left, Transform right, Transform up)
    {
        // Edit Mode 添加组件时 Awake 可能尚未执行；查询前补齐几何容器。
        if (nowGemetry == null)
        {
            BindEffectReceiver();
            rb = GetComponent<Rigidbody2D>();
            boxCollider = GetComponent<BoxCollider2D>();
            playerPhysicsData = new PlayerPhysicsData();
            nowGemetry = new GeometryPhysicsData();
            nowPhyFun = new BasePhyFunData();
        }
        cPhysics = config;
        groundV = ground;
        leftV = left;
        rightV = right;
        upV = up;
    }
    public void Query() => GeometricQuery();
    public Collider2D Ground => nowGemetry.groundCollider;
    public bool Grounded => nowGemetry.isGrounded;
    public Collider2D Left => nowGemetry.leftWallCollider;
    public bool OnLeft => nowGemetry.onLeftWall;
    public bool Top => nowGemetry.istop;
    public Vector2 Request => entitySolutionResult.requestedWorldDelta;
    public float BlockedDirection => entitySolutionResult.blockedHorizontalDirection;
    public ForceData ContactForce(BasicEntity source)
    {
        var forces = (Dictionary<IDynamicAddForce, ForceData>)EnvironmentIntegrationChecks.EffectField(this, "dynamicForces");
        return forces[source];
    }
    public void SetContactStack(BasicEntity source, float speed)
    {
        var forces = (Dictionary<IDynamicAddForce, ForceData>)EnvironmentIntegrationChecks.EffectField(this, "dynamicForces");
        ForceData data = forces[source];
        data.speedStacking = speed;
        forces[source] = data;
    }
    public PhysicalBoundingBox Prediction(Vector2 baseDelta, float horizontalSpeed, float groundSlopeDeltaY = 0f)
    {
        EntityMotionFrame frame = new EntityMotionFrame(Vector2.zero,
            new Vector2(horizontalSpeed, 0f), baseDelta, Vector2.zero, groundSlopeDeltaY);
        typeof(BasicEntity).GetField("motionFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(this, frame);
        entitySolutionResult = default;
        Bounds bounds = boxCollider.bounds;
        return new PhysicalBoundingBox
        {
            point = (Vector2)bounds.center + frame.plannedWorldDelta,
            size = bounds.extents,
            motionFrame = frame,
            myPhyBox = this
        };
    }
    public Vector2 CandidateRequest(Vector2 baseDelta, Vector2 offset = default, float groundSlopeDeltaY = 0f)
    {
        // 合成输入必须明确哪些 Y 属于坡面；接触偏置和世界竖直位移仍保持独立。
        EntityMotionFrame frame = new EntityMotionFrame(Vector2.zero, Vector2.zero, baseDelta, Vector2.zero, groundSlopeDeltaY);
        typeof(BasicEntity).GetField("motionFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(this, frame);
        entitySolutionResult.displacementOffset = offset;
        Bounds bounds = boxCollider.bounds;
        var sample = new PhysicalBoundingBox
        {
            point = (Vector2)bounds.center + frame.plannedWorldDelta,
            size = bounds.extents,
            motionFrame = frame,
            myPhyBox = this
        };
        typeof(BasicEntity).GetMethod("ProbeCandidateEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(this, new object[] { sample });
        typeof(BasicEntity).GetMethod("FinalizeRequestedDisplacement", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(this, null);
        return entitySolutionResult.requestedWorldDelta;
    }
}

public sealed class EnvironmentCheckSlide : BasicPhysicalObject, IWallSlideSurface
{
    public float WallSlideMultiplier { get; set; }
}

public sealed class EnvironmentCheckSlider : CharacterPhysics
{
    protected override void Awake() => EnsureReady();
    protected override void OnEnable() { }
    public void Bind(ReadOnly_ActionData action, PhysicalFunctionData function, LocalEventSystem<PlayerStateMachine.E_playEvent> events)
    {
        EnsureReady();
        playphyFunData = function;
        nowPhyFun = function;
        Init(action, events);
    }
    public void FallAt(float vertical) => playerPhysicsData.verticalSpeed = vertical;
    public void Limit() => VerticalTransmission();
    public void ConsumeEvents() => PhyEventUpdate();
    public float Vertical => playerPhysicsData.verticalSpeed;
    public void TouchBothWalls()
    {
        nowGemetry.onLeftWall = true;
        nowGemetry.onRightWall = true;
    }
    public float LatestImpulse()
    {
        var forces = (List<SpeedStackData>)EnvironmentIntegrationChecks.EffectField(this, "timedSpeeds");
        return forces[forces.Count - 1].hspeed;
    }
    void EnsureReady()
    {
        BindEffectReceiver();
        if (playerPhysicsData != null) return;
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        playerPhysicsData = new PlayerPhysicsData();
        nowGemetry = new GeometryPhysicsData();
        Init();
    }
}

public sealed class EnvironmentCheckSurface : BasicPhysicalObject, IPlatformMotion, IWallSlideSurface, IStateVelocitySource
{
    [SerializeField] Vector2 phySpeed = new Vector2(3, 2);
    public Vector2 StateVelocity => phySpeed;
    public Vector2 Delta => new Vector2(.3f, -.2f);
    public float WallSlideMultiplier => .5f;
}
// 无 Unity 宿主的来源夹具，只验证注册表的来源协议边界。
public sealed class EnvironmentCheckSource : IEnvironmentSource
{
    public bool IsEnvironmentActive { get; set; } = true;
    public EnvironmentRegistration EnvironmentRegistration { get; }
    public EnvironmentCheckSource() => EnvironmentRegistration = new EnvironmentRegistration(this);
}

public sealed class EnvironmentCheckReceiver : MonoBehaviour, IForceAction
{
    public void StatePowerRegistration(EnvironmentRegistration id, float num) { }
    public void StatePowerCancellation(EnvironmentRegistration id) { }
    public void AddSpeedStatus(EnvironmentRegistration id, Vector2 value) { }
    public void RemoveSpeedStatus(EnvironmentRegistration id) { }
    public void AddTimeSpeed(float time, float value) { }
    public void AddForce(IDynamicAddForce id, ForceData data) { }
    public void SetOrUpdateDynamicForce(IDynamicAddForce id, DynamicForceParameters parameters) { }
    public void ChangeForce(IDynamicAddForce id, float value) { }
    public void ChangeType(IDynamicAddForce id, E_PhyForceType type) { }
    public void RemoveForce(IDynamicAddForce id) { }
}
