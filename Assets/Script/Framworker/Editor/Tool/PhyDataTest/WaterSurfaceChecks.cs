#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PhyData;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>真实预设、原水域协议、表现接触边沿和 GPU 输出；不保存制作场景。</summary>
public static class WaterSurfaceChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public const string Output = ".utmp/WaterSurfaceValidation/";
    static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/验证 P18 水体表现")]
    public static void Run()
    {
        passed.Clear(); Directory.CreateDirectory(Output);
        foreach (string path in WaterSurfaceAssets.Prefabs) CheckPrefab(path);
        RenderPreview();
        CatgirlPresentationAnimationChecks.Run();
        Check(true, "原角色动画表现回归通过");
        File.WriteAllLines(Output + "checks.txt", passed);
        Debug.Log("P18 water surface: " + passed.Count + " checks passed.");
    }

    public static List<string> RunPlayMode(Player actor, WaterRegion pool, Action<PlayerInputData> step)
    {
        int first = passed.Count;
        var visual = pool.GetComponent<WaterSurfaceVisual>();
        var layer = actor.GetComponentInChildren<PresentationLayer>();
        var physics = actor.GetComponent<CharacterPhysics>();
        var body = actor.GetComponent<BoxCollider2D>();
        Tick(visual, 0f); visual.ClearRipples();
        actor.RefreshRuntime(new Vector2(pool.GetComponent<BoxCollider2D>().bounds.center.x,
            pool.SurfaceY + body.bounds.size.y + 1f));
        Invoke(layer, "UpdateWaterRipples");
        actor.GetComponent<Rigidbody2D>().velocity = Vector2.down * 8f;
        for (int i = 0; i < 60; i++)
        {
            step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples");
            if (RippleCount(visual) > 0) break;
        }
        Check(RippleCount(visual) == 1, "P18 " + actor.name + " 经原相位落水，自动产生单次入水扰动；velocity=" + physics.GetPlanarVelocity());
        Check(actor.WaterFacts.submerged || actor.WaterFacts.partlyAboveSurface, "P18 入水表现仍对应原水域事实");
        for (int i = 0; i < 10; i++) { step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples"); }
        Check(RippleCount(visual) == 1, "P18 入水后静止不会逐帧重复触发");
        visual.ClearRipples();
        actor.RefreshRuntime(new Vector2(pool.GetComponent<BoxCollider2D>().bounds.center.x, pool.SurfaceY - 1f));
        for (int i = 0; i < 3; i++) { step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples"); }
        Check(RippleCount(visual) == 0, "P18 真实水下重新安置不误发入水波纹");
        float centerOffset = body.bounds.center.y - actor.GetComponent<Rigidbody2D>().position.y;
        actor.RefreshRuntime(new Vector2(pool.GetComponent<BoxCollider2D>().bounds.center.x, pool.SurfaceY - centerOffset));
        step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples");
        for (int i = 0; i < 8; i++)
        {
            step(new PlayerInputData { moveInput = 1f }); Tick(visual, Time.fixedDeltaTime);
            // 测试夹具冻结 Time.time，只为限频注入单调时间；位置、命中与意图仍走真实管线。
            Sample(layer, physics.ReadOnly_GeometryPhysicsData.RegionBodyBounds, physics.MotionRefreshVersion,
                physics.ReadOnly_GeometryPhysicsData.RegionBodyVelocity, true, 1f + i * .3f);
        }
        Check(actor._ActionData.isSurfaceSwimming && RippleCount(visual) >= 4, "P18 真实水面游动意图自动接入限频扰动");
        int movingCount = Get<int>(visual, "nextRipple");
        step(null); Tick(visual, Time.fixedDeltaTime);
        Sample(layer, physics.ReadOnly_GeometryPhysicsData.RegionBodyBounds, physics.MotionRefreshVersion,
            physics.ReadOnly_GeometryPhysicsData.RegionBodyVelocity, true, 5f);
        Check(Get<int>(visual, "nextRipple") == movingCount, "P18 真实角色撤销输入后不继续发划水扰动");
        visual.ClearRipples();
        actor.RefreshRuntime(new Vector2(pool.GetComponent<BoxCollider2D>().bounds.center.x,
            pool.SurfaceY + body.bounds.size.y + 1f));
        Invoke(actor, "BeginDeathMotion");
        Check(actor._ActionData.HoldIdle && layer.TryBeginDeathPlayback(out _), "P18 真实死亡待机与死亡播放夹具成立");
        Invoke(layer, "UpdateWaterRipples");
        actor.GetComponent<Rigidbody2D>().velocity = Vector2.down * 8f;
        for (int i = 0; i < 60; i++)
        {
            step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples");
            if (RippleCount(visual) > 0) break;
        }
        Check(RippleCount(visual) == 1, "P18 " + actor.name + " 死亡被动下落仍产生一次入水波纹");
        for (int i = 0; i < 10; i++) { step(null); Tick(visual, Time.fixedDeltaTime); Invoke(layer, "UpdateWaterRipples"); }
        Check(RippleCount(visual) == 1, "P18 死亡入水后不重复发入水或划水波纹");
        layer.CancelDeathPlayback();
        actor.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        actor.RefreshRuntime(pool.GetComponent<BoxCollider2D>().bounds.center);
        List<string> results = passed.GetRange(first, passed.Count - first);
        Directory.CreateDirectory(Output); File.WriteAllLines(Output + "play-checks.txt", results);
        return results;
    }

    static void CheckPrefab(string path)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(asset != null, path + " 预设存在");
        GameObject root = Object.Instantiate(asset), other = Object.Instantiate(asset);
        GameObject actor = new GameObject("water contact test actor");
        try
        {
            var water = root.GetComponent<WaterSurfaceVisual>();
            var second = other.GetComponent<WaterSurfaceVisual>();
            var source = root.GetComponent<WaterRegion>();
            var box = root.GetComponent<BoxCollider2D>();
            var sprite = root.GetComponent<SpriteRenderer>();
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            Check(water != null && water.ValidateConfiguration(out _), "预设内部关联完整，无需场景手动接角色");
            root.transform.localScale = new Vector3(2f, 3f, 1f);
            box.size = new Vector2(6f, 2f); box.offset = new Vector2(.4f, .2f);
            Physics2D.SyncTransforms(); Tick(water, .1f);
            Bounds before = box.bounds;
            Mesh mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Near(root.transform.TransformPoint(vertices[0]).x, before.min.x, "碰撞器偏移与 X 非等比缩放驱动网格左边");
            Near(root.transform.TransformPoint(vertices[vertices.Length - 1]).x, before.max.x, "网格右边与实际水域一致");
            Near(root.transform.TransformPoint(vertices[0]).y, before.min.y, "底边与碰撞器一致");
            Near(root.transform.TransformPoint(vertices[vertices.Length - 1]).y, before.max.y, "未变形网格上缘就是水平判定基线");
            Check(mesh.vertexCount > 100 && mesh.vertexCount <= 6147, "横向细分随宽度产生且有有限预算");
            MaterialPropertyBlock properties = Properties(renderer);
            Vector4 wave = properties.GetVector("_Wave");
            Near(wave.x, .045f, "非等比拉伸不放大世界波幅");
            Near(wave.y, 1.6f, "非等比拉伸不拉长世界波长");
            Check(sprite.forceRenderingOff && renderer.enabled, "水域由网格接管显示，不叠加两份透明水体");
            Check(renderer.sharedMaterial == other.GetComponentInChildren<MeshRenderer>().sharedMaterial, "水域共享制作材质");
            Color otherColor = Properties(other.GetComponentInChildren<MeshRenderer>()).GetColor("_BodyColor");
            sprite.color = new Color(.2f, .4f, 1f, .31f); Tick(water, .1f);
            Check(Properties(renderer).GetColor("_BodyColor") == sprite.color
                && Properties(other.GetComponentInChildren<MeshRenderer>()).GetColor("_BodyColor") == otherColor,
                "颜色／透明度读取本实例主体，修改不污染另一池");
            Check(source.TryGetSurfaceY(box, before.center.x, out float surface), "原水平水面协议仍可采样");
            Near(surface, before.max.y, "波浪不会抬高物理水面");
            Near(box.bounds.size.x, before.size.x, "表现没有改写碰撞宽度");
            Near(box.bounds.size.y, before.size.y, "表现没有改写碰撞高度");

            PresentationLayer presentation = actor.AddComponent<PresentationLayer>();
            var geometry = new GeometryPhysicsData(); geometry.regionOverlaps.Add(box);
            var actions = new MovementData();
            Set(presentation, "geometryPhysicsData", new ReadOnly_GeometryPhysicsData(geometry, new PhysicalFunctionData()));
            Set(presentation, "actionData", new ReadOnly_ActionData(actions));
            Bounds above = Body(before.center.x, surface + .8f), contact = Body(before.center.x, surface + .2f);
            Sample(presentation, above, 0, Vector2.down * 8f, true, 0f);
            Sample(presentation, contact, 0, Vector2.down * 8f, true, .1f);
            Check(RippleCount(water) == 1 && RippleCount(second) == 0, "从上方真实入水只扰动原命中的水池");
            actions.nowState = PlayerStateMachine.E_playerState.surfaceSwimming; actions.onMove = 1f;
            Sample(presentation, contact, 0, Vector2.right, true, .5f);
            int count = RippleCount(water);
            Sample(presentation, contact, 0, Vector2.right, true, .51f);
            Check(count == 2 && RippleCount(water) == count, "主动水面游动产生弱扰动且逐帧限频");
            actions.onMove = 0f;
            Sample(presentation, contact, 0, Vector2.right * 5f, true, 1f);
            Check(RippleCount(water) == count, "被动水流与静止不制造划水波纹");
            actions.onMove = 1f; actions.nowState = PlayerStateMachine.E_playerState.swimming;
            Sample(presentation, Body(before.center.x, surface - 1.2f), 0, Vector2.right, true, 1.5f);
            Check(RippleCount(water) == count, "深水划水不持续激起水面波纹");
            water.ClearRipples();
            Sample(presentation, above, 1, Vector2.zero, true, 2f);
            Sample(presentation, contact, 2, Vector2.down * 30f, true, 2.1f);
            Check(RippleCount(water) == 0, "显式传送刷新序号丢弃旧位置边沿");
            actions.onMove = 0f;
            for (uint i = 0; i < 20; i++)
            {
                Sample(presentation, above, 2, Vector2.zero, true, 3f + i);
                Sample(presentation, contact, 2, Vector2.down * 8f, true, 3.1f + i);
            }
            Check(RippleCount(water) == 8, "持续接触只保留最多八个扰动");
            float clock = Get<float>(water, "visualTime"); Tick(water, 2f, true);
            Near(Get<float>(water, "visualTime"), clock, "世界暂停停止波形与扰动计时");
            Tick(water, .2f); Near(Get<float>(water, "visualTime"), clock + .2f, "恢复继续原时钟而不补跑暂停时间");
            source.RestoreEnvironmentDefaults(); Tick(water, 0f);
            Check(RippleCount(water) == 0, "同值默认恢复也清旧扰动");
            var trigger = source.CopyEnvironmentSlots(true);
            for (int i = 0; i < trigger.Length; i++) if (trigger[i] is WaterSwimmingParameter) trigger[i] = null;
            Check(source.TryCommitEnvironmentEdit(source.CopyEnvironmentSlots(false), trigger, true, out _), "沿原能力接口抽走水性质");
            Tick(water, 0f);
            Check(!renderer.enabled && !sprite.forceRenderingOff, "水性质撤销恢复主体精灵并关闭波浪");
            source.RestoreEnvironmentDefaults(); Tick(water, 0f);
            Check(renderer.enabled && sprite.forceRenderingOff, "恢复水性质重新启用表现");
            Check(source.TryCommitEnvironmentEdit(source.CopyEnvironmentSlots(false), source.CopyEnvironmentSlots(true), false, out _), "沿原接口切换实心");
            Tick(water, 0f);
            Check(!renderer.enabled && !sprite.forceRenderingOff && !box.isTrigger, "实心几何不保留水波或隐藏主体");
            source.RestoreEnvironmentDefaults(); Tick(water, 0f);
            float bottom = box.bounds.min.y, initial = box.bounds.max.y;
            Check(source.PrepareRun(1f, initial + 1f), "主水复用原最高点配置");
            source.SetRisingAllowed(true);
            for (int i = 0; i < 100; i++) Invoke(source, "AdvanceWaterLevel");
            Tick(water, 0f);
            Near(box.bounds.min.y, bottom, "上涨后网格与水域底边保持固定");
            Near(box.bounds.max.y, initial + 1f, "上涨到顶仍沿原水平上限");
            source.RestoreRunDefaults(); Tick(water, 0f);
            Near(box.bounds.max.y, initial, "主水默认恢复显示与物理一致");
            root.transform.localScale = new Vector3(-1f, 1f, 1f);
            Check(!water.ValidateConfiguration(out _), "负缩放明确拒绝");
            root.transform.localScale = Vector3.one; root.transform.rotation = Quaternion.Euler(0, 0, 15);
            Check(!water.ValidateConfiguration(out _), "旋转水面明确拒绝");
            root.transform.rotation = Quaternion.identity; box.size = new Vector2(10000f, 1f);
            Check(!water.ValidateConfiguration(out _), "超出网格预算明确拒绝");
            box.size = Vector2.one; Set(water, "waveLength", float.NaN);
            Check(!water.ValidateConfiguration(out _), "非有限波形参数明确拒绝");
            water.enabled = false;
            Check(!sprite.forceRenderingOff && !renderer.enabled, "停用组件恢复原显示并关闭动态网格");
        }
        finally { Object.DestroyImmediate(actor); Object.DestroyImmediate(root); Object.DestroyImmediate(other); }
    }

    static void RenderPreview()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active, target = null;
        Texture2D image = null;
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WaterSurfaceAssets.Prefabs[0]);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            root.transform.localScale = Vector3.one;
            root.GetComponent<BoxCollider2D>().size = new Vector2(8f, 2.5f);
            var visual = root.GetComponent<WaterSurfaceVisual>();
            Physics2D.SyncTransforms(); Tick(visual, 0f);
            var cameraRoot = new GameObject("water preview camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            Camera camera = cameraRoot.AddComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(0, .4f, -10);
            camera.orthographic = true; camera.orthographicSize = 2.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .14f, 1);
            target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32); camera.targetTexture = target;
            image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var box = root.GetComponent<BoxCollider2D>(); float surface = box.bounds.max.y;
            Color[] baseline = null; float difference = 0f;
            for (int frame = 0; frame < 24; frame++)
            {
                if (frame == 1) Invoke(visual, "EmitRipple", 0f, .15f);
                Tick(visual, .08f); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
                Color[] pixels = image.GetPixels();
                if (frame == 0) baseline = pixels;
                if (frame == 8)
                    for (int i = 0; i < pixels.Length; i++) difference += Mathf.Abs(pixels[i].g - baseline[i].g);
                File.WriteAllBytes(Output + "frame-" + frame.ToString("D2") + ".png", image.EncodeToPNG());
            }
            Check(difference > 5f, "GPU 渲染实际产生随时间变化的波浪与局部扰动");
            Near(box.bounds.max.y, surface, "GPU 波纹全过程不移动水平碰撞水面");
            Check(!ShaderUtil.ShaderHasError(root.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader), "实际 GPU 编译没有 shader 错误");
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (image != null) Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static Bounds Body(float x, float y) => new Bounds(new Vector3(x, y, 0), new Vector3(.6f, 1f, 0));
    static MaterialPropertyBlock Properties(Renderer renderer) { var p = new MaterialPropertyBlock(); renderer.GetPropertyBlock(p); return p; }
    static void Tick(WaterSurfaceVisual water, float delta, bool pause = false) => Invoke(water, "RefreshVisual", delta, pause);
    static void Sample(PresentationLayer layer, Bounds bounds, uint version, Vector2 velocity, bool permit, float time) => Invoke(layer, "SampleWaterRipples", bounds, version, velocity, permit, time);
    static int RippleCount(WaterSurfaceVisual water) { int count = 0; foreach (Vector4 ripple in Get<Vector4[]>(water, "ripples")) if (ripple.z > 0f) count++; return count; }
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    static void Invoke(object target, string name, params object[] args)
    {
        // 测试：效果水继承普通水，私有相位方法须从实际声明层读取。
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo method = type.GetMethod(name, Hidden | BindingFlags.DeclaredOnly);
            if (method != null) { method.Invoke(target, args); return; }
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }
    static void Near(float actual, float expected, string text) => Check(Mathf.Abs(actual - expected) < .002f, text);
    static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException("FAILED: " + text); passed.Add("PASS " + text); }
}
#endif
