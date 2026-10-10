#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>P8.3 手工格子与单余额占用回归；使用临时场景，不保存关卡或预设。</summary>
public static class P8MapChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Scene> scenes = new List<Scene>();
    static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/P8.3 验证手工地图格子（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行地图检查。");
        passed.Clear();
        try
        {
            CheckBoundaries();
            CheckInvalidConfiguration();
            CheckOccupancyAndSpending();
            CheckCancelledEntry();
            CheckRelocation();
            CheckConfigurationLifetime();
            CheckRuleBinding();
            Debug.Log("P8.3 map: " + passed.Count + " checks passed.");
        }
        finally
        {
            for (int i = scenes.Count - 1; i >= 0; i--)
                if (scenes[i].IsValid()) EditorSceneManager.ClosePreviewScene(scenes[i]);
            scenes.Clear();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/P8Validation"));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "map-checks.txt"), passed);
        }
    }

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void CheckBoundaries()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "negative left", new Vector2(-5f, 0f), 10f, 6f, 2f);
        MapCell b = Cell(scene, "right", new Vector2(5f, 0f), 10f, 6f, 2f);
        var record = new LevelMapRecord(new[] { b, a }, new LevelCognition(10f), scene);
        Check(record.IsValid, "不同手工格子允许边界相接，引用顺序不决定归属");
        Check(record.TryLocate(new Vector2(-10f, -3f), out MapCell found, out _) && found == a,
            "负坐标的最小 X/Y 边均包含");
        Check(record.TryLocate(Vector2.zero, out found, out _) && found == b, "共享边界唯一归属最小边包含的一侧");
        Check(!record.TryLocate(new Vector2(10f, 0f), out _, out _)
            && !record.TryLocate(new Vector2(0f, 3f), out _, out _), "地图最外侧最大 X/Y 边统一属于范围外");
        Check(a.GetComponents<Component>().Length == 2 && a.GetComponent<Collider2D>() == null,
            "格子只有 Transform 与配置组件，不参与物理触发");
    }

    static void CheckInvalidConfiguration()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "first", Vector2.zero, 4f, 4f, 2f);
        MapCell b = Cell(scene, "second", new Vector2(3f, 0f), 4f, 4f, 2f);
        var cognition = new LevelCognition(10f);
        var overlap = new LevelMapRecord(new[] { a, b }, cognition, scene);
        Check(!overlap.IsValid && !overlap.TryLocate(a.Center, out _, out _) && cognition.Remaining == 10f,
            "面积重叠拒绝全列表及查询，不先选第一个格子或扣费");
        var duplicate = new LevelMapRecord(new[] { a, a }, cognition, scene);
        Check(!duplicate.IsValid && duplicate.ConfigurationError.Contains(a.name), "重复引用报出格子名称");
        b.transform.position = new Vector2(4f, 0f);
        var missing = new LevelMapRecord(new[] { a }, cognition, scene);
        Check(!missing.IsValid && missing.ConfigurationError.Contains(b.name), "场景有格子但未关联时拒绝并指出漏引用对象");
        var empty = new LevelMapRecord(new MapCell[0], cognition, scene);
        var unset = new LevelMapRecord(new MapCell[] { null }, cognition, scene);
        Check(!empty.IsValid && !unset.IsValid, "启用地图时空列表或空引用均是配置问题");
        Set(a, "width", 0f);
        Check(!new LevelMapRecord(new[] { a, b }, cognition, scene).IsValid, "零尺寸拒绝整份地图");
        Set(a, "width", 4f); Set(a, "cognitionCost", float.NaN);
        Check(!new LevelMapRecord(new[] { a, b }, cognition, scene).IsValid, "非数值占用配置拒绝整份地图");
        Scene foreignScene = NewScene();
        MapCell foreign = Cell(foreignScene, "foreign", Vector2.zero, 4f, 4f, 1f);
        Check(!new LevelMapRecord(new[] { foreign }, cognition, scene).IsValid, "其它场景格子不可关联为本关配置");
    }

    static void CheckOccupancyAndSpending()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "reserve A", Vector2.zero, 4f, 4f, 6f);
        MapCell b = Cell(scene, "reserve B", new Vector2(4f, 0f), 4f, 4f, 9f);
        var cognition = new LevelCognition(10f);
        var record = new LevelMapRecord(new[] { a, b }, cognition, scene);
        Check(record.SampleNormalMovement(a.Center) && record.IsLit(a), "首次正常进入点亮并登记本关格子实例");
        Near(cognition.Remaining, 4f, "点亮从同一认知余额扣占用");
        Check(!record.SampleNormalMovement(new Vector2(.5f, .5f)), "同格内移动不重复点亮或占用");
        Check(cognition.TryPayAbilityCost(1f), "合法且足够的能力费用可支付");
        Check(!cognition.TryPayAbilityCost(4f) && !cognition.TryPayAbilityCost(-1f)
            && !cognition.TryPayAbilityCost(float.PositiveInfinity), "能力预算不足或费用无效时拒绝，不部分付款");
        Near(cognition.Remaining, 3f, "失败能力付款保持已有余额");
        Check(record.SampleNormalMovement(b.Center) && cognition.Remaining == 0f, "配置占用大于余额时点亮并钳到零");
        Near(record.ReservedCognition(b), 3f, "格子记录实际支付，不登记配置全额");
        Check(record.TryCancel(b) && !record.TryCancel(b), "取消一次释放一次，重复取消不退款");
        Near(cognition.Remaining, 3f, "取消大费用格只退当次实际预留");
        typeof(LevelCognition).GetMethod("PayFormCost", Hidden).Invoke(cognition, new object[] { 2f });
        var rules = new PlayerLifecycleRules(null, null, cognition: cognition, simulationCognitionPerSecond: 1f);
        rules.ConfigureStage(PlayerGameplayStage.Exploration, true); rules.AdvanceSimulationTime(.5f);
        Check(record.TryCancel(a), "另一格取消仍释放其实际预留");
        Near(cognition.Remaining, 6.5f, "释放上界扣除能力、转换与时间消耗，不把费用一起退还");
        Check(record.SampleNormalMovement(a.Center), "离开后再次正常进入可重新点亮");
        Near(cognition.Remaining, .5f, "重新点亮按本次余额预留");
        record.Reset();
        Check(!record.IsLit(a) && !record.IsLit(b) && record.CurrentCell == null, "清地图记录移除点亮与当前位置");
        Near(cognition.Remaining, 6.5f, "清地图只释放容量，新局初始余额由规则另建");

        Scene emptyScene = NewScene();
        MapCell broke = Cell(emptyScene, "broke", Vector2.zero, 4f, 4f, 2f);
        MapCell free = Cell(emptyScene, "free", new Vector2(4f, 0f), 4f, 4f, 0f);
        var empty = new LevelCognition(0f);
        var brokeRecord = new LevelMapRecord(new[] { broke, free }, empty, emptyScene);
        Check(!brokeRecord.SampleNormalMovement(broke.Center) && !brokeRecord.IsLit(broke) && empty.Remaining == 0f,
            "余额已是零时，正费用新格不点亮");
        Check(!brokeRecord.SampleNormalMovement(broke.Center) && !brokeRecord.IsLit(broke),
            "停在未付款的格子里不会事后补亮");
        Check(brokeRecord.SampleNormalMovement(free.Center) && brokeRecord.IsLit(free) && empty.Remaining == 0f,
            "零费用格子在余额为零时仍可点亮");
    }

    static void CheckCancelledEntry()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "cancelled current", Vector2.zero, 4f, 4f, 2f);
        MapCell b = Cell(scene, "other cell", new Vector2(8f, 0f), 4f, 4f, 2f);
        var cognition = new LevelCognition(8f);
        var record = new LevelMapRecord(new[] { a, b }, cognition, scene);
        record.SampleNormalMovement(a.Center); record.TryCancel(a);
        Check(!record.SampleNormalMovement(new Vector2(1f, 1f)) && !record.IsLit(a), "取消当前格后在格内移动不补亮");
        record.OnRelocated(a.Center);
        Check(!record.SampleNormalMovement(a.Center) && !record.IsLit(a), "重置回取消格中心不补亮或重新占用");
        Check(!record.SampleNormalMovement(new Vector2(4f, 0f)) && record.CurrentCell == a && !record.IsLit(a),
            "走到格子缝仍保留取消前的当前格");
        Check(record.TryGetResetCenter(new Vector2(4f, 0f), out Vector2 reset, out _) && reset == a.Center,
            "缝里重置使用保留格中心");
        Check(!record.SampleNormalMovement(a.Center) && !record.IsLit(a), "从缝走回原格不算离格，不补亮");
        Check(record.SampleNormalMovement(b.Center) && record.IsLit(b) && !record.IsLit(a), "进入另一格才算离开");
        Check(record.SampleNormalMovement(a.Center) && record.IsLit(a), "再进入原格才重新点亮");
        Near(cognition.Remaining, 4f, "离开后再进入原格，两格各预留一次");
    }

    static void CheckRelocation()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "relocation A", Vector2.zero, 4f, 4f, 2f);
        MapCell b = Cell(scene, "relocation B", new Vector2(4f, 0f), 4f, 4f, 2f);
        var cognition = new LevelCognition(8f);
        var record = new LevelMapRecord(new[] { a, b }, cognition, scene);
        record.OnRelocated(a.Center);
        Check(!record.SampleNormalMovement(new Vector2(1f, 0f)) && !record.IsLit(a), "显式安置只同步当前格，后续格内移动不自动点亮");
        Check(record.SampleNormalMovement(b.Center) && record.IsLit(b), "安置后正常进入另一格仍可点亮");
        record.OnRelocated(a.Center);
        Check(!record.IsLit(a) && !record.SampleNormalMovement(a.Center), "显式搬到未亮目标不新占用认知");
        Near(cognition.Remaining, 6f, "显式安置保持余额与已亮记录");
        record.OnRelocated(new Vector2(float.NaN, 0f));
        Check(record.CurrentCell == a, "无效安置点不伪造离格事实");
        record.OnRelocated(new Vector2(30f, 30f));
        Check(record.CurrentCell == a, "安置到所有格子之外仍保留上一格");
        var fresh = new LevelMapRecord(new[] { a, b }, new LevelCognition(8f), scene);
        Check(!fresh.TryGetResetCenter(new Vector2(30f, 30f), out _, out string error)
            && error.Contains("尚未进入"), "还没有当前格时，人在格子外不能重置");
    }

    static void CheckConfigurationLifetime()
    {
        Scene scene = NewScene();
        MapCell a = Cell(scene, "lifetime A", Vector2.zero, 4f, 4f, 2f);
        MapCell b = Cell(scene, "lifetime B", new Vector2(4f, 0f), 4f, 4f, 2f);
        var record = new LevelMapRecord(new[] { a, b }, new LevelCognition(8f), scene);
        Check(record.SampleNormalMovement(a.Center) && record.CurrentCell == a, "重叠发生前已有当前格");
        b.transform.position = a.Center;
        Check(!record.TryGetResetCenter(a.Center, out _, out _) && !record.IsValid,
            "已有当前格后发生重叠，重置仍拒绝整份地图");
        b.transform.position = new Vector2(4f, 0f);
        var destroyed = new LevelMapRecord(new[] { a, b }, new LevelCognition(8f), scene);
        Object.DestroyImmediate(b.gameObject);
        Check(!destroyed.TryLocate(a.Center, out _, out _) && !destroyed.IsValid, "任何格子引用中途失效都拒绝整份地图");
    }

    static void CheckRuleBinding()
    {
        Scene scene = NewScene();
        MapCell cell = Cell(scene, "binding cell", Vector2.zero, 4f, 4f, 2f);
        var item = new GameObject("temporary level setup") { hideFlags = HideFlags.DontSave }; item.SetActive(false);
        SceneManager.MoveGameObjectToScene(item, scene);
        SceneLevelSetup setup = item.AddComponent<SceneLevelSetup>();
        Set(setup, "mapCells", new[] { cell });
        var rules = new PlayerLifecycleRules(null, null);
        Check(rules.BindExplorationMap(setup) && rules.MapRecord == null, "默认未启用地图保留旧场景固定试验起点");
        Check(rules.TryGetTrialResetPoint(new Vector2(7f, 8f), out Vector2 point, out _) && point == new Vector2(7f, 8f),
            "地图未启用时明确使用传入的固定试验起点");
        Set(setup, "mapEnabled", true); Set(setup, "teaching", true);
        Check(rules.BindExplorationMap(setup) && rules.MapRecord == null, "教学即使配置了地图也不创建局内格子记录");
        Set(setup, "teaching", false);
        Check(rules.BindExplorationMap(setup) && rules.MapRecord != null && rules.MapRecord.IsValid,
            "非教学启用后性质配置绑定本关共享余额与手工格子");
    }

    static Scene NewScene()
    {
        // 预览场景无需保存默认 untitled 场景，也不替换用户正在编辑的关卡。
        Scene scene = EditorSceneManager.NewPreviewScene();
        scenes.Add(scene); return scene;
    }

    static MapCell Cell(Scene scene, string name, Vector2 center, float width, float height, float cost)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(item, scene); item.transform.position = center;
        MapCell cell = item.AddComponent<MapCell>();
        Set(cell, "width", width); Set(cell, "height", height); Set(cell, "cognitionCost", cost);
        return cell;
    }

    static void Set(object item, string field, object value) => item.GetType().GetField(field, Hidden).SetValue(item, value);
    static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .001f, message);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P8.3 FAILED: " + message);
        passed.Add(message);
    }
}
#endif
