#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>只在临时预览场景中验收真实表现组件和 Animator，不保存场景或修改玩法数据。</summary>
public static class CatgirlPresentationAnimationChecks
{
    private const string ControllerPath = "Assets/MyObjAssets/gameObject/Animation/player.controller";
    private const string OutputFolder = "output/animation-integration-20261009";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo AwakeMethod = typeof(PresentationLayer).GetMethod("Awake", Hidden);
    private static readonly MethodInfo UpdateMethod = typeof(PresentationLayer).GetMethod("Update", Hidden);
    private static readonly List<string> report = new List<string>();
    private static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
    private static AnimatorController controller;

    [MenuItem("自定义工具/测试工具/验证/验证猫娘表现动画（Edit Mode）")]
    public static void Run()
    {
        report.Clear();
        report.Add("Catgirl presentation animation checks — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.Add("Edit Mode; real player.controller; temporary preview scenes; no saved scene/settings changes.");
        var errors = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        Application.logMessageReceived += capture;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run these checks in Edit Mode.");
            LoadController();
            CheckGroundAndSprint();
            CheckPushPull();
            CheckAirAndWall();
            CheckSwimming(PlayerStateMachine.E_playerState.swimming, "SwimIdle", "Swim");
            CheckSwimming(PlayerStateMachine.E_playerState.surfaceSwimming, "SurfaceSwimIdle", "SurfaceSwim");
            CheckFacing();
            CheckFlightAndSurfaceJump();
            CheckMotionSuspension();
            CheckSequence("Sprint", PlayerStateMachine.E_playerState.isOnGround, 1f, 0f, 1.5f);
            CheckSequence("SwimIdle", PlayerStateMachine.E_playerState.swimming, 0f, 0f, 1f);
            CheckSequence("Swim", PlayerStateMachine.E_playerState.swimming, 0f, 1f, 1f);
            CheckSequence("SurfaceSwimIdle", PlayerStateMachine.E_playerState.surfaceSwimming, 0f, 0f, 1f);
            CheckSequence("SurfaceSwim", PlayerStateMachine.E_playerState.surfaceSwimming, 1f, 0f, 1f);
            CheckSequence("Push", PlayerStateMachine.E_playerState.isOnGround, 1f, 0f, 1f, PushPullAction.Push);
            CheckSequence("Pull", PlayerStateMachine.E_playerState.isOnGround, -1f, 0f, 1f, PushPullAction.Pull);
            CheckFootGrounding();
            Require(errors.Count == 0, "临时验收没有 Unity 错误日志：" + string.Join(" | ", errors));
            report.Add("RESULT: PASS");
            Debug.Log("Catgirl presentation animation checks: PASS. Report: " + ReportPath);
        }
        catch (Exception error)
        {
            report.Add("RESULT: FAIL\n" + error);
            throw;
        }
        finally
        {
            Application.logMessageReceived -= capture;
            WriteReport();
            clips.Clear();
            controller = null;
        }
    }

