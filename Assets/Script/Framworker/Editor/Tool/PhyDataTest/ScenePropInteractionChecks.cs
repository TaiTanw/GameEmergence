#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>V_14 的控制交接、刷新与复合范围检查；只使用临时几何，不保存场景。</summary>
public static class ScenePropInteractionChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<string> passed = new List<string>();
    static InputControlMgr input;
    static PlayerLifecycleRules rules;

    [MenuItem("自定义工具/测试工具/验证/V14 验证道具受控角色范围（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行道具范围检查。");
        passed.Clear();
        FieldInfo inputSlot = Singleton(typeof(BaseAutoMonoMgr<InputControlMgr>));
        FieldInfo schedulerSlot = Singleton(typeof(BaseAutoMonoMgr<MonoPublicMgr>));
        FieldInfo stageSlot = Singleton(typeof(BaseMgr<LevelStageMgr>));
        object previousInput = inputSlot.GetValue(null);
        object previousScheduler = schedulerSlot.GetValue(null);
        object previousStage = stageSlot.GetValue(null);
        bool previousQuitting = MonoPublicMgr.IsQuitting;
        var registered = (List<SceneProp>)typeof(SceneProp).GetField("interactProps", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        SceneProp[] previousProps = registered.ToArray();
        Vector3 previousVector = DataAndInitMgr.Instance.CurrentLevelEndingVector;
        try
        {
            MonoPublicMgr.IsQuitting = false;
            schedulerSlot.SetValue(null, NewObject("V14 scheduler", Vector2.zero).AddComponent<MonoPublicMgr>());
            input = NewObject("V14 input", Vector2.zero).AddComponent<InputControlMgr>();
            inputSlot.SetValue(null, input);
            rules = new PlayerLifecycleRules(null, null);
            var stage = new LevelStageMgr();
            typeof(LevelStageMgr).GetField("rules", Hidden).SetValue(stage, rules);
            stageSlot.SetValue(null, stage);
            registered.Clear();
            CheckHandoff();
            CheckCompoundRanges();
            CheckRefreshAndMovement();
            CheckTouchAndEligibility();
            CheckTargetsAndLightInterface();
            CheckEndingContribution();
            Debug.Log("Scene prop interaction: " + passed.Count + " checks passed.");
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            registered.Clear(); registered.AddRange(previousProps);
            inputSlot.SetValue(null, previousInput);
            schedulerSlot.SetValue(null, previousScheduler);
            stageSlot.SetValue(null, previousStage);
            MonoPublicMgr.IsQuitting = previousQuitting;
            typeof(DataAndInitMgr).GetMethod("RestoreCurrentLevelEndingVector", Hidden)
                .Invoke(DataAndInitMgr.Instance, new object[] { previousVector });
            Directory.CreateDirectory(".utmp/ScenePropValidation");
            File.WriteAllLines(".utmp/ScenePropValidation/checks.txt", passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckHandoff()
    {
        Player spirit = Actor("V14 spirit", Vector2.zero, true);
        spirit.GetComponentInChildren<Collider2D>().isTrigger = true;
        Player body = Actor("V14 body", new Vector2(1000f, 1000f));
        SceneProp prop = Prop(Vector2.zero);
        Control(spirit);
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Invoke(prop, "OnTriggerEnter2D", spirit.GetComponentInChildren<Collider2D>());
        Check(!Used(prop), "交互道具进入范围只等点按，不自动领取");
        Control(body);
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        SceneProp.OfferInteract(body);
        Check(!Used(prop) && prop.gameObject.activeSelf, "精神体留在道具旁，交接后的远处本体不能领取");
        SceneProp.OfferInteract(spirit);
        Check(!Used(prop), "旧精神体仍重叠时也不能代替本体点按");
        body.transform.position = Vector2.zero;
        SceneProp.OfferInteract(body);
        Check(Used(prop) && !prop.gameObject.activeSelf, "本体移入后无需新的进入回调即可近距领取");
        Check(!Acquire(prop, body), "领取后重复请求没有第二次结算");
    }

    static void CheckCompoundRanges()
    {
        Vector2 at = new Vector2(20f, 20f);
        Player player = Actor("V14 child bodies", at, true);
        Collider2D first = player.GetComponentInChildren<Collider2D>();
        BoxCollider2D second = ChildCollider(player.transform, Vector2.zero);
        Control(player);
        SceneProp prop = Prop(at);
        first.transform.localPosition = new Vector2(5f, 0f);
        SceneProp.OfferInteract(player);
        Check(Used(prop), "一个子碰撞框退出时，另一个仍重叠可以领取");
        prop.RestoreDefault();
        second.enabled = false;
        SceneProp.OfferInteract(player);
        Check(!Used(prop), "停用的重叠子碰撞框不能放行");
        second.enabled = true;
        first.transform.localPosition = Vector2.zero;
        player.enabled = false;
        SceneProp.OfferInteract(player);
        Check(!Used(prop), "受控引用尚在但角色组件停用时不能领取");
        player.enabled = true;
        prop.GetComponent<Collider2D>().enabled = false;
        BoxCollider2D childRange = ChildCollider(prop.transform, Vector2.zero);
        childRange.isTrigger = true;
        SceneProp.OfferInteract(player);
        Check(Used(prop), "道具的子触发范围可以和角色子碰撞框配对");
        prop.RestoreDefault();
        childRange.isTrigger = false;
        SceneProp.OfferInteract(player);
        Check(!Used(prop), "普通实心框不充当道具领取范围");

        SceneProp narrow = Prop(new Vector2(40f, 40f));
        BoxCollider2D range = narrow.GetComponent<BoxCollider2D>();
        range.size = new Vector2(4f, .2f);
        narrow.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
        player.transform.position = new Vector2(41f, 39f);
        SceneProp.OfferInteract(player);
        Check(!Used(narrow), "旋转窄范围的外包矩形重叠不冒充真实相交");
        player.transform.position = new Vector2(41f, 41f);
        SceneProp.OfferInteract(player);
        Check(Used(narrow), "旋转范围的真实形状相交允许领取");
    }

    static void CheckRefreshAndMovement()
    {
        Vector2 at = new Vector2(60f, 60f);
        Player player = Actor("V14 moved player", at);
        Control(player);
        SceneProp prop = Prop(at);
        Invoke(prop, "OnTriggerEnter2D", player.GetComponent<Collider2D>());
        player.transform.position += Vector3.right * 10f;
        SceneProp.OfferInteract(player);
        Check(!Used(prop), "轻刷新同帧搬走角色，未收到退出回调也拒绝领取");
        player.transform.position = at;
        prop.transform.position += Vector3.right * 10f;
        SceneProp.OfferInteract(player);
        Check(!Used(prop), "同帧搬走道具不沿用原位置范围");
        prop.RestoreDefault();
        SceneProp.OfferInteract(player);
        Check(Used(prop) && (Vector2)prop.transform.position == at, "重刷新恢复默认位置和未领取后重新核对当前范围");
        player.transform.position += Vector3.up * 10f;
        SceneProp.OfferInteract(player);
        Check(Used(prop) && !prop.gameObject.activeSelf, "后续轻刷新搬移不重生已领取道具");
        prop.RestoreDefault();
        SceneProp.OfferInteract(player);
        Check(!Used(prop) && prop.gameObject.activeSelf, "重刷新后角色仍在远处时不自动领取");
    }

    static void CheckTouchAndEligibility()
    {
        Vector2 at = new Vector2(80f, 80f);
        Player player = Actor("V14 touch player", at, true);
        Player other = Actor("V14 uncontrolled player", at);
        Control(player);
        SceneProp prop = Prop(at, true);
        Collider2D collider = player.GetComponentInChildren<Collider2D>();
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        Invoke(prop, "OnTriggerEnter2D", collider);
        Check(!Used(prop), "模拟中真实触碰仍禁止普通道具领取");
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Invoke(prop, "OnTriggerStay2D", other.GetComponent<Collider2D>());
        Check(!Used(prop), "未受控角色的触碰回调不能代领");
        Invoke(prop, "OnTriggerStay2D", collider);
        Check(Used(prop), "返回漫游后仍在范围内可经 Stay 领取，不要求重新进入");
        prop.RestoreDefault();
        player.transform.position += Vector3.right * 10f;
        Invoke(prop, "OnTriggerStay2D", collider);
        Check(!Used(prop), "搬移后遗留触碰回调也须通过当前几何核对");
        player.transform.position = at;
        SceneProp clear = Prop(at);
        typeof(SceneProp).GetField("clearsLevel", Hidden).SetValue(clear, true);
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        Check(!Acquire(clear, player), "模拟中的通关物仍被统一领取禁令拒绝");
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        player.transform.position += Vector3.right * 1000f;
        Check(!Acquire(clear, player) && !Used(clear), "逃生通关物在远处不能推进关序");
    }

    static void CheckTargetsAndLightInterface()
    {
        Vector2 at = new Vector2(120f, 120f);
        Player player = Actor("P4.3 target player", at);
        Control(player);
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        SceneProp farther = Prop(at + Vector2.right * .5f);
        SceneProp nearer = Prop(at);
        Check(SceneProp.FindInteractTarget(player) == nearer, "多个范围重叠时，最近道具是唯一提示及点按候选");
        Check(!Used(nearer) && !Used(farther), "提示查询不领取或锁定任何道具");
        SceneProp.OfferInteract(player);
        Check(Used(nearer) && !Used(farther), "点按只领取提示候选，不按注册顺序领取另一件");
        Check(SceneProp.FindInteractTarget(player) == farther, "已使用道具不再成为候选，其它入口仍可查询");
        nearer.RestoreDefault();
        Check(SceneProp.FindInteractTarget(player) == nearer, "重刷新恢复未领取道具的候选资格");
        Check(nearer.DisplayBounds == nearer.GetComponent<Collider2D>().bounds, "无精灵占位道具按触发范围显示提示框");

        var light = nearer.gameObject.AddComponent<Light>();
        typeof(SceneProp).GetField("explorationLightEffect", Hidden).SetValue(nearer, light);
        typeof(SceneProp).GetField("clearsLevel", Hidden).SetValue(nearer, true);
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Invoke(nearer, "LateUpdate");
        Check(light.enabled && SceneProp.FindInteractTarget(player) != nearer, "探索开启组件光效接口，但通关道具不提供交互候选");
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        Invoke(nearer, "LateUpdate");
        Check(light.enabled, "模拟仍属于探索，光效组件保持开启");
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        Invoke(nearer, "LateUpdate");
        Check(!light.enabled, "逃生阶段关闭探索光效组件");
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        typeof(SceneProp).GetField("used", Hidden).SetValue(nearer, true);
        Invoke(nearer, "LateUpdate");
        Check(!light.enabled, "已使用通关道具不重新开启光效");
        nearer.RestoreDefault();
        Check(light.enabled, "失败恢复默认时重新同步探索光效");
        nearer.enabled = false;
        Invoke(nearer, "OnDisable");
        Check(!light.enabled, "道具组件停用同步关闭光效");
        rules.ConfigureStage(PlayerGameplayStage.Escape);
    }

    static Player Actor(string name, Vector2 position, bool child = false)
    {
        GameObject item = NewObject(name, position);
        var rigidbody = item.AddComponent<Rigidbody2D>(); rigidbody.bodyType = RigidbodyType2D.Kinematic;
        Player player = item.AddComponent<Player>();
        if (child) ChildCollider(item.transform, Vector2.zero);
        else item.AddComponent<BoxCollider2D>().size = Vector2.one * .4f;
        return player;
    }

    static void CheckEndingContribution()
    {
        DataAndInitMgr data = DataAndInitMgr.Instance;
        data.DiscardCurrentLevelEndingVector();
        Vector2 at = new Vector2(150, 150);
        Player player = Actor("P16 contribution player", at);
        SceneProp prop = Prop(at);
        typeof(SceneProp).GetField("endingContribution", Hidden).SetValue(prop, new Vector2(2, -3));
        Control(player);
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true);
        Check(!Acquire(prop, player) && data.CurrentLevelEndingVector == Vector3.zero, "模拟拒绝领取不累计向量");
        rules.ConfigureStage(PlayerGameplayStage.Exploration);
        Check(Acquire(prop, player) && data.CurrentLevelEndingVector == new Vector3(2, -3, 0), "普通道具真实领取登记二维贡献");
        Check(!Acquire(prop, player) && data.CurrentLevelEndingVector == new Vector3(2, -3, 0), "重复领取不重复累计");
        typeof(SceneProp).GetField("endingContribution", Hidden).SetValue(prop, new Vector2(float.NaN, 0));
        prop.RestoreDefault();
        Check(!Acquire(prop, player) && !Used(prop) && data.CurrentLevelEndingVector == new Vector3(2, -3, 0), "非法贡献拒绝领取并保留向量与道具");
    }

    static SceneProp Prop(Vector2 position, bool touch = false)
    {
        GameObject item = NewObject("V14 prop", position);
        BoxCollider2D range = item.AddComponent<BoxCollider2D>(); range.isTrigger = true; range.size = Vector2.one * 2f;
        SceneProp prop = item.AddComponent<SceneProp>();
        typeof(SceneProp).GetField("clearsLevel", Hidden).SetValue(prop, false);
        Invoke(prop, "Awake");
        var registered = (List<SceneProp>)typeof(SceneProp).GetField("interactProps", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        registered.Remove(prop);
        if (touch)
        {
            FieldInfo mode = typeof(SceneProp).GetField("acquireMode", Hidden);
            mode.SetValue(prop, Enum.ToObject(mode.FieldType, 1));
        }
        else registered.Add(prop);
        return prop;
    }

    static BoxCollider2D ChildCollider(Transform parent, Vector2 localPosition)
    {
        GameObject item = NewObject("V14 child collider", Vector2.zero);
        item.transform.SetParent(parent, false); item.transform.localPosition = localPosition;
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>(); collider.size = Vector2.one * .4f;
        return collider;
    }

    static GameObject NewObject(string name, Vector2 position)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        item.transform.position = position; objects.Add(item); return item;
    }

    static FieldInfo Singleton(Type type) => type.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
    static void Control(Player player) => typeof(InputControlMgr).GetField("player", Hidden).SetValue(input, player);
    static bool Used(SceneProp prop) => (bool)typeof(SceneProp).GetField("used", Hidden).GetValue(prop);
    static bool Acquire(SceneProp prop, Player player) => (bool)typeof(SceneProp).GetMethod("TryAcquire", Hidden).Invoke(prop, new object[] { player });
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add("PASS " + message);
    }
}
#endif
