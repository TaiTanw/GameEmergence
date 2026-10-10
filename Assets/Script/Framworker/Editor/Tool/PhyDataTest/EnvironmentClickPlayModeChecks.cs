#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>保存地面的真实点击与应用，以及临时对象的框线生命周期检查；不保存场景。</summary>
public static class EnvironmentClickPlayModeChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Run(GameplayPanel panel, Player spirit)
    {
        var passed = new List<string>();
        Check(Application.isPlaying && Time.timeScale == 1f && SceneLevelSetup.Current != null
            && SceneLevelSetup.Current.gameObject.scene.name == "TeskScenes2", "在保存的探索关正常游戏时间执行", passed);
        Check(spirit != null && spirit.Identity == PlayerIdentity.Spirit && InputControlMgr.Instance.Player == spirit
            && SceneChangeMgr.Instance.CanEditEnvironment, "当前唯一受控精神体处于可编辑漫游", passed);
        Check(panel != null && panel.gameObject.activeInHierarchy, "真实 GameplayPanel 已显示", passed);
        typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
        EnvironmentEditView view = panel.GetComponentInChildren<EnvironmentEditView>();
        Camera camera = Camera.main;
        Check(view != null && view.isActiveAndEnabled && camera != null, "实际编辑视图与游戏镜头就绪", passed);

        BaseGround ground = null;
        int grounds = 0;
        foreach (BaseGround candidate in Object.FindObjectsOfType<BaseGround>())
        {
            if (candidate.name != "Square" || candidate.gameObject.scene != SceneLevelSetup.Current.gameObject.scene) continue;
            ground = candidate; grounds++;
        }
        Check(grounds == 1 && ground.GetComponents<BasicPhysicalObject>().Length == 1,
            "保存场景 Square 只有一个环境宿主", passed);
        Check(ground.TryGetEnvironmentParameter(0, out EnvironmentParameter parameter)
            && parameter.Kind == EnvironmentParameterKind.GroundSlowing
            && Mathf.Abs(parameter.DefaultValue - 1f) < .0001f && Mathf.Abs(ground.SlowingEffect - 1f) < .0001f,
            "Square 的制作默认与当前消退倍率均为 1", passed);
        Collider2D collider = ground.GetComponent<Collider2D>();
        Check(collider != null && collider.isActiveAndEnabled, "保存地面具有实际活动碰撞范围", passed);
        Canvas.ForceUpdateCanvases(); Physics2D.SyncTransforms();
        CheckFrames(panel, view, ground, passed);
        CaptureScene(view, "environment-available.png");
        Vector2 screen = FindScreenPoint(camera, collider, ground, view);
        Check(screen.x > 0f && screen.y > 0f && screen.x < Screen.width && screen.y < Screen.height,
            "在真实地面可见范围找到无 UI 遮挡的屏幕点", passed);

        var rules = (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
        Check(rules != null && rules.Stage == PlayerGameplayStage.Exploration && !rules.IsSimulation,
            "认知核对使用本关唯一漫游规则", passed);
        CharacterPhysics physics = spirit.GetComponentInChildren<CharacterPhysics>();
        Check(physics != null, "精神体真实物理组件存在", passed);
        Vector3 beforePosition = spirit.transform.position;
        Vector2 beforeVelocity = physics.GetPlanarVelocity();
        float beforeCognition = rules.Cognition.Remaining;
        InputSettings.UpdateMode previousMode = InputSystem.settings.updateMode;
        Mouse previousMouse = Mouse.current;
        object inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo editUpdates = inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        bool previousEditUpdates = (bool)editUpdates.GetValue(inputManager);
        Mouse testMouse = null;
        try
        {
            editUpdates.SetValue(inputManager, true);
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            testMouse = InputSystem.AddDevice<Mouse>("Environment click regression mouse");
            InputSystem.QueueStateEvent(testMouse, new MouseState { position = screen });
            InputSystem.Update();
            InputSystem.QueueStateEvent(testMouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
            InputSystem.Update(); testMouse.MakeCurrent();
            Check(Mouse.current == testMouse && testMouse.leftButton.wasPressedThisFrame,
                "真实输入系统产生当前鼠标左键按下边沿", passed);
            typeof(EnvironmentEditView).GetMethod("Update", Hidden).Invoke(view, null);
            var selected = (BasicPhysicalObject)typeof(EnvironmentEditView).GetField("selected", Hidden).GetValue(view);
            Check(selected == ground && selected.name == "Square", "屏幕点击选中保存场景的 Square", passed);
            Mesh selectedMesh = FrameMesh(panel, view);
            try
            {
                Color32 highlight = (Color)typeof(EnvironmentEditView).GetField("selectedFrameColor", Hidden).GetValue(view);
                Check(Array.Exists(selectedMesh.colors32, color => color.Equals(highlight)),
                    "真实点选后的边框换为高光色", passed);
            }
            finally { Object.DestroyImmediate(selectedMesh); }
            CaptureScene(view, "environment-selected.png");
            Transform editor = view.transform.Find("EnvironmentEditorContent");
            Button slot = editor.Find("EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_0").GetComponent<Button>();
            Check(slot.interactable && view.DisplayedDraft != null, "点选后真实能力槽与编辑草稿可用", passed);
            slot.onClick.Invoke();
            CognitionBackpackItem point = null;
            foreach (CognitionBackpackItem candidatePoint in rules.CognitionBackpack.Items)
                if (candidatePoint.IsCalculation) { point = candidatePoint; break; }
            Check(point != null, "真实关卡认知背包具有制作计算点", passed);
            Button bagPoint = panel.transform.Find("CognitionBackpackArea/CognitionBackpackScroll/Viewport/Content/CognitionBackpackItem_" + point.Id).GetComponent<Button>();
            Check(view.IsBackpackOpen && !view.IsBackpackReadOnly && bagPoint.interactable,
                "点击已有能力槽联动打开侧栏并启用兼容计算点", passed);
            bagPoint.onClick.Invoke();
            Check(Mathf.Abs(ground.SlowingEffect - 1f) < .0001f
                && Mathf.Abs(view.DisplayedDraft.GetSlot(false, 0).Value - point.CalculationPoint.Multiplier) < .0001f,
                "点击背包计算点只在环境槽展示结果，真实来源保持默认", passed);
            Button apply = editor.Find("ConfirmEnvironmentButton").GetComponent<Button>();
            Check(apply.interactable, "计算预览使整个环境面板确认按钮可用", passed);
            CaptureScene(view, "environment-backpack-preview.png");
            apply.onClick.Invoke();
            Check(Mathf.Abs(ground.SlowingEffect - point.CalculationPoint.Multiplier) < .0001f,
                "真实整面板确认按钮提交计算结果到原环境来源", passed);
            Check(ground.CopyEnvironmentSlots(false)[0].CalculationPoint == point.CalculationPoint,
                "确认后环境槽保存所用计算点记录", passed);
            foreach (CognitionBackpackItem remaining in rules.CognitionBackpack.Items)
                Check(remaining.Id != point.Id, "统一确认后所用计算点实际移出背包", passed);
            Check((spirit.transform.position - beforePosition).sqrMagnitude < .00000001f,
                "点选与应用不搬移精神体", passed);
            Check((physics.GetPlanarVelocity() - beforeVelocity).sqrMagnitude < .00000001f,
                "点选与应用不改写精神体运动速度", passed);
            Check(Mathf.Abs(rules.Cognition.Remaining - beforeCognition) < .0001f,
                "点选与应用不扣本关认知", passed);
            CheckReadOnlyBackpack(panel, view, passed);
        }
        finally
        {
            ground.RestoreEnvironmentDefaults();
            if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
            InputSystem.settings.updateMode = previousMode;
            editUpdates.SetValue(inputManager, previousEditUpdates);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            typeof(EnvironmentEditView).GetMethod("ClearSelection", Hidden).Invoke(view, new object[] { "点击环境物体，查看并调整参数。" });
            Directory.CreateDirectory(".utmp/EnvironmentClickValidation");
            File.WriteAllLines(".utmp/EnvironmentClickValidation/checks.txt", passed);
        }
        Check(Mathf.Abs(ground.SlowingEffect - 1f) < .0001f, "回归完成恢复地面制作默认值", passed);
        Check(InputSystem.settings.updateMode == previousMode && (bool)editUpdates.GetValue(inputManager) == previousEditUpdates
            && (previousMouse == null || !previousMouse.added || Mouse.current == previousMouse),
            "回归完成移除测试鼠标并恢复输入设置和原当前设备", passed);
        File.WriteAllLines(".utmp/EnvironmentClickValidation/checks.txt", passed);
        Debug.Log("Environment click Play Mode: " + passed.Count + " checks passed.");
        return passed.Count;
    }

    static void CheckReadOnlyBackpack(GameplayPanel panel, EnvironmentEditView view, List<string> passed)
    {
        Button open = panel.transform.Find("OpenCognitionBackpackButton").GetComponent<Button>();
        open.onClick.Invoke();
        Transform bag = panel.transform.Find("CognitionBackpackArea");
        Check(view.IsBackpackOpen && view.IsBackpackReadOnly && bag.gameObject.activeSelf,
            "主动打开背包显示只读侧栏", passed);
        foreach (Button item in bag.Find("CognitionBackpackScroll/Viewport/Content").GetComponentsInChildren<Button>())
            Check(!item.interactable, "独立背包内容槽不可点击", passed);
        Check(!bag.Find("SplitBackpackCalculationButton").GetComponent<Button>().interactable,
            "独立背包拆点按钮不可点击", passed);
        RectTransform rect = (RectTransform)bag;
        RectTransform environment = (RectTransform)view.transform;
        Check(rect.anchorMin.x == 1f && rect.anchorMax.x == 1f && rect.pivot.x == 1f
            && environment.anchorMin.x == .5f && environment.pivot.y == 0f,
            "实际环境面板位于中下区域，背包锚定右侧", passed);
        bag.Find("CloseCognitionBackpackButton").GetComponent<Button>().onClick.Invoke();
        Check(!view.IsBackpackOpen && !bag.gameObject.activeSelf, "背包真实关闭按钮结束只读查看", passed);
    }

    static void CheckFrames(GameplayPanel panel, EnvironmentEditView view, BaseGround ground, List<string> passed)
    {
        Mesh mesh = FrameMesh(panel, view);
        int baselineVertices = mesh.vertexCount;
        var frames = panel.GetComponentInChildren<EnvironmentSelectionFrames>(true);
        Color32 normal = (Color)typeof(EnvironmentEditView).GetField("availableFrameColor", Hidden).GetValue(view);
        try
        {
            Check(frames != null && frames.isActiveAndEnabled && !frames.raycastTarget && mesh.vertexCount >= 16,
                "漫游真实地面有四边框且不拦截点击", passed);
            Check(Array.TrueForAll(mesh.colors32, color => color.Equals(normal)), "未选中对象以普通色提示可选", passed);
        }
        finally { Object.DestroyImmediate(mesh); }

        var item = new GameObject("P12 temporary selectable object") { hideFlags = HideFlags.DontSave };
        BasicPhysicalObject extra = null;
        try
        {
            item.transform.position = Camera.main.ViewportToWorldPoint(new Vector3(.8f, .7f, 10f));
            item.transform.position = new Vector3(item.transform.position.x, item.transform.position.y, 0f);
            item.AddComponent<BoxCollider2D>().size = Vector2.one;
            extra = item.AddComponent<BasicPhysicalObject>();
            Physics2D.SyncTransforms();
            typeof(EnvironmentEditView).GetField("nextFrameScan", Hidden).SetValue(view, 0f);
            mesh = FrameMesh(panel, view);
            Check(mesh.vertexCount == baselineVertices + 16, "漫游中新出现的另一可编辑物体也有普通框"
                + $" [vertices {mesh.vertexCount}/{baselineVertices + 16}]", passed);
            Check(extra.EnvironmentParameterCount == 0, "纯 BasicPhysicalObject 未配置数值项仍能成为可选环境", passed);
            Vector2 pointer = FindScreenPoint(Camera.main, item.GetComponent<Collider2D>(), extra, view);
            Ray ray = Camera.main.ScreenPointToRay(pointer);
            new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance);
            typeof(EnvironmentEditView).GetMethod("SelectAt", Hidden).Invoke(view, new object[] { (Vector2)ray.GetPoint(distance) });
            Check((BasicPhysicalObject)typeof(EnvironmentEditView).GetField("selected", Hidden).GetValue(view) == extra
                && view.DisplayedDraft != null && view.DisplayedDraft.GetSlot(false, 0) == null
                && view.transform.Find("EnvironmentEditorContent/EnvironmentAbilityScroll/Viewport/Content/EnvironmentAbilitySlot_0").GetComponent<Button>().interactable
                && !view.transform.Find("EnvironmentEditorContent/ConfirmEnvironmentButton").GetComponent<Button>().interactable,
                "无性质的环境仍有固定空槽，但未修改时不启用确认", passed);
            typeof(EnvironmentEditView).GetMethod("ClearSelection", Hidden).Invoke(view, new object[] { "点击环境物体，查看并调整参数。" });
            Vector3[] before = mesh.vertices;
            Object.DestroyImmediate(mesh);
            item.transform.position += Vector3.right * .2f; Physics2D.SyncTransforms();
            mesh = FrameMesh(panel, view);
            Check(mesh.vertexCount == before.Length && Array.Exists(mesh.vertices, vertex => !Array.Exists(before, old => old == vertex)),
                "可编辑物体移动时框线位置及时跟随", passed);
            Object.DestroyImmediate(mesh);
            Rigidbody2D paused = item.AddComponent<Rigidbody2D>();
            paused.bodyType = RigidbodyType2D.Kinematic; paused.simulated = false;
            item.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            Physics2D.SyncTransforms();
            mesh = FrameMesh(panel, view);
            Check(mesh.vertexCount == baselineVertices + 16, "暂停的矩形环境仍有可选框，不依赖物理查询", passed);
            Object.DestroyImmediate(mesh);
            typeof(EnvironmentEditView).GetMethod("SelectAt", Hidden).Invoke(view, new object[] { (Vector2)item.transform.position });
            Check((BasicPhysicalObject)typeof(EnvironmentEditView).GetField("selected", Hidden).GetValue(view) == extra
                && !paused.simulated, "暂停矩形经只读几何可以选中，未打开刚体模拟", passed);
            typeof(EnvironmentEditView).GetMethod("SelectAt", Hidden).Invoke(view,
                new object[] { (Vector2)item.transform.position + Vector2.one * .6f });
            Check((BasicPhysicalObject)typeof(EnvironmentEditView).GetField("selected", Hidden).GetValue(view) != extra,
                "暂停旋转矩形的外包空角不被错误选中", passed);
            extra.enabled = false;
            mesh = FrameMesh(panel, view);
            Check(mesh.vertexCount == baselineVertices, "对象停用后框线立即移除，不等待重新扫描", passed);
            Object.DestroyImmediate(mesh);
            view.enabled = false;
            Check(!frames.gameObject.activeSelf, "编辑显示停用时整屏绘制节点一同隐藏", passed);
            view.enabled = true;
            mesh = FrameMesh(panel, view);
            Check(frames.gameObject.activeSelf && mesh.vertexCount == baselineVertices, "回漫游显示后正常恢复有效对象框线", passed);
            Object.DestroyImmediate(mesh);
            Check(SceneChangeMgr.Instance.ToggleGameMenu(), "框线回归进入真实暂停菜单", passed);
            typeof(EnvironmentEditView).GetMethod("LateUpdate", Hidden).Invoke(view, null);
            Check(!frames.gameObject.activeSelf, "暂停菜单没有遗留可编辑框线", passed);
            Check(SceneChangeMgr.Instance.ToggleGameMenu(), "框线回归关闭真实暂停菜单", passed);
        }
        finally
        {
            if (SceneChangeMgr.Instance.GameMenuOpen) SceneChangeMgr.Instance.ToggleGameMenu();
            view.enabled = true;
            Object.DestroyImmediate(item);
            typeof(EnvironmentEditView).GetField("nextFrameScan", Hidden).SetValue(view, 0f);
        }
        mesh = FrameMesh(panel, view);
        try { Check(mesh.vertexCount == baselineVertices, "临时对象销毁后仅保留保存场景的框线", passed); }
        finally { Object.DestroyImmediate(mesh); }
    }

    static Mesh FrameMesh(GameplayPanel panel, EnvironmentEditView view)
    {
        typeof(EnvironmentEditView).GetMethod("LateUpdate", Hidden).Invoke(view, null);
        Canvas.ForceUpdateCanvases();
        EnvironmentSelectionFrames frames = panel.GetComponentInChildren<EnvironmentSelectionFrames>(true);
        frames.Rebuild(CanvasUpdate.PreRender);
        return Object.Instantiate(frames.canvasRenderer.GetMesh());
    }

    static void CaptureScene(EnvironmentEditView view, string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Camera world = Camera.main;
        var ui = (Camera)typeof(UIMgr).GetField("uiCamera", Hidden).GetValue(UIMgr.Instance);
        RenderTexture oldWorld = world.targetTexture, oldUI = ui.targetTexture, oldActive = RenderTexture.active;
        var render = new RenderTexture(Screen.width, Screen.height, 24);
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        try
        {
            world.targetTexture = ui.targetTexture = render;
            typeof(EnvironmentEditView).GetMethod("LateUpdate", Hidden).Invoke(view, null);
            Canvas.ForceUpdateCanvases();
            world.Render(); ui.Render(); RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); texture.Apply();
            Directory.CreateDirectory(".utmp/P12Selection");
            File.WriteAllBytes(".utmp/P12Selection/" + name, texture.EncodeToPNG());
        }
        finally
        {
            world.targetTexture = oldWorld; ui.targetTexture = oldUI; RenderTexture.active = oldActive;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(render);
        }
    }

    static Vector2 FindScreenPoint(Camera camera, Collider2D collider, BasicPhysicalObject ground, EnvironmentEditView view)
    {
        float[] samples = { .5f, .7f, .3f, .9f, .1f, .6f, .4f, .8f, .2f, .95f, .05f };
        MethodInfo hitsUI = typeof(EnvironmentEditView).GetMethod("HitsUI", Hidden);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        foreach (float y in samples)
            foreach (float x in samples)
            {
                Vector2 screen = camera.ViewportToScreenPoint(new Vector3(x, y, 0f));
                Ray ray = camera.ScreenPointToRay(screen);
                if (!plane.Raycast(ray, out float distance)) continue;
                Vector2 world = ray.GetPoint(distance);
                if (!collider.OverlapPoint(world) || (bool)hitsUI.Invoke(view, new object[] { screen })) continue;
                bool foundGround = false, ambiguous = false;
                foreach (Collider2D hit in Physics2D.OverlapPointAll(world))
                {
                    BasicPhysicalObject[] sources = hit.GetComponents<BasicPhysicalObject>();
                    if (sources.Length != 1 || !sources[0].IsEnvironmentActive
                        || sources[0].gameObject.scene != ground.gameObject.scene) continue;
                    if (sources[0] == ground) foundGround = true;
                    else ambiguous = true;
                }
                if (foundGround && !ambiguous) return screen;
            }
        throw new InvalidOperationException("ENVIRONMENT CLICK FAILED: 保存地面在当前游戏镜头中没有清晰且无 UI 遮挡的可点击点。");
    }

    static void Check(bool condition, string message, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException("ENVIRONMENT CLICK FAILED: " + message);
        passed.Add("PASS " + message);
    }
}
#endif
