using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>编辑器生成的线性关序与二维结局条件；结局不占线性序号。</summary>
[Serializable]
public class LevelOrderFile
{
    public string[] scenes = new string[0];
    public EndingSceneEntry[] endings = new EndingSceneEntry[0];

    public bool ValidateEndings(out string error)
    {
        error = null;
        // 旧顺序文件没有结局栏时仍能加载普通关。
        if (endings == null) return true;
        var names = new HashSet<string>();
        var linearNames = new HashSet<string>(scenes ?? new string[0]);
        for (int i = 0; i < endings.Length; i++)
        {
            EndingSceneEntry entry = endings[i];
            // 安全：缺失的行或范围不能解释成全域条件。
            if (entry == null || entry.x == null || entry.y == null || string.IsNullOrWhiteSpace(entry.scene))
            { error = $"结局第 {i + 1} 行缺少场景或区间。"; return false; }
            if (!names.Add(entry.scene) || linearNames.Contains(entry.scene))
            { error = $"结局场景重复或也在线性关序中：{entry.scene}。"; return false; }
            if (!entry.x.Validate(out string xError))
            { error = $"结局 {entry.scene} 的 X 区间：{xError}"; return false; }
            if (!entry.y.Validate(out string yError))
            { error = $"结局 {entry.scene} 的 Y 区间：{yError}"; return false; }
        }
        return true;
    }

    /// <summary>只查询结果，不领取道具、不写进度、不加载结局；Z 预留而不参与判定。</summary>
    public bool TrySelectEnding(Vector3 total, out string scene, out string error)
    {
        scene = null;
        if (!ValidateEndings(out error)) return false;
        // 安全：非法累计不能通过无界区间绕过数值校验。
        if (!EndingAxisRange.Finite(total.x) || !EndingAxisRange.Finite(total.y) || !EndingAxisRange.Finite(total.z))
        { error = "结局向量必须是有限数值。"; return false; }
        if (endings != null)
            foreach (EndingSceneEntry entry in endings)
            {
                if (!entry.x.Contains(total.x) || !entry.y.Contains(total.y)) continue;
                // 唯一命中是运行时底线；不做结局区间之间的两两比较。
                if (scene != null)
                { error = $"结局条件同时命中 {scene} 与 {entry.scene}，请在关卡顺序窗口检查区间交集。"; scene = null; return false; }
                scene = entry.scene;
            }
        if (scene == null) { error = $"没有结局条件匹配当前向量 ({total.x}, {total.y})。"; return false; }
        return true;
    }

#if UNITY_EDITOR
    /// <summary>编辑按钮使用的两两交集检查，构建中不包含此检测。</summary>
    public bool CheckEndingIntersections(out string report)
    {
        if (!ValidateEndings(out report)) return false;
        var conflicts = new List<string>();
        if (endings != null)
            for (int i = 0; i < endings.Length; i++)
                for (int j = i + 1; j < endings.Length; j++)
                    if (endings[i].x.Intersects(endings[j].x) && endings[i].y.Intersects(endings[j].y))
                        conflicts.Add($"第 {i + 1} 行 {endings[i].scene} 与第 {j + 1} 行 {endings[j].scene}");
        report = conflicts.Count == 0 ? "结局触发区间没有交集。此检查不保证所有向量都有对应结局。"
            : "结局触发区间有交集：\n" + string.Join("\n", conflicts);
        return conflicts.Count == 0;
    }
#endif
}

public enum EndingBoundKind { Unbounded, Open, Closed }

[Serializable]
public sealed class EndingAxisRange
{
    public EndingBoundKind lowerKind;
    public float lower;
    public EndingBoundKind upperKind;
    public float upper;

    public bool Validate(out string error)
    {
        error = null;
        if (lowerKind < EndingBoundKind.Unbounded || lowerKind > EndingBoundKind.Closed
            || upperKind < EndingBoundKind.Unbounded || upperKind > EndingBoundKind.Closed || !Finite(lower) || !Finite(upper))
        { error = "边界类型或数值无效；无界请使用无界选项。"; return false; }
        // 两侧有界才比较端点；同一点只有双闭端点构成非空集合。
        if (lowerKind != EndingBoundKind.Unbounded && upperKind != EndingBoundKind.Unbounded
            && (lower > upper || (lower == upper && (lowerKind != EndingBoundKind.Closed || upperKind != EndingBoundKind.Closed))))
        { error = "下界必须小于上界；相等时两端必须闭合。"; return false; }
        return true;
    }

    public bool Contains(float value)
    {
        if (!Finite(value)) return false;
        bool above = lowerKind == EndingBoundKind.Unbounded || value > lower || (value == lower && lowerKind == EndingBoundKind.Closed);
        bool below = upperKind == EndingBoundKind.Unbounded || value < upper || (value == upper && upperKind == EndingBoundKind.Closed);
        return above && below;
    }

#if UNITY_EDITOR
    internal bool Intersects(EndingAxisRange other)
    {
        if (upperKind != EndingBoundKind.Unbounded && other.lowerKind != EndingBoundKind.Unbounded
            && (upper < other.lower || (upper == other.lower && (upperKind != EndingBoundKind.Closed || other.lowerKind != EndingBoundKind.Closed)))) return false;
        if (other.upperKind != EndingBoundKind.Unbounded && lowerKind != EndingBoundKind.Unbounded
            && (other.upper < lower || (other.upper == lower && (other.upperKind != EndingBoundKind.Closed || lowerKind != EndingBoundKind.Closed)))) return false;
        return true;
    }
#endif

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public override string ToString() => (lowerKind == EndingBoundKind.Closed ? "[" : "(")
        + (lowerKind == EndingBoundKind.Unbounded ? "−∞" : lower.ToString("G")) + ", "
        + (upperKind == EndingBoundKind.Unbounded ? "+∞" : upper.ToString("G"))
        + (upperKind == EndingBoundKind.Closed ? "]" : ")");
}

[Serializable]
public sealed class EndingSceneEntry
{
    public string scene;
    public EndingAxisRange x = new EndingAxisRange();
    public EndingAxisRange y = new EndingAxisRange();
}