    // 统一批处理入口先导入已确认资源；普通 Run 不导入、不退出编辑器。
    public static void RunForBatch()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("RunForBatch requires a fresh batch-mode editor.");
        bool checksStarted = false;
        try
        {
            CatgirlSprintSwimAssets.ImportAndValidate();
            checksStarted = true;
            Run();
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            if (!checksStarted)
            {
                report.Clear();
                report.Add("RESULT: FAIL before presentation checks — asset import\n" + error);
                WriteReport();
            }
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../" + OutputFolder,
        "catgirl-presentation-animation-checks.txt"));

    private static void WriteReport()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllLines(ReportPath, report);
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Add("PASS: " + description);
    }

    private static void LoadController()
    {
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Require(controller != null && controller.layers.Length > 0, "真实 player.controller 已加载");
        clips.Clear();
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            AnimationClip clip = child.state.motion as AnimationClip;
            if (clip != null) clips[child.state.name] = clip;
        }
        string[] required = { "On", "move", "Sprint", "Push", "Pull", "Jump_Up", "Jump_Down", "OnAir", "OnAir_Down", "Wall",
            "SwimIdle", "Swim", "SurfaceSwimIdle", "SurfaceSwim", "Flight" };
        foreach (string state in required)
            Require(clips.ContainsKey(state), state + " 有真实 AnimationClip");
    }

    private static void CheckGroundAndSprint()
    {
        using (var fixture = new Fixture())
        {
            fixture.Select(PlayerStateMachine.E_playerState.isOnGround, 0f, 0f);
            fixture.Physics.phyHSpeed = 3f;
            fixture.Expect("On", "地面零输入且被环境带动仍待机");
            fixture.Action.onMove = 1f;
            fixture.Expect("move", "地面普通水平移动");
            fixture.Action.speedScale = 1.5f;
            fixture.Expect("Sprint", "有效疾跑倍率选择 Sprint");
            fixture.Action.speedScale = 1f;
            fixture.Expect("move", "倍率归一立即退出 Sprint");
            fixture.Action.speedScale = 1.5f;
            fixture.Action.onMove = 0f;
            fixture.Expect("On", "零移动意图不能疾跑");
            fixture.Action.onMove = .009f;
            fixture.Expect("On", "水平意图死区内不能疾跑");
        }
    }

    private static void CheckAirAndWall()
    {
        using (var fixture = new Fixture())
        {
            fixture.Select(PlayerStateMachine.E_playerState.inAir, 1f, 0f, 1.5f, false);
            fixture.Physics.verticalSpeed = 2f;
            fixture.Expect("OnAir", "空中存在疾跑倍率也只播放上升循环");
            fixture.Action.nowState = PlayerStateMachine.E_playerState.onWallSliding;
            fixture.Expect("Wall", "贴墙存在疾跑倍率也只播放贴墙动画");
        }
    }

    private static void CheckPushPull()
    {
        using (var fixture = new Fixture())
        {
            fixture.Select(PlayerStateMachine.E_playerState.isOnGround, 1f, 0f, 1.5f);
            fixture.SetPushPull(PushPullAction.None, 3f);
            fixture.Expect("Sprint", "只选中／尚在自动走近时，不提前播放推拉动画");
            fixture.Action.pushPullAction = PushPullAction.Push;
            fixture.Expect("Push", "逻辑推箱行为优先于疾跑倍率");
            Require(!fixture.Renderer.flipX, "推右侧箱子面朝右");
            fixture.Action.onMove = -1f;
            fixture.Action.pushPullAction = PushPullAction.Pull;
            fixture.Expect("Pull", "向左后退拉右侧箱子");
            Require(!fixture.Renderer.flipX, "拉右侧箱子保持右朝向，不随后退输入翻转");
            fixture.Box.transform.position = new Vector3(-3f, 0f, 0f);
            Physics2D.SyncTransforms();
            fixture.Action.pushPullAction = PushPullAction.Push;
            fixture.Expect("Push", "向左推左侧箱子");
            Require(fixture.Renderer.flipX, "推左侧箱子面朝左");
            fixture.Action.onMove = 1f;
            fixture.Action.pushPullAction = PushPullAction.Pull;
            fixture.Expect("Pull", "向右后退拉左侧箱子");
            Require(fixture.Renderer.flipX, "拉左侧箱子保持左朝向");
            fixture.Action.onMove = 0f;
            fixture.Action.pushPullAction = PushPullAction.None;
            fixture.Expect("On", "停止推拉后回到待机");
            fixture.Action.onMove = 1f;
            fixture.Action.speedScale = 1f;
            fixture.Action.pushPullBox = null;
            fixture.Expect("move", "解除选中／普通接触推动使用行走");
            fixture.Action.pushPullAction = PushPullAction.Push;
            fixture.Expect("move", "缺少有效箱子时不播放悬空推箱姿势");
            fixture.Action.pushPullBox = fixture.Box;
            fixture.Action.nowState = PlayerStateMachine.E_playerState.inAir;
            fixture.Geometry.isGrounded = false;
            fixture.Physics.verticalSpeed = 2f;
            fixture.Expect("OnAir", "离地事实优先于推拉输出");
            fixture.Action.nowState = PlayerStateMachine.E_playerState.swimming;
            fixture.Expect("Swim", "游泳事实优先于推拉输出");
        }
    }

    private static void CheckFootGrounding()
    {
        Sprite idle = SpriteKeys(clips["On"])[0].value as Sprite;
        float idleBottom = OpaqueBottom(idle);
        var sprint = SpriteKeys(clips["Sprint"]);
        var support = new HashSet<int> { 1, 2, 5, 6 };
        for (int i = 0; i < 8; i++)
        {
            float bottom = OpaqueBottom((Sprite)sprint[i].value);
            Require(bottom >= idleBottom - .01f, "疾跑第 " + (i + 1) + " 帧脚底不穿过待机地面基线");
            if (support.Contains(i)) Require(Mathf.Abs(bottom - idleBottom) < .01f,
                "疾跑支撑帧 " + (i + 1) + " 与待机脚底基线对齐");
            if (i == 4) Require(bottom - idleBottom > .1f && bottom - idleBottom < .17f,
                "疾跑腾空帧保留短暂离地，不强行把每帧脚贴地");
        }
        foreach (string state in new[] { "Push", "Pull" })
        {
            var keys = SpriteKeys(clips[state]);
            for (int i = 0; i < 6; i++) Require(Mathf.Abs(OpaqueBottom((Sprite)keys[i].value) - idleBottom) < .01f,
                state + " 第 " + (i + 1) + " 帧使用待机脚底与尺度基准");
        }
    }

    private static float OpaqueBottom(Sprite sprite)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Require(texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite))), "可读入脚底核对的原始 PNG");
            Color32[] pixels = texture.GetPixels32();
            Rect rect = sprite.rect;
            for (int y = (int)rect.yMin; y < (int)rect.yMax; y++)
                for (int x = (int)rect.xMin; x < (int)rect.xMax; x++)
                    if (pixels[y * texture.width + x].a > 20) return (y - rect.yMin - sprite.pivot.y) / sprite.pixelsPerUnit;
            throw new InvalidOperationException("没有可见脚底像素：" + sprite.name);
        }
        finally { Object.DestroyImmediate(texture); }
    }

    private static void CheckSwimming(PlayerStateMachine.E_playerState mode, string idle, string moving)
    {
        bool surface = mode == PlayerStateMachine.E_playerState.surfaceSwimming;
        using (var fixture = new Fixture())
        {
            fixture.Select(mode, 0f, 0f, 1.5f, true);
            fixture.Expect(idle, mode + "：游泳行为优先于池底几何着地和疾跑倍率");
            fixture.Action.onMove = 1f;
            fixture.Expect(moving, mode + "：向右划水");
            fixture.Action.onMove = -1f;
            fixture.Expect(moving, mode + "：向左划水");
            fixture.Action.onMove = 0f;
            fixture.Action.verticalInput = surface ? 0f : 1f;
            fixture.Expect(surface ? idle : moving, surface
                ? "水面向上已经由逻辑过滤为零：仍待机"
                : "水底纯向上：移动动画");
            fixture.Action.verticalInput = -1f;
            fixture.Expect(moving, mode + "：纯向下／下潜使用移动动画");
            fixture.Action.verticalInput = 0f;
            fixture.Physics.horizontalSpeed = .8f;
            fixture.Physics.verticalSpeed = .3f;
            fixture.Physics.phyHSpeed = 2f;
            fixture.Physics.phyVSpeed = 1.1f;
            fixture.Expect(idle, mode + "：被动水流／水面高度修正速度不触发主动划水");
            fixture.Action.onMove = .009f;
            fixture.Action.verticalInput = -.009f;
            fixture.Expect(idle, mode + "：两轴均在输入死区时待机");
            fixture.Geometry.isGrounded = false;
            fixture.Action.onMove = 1f;
            fixture.Expect(moving, mode + "：离开池底仍由游泳事实选择动画");
        }
    }

    private static void CheckFacing()
    {
        foreach (PlayerStateMachine.E_playerState mode in new[] { PlayerStateMachine.E_playerState.isOnGround,
            PlayerStateMachine.E_playerState.swimming, PlayerStateMachine.E_playerState.surfaceSwimming })
        {
            using (var fixture = new Fixture())
            {
                fixture.Select(mode, 1f, 0f);
                fixture.Tick(0f);
                Require(!fixture.Renderer.flipX, mode + "：向右不翻转");
                fixture.Action.onMove = -1f;
                fixture.Tick(0f);
                Require(fixture.Renderer.flipX, mode + "：向左翻转");
                fixture.Action.onMove = 0f;
                fixture.Action.verticalInput = -1f;
                fixture.Tick(0f);
                Require(fixture.Renderer.flipX, mode + "：纯向下保留左朝向");
                fixture.Action.verticalInput = mode == PlayerStateMachine.E_playerState.surfaceSwimming ? 0f : 1f;
                fixture.Tick(0f);
                Require(fixture.Renderer.flipX, mode + "：纯向上／水面已过滤向上保留左朝向");
                fixture.Action.verticalInput = 0f;
                fixture.Tick(0f);
                Require(fixture.Renderer.flipX, mode + "：零输入保留朝向");
            }
        }
    }

    private static void CheckFlightAndSurfaceJump()
    {
        using (var fixture = new Fixture())
        {
            fixture.Select(PlayerStateMachine.E_playerState.flying, 1f, 1f, 1.5f, true);
            fixture.Expect("Flight", "飞行优先于几何着地、四向意图和疾跑倍率");
            float before = fixture.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            fixture.Tick(clips["Flight"].length * .25f);
            fixture.Tick(clips["Flight"].length * .25f);
            Require(fixture.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before + .4f,
                "持续飞行逐帧推进，不反复重启");
        }
        using (var fixture = new Fixture())
        {
            fixture.Select(PlayerStateMachine.E_playerState.surfaceSwimming, 0f, 0f);
            fixture.Expect("SurfaceSwimIdle", "出水跳跃前处于水面待机");
            fixture.Action.nowState = PlayerStateMachine.E_playerState.inAir;
            fixture.Action.surfaceJumpInProgress = true;
            fixture.Physics.verticalSpeed = 3f;
            fixture.Physics.jumpSequence++;
            fixture.Expect("Jump_Up", "水面跃出事件即使池底仍着地也播放起跳，不误播游泳");
            fixture.Tick(clips["Jump_Up"].length + .03f);
            fixture.Expect("OnAir", "跃出起手完成后仍优先为空中上升，不回池底或游泳");
            fixture.Geometry.isGrounded = false;
            fixture.Physics.verticalSpeed = -1f;
            fixture.Expect("OnAir_Down", "跃出下落按真实竖直速度选择下降循环");
        }
    }

    private static void CheckMotionSuspension()
    {
        var modes = new[] { PlayerStateMachine.E_playerState.flying, PlayerStateMachine.E_playerState.isOnGround,
            PlayerStateMachine.E_playerState.swimming, PlayerStateMachine.E_playerState.inAir };
        string[] states = { "Flight", "move", "Swim", "OnAir" };
        for (int i = 0; i < modes.Length; i++)
        {
            using (var fixture = new Fixture())
            {
                fixture.Select(modes[i], 1f, 1f, 1f, i == 1);
                fixture.Physics.verticalSpeed = 2f;
                fixture.Expect(states[i], "暂停前选中原动作 " + states[i]);
                bool facing = fixture.Renderer.flipX;
                fixture.Action.motionUpdates = false;
                fixture.Select(PlayerStateMachine.E_playerState.inAir, -1f, 0f, 1f, false);
                fixture.Physics.verticalSpeed = -2f;
                fixture.Tick(.15f);
                fixture.Expect(states[i], "运动停用后保留原动作 " + states[i]);
                Require(fixture.Renderer.flipX == facing, "运动停用后保留原朝向 " + states[i]);
                fixture.Action.motionUpdates = true;
                fixture.Expect("OnAir_Down", "恢复运动后重新消费真实下落事实 " + states[i]);
            }
        }
    }
    private static ObjectReferenceKeyframe[] SpriteKeys(AnimationClip clip)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite" && binding.path == "")
                return AnimationUtility.GetObjectReferenceCurve(clip, binding);
        }
        throw new InvalidOperationException(clip.name + " 缺少根对象 SpriteRenderer.m_Sprite 曲线。");
    }

    private static void CheckSequence(string state, PlayerStateMachine.E_playerState mode, float x, float y, float scale,
        PushPullAction pushPull = PushPullAction.None)
    {
        AnimationClip clip = clips[state];
        Require(AssetDatabase.GetAssetPath(clip) == "Assets/MyObjAssets/gameObject/Animation/Loop/" + state + ".anim",
            state + " 关联已批准动画对应的工程片段");
        Require(AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 1
            && AnimationUtility.GetCurveBindings(clip).Length == 0,
            state + " 只采样精灵，不额外改写位置、缩放或其它属性");
        ObjectReferenceKeyframe[] keys = SpriteKeys(clip);
        Require(clip.length > 0f && AnimationUtility.GetAnimationClipSettings(clip).loopTime,
            state + " 持续动画有时长并启用循环");
        var uniqueFrames = new HashSet<Sprite>();
        foreach (ObjectReferenceKeyframe key in keys)
        {
            Sprite sprite = key.value as Sprite;
            Require(sprite != null && sprite.texture != null, state + " 帧引用完整：" + key.time.ToString("F3"));
            uniqueFrames.Add(sprite);
        }
        int expectedFrames = state == "Sprint" ? 8 : state.EndsWith("Idle", StringComparison.Ordinal) ? 4 : 6;
        Require(uniqueFrames.Count == expectedFrames, state + " 包含预期真实精灵帧（" + expectedFrames + "）");
        using (var fixture = new Fixture())
        {
            fixture.Select(mode, x, y, scale);
            if (pushPull != PushPullAction.None) fixture.SetPushPull(pushPull, 3f);
            fixture.Expect(state, state + " 正确进入并采样首帧");
            float elapsed = 0f;
            for (int i = 0; i < keys.Length; i++)
            {
                float next = i + 1 < keys.Length ? keys[i + 1].time : clip.length;
                if (next <= keys[i].time + .0001f) continue;
                float sampleTime = (keys[i].time + next) * .5f;
                fixture.Tick(sampleTime - elapsed);
                elapsed = sampleTime;
                fixture.Expect(state, state + " 真正 Animator 采样帧 " + i + "：" + keys[i].value.name);
                Require(fixture.Renderer.sprite == keys[i].value, state + " 对应时间实际显示预期精灵 " + i);
            }
            float before = fixture.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            for (int i = 0; i < 8; i++) fixture.Tick(clip.length * .2f);
            float after = fixture.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            Require(after > before + 1.5f, state + " 连续八次表现更新跨越循环，未逐帧重启");
            fixture.Expect(state, state + " 跨循环后状态及精灵采样仍一致");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly MovementData Action = new MovementData();
        public readonly PlayerPhysicsData Physics = new PlayerPhysicsData();
        public readonly GeometryPhysicsData Geometry = new GeometryPhysicsData();
        public readonly Animator Animator;
        public readonly SpriteRenderer Renderer;
        public PhysicalBox Box { get; private set; }
        private readonly PresentationLayer presentation;
        private readonly GameObject owner;
        private readonly Scene scene;

        public Fixture()
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                owner = new GameObject("Catgirl presentation check fixture") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(owner, scene);
                Renderer = owner.AddComponent<SpriteRenderer>();
                Animator = owner.AddComponent<Animator>();
                Animator.runtimeAnimatorController = controller;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.applyRootMotion = false;
                presentation = owner.AddComponent<PresentationLayer>();
                presentation.enabled = false; // Edit Mode 中由验收逐次调用真实 Update，不依赖编辑器帧调度。
                AwakeMethod.Invoke(presentation, null);
                Animator.Rebind();
                Animator.Update(0f);
                Action.nowState = PlayerStateMachine.E_playerState.isOnGround;
                Geometry.isGrounded = true;
                presentation.Init(new ReadOnly_PlayerPhysicsData(Physics),
                    new ReadOnly_GeometryPhysicsData(Geometry, new PhysicalFunctionData()), new ReadOnly_ActionData(Action));
                if (!(bool)typeof(PresentationLayer).GetField("isInitialized", Hidden).GetValue(presentation))
                    throw new InvalidOperationException("临时表现层未完成真实 Init；请先导入新 Animator 状态。");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Select(PlayerStateMachine.E_playerState mode, float x, float y, float scale = 1f, bool grounded = true)
        {
            Action.nowState = mode;
            Action.onMove = x;
            Action.verticalInput = y;
            Action.speedScale = scale;
            Geometry.isGrounded = grounded;
        }

        public void Tick(float delta)
        {
            UpdateMethod.Invoke(presentation, null);
            Animator.Update(delta);
        }

        public void SetPushPull(PushPullAction action, float boxX)
        {
            if (Box == null)
            {
                var boxOwner = new GameObject("Presentation-only box fixture") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(boxOwner, scene);
                BoxCollider2D collider = boxOwner.AddComponent<BoxCollider2D>();
                Box = boxOwner.AddComponent<PhysicalBox>();
                // Edit Mode 不运行物理生命周期；只给临时箱提供真实碰撞矩形以验证左右朝向。
                typeof(BasicEntity).GetField("boxCollider", Hidden).SetValue(Box, collider);
            }
            Box.transform.position = new Vector3(boxX, 0f, 0f);
            Physics2D.SyncTransforms();
            Action.pushPullBox = Box;
            Action.pushPullAction = action;
        }

        public void Expect(string state, string description)
        {
            Tick(0f);
            AnimatorStateInfo info = Animator.GetCurrentAnimatorStateInfo(0);
            Require(info.fullPathHash == UnityEngine.Animator.StringToHash("Base Layer." + state), description);
            AnimationClip clip = clips[state];
            AnimatorClipInfo[] current = Animator.GetCurrentAnimatorClipInfo(0);
            Require(current.Length > 0 && current[0].clip == clip, state + " 使用 controller 中预期片段");
            ObjectReferenceKeyframe[] keys = SpriteKeys(clip);
            float sampleTime = (clip.isLooping ? Mathf.Repeat(info.normalizedTime, 1f)
                : Mathf.Clamp01(info.normalizedTime)) * clip.length;
            Sprite expected = null;
            foreach (ObjectReferenceKeyframe key in keys)
            {
                if (key.time > sampleTime + .00001f) break;
                expected = key.value as Sprite;
            }
            Require(expected != null && Renderer.sprite == expected,
                state + " 的真实 SpriteRenderer 与片段时间采样一致");
        }

        public void Dispose()
        {
            try
            {
                if (owner != null) Object.DestroyImmediate(owner);
            }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
#endif
