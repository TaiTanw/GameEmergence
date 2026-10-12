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
/// P_5.4 箱子交互的 Edit Mode 批处理检查：克隆实际玩家与箱子预制体，手动推进 1–3、真实相位 4 回调、统一 Unity 模拟及相位 5 回读。
/// 物理组直接写动作数据核对共同水平运动；玩家组经 Player 的交互消费与相位 2 关系核对选中、解除和带电触碰。
/// 只创建临时对象，结束即销毁，不保存场景或预制体。
/// </summary>
public static class BoxInteractionChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo sidePush = typeof(BasicEntity).Assembly.GetType("EntitySidePush")
        .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static readonly List<string> passed = new List<string>();
    private static float nextX = 1000f;

    // Unity -batchmode -executeMethod BoxInteractionChecks.RunForBatch
    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/EPhy 验证箱子交互（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
        SimulationMode2D oldMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        try
        {
            CheckHighSpeedSurfaces();
            CheckTopContactClears();
            CheckEnvironmentalMotion();
            CheckDescendingPlatform();
            CheckNativeChain();
            CheckUnderBoxJump();
            CheckSelectedPushAndPull();
            CheckSelectedWallStops();
            CheckApproachThenPull();
            CheckUnselectedContact();
            CheckSelectionRelations();
            CheckElectrifiedTouch();
            CheckMovingBoxContact();
            CheckStorageAndRestore();
            CheckStorageConflicts();
            CheckPushPullAction();
            CheckReachAndHints();
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

    /// <summary>真实刚体高速跨入薄面时由原生连续检测阻挡，不调用旧扫掠或纠偏。</summary>
    private static void CheckHighSpeedSurfaces()
    {
        foreach (bool isPlayer in new[] { true, false })
        {
            TestWorld wallWorld = World(80f);
            BasicEntity body = isPlayer ? (BasicEntity)wallWorld.player : wallWorld.box;
            BasicEntity other = isPlayer ? (BasicEntity)wallWorld.box : wallWorld.player;
            other.SetMotionParticipation(false);
            Collider2D wall = Solid(new Vector2(body.BodyBounds.max.x + .2f, wallWorld.groundTop + 1.5f),
                new Vector2(.1f, 3f), wallWorld.wallLayer);
            body.GetComponent<Rigidbody2D>().velocity = new Vector2(45f, 0f);
            Step(wallWorld);
            Check(body.BodyBounds.max.x <= wall.bounds.min.x + .025f,
                (isPlayer ? "角色" : "箱子") + "高速水平运动被真实薄墙阻挡");

            TestWorld floorWorld = World(80f);
            body = isPlayer ? (BasicEntity)floorWorld.player : floorWorld.box;
            other = isPlayer ? (BasicEntity)floorWorld.box : floorWorld.player;
            other.SetMotionParticipation(false);
            floorWorld.ground.size = new Vector2(80f, .04f);
            floorWorld.ground.transform.position = new Vector2(floorWorld.ground.transform.position.x, floorWorld.groundTop - .02f);
            MoveCenter(body, (Vector2)body.BodyBounds.center + Vector2.up);
            body.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, -80f);
            Step(floorWorld);
            Check(body.BodyBounds.min.y >= floorWorld.ground.bounds.max.y - .025f,
                (isPlayer ? "角色" : "箱子") + "高速下落被真实薄地面阻挡");
        }
    }

    private static void CheckTopContactClears()
    {
        TestWorld w = World(80f, true);
        var input = new PlayerInputData();
        w.owner.ChangeInputAsset(input);
        w.box.SetMotionParticipation(false);
        w.owner.TickLogic(Time.fixedDeltaTime); Step(w);
        Collider2D roof = Solid(new Vector2(w.player.BodyBounds.center.x, w.player.BodyBounds.max.y + .2f),
            new Vector2(4f, .1f), w.wallLayer);
        input.jumpPressed = true;
        for (int tick = 0; tick < 2; tick++) { w.owner.TickLogic(Time.fixedDeltaTime); Step(w); }
        Check(Mathf.Abs(w.player.BodyBounds.max.y - roof.bounds.min.y) < .025f,
            "角色实际跳跃贴近屋顶且不穿顶 [gap=" + (roof.bounds.min.y - w.player.BodyBounds.max.y) + "]");
        float startX = w.player.DebugActualPosition.x;
        bool fell = false;
        input.moveInput = 1f;
        for (int tick = 0; tick < 20; tick++)
        {
            w.owner.TickLogic(Time.fixedDeltaTime); Step(w);
            fell |= w.player.GetPlanarVelocity().y < -.1f;
        }
        Check(w.player.DebugActualPosition.x > startX + 1f && fell
            && w.player.BodyBounds.max.y < roof.bounds.min.y - .05f && !w.player.DebugIsTopBlocked,
            "角色顶下仍可水平前进、正常落回并清除旧顶头事实");
    }

    /// <summary>环境持续施力与固定流速直接作用真实运动，不留第二份累计速度。</summary>
    private static void CheckEnvironmentalMotion()
    {
        TestWorld force = World(80f);
        MoveCenter(force.box, (Vector2)force.box.BodyBounds.center + Vector2.right * 30f);
        ForceField field = Instantiate("F1.prefab").GetComponent<ForceField>();
        field.transform.position = force.player.BodyBounds.center;
        field.transform.localScale = Vector3.one;
        BoxCollider2D trigger = field.GetComponent<BoxCollider2D>();
        trigger.isTrigger = true; trigger.size = new Vector2(20f, 10f);
        typeof(ForceField).GetField("add", Hidden).SetValue(field, 2f);
        typeof(ForceField).GetField("maxSpeed", Hidden).SetValue(field, 8f);
        typeof(ForceField).GetField("speedChangeNum", Hidden).SetValue(field, 0f);
        for (int i = 0; i < 50; i++) Step(force);
        float speed = force.player.GetComponent<Rigidbody2D>().velocity.x;
        Check(speed > 1.5f && speed < 2.2f, "零输入角色连续环境施力逐步取得实际速度 [" + speed + "]");

        TestWorld flow = World(80f);
        flow.ground.enabled = false;
        flow.player.SetMotionParticipation(false);
        WaterWithEffects water = Instantiate("WaterPoolWithEffects.prefab").GetComponent<WaterWithEffects>();
        water.transform.position = flow.box.BodyBounds.center;
        water.transform.localScale = Vector3.one;
        water.GetComponent<BoxCollider2D>().size = new Vector2(30f, 30f);
        typeof(WaterWithEffects).GetField("flowVelocity", Hidden).SetValue(water, new Vector2(2f, 3f));
        typeof(WaterWithEffects).GetField("movementSpeedOffset", Hidden).SetValue(water, 0f);
        flow.box.gravity = 0f;
        Vector2 start = flow.box.DebugActualPosition;
        for (int i = 0; i < 50; i++) Step(flow);
        Vector2 actual = flow.box.GetComponent<Rigidbody2D>().velocity;
        Check((actual - new Vector2(2f, 3f)).magnitude < .02f,
            "箱子连续二维固定水流保持原速，不逐帧翻倍 [" + actual + "]");
        Check((flow.box.DebugActualPosition - start - new Vector2(2f, 3f)).magnitude < .05f,
            "箱子一秒实际位移与二维水流一致");

        var remaining = new List<float>();
        foreach (float direction in new[] { -1f, 1f })
        {
            TestWorld iceWorld = World(80f);
            iceWorld.player.SetMotionParticipation(false);
            IceGround ice = iceWorld.ground.gameObject.AddComponent<IceGround>();
            typeof(IceGround).GetField("phySpeed", Hidden).SetValue(ice, Vector2.zero);
            ice.Odex = 2f;
            for (int i = 0; i < 5; i++) Step(iceWorld);
            iceWorld.box.GetComponent<Rigidbody2D>().velocity = new Vector2(direction * 4f, 0f);
            for (int i = 0; i < 50; i++) Step(iceWorld);
            remaining.Add(Mathf.Abs(iceWorld.box.GetComponent<Rigidbody2D>().velocity.x));
        }
        Check(remaining[0] < 3.5f && remaining[1] < 3.5f && Mathf.Abs(remaining[0] - remaining[1]) < .03f,
            "自由箱冰面左右实际惯性对称制动 [" + remaining[0] + ", " + remaining[1] + "]");
    }

    /// <summary>实际移动平台每步下降超过旧探针间隙时，站立角色仍保持支撑并同步下降。</summary>
    private static void CheckDescendingPlatform()
    {
        TestWorld w = World(80f);
        w.ground.enabled = false;
        w.box.SetMotionParticipation(false);
        Taijie platform = Instantiate("Taijie.prefab").GetComponent<Taijie>();
        platform.transform.position = new Vector3(w.player.BodyBounds.center.x, 99f, 0f);
        platform.ToMovePoint(new Vector3(w.player.BodyBounds.center.x, 94f, 0f));
        Physics2D.SyncTransforms();
        float top = platform.GetComponent<Collider2D>().bounds.max.y;
        PlaceOn(w.player, platform.GetComponent<Collider2D>().bounds.center.x, top);
        Sample(w);
        Vector2 start = w.player.DebugActualPosition;
        Vector2 platformStart = platform.transform.position;
        bool supported = true;
        for (int tick = 0; tick < 50; tick++)
        {
            typeof(Taijie).GetField("nowtime", Hidden).SetValue(platform, AudioSettings.dspTime - (tick + 1) * Time.fixedDeltaTime);
            Invoke(platform, "FixFun"); Step(w);
            supported &= w.player.DebugIsGrounded;
        }
        Vector2 delta = (Vector2)platform.transform.position - platformStart;
        Check(supported && delta.y < -1f, "实际平台持续下降且每步保留角色支撑");
        Check((w.player.DebugActualPosition - start - delta).magnitude < .04f,
            "下降平台和站立角色实际位移一致");
        Check(Mathf.Abs(w.player.GetComponent<Rigidbody2D>().velocity.y - platform.Delta.y / Time.fixedDeltaTime) < .05f,
            "下降平台和角色实际竖直速度一致");
        float currentGap = w.player.BodyBounds.min.y - platform.GetComponent<Collider2D>().bounds.max.y;
        w.player.RefreshMotionState(w.player.DebugActualPosition + Vector2.up * (.04f - currentGap));
        Check(!w.player.DebugIsGrounded, "迁移到下降平台上方后立即清除旧支撑");
        Sample(w);
        Check(!w.player.DebugIsGrounded, "新位置模拟前的采样不重用旧下降平台接触");
    }

    /// <summary>真正碰撞推动有限箱链：前进、抵墙、松手、反向退出及移墙后恢复。</summary>
    private static void CheckNativeChain()
    {
        for (int count = 1; count <= 3; count++)
        {
            TestWorld w = World(80f);
            var chain = new List<PhysicalBox> { w.box };
            for (int i = 1; i < count; i++)
            {
                PhysicalBox previous = chain[i - 1];
                PhysicalBox next = ExtraBox(w, (Vector2)previous.BodyBounds.center + Vector2.right * (previous.BodyBounds.size.x + .01f));
                chain.Add(next);
            }
            var start = new List<float>();
            foreach (PhysicalBox box in chain) start.Add(box.DebugActualPosition.x);
            for (int tick = 0; tick < 100; tick++) { w.action.onMove = 1f; Step(w); }
            for (int i = 0; i < count; i++)
                Check(chain[i].DebugActualPosition.x - start[i] > .5f, count + " 箱链第 " + (i + 1) + " 箱通过实际碰撞前进");
            PhysicalBox front = chain[count - 1];
            Collider2D wall = Solid(new Vector2(front.BodyBounds.max.x + .75f, w.groundTop + 1.5f), new Vector2(.5f, 3f), w.wallLayer);
            for (int tick = 0; tick < 100; tick++) Step(w);
            Check(front.BodyBounds.max.x <= wall.bounds.min.x + .025f, count + " 箱鏈抵墙不穿入");
            float stopped = front.DebugActualPosition.x;
            for (int tick = 0; tick < 20; tick++) Step(w);
            Check(Mathf.Abs(front.DebugActualPosition.x - stopped) < .025f, count + " 箱链持续推动抵墙后位置稳定");
            for (int i = 0; i < count - 1; i++)
                Check(chain[i + 1].BodyBounds.min.x - chain[i].BodyBounds.max.x >= -.025f, count + " 箱链相邻箱不穿入");
            w.action.onMove = 0f;
            for (int tick = 0; tick < 40; tick++) Step(w);
            float rest = front.DebugActualPosition.x;
            w.action.onMove = -1f;
            for (int tick = 0; tick < 40; tick++) Step(w);
            Check(Mathf.Abs(front.DebugActualPosition.x - rest) < .06f, count + " 箱链未选中反向走开不被拉动");
            wall.enabled = false;
            w.action.onMove = 1f;
            for (int tick = 0; tick < 120; tick++) Step(w);
            Check(front.DebugActualPosition.x > stopped + .5f, count + " 箱链移除阻挡后恢复推动");
            w.box.SetMotionParticipation(false);
            Vector2 paused = w.box.DebugActualPosition;
            for (int tick = 0; tick < 20; tick++) Step(w);
            Check(w.box.DebugActualPosition == paused, count + " 箱链暂停箱体保持现场坐标");
            w.box.SetMotionParticipation(true);
            Check(w.box.GetComponent<Rigidbody2D>().simulated, count + " 箱链恢复箱体重新参加原生模拟");
        }
    }

    /// <summary>允许从下方顶普通箱；接触速度由原生模拟决定，侧移后不会重放旧下落速度。</summary>
    private static void CheckUnderBoxJump()
    {
        TestWorld w = World(80f, true);
        var input = new PlayerInputData();
        w.owner.ChangeInputAsset(input);
        MoveCenter(w.box, new Vector2(w.player.BodyBounds.center.x, w.player.BodyBounds.max.y + w.box.BodyBounds.extents.y + .01f));
        for (int tick = 0; tick < 5; tick++) { w.owner.TickLogic(Time.fixedDeltaTime); Step(w); }
        float startY = w.box.DebugActualPosition.y, peak = startY;
        input.jumpPressed = true;
        for (int tick = 0; tick < 12; tick++)
        {
            w.owner.TickLogic(Time.fixedDeltaTime); Step(w);
            peak = Mathf.Max(peak, w.box.DebugActualPosition.y);
        }
        Check(peak > startY + .1f, "普通箱允许从下方被跳跃顶起");
        input.moveInput = -1f;
        bool bounded = true;
        for (int tick = 0; tick < 160; tick++)
        {
            w.owner.TickLogic(Time.fixedDeltaTime); Step(w);
            Vector2 velocity = w.box.GetComponent<Rigidbody2D>().velocity;
            bounded &= !float.IsNaN(velocity.y) && Mathf.Abs(velocity.y) < 50f;
        }
        Check(bounded, "顶箱后整个下落过程实际竖直速度有界");
        Check(w.box.BodyBounds.min.y >= w.groundTop - .025f && Mathf.Abs(w.box.GetComponent<Rigidbody2D>().velocity.y) < .1f,
            "角色侧移后箱子落地稳定，没有旧下落速度突增");
    }

    /// <summary>平地上选中推与拉：双方实际移动并保持贴合。</summary>
    private static void CheckSelectedPushAndPull()
    {
        var w = World(80f);
        w.player.GetComponent<Rigidbody2D>().velocity = Vector2.right * 3f;
        w.box.GetComponent<Rigidbody2D>().velocity = Vector2.right * 3f;
        for (int tick = 0; tick < 5; tick++) Step(w);
        float movingGap = Gap(w);
        w.action.pushPullBox = w.box;
        Step(w);
        Check(Mathf.Abs(Gap(w) - movingGap) < .02f, "已移动的缩放箱首次建立关节不改变实际贴合间距");
        float startGap = Gap(w);
        Vector2 boxStart = w.box.DebugActualPosition;
        for (int tick = 0; tick < 60; tick++)
        {
            w.action.onMove = tick < 5 ? 0f : 1f;
            Step(w);
        }
        Check(w.box.DebugActualPosition.x - boxStart.x > 2f, $"选中推动箱子实际前进 [delta={w.box.DebugActualPosition.x - boxStart.x}, player={w.player.DebugActualPosition}, box={w.box.DebugActualPosition}, gap={Gap(w)}]");
        Check(Mathf.Abs(Gap(w) - startGap) < .02f, $"选中推动间距保持 [{startGap} → {Gap(w)}]");
        Check(w.box.DebugIsGrounded, "推动后箱子仍着地");

        Vector2 pullStart = w.box.DebugActualPosition;
        for (int tick = 0; tick < 60; tick++)
        {
            w.action.onMove = -1f;
            Step(w);
        }
        Check(pullStart.x - w.box.DebugActualPosition.x > 2f, "选中拉动箱子跟随后退");
        Check(Mathf.Abs(Gap(w) - startGap) < .02f, $"选中拉动间距保持 [{Gap(w)}]");

        // 松手按真实惯性制动，再解除水平贴合。
        w.action.onMove = 0f;
        for (int tick = 0; tick < 30; tick++) Step(w);
        w.action.pushPullBox = null;
        Vector2 rest = w.box.DebugActualPosition;
        for (int tick = 0; tick < 30; tick++) Step(w);
        Check((w.box.DebugActualPosition - rest).magnitude < .025f, "松手制动后解除箱子保持稳定");
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

    /// <summary>经 Player 隔距选中：自动走到箱边不撞开箱子，方向输入期间不生效；贴合后反向即拉动且选中不断开。</summary>
    private static void CheckApproachThenPull()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        StandLeftOf(w, w.box.BodyBounds, .4f);
        Tap(player, input);
        Check(player._ActionData.PushPullBox == w.box, "间隙 0.4 点按选中");
        float boxStartX = w.box.DebugActualPosition.x;
        input.moveInput = -1f;
        bool engaged = false;
        for (int tick = 0; tick < 20 && !engaged; tick++)
        {
            player.TickLogic(Time.fixedDeltaTime);
            Step(w);
            engaged = Gap(w) <= .05f;
        }
        Check(engaged, $"选中后自动走到箱边，反向输入期间不生效 [{Gap(w)}]");
        Check(Mathf.Abs(w.box.DebugActualPosition.x - boxStartX) < .01f,
            $"自动走近不撞开箱子 [{w.box.DebugActualPosition.x - boxStartX}]");
        for (int tick = 0; tick < 20; tick++)
        {
            player.TickLogic(Time.fixedDeltaTime);
            Step(w);
            Invoke(player, "UpdateBoxRelations");
        }
        Check(player._ActionData.PushPullBox == w.box && boxStartX - w.box.DebugActualPosition.x > .5f,
            "贴合后反向拉动箱子跟随，选中不断开");
        Check(player._ActionData.PushPullAction == PushPullAction.Pull, "贴合拉动时输出拉");
    }

    /// <summary>未选中：沿原接触仍能推动，反向不拉动。</summary>
    private static void CheckUnselectedContact()
    {
        var w = World(80f);
        Vector2 start = w.box.DebugActualPosition;
        for (int tick = 0; tick < 60; tick++) { w.action.onMove = 1f; Step(w); }
        Check(w.box.DebugActualPosition.x - start.x > .5f, "未选中箱子仍可接触推动");
        w.action.onMove = 0f;
        int settling = 0;
        while (Mathf.Abs(w.box.GetComponent<Rigidbody2D>().velocity.x) > .02f && settling++ < 90) Step(w);
        Check(Mathf.Abs(w.box.GetComponent<Rigidbody2D>().velocity.x) <= .02f, "未选中箱子按实际惯性在合理时间内制动");
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
        input.jumpPressed = true;
        player.TickLogic(0f);
        Check(!player._ActionData.PushPullEnabled, "跳跃解除选中");
        Settle(w);

        Tap(player, input);
        w.box.transform.position += Vector3.right * .7f;
        Sample(w);
        Invoke(player, "UpdateBoxRelations");
        Check(player._ActionData.PushPullEnabled, "间隙 0.7 未超过解除距离，选中保持");
        w.box.transform.position += Vector3.right * 1.3f;
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
            $"touch={ElectricContact.Touches(player, .03f)}]");
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

    /// <summary>从真实间隙步行／疾跑接近：带电箱须触碰致死，普通箱须闭合间隙后持续共同推动。</summary>
    private static void CheckMovingBoxContact()
    {
        foreach (bool electrified in new[] { true, false })
        foreach (bool sprint in new[] { true, false })
        foreach (float direction in new[] { 1f, -1f })
        foreach (float initialGap in new[] { .08f, .19f, .35f })
        {
            var w = World(80f, true);
            typeof(PhysicalBox).GetField("electrified", Hidden).SetValue(w.box, electrified);
            LevelBoxContext.Register(w.box);
            var input = new PlayerInputData { moveInput = direction, sprintHeld = sprint };
            w.owner.ChangeInputAsset(input);
            var receiver = new CountingDeathReceiver();
            w.owner.BindDeathReceiver(receiver);
            MoveCenter(w.box, new Vector2(w.player.BodyBounds.center.x + direction *
                (w.player.BodyBounds.extents.x + w.box.BodyBounds.extents.x + initialGap), w.box.BodyBounds.center.y));
            Collider2D playerCollider = w.player.GetComponent<Collider2D>();
            Collider2D boxCollider = w.box.GetComponent<Collider2D>();
            float startX = w.box.BodyBounds.center.x;
            bool prematureDeath = false;
            float deathGap = float.MaxValue;
            for (int tick = 0; tick < 30; tick++)
            {
                Sample(w);
                w.owner.TickLogic(Time.fixedDeltaTime);
                float beforeGap = boxCollider.Distance(playerCollider).distance;
                Invoke(w.owner, "UpdateBoxRelations");
                if (receiver.count > 0)
                {
                    deathGap = beforeGap;
                    prematureDeath |= beforeGap > .03f;
                    break;
                }
                Step(w);
            }
            string label = $"{(electrified ? "带电" : "普通")}箱 {(sprint ? "疾跑" : "步行")} {(direction > 0f ? "向右" : "向左")} 初始间隙 {initialGap}";
            if (electrified)
            {
                Check(receiver.count == 1 && receiver.last == PlayerDeathCause.Electrocuted,
                    label + " 从自由接近进入真实触碰并请求一次电击死亡");
                Check(!prematureDeath && deathGap <= .03f, label + " 预测命中不提前结算电击");
            }
            else
            {
                float gap = boxCollider.Distance(playerCollider).distance;
                Check(receiver.count == 0 && direction * (w.box.BodyBounds.center.x - startX) > 1f,
                    label + " 可持续推动且不会请求死亡");
                Check(gap < .015f, label + $" 推动闭合真实间隙 [gap={gap}]");
                float playerVx = w.player.GetComponent<Rigidbody2D>().velocity.x;
                float boxVx = w.box.GetComponent<Rigidbody2D>().velocity.x;
                float expected = w.player.EffectiveMovementSpeed * (sprint ? 1.5f : 1f);
                Check(Mathf.Abs(playerVx - boxVx) < .05f && Mathf.Abs(playerVx) > expected - .15f,
                    label + $" 贴合后保持共同速度 [player={playerVx}, box={boxVx}, expected={expected}]");
            }
        }
    }

    /// <summary>长按收纳、固定、变暗、可穿过；拉出受玩家／墙阻挡拒绝，Trigger 不阻挡；显式恢复默认。</summary>
    private static void CheckStorageAndRestore()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        Sample(w);
        Rigidbody2D boxBody = w.box.GetComponent<Rigidbody2D>();
        SpriteRenderer tint = w.box.GetComponentInChildren<SpriteRenderer>();
        Color baseColor = tint.color;
        Vector2 storedAt = w.box.DebugActualPosition;

        Tap(player, input);
        input.interactLongPressed = true;
        player.TickLogic(0f);
        Check(w.box.IsStored && !player._ActionData.PushPullEnabled, "选中后长按收纳并解除选中");
        Check(!w.box.GetComponent<BoxCollider2D>().enabled && !boxBody.simulated && !w.box.IsMotionParticipating,
            "收纳关闭碰撞器并退出双端运动");
        Check(tint.color != baseColor, "收纳后显示变暗");
        w.ground.enabled = false;
        for (int tick = 0; tick < 20; tick++) Step(w);
        Check((w.box.DebugActualPosition - storedAt).magnitude < .0001f, "收纳箱失去地面后多个物理步仍固定原位");
        w.ground.enabled = true;

        Bounds rect = w.box.InteractionBounds;
        MoveCenter(w.player, rect.center);
        Check(w.box.Interact(new PlayerInteractRequest(player, PlayerInteractKind.Tap, false)) == PlayerInteractOutcome.Rejected
            && w.box.IsStored, "玩家站在恢复位置时拒绝拉出");
        Collider2D wall = Solid(rect.center, new Vector2(.4f, .4f), w.wallLayer);
        PlaceOn(w.player, rect.min.x - .01f - w.player.BodyBounds.extents.x, w.groundTop);
        Physics2D.SyncTransforms();
        Check(w.box.Interact(new PlayerInteractRequest(player, PlayerInteractKind.Tap, false)) == PlayerInteractOutcome.Rejected
            && w.box.IsStored, "墙挡住恢复位置时拒绝拉出");
        wall.isTrigger = true;
        Physics2D.SyncTransforms();
        Sample(w);
        Tap(player, input);
        Check(!w.box.IsStored && w.box.GetComponent<BoxCollider2D>().enabled && boxBody.simulated,
            "贴合点按拉出，Trigger 与脚下地面不算阻挡");
        Check((w.box.DebugActualPosition - storedAt).magnitude < .0001f && tint.color == baseColor,
            "拉出回到收纳坐标并恢复显示");
        Check(!player._ActionData.PushPullEnabled, "拉出后不自动选中");

        var home = (Vector2)typeof(PhysicalBox).GetField("defaultPosition", Hidden).GetValue(w.box);
        MoveCenter(w.box, (Vector2)w.box.BodyBounds.center + Vector2.right * 5f);
        Sample(w);
        Check(w.box.Interact(new PlayerInteractRequest(player, PlayerInteractKind.LongPress, true))
            == PlayerInteractOutcome.Stored, "契约直接请求收纳");
        w.box.RestoreDefault();
        Check(!w.box.IsStored && (w.box.DebugActualPosition - home).magnitude < .0001f && boxBody.simulated,
            "显式恢复默认退出收纳并回到默认位置");
        player.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, false, true, true, false));
        player.TickLogic(0f);
        Check(!w.box.IsStored && (w.box.DebugActualPosition - home).magnitude < .0001f,
            "推拉许可关闭只退操作，不改箱子现场");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
    }

    /// <summary>与已收纳箱矩形重合拒绝收纳，不重合可收纳；同时贴合时间隙相同优先外部箱。</summary>
    private static void CheckStorageConflicts()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        Sample(w);
        var request = new PlayerInteractRequest(player, PlayerInteractKind.LongPress, true);
        Check(w.box.Interact(request) == PlayerInteractOutcome.Stored, "第一只箱收纳");

        PhysicalBox second = ExtraBox(w, w.box.InteractionBounds.center);
        Check(second.Interact(request) == PlayerInteractOutcome.Rejected && !second.IsStored
            && second.GetComponent<BoxCollider2D>().enabled, "与已收纳箱矩形重合时拒绝收纳且状态不变");

        Tap(player, input);
        Check(player._ActionData.PushPullBox == second && w.box.IsStored, "同等贴合时优先外部箱");
        player.RemoveInput();
        player.ChangeInputAsset(input);

        Bounds stored = w.box.InteractionBounds;
        MoveCenter(second, new Vector2(stored.max.x + second.BodyBounds.extents.x + .005f, stored.center.y));
        Check(second.Interact(request) == PlayerInteractOutcome.Stored, "与已收纳箱仅贴边不算重合，可收纳");
    }

    /// <summary>推箱行为只在选中、地面且有水平操作时输出，朝箱为推、背离为拉。</summary>
    private static void CheckPushPullAction()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        Sample(w);
        input.moveInput = 1f;
        player.TickLogic(0f);
        Check(player._ActionData.PushPullAction == PushPullAction.None, "未选中的接触推动不输出推箱行为");
        input.moveInput = 0f;
        Tap(player, input);
        Check(player._ActionData.PushPullAction == PushPullAction.None, "选中站立不算推箱");
        input.moveInput = 1f;
        player.TickLogic(0f);
        Check(player._ActionData.PushPullAction == PushPullAction.Push, "朝箱移动输出推");
        input.moveInput = -1f;
        player.TickLogic(0f);
        Check(player._ActionData.PushPullAction == PushPullAction.Pull, "背离箱移动输出拉");
        input.jumpPressed = true;
        player.TickLogic(0f);
        Check(player._ActionData.PushPullAction == PushPullAction.None, "跳跃解除后不输出推箱行为");
    }

    /// <summary>收纳箱扩大取出范围、单目标提示与实际按键一致；外部箱仍须贴合；不可选的带电箱不抢候选。</summary>
    private static void CheckReachAndHints()
    {
        var w = World(80f, true);
        Player player = w.owner;
        var input = new PlayerInputData();
        player.ChangeInputAsset(input);
        LevelBoxContext.Register(w.box);
        Sample(w);
        Bounds rect = w.box.BodyBounds;

        StandLeftOf(w, rect, .7f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.None, "外部箱间隙 0.7 超出选中距离，不提示");
        StandLeftOf(w, rect, .4f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.Select, "外部箱间隙 0.4 在选中距离内，提示点按选中");
        StandLeftOf(w, rect, .01f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.Select && player._ActionData.InteractTarget == w.box,
            "外部箱贴合时提示点按选中");
        Tap(player, input);
        Check(player._ActionData.InteractHint == PlayerInteractHint.Selected, "选中后提示解除／收纳");
        input.interactLongPressed = true;
        player.TickLogic(0f);
        Check(w.box.IsStored, "长按收纳");

        StandLeftOf(w, rect, .4f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.Restore && player._ActionData.InteractTarget == w.box,
            "收纳箱间隙 0.4 在取出范围内，提示点按取出");
        StandLeftOf(w, rect, .8f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.None, "超出取出范围后提示消失");

        MoveCenter(w.player, new Vector2(rect.center.x, w.player.BodyBounds.center.y));
        Sample(w);
        player.TickLogic(0f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.RestoreBlockedBySelf, "站在收纳矩形内提示退开");
        Tap(player, input);
        Check(w.box.IsStored, "自身占位时点按拒绝取出");
        StandLeftOf(w, rect, .2f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.Restore, "退开后立即转为可取出");

        Collider2D wall = Solid(rect.center, new Vector2(.4f, .4f), w.wallLayer);
        Physics2D.SyncTransforms();
        player.TickLogic(0f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.RestoreBlockedByOther, "其它物体占位提示恢复位置被占用");
        Tap(player, input);
        Check(w.box.IsStored, "其它物体占位时点按拒绝取出");
        Object.DestroyImmediate(wall.gameObject);
        Physics2D.SyncTransforms();

        PhysicalBox charged = ExtraBox(w, new Vector2(w.player.BodyBounds.min.x - .01f - rect.extents.x, rect.center.y));
        typeof(PhysicalBox).GetField("electrified", Hidden).SetValue(charged, true);
        player.TickLogic(0f);
        Check(player._ActionData.InteractTarget == w.box, "贴着的带电箱不抢走取出目标");
        Tap(player, input);
        Check(!w.box.IsStored && player._ActionData.InteractHint == PlayerInteractHint.Select
            && player._ActionData.InteractTarget == w.box, "提示目标与按键作用一致：取出后同一箱转为可选中");
        LevelBoxContext.Unregister(charged);

        player.RemoveInput();
        Check(player._ActionData.InteractHint == PlayerInteractHint.None && player._ActionData.InteractTarget == null,
            "撤销控制后提示消失");
        player.ChangeInputAsset(input);
        StandLeftOf(w, w.box.BodyBounds, .01f);
        player.ApplyCapabilities(new PlayerCapabilityPermissions(true, true, false, true, true, false));
        player.TickLogic(0f);
        Check(player._ActionData.InteractHint == PlayerInteractHint.None, "推拉许可关闭后不提示");
        player.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
    }

    /// <summary>把玩家放到矩形左侧、水平间隙为 gap 的地面上，采样并跑一次逻辑更新。</summary>
    private static void StandLeftOf(TestWorld w, Bounds rect, float gap)
    {
        PlaceOn(w.player, rect.min.x - gap - w.player.BodyBounds.extents.x, w.groundTop);
        Sample(w);
        w.owner.TickLogic(0f);
    }

    private static PhysicalBox ExtraBox(TestWorld w, Vector2 center)
    {
        PhysicalBox box = Instantiate("box.prefab").GetComponent<PhysicalBox>();
        Invoke(box, "Awake");
        MoveCenter(box, center);
        LevelBoxContext.Register(box);
        Invoke(box, "GeometricQueryPhase");
        w.entities.Add(box);
        return box;
    }

    private static void MoveCenter(BasicEntity e, Vector2 center)
    {
        e.transform.position += (Vector3)(center - (Vector2)e.BodyBounds.center);
        Physics2D.SyncTransforms();
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
        public readonly List<BasicEntity> entities = new List<BasicEntity>();
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
        w.entities.Add(w.player);
        w.entities.Add(w.box);
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
        BasicEntity[] entities = w.entities.ToArray();
        foreach (BasicEntity e in entities) Invoke(e, "GeometricQueryPhase");
        foreach (BasicEntity e in entities) Invoke(e, "PhyFunUpdate");
        foreach (BasicEntity e in entities) Invoke(e, "SpeedCalculation");
        // 与其他手动相位相同，直接执行运行时相位 4 回调；调度订阅由两次 Play 回归验证。
        sidePush.Invoke(null, null);
        Physics2D.Simulate(Time.fixedDeltaTime);
        foreach (BasicEntity e in entities) Invoke(e, "DisplacementCorrection");
    }

    private static float Gap(TestWorld w) => w.box.BodyBounds.min.x - w.player.BodyBounds.max.x;


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
        string folder = prefab == "player20.prefab" || prefab == "SpiritPlayer.prefab" ? "Player/" : "SceneObj/";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/gameObject/Pf/" + folder + prefab);
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/default/" + prefab);
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
