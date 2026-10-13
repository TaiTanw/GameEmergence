using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>P16.2–P16.3 配置与二维边界检查；只构造内存配置，不覆盖制作文件。</summary>
public static class EndingConfigurationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Run()
    {
        int count = 0;
        Action<bool, string> check = (value, label) =>
        { if (!value) throw new InvalidOperationException(label); count++; };
        LevelOrderFile old = JsonUtility.FromJson<LevelOrderFile>("{\"scenes\":[\"A\",\"B\"]}");
        check(old.scenes.Length == 2 && old.ValidateEndings(out _), "旧线性顺序仍能读取");
        check(!old.TrySelectEnding(Vector3.zero, out _, out string empty) && !string.IsNullOrEmpty(empty), "空结局栏拒绝查询");

        var left = new EndingSceneEntry { scene = "Left", x = Range(EndingBoundKind.Unbounded, 0, EndingBoundKind.Open, 0) };
        var right = new EndingSceneEntry { scene = "Right", x = Range(EndingBoundKind.Closed, 0, EndingBoundKind.Unbounded, 0) };
        var order = new LevelOrderFile { scenes = new[] { "A" }, endings = new[] { left, right } };
        check(order.CheckEndingIntersections(out _), "半开边界相接不算交集");
        check(order.TrySelectEnding(new Vector3(-1, 99, 8), out string selected, out _) && selected == "Left", "负 X 命中左区间");
        check(order.TrySelectEnding(new Vector3(0, -99, 17), out selected, out _) && selected == "Right", "闭端点命中右区间且 Z 不参与选择");
        left.x.upperKind = EndingBoundKind.Closed;
        check(!order.CheckEndingIntersections(out string conflict) && conflict.Contains("第 1 行") && conflict.Contains("第 2 行"), "双闭端点交集指出冲突行");
        check(!order.TrySelectEnding(Vector3.zero, out selected, out string ambiguous) && selected == null && ambiguous.Contains("同时命中"), "多个命中拒绝且不给出目标");
        right.y = Range(EndingBoundKind.Open, 1, EndingBoundKind.Unbounded, 0);
        left.y = Range(EndingBoundKind.Unbounded, 0, EndingBoundKind.Closed, 1);
        check(order.CheckEndingIntersections(out _), "X 重叠但 Y 分离时二维没有交集");
        check(order.TrySelectEnding(Vector3.zero, out selected, out _) && selected == "Left", "两个维度必须同时命中");
        left.x.upperKind = EndingBoundKind.Open;
        check(!order.TrySelectEnding(Vector3.zero, out _, out string unmatched) && unmatched.Contains("没有结局"), "未覆盖向量明确拒绝");
        check(!order.TrySelectEnding(new Vector3(float.NaN, 0, 0), out _, out _), "无界条件仍拒绝非法总向量");
        check(Range(EndingBoundKind.Closed, 2, EndingBoundKind.Closed, 2).Validate(out _), "双闭相同端点允许单点集合");
        check(!Range(EndingBoundKind.Open, 2, EndingBoundKind.Closed, 2).Validate(out _), "空区间拒绝");
        check(!Range(EndingBoundKind.Closed, 3, EndingBoundKind.Closed, 2).Validate(out _), "反向区间拒绝");
        check(!Range(EndingBoundKind.Unbounded, float.PositiveInfinity, EndingBoundKind.Unbounded, 0).Validate(out _), "正负无穷通过选项表达，不能写入浮点无穷");
        check(!Range((EndingBoundKind)99, 0, EndingBoundKind.Unbounded, 0).Validate(out _), "未知边界类型拒绝");
        right.scene = "Left";
        check(!order.ValidateEndings(out _), "重复结局场景拒绝");
        right.scene = "A";
        check(!order.ValidateEndings(out _), "结局与线性场景重复拒绝");
        right.scene = "Right";
        right.x = null;
        check(!order.ValidateEndings(out _), "缺失区间不能解释为无界");
        right.x = Range(EndingBoundKind.Closed, 0, EndingBoundKind.Unbounded, 0);
        LevelOrderFile restored = JsonUtility.FromJson<LevelOrderFile>(JsonUtility.ToJson(order));
        check(restored.endings.Length == 2 && restored.endings[0].x.upperKind == EndingBoundKind.Open
            && restored.endings[1].y.lower == 1 && restored.endings[1].y.lowerKind == EndingBoundKind.Open,
            "场景与两维端点类型数值序列化往返");

        CheckAllIntersections(check);
        CheckEditorWindow(check);
        Debug.Log($"Ending configuration: {count} checks passed.");
        return count;
    }

    static void CheckAllIntersections(Action<bool, string> check)
    {
        var ranges = new List<EndingAxisRange>();
        foreach (EndingBoundKind lower in Enum.GetValues(typeof(EndingBoundKind)))
            foreach (EndingBoundKind upper in Enum.GetValues(typeof(EndingBoundKind)))
                for (int a = -1; a <= 0; a++)
                    for (int b = 0; b <= 1; b++)
                    {
                        var range = Range(lower, a, upper, b);
                        if (range.Validate(out _)) ranges.Add(range);
                    }
        // 端点、端点之间、两侧各一点足以覆盖这些分段常量的成员关系。
        float[] probes = { -2, -1, -.5f, 0, .5f, 1, 2 };
        foreach (EndingAxisRange a in ranges)
            foreach (EndingAxisRange b in ranges)
            {
                bool common = false;
                foreach (float value in probes) common |= a.Contains(value) && b.Contains(value);
                var order = new LevelOrderFile { endings = new[] {
                    new EndingSceneEntry { scene = "A", x = a }, new EndingSceneEntry { scene = "B", x = b } } };
                if (order.CheckEndingIntersections(out _) == common)
                    throw new InvalidOperationException($"交集与独立采样不一致：{a} / {b}");
            }
        check(true, "全部开闭无界组合的交集检查与独立成员采样一致");
    }

    static void CheckEditorWindow(Action<bool, string> check)
    {
        var window = ScriptableObject.CreateInstance<LevelOrderWindow>();
        try
        {
            var scenes = (List<SceneAsset>)typeof(LevelOrderWindow).GetField("scenes", Hidden).GetValue(window);
            var endings = (IList)typeof(LevelOrderWindow).GetField("endings", Hidden).GetValue(window);
            scenes.Clear(); endings.Clear();
            SceneAsset linear = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/MyObjAssets/Scenes/TeskScenes1.unity");
            SceneAsset ending = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/MyObjAssets/Scenes/TeskScenes3.unity");
            scenes.Add(linear);
            Type rowType = typeof(LevelOrderWindow).GetNestedType("EndingRow", BindingFlags.NonPublic);
            object row = Activator.CreateInstance(rowType);
            rowType.GetField("scene").SetValue(row, ending);
            rowType.GetField("x").SetValue(row, Range(EndingBoundKind.Closed, -2, EndingBoundKind.Open, 5));
            endings.Add(row);
            object[] args = { null, null };
            bool valid = (bool)typeof(LevelOrderWindow).GetMethod("TryBuildOrder", Hidden).Invoke(window, args);
            var file = (LevelOrderFile)args[0];
            check(valid && file.scenes.Length == 1 && file.endings.Length == 1
                && file.endings[0].x.lower == -2 && file.endings[0].x.upperKind == EndingBoundKind.Open,
                "关卡窗口把结局条件写入独立栏且不占线性序号");
            rowType.GetField("scene").SetValue(row, linear);
            check(!(bool)typeof(LevelOrderWindow).GetMethod("TryBuildOrder", Hidden).Invoke(window, args), "窗口生成前拒绝结局与线性场景重复");
            rowType.GetField("scene").SetValue(row, null);
            check(!(bool)typeof(LevelOrderWindow).GetMethod("TryBuildOrder", Hidden).Invoke(window, args), "窗口生成前拒绝空结局槽");
        }
        finally { Object.DestroyImmediate(window); }
    }

    static EndingAxisRange Range(EndingBoundKind lowerKind, float lower, EndingBoundKind upperKind, float upper)
        => new EndingAxisRange { lowerKind = lowerKind, lower = lower, upperKind = upperKind, upper = upper };
}
