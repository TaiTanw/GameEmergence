#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// P_5.4 箱子交互的 Edit Mode 批处理检查：克隆实际玩家与箱子预制体，手动推进 1–5 相位并调用 Physics2D.Simulate。
/// 物理组直接写动作数据核对共同水平运动；玩家组经 Player 的交互消费与相位 2 关系核对选中、解除和带电触碰。
/// 只创建临时对象，结束即销毁，不保存场景或预制体。
/// </summary>
public static class BoxInteractionChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static float nextX = 1000f;

    // Unity -batchmode -executeMethod BoxInteractionChecks.RunForBatch
    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/EPhy 验证箱子交互（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
        SimulationMode2D oldMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        try
        {
            CheckSelectedPushAndPull();
            CheckSelectedWallStops();
            CheckUnselectedContact();
            CheckSelectionRelations();
            CheckElectrifiedTouch();
            Debug.Log($"Box interaction: {passed.Count} checks passed.");
        }
        finally
        {
            Physics2D.simulationMode = oldMode;
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] == null) continue;
                foreach (PhysicalBox box in objects[i].GetComponentsInChildren<PhysicalBox>()) LevelBoxContext.Unregister(box);
                Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            Directory.CreateDirectory(".utmp/BoxInteraction");
            File.WriteAllLines(".utmp/BoxInteraction/checks.txt", passed);
        }
    }

    /// <summary>平地上选中推与拉：每步双方请求 X 相同、间距不变，双方都实际移动。</summary>
    private static void CheckSelectedPushAndPull()
    {
        var w = World(80f);
        w.action.pushPullBox = w.box;
        float startGap = Gap(w);
        Vector2 boxStart = w.box.DebugActualPosition;
        float maxDiff = 0f;
        for (int tick = 0; tick < 60; tick++)
        {
            w.action.onMove = tick < 5 ? 0f : 1f;
            Step(w);
            maxDiff = Mathf.Max(maxDiff, Mathf.Abs(Request(w.player).x - Request(w.box).x));
        }
        Check(maxDiff < .0001f, $"选中推动每步双方请求 X 一致 [{maxDiff}]");
        Check(w.box.DebugActualPosition.x - boxStart.x > 2f, "选中推动箱子实际前进");
        Check(Mathf.Abs(Gap(w) - startGap) < .02f, $"选中推动间距保持 [{startGap} → {Gap(w)}]");
        Check(w.box.DebugIsGrounded, "推动后箱子仍着地");

        Vector2 pullStart = w.box.DebugActualPosition;
        maxDiff = 0f;
        for (int tick = 0; tick < 60; tick++)
        {
            w.action.onMove = -1f;
            Step(w);
            maxDiff = Mathf.Max(maxDiff, Mathf.Abs(Request(w.player).x - Request(w.box).x));
        }
        Check(maxDiff < .0001f, $"选中拉动每步双方请求 X 一致 [{maxDiff}]");
        Check(pullStart.x - w.box.DebugActualPosition.x > 2f, "选中拉动箱子跟随后退");
        Check(Mathf.Abs(Gap(w) - startGap) < .02f, $"选中拉动间距保持 [{Gap(w)}]");

        // 松手并解除：旧接触已清，箱子不继续滑行。
        w.action.onMove = 0f;
        Step(w);
        w.action.pushPullBox = null;
        PhysicsSolverMgr.Instance.ForgetContactForcesBetween(w.player, w.box);
        Vector2 rest = w.box.DebugActualPosition;
        for (int tick = 0; tick < 30; tick++) Step(w);
        Check((w.box.DebugActualPosition - rest).magnitude < .003f, "解除后箱子无可见滑行");
    }

    /// <summary>箱子前方或玩家身后有墙：共同运动被较受限的一方截停，双方不嵌入墙或彼此。</summary>
    private static void CheckSelectedWallStops()
    {
        var push = World(80f);
        Bounds b = push.box.BodyBounds;
        Collider2D front = Solid(new Vector2(b.max.x + 1f + .25f, push.groundTop + 1.5f), new Vector2(.5f, 3f), push.wallLayer);
        push.action.pushPullBox = push.box;
        for (int tick = 0; tick < 80; tick++) { push.action.onMove = 1f; Step(push); }
        Check(push.box.BodyBounds.max.x <= front.bounds.min.x + .02f, "推向墙时箱子不嵌入墙");
        Check(Gap(push) > -.02f, $"推向墙时玩家不嵌入箱子 [{Gap(push)}]");
        float stopped = push.player.DebugActualPosition.x;
        for (int tick = 0; tick < 10; tick++) Step(push);
        Check(Mathf.Abs(push.player.DebugActualPosition.x - stopped) < .003f, "箱子抵墙后整组停止");

        var pull = World(80f);
        Bounds p = pull.player.BodyBounds;
        Collider2D back = Solid(new Vector2(p.min.x - .6f - .25f, pull.groundTop + 1.5f), new Vector2(.5f, 3f), pull.wallLayer);
        pull.action.pushPullBox = pull.box;
        float gap = Gap(pull);
        for (int tick = 0; tick < 80; tick++) { pull.action.onMove = -1f; Step(pull); }
        Check(pull.player.BodyBounds.min.x >= back.bounds.max.x - .02f, "拉动时玩家不嵌入身后墙");
        Check(Mathf.Abs(Gap(pull) - gap) < .03f, $"玩家抵墙后箱子不继续压向玩家 [{gap} → {Gap(pull)}]");
    }

    /// <summary>未选中：沿原接触仍能推动，反向不拉动。</summary>
    private static void CheckUnselectedContact()
    {
        var w = World(80f);
        Vector2 start = w.box.DebugActualPosition;
        for (int tick = 0; tick < 60; tick++) { w.action.onMove = 1f; Step(w); }
        Check(w.box.DebugActualPosition.x - start.x > .5f, "未选中箱子仍可接触推动");
        for (int tick = 0; tick < 30; tick++) { w.action.onMove = 0f; Step(w); }
        Vector2 before = w.box.DebugActualPosition;
        for (int tick = 0; tick < 40; tick++) { w.action.onMove = -1f; Step(w); }
        Check(Mathf.Abs(w.box.DebugActualPosition.x - before.x) < .05f, "未选中反向走开不拉动箱子");
    }

    /// <summary>经 Player 的交互消费与相位 2 关系：选中、再点解除、跳跃优先、走远、失去支撑、许可关闭、撤输入。</summary>
    private static void CheckSelectionRelations()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        Sample(w);

        Tap(player, input);
        Check(player._ActionData.PushPullBox == w.box, "地面贴合点按选中并打开推拉能力");
        Tap(player, input);
        Check(!player._ActionData.PushPullEnabled, "再次点按解除选中");

        input.interactTapped = input.jumpPressed = true;
        player.TickLogic(0f);
        Check(!player._ActionData.PushPullEnabled, "同帧跳跃优先，交互不建立选中");
        Settle(w);

        Tap(player, input);
        input.interactLongPressed = true;
        player.TickLogic(0f);
        Check(player._ActionData.PushPullBox == w.box, "收纳未接入时长按被拒绝，选中保持");
        input.jumpPressed = true;
        player.TickLogic(0f);
        Check(!player._ActionData.PushPullEnabled, "跳跃解除选中");
        Settle(w);

        Tap(player, input);
        w.box.transform.position += Vector3.right * 2f;
        Sample(w);
        Invoke(player, "UpdateBoxRelations");
        Check(!player._ActionData.PushPullEnabled, "走远超过解除间隙后相位 2 解除");
        w.box.transform.position -= Vector3.right * 2f;
        Sample(w);

        Tap(player, input);
        w.ground.enabled = false;
        Sample(w);
        Invoke(player, "UpdateBoxRelations");
        Check(!player._ActionData.PushPullEnabled, "选中箱失去支撑后相位 2 解除");
        w.ground.enabled = true;
        Sample(w);

        Tap(player, input);
        player.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, false, true, true, false));
        player.TickLogic(0f);
        Check(!player._ActionData.PushPullEnabled, "推拉许可关闭解除选中");
        Tap(player, input);
        Check(!player._ActionData.PushPullEnabled, "许可关闭时点按不建立选中");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);

        Tap(player, input);
        player.RemoveInput();
        Check(!player._ActionData.PushPullEnabled, "撤销控制解除本角色选中");

        player.ChangeInputAsset(input);
        w.box.transform.position += Vector3.right * 2f;
        Sample(w);
        Tap(player, input);
        Check(!player._ActionData.PushPullEnabled, "远处箱子不成为候选");
    }

    /// <summary>带电方块：拒绝交互；任意面接触请求一次电击死亡，持续接触不重复，离开后重开。</summary>
    private static void CheckElectrifiedTouch()
    {
        var w = World(80f, true);
        Player player = w.owner;
        typeof(PhysicalBox).GetField("electrified", Hidden).SetValue(w.box, true);
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        var receiver = new CountingDeathReceiver();
        player.BindDeathReceiver(receiver);
        LevelBoxContext.Register(w.box);
        Sample(w);

        Tap(player, input);
        Check(!player._ActionData.PushPullEnabled, "带电方块拒绝点按选中");
        Invoke(player, "UpdateBoxRelations");
        Check(receiver.count == 1 && receiver.last == PlayerDeathCause.Electrocuted,
            $"侧面贴合带电方块请求电击死亡 [count={receiver.count}, cause={receiver.last}, pending={player.PendingDeath.HasValue}, " +
            $"touch={LevelBoxContext.TouchesElectrified(player.GetComponent<CharacterPhysics>().BodyBounds, .03f)}]");
        player.ResetDeathCycle();
        for (int i = 0; i < 5; i++) Invoke(player, "UpdateBoxRelations");
        Check(receiver.count == 1, "持续接触不在每个物理步重复请求死亡");

        w.box.transform.position += Vector3.right * 2f;
        Physics2D.SyncTransforms();
        Invoke(player, "UpdateBoxRelations");
        w.box.transform.position -= Vector3.right * 2f;
        Physics2D.SyncTransforms();
        Invoke(player, "UpdateBoxRelations");
        Check(receiver.count == 2, "离开后再次触碰重新请求");

        Bounds body = player.GetComponent<CharacterPhysics>().BodyBounds;
        Bounds boxBounds = w.box.BodyBounds;
        w.box.transform.position += new Vector3(body.center.x - boxBounds.center.x,
            body.max.y + boxBounds.extents.y + .01f - boxBounds.center.y, 0f);
        Physics2D.SyncTransforms();
        player.ResetDeathCycle();
        Invoke(player, "UpdateBoxRelations");
        Invoke(player, "UpdateBoxRelations");
        Check(receiver.count == 2, "持续接触换到顶面仍属同一段接触");
        w.box.transform.position += Vector3.up * 3f;
        Physics2D.SyncTransforms();
        Invoke(player, "UpdateBoxRelations");
        w.box.transform.position -= Vector3.up * 3f;
        Physics2D.SyncTransforms();
        Invoke(player, "UpdateBoxRelations");
        Check(receiver.count == 3, "带电方块顶面接触也请求死亡");
    }

    private sealed class CountingDeathReceiver : IPlayerDeathReceiver
    {
        public int count;
        public PlayerDeathCause last;
        public PlayerDeathStatus ReceiveDeath(PlayerDeathRequest request)
        {
            count++;
            last = request.cause;
            return PlayerDeathStatus.Handled;
        }
    }

    private sealed class TestWorld
    {
        public CharacterPhysics player;
        public Player owner;
        public PhysicalBox box;
        public MovementData action;
        public BoxCollider2D ground;
        public float groundTop;
        public int wallLayer;
    }

    /// <summary>平地上玩家在左、箱子在右，碰撞框间隙约 0.01。owner 为真时经 Player 组装动作数据。</summary>
    private static TestWorld World(float groundWidth, bool owner = false)
    {
        float x = nextX;
        nextX += 200f;
        var w = new TestWorld();
        GameObject playerObject = Instantiate("player20.prefab");
        GameObject boxObject = Instantiate("box.prefab");
        w.box = boxObject.GetComponent<PhysicalBox>();
        w.player = playerObject.GetComponent<CharacterPhysics>();
        Invoke(w.box, "Awake");
        Invoke(w.player, "Awake");
        if (owner)
        {
            w.owner = playerObject.GetComponent<Player>();
            Invoke(w.owner, "Awake");
            Invoke(playerObject.GetComponentInChildren<PresentationLayer>(), "Awake");
            Invoke(w.owner, "Start");
            w.action = (MovementData)typeof(Player).GetField("actionData", Hidden).GetValue(w.owner);
        }
        else
        {
            w.action = new MovementData();
            w.player.Init(new ReadOnly_ActionData(w.action), new LocalEventSystem<PlayerStateMachine.E_playEvent>());
        }
        var config = (SO_CPhysics)typeof(BasicEntity).GetField("cPhysics", Hidden).GetValue(w.player);
        int groundLayer = FirstLayer(config.groundLayer);
        w.wallLayer = FirstLayer(config.wallLayer);
        w.groundTop = 100f;
        w.ground = (BoxCollider2D)Solid(new Vector2(x, w.groundTop - .5f), new Vector2(groundWidth, 1f), groundLayer);
        Physics2D.SyncTransforms();
        PlaceOn(w.player, x - 3f, w.groundTop);
        Physics2D.SyncTransforms();
        PlaceOn(w.box, w.player.BodyBounds.max.x + .01f + w.box.BodyBounds.extents.x, w.groundTop);
        Physics2D.SyncTransforms();
        return w;
    }

    private static void Tap(Player player, PlayerInputData input)
    {
        input.interactTapped = true;
        player.TickLogic(0f);
    }

    /// <summary>显式刷新角色运动与行为，撤销跳跃留下的待执行冲量和空中状态，再按当前几何采样。</summary>
    private static void Settle(TestWorld w)
    {
        w.owner.RefreshRuntime();
        Sample(w);
        w.owner.TickLogic(0f);
    }

    private static void Sample(TestWorld w)
    {
        Physics2D.SyncTransforms();
        Invoke(w.player, "GeometricQueryPhase");
        Invoke(w.box, "GeometricQueryPhase");
    }

    private static void Step(TestWorld w)
    {
        Physics2D.SyncTransforms();
        BasicEntity[] entities = { w.player, w.box };
        foreach (BasicEntity e in entities) Invoke(e, "GeometricQueryPhase");
        foreach (BasicEntity e in entities) Invoke(e, "PhyFunUpdate");
        foreach (BasicEntity e in entities) Invoke(e, "SpeedCalculation");
        Invoke(PhysicsSolverMgr.Instance, "ContactForSolution");
        foreach (BasicEntity e in entities) Invoke(e, "DisplacementCorrection");
        Physics2D.Simulate(Time.fixedDeltaTime);
    }

    private static float Gap(TestWorld w) => w.box.BodyBounds.min.x - w.player.BodyBounds.max.x;

    private static Vector2 Request(BasicEntity e) =>
        ((EntitySolutionResult)typeof(BasicEntity).GetField("entitySolutionResult", Hidden).GetValue(e)).requestedWorldDelta;

    private static int FirstLayer(LayerMask mask)
    {
        for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
        throw new InvalidOperationException("物理配置缺少查询层");
    }

    private static Collider2D Solid(Vector2 center, Vector2 size, int layer)
    {
        var item = new GameObject("box check solid") { hideFlags = HideFlags.DontSave, layer = layer };
        item.transform.position = center;
        objects.Add(item);
        BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
        collider.size = size;
        return collider;
    }

    private static GameObject Instantiate(string prefab)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/" + prefab);
        if (asset == null) throw new InvalidOperationException("Missing fixture prefab: " + prefab);
        GameObject item = Object.Instantiate(asset);
        item.hideFlags = HideFlags.DontSave;
        objects.Add(item);
        return item;
    }

    /// <summary>按碰撞框中心放到地面上方 0.005，补偿预制体碰撞框相对根节点的偏移。</summary>
    private static void PlaceOn(BasicEntity e, float x, float groundTop)
    {
        Bounds b = e.BodyBounds;
        e.transform.position += (Vector3)(new Vector2(x, groundTop + b.extents.y + .005f) - (Vector2)b.center);
    }

    private static void Invoke(object instance, string method)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo found = type.GetMethod(method, Hidden | BindingFlags.DeclaredOnly);
            if (found == null) continue;
            found.Invoke(instance, null);
            return;
        }
        throw new MissingMethodException(method);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + message);
        passed.Add(message);
    }
}
#endif
