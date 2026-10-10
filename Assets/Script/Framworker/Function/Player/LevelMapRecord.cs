using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>本关地图点亮与认知预留；配置来源仍是格子实例，记录不写回场景或预设资产。</summary>
public sealed class LevelMapRecord
{
    readonly List<MapCell> cells = new List<MapCell>();
    readonly Dictionary<MapCell, float> reserved = new Dictionary<MapCell, float>();
    readonly LevelCognition cognition;
    readonly Scene scene;
    MapCell blockedUntilExit;
    public IReadOnlyList<MapCell> Cells => cells;
    public MapCell CurrentCell { get; private set; }
    public string ConfigurationError { get; private set; }
    public bool IsValid => ConfigurationError == null;

    public LevelMapRecord(IReadOnlyList<MapCell> configuredCells, LevelCognition cognition, Scene scene)
    {
        this.cognition = cognition ?? throw new ArgumentNullException(nameof(cognition));
        this.scene = scene;
        if (configuredCells != null) for (int i = 0; i < configuredCells.Count; i++) cells.Add(configuredCells[i]);
        ConfigurationError = ValidateConfiguration();
    }

    string ValidateConfiguration()
    {
        if (!scene.IsValid() || cells.Count == 0) return "地图已启用，但没有配置本关地图格子。";
        var unique = new HashSet<MapCell>();
        foreach (MapCell cell in cells)
        {
            if (cell == null) return "地图格子列表有未配置的引用。";
            if (!unique.Add(cell)) return $"地图格子 {cell.name} 被重复引用。";
        }
        string error = ValidateCells();
        if (error != null) return error;
        // 制作关系：本关场景中的每个格子都必须由性质配置显式引用。
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MapCell cell in root.GetComponentsInChildren<MapCell>(true))
                if (!unique.Contains(cell)) return $"地图格子 {cell.name} 未关联到本关配置。";
        return null;
    }

    string ValidateCells()
    {
        foreach (MapCell configured in cells)
        {
            if (configured == null) return "本关地图格子引用失效，请核对关卡配置。";
            if (configured.gameObject.scene != scene) return $"地图格子 {configured.name} 不属于本关场景。";
            if (!configured.ValidateConfiguration(out string error)) return error;
        }
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
            {
                Rect a = cells[i].WorldRect, b = cells[j].WorldRect;
                if (a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin)
                    return $"地图格子 {cells[i].name} 与 {cells[j].name} 存在面积重叠。";
            }
        return null;
    }

    public bool TryLocate(Vector2 point, out MapCell cell, out string error)
    {
        cell = null;
        error = ConfigurationError;
        if (!IsValid) return false;
        // 生命周期：查询前核对全列表；配置或引用失效时不能跳过坏格继续使用部分地图。
        string validation = ValidateCells();
        if (validation != null)
        {
            ConfigurationError = error = validation;
            return false;
        }
        if (!Finite(point))
        {
            error = "玩家碰撞框中心无效，不能确定当前地图格子。";
            return false;
        }
        foreach (MapCell configured in cells)
        {
            Rect rect = configured.WorldRect;
            // 所有边界统一最小边含、最大边不含；外侧最大边也属于范围外。
            if (point.x >= rect.xMin && point.x < rect.xMax && point.y >= rect.yMin && point.y < rect.yMax)
            {
                cell = configured;
                return true;
            }
        }
        error = "玩家当前不在地图格子范围内。";
        return false;
    }

    /// <summary>试验重置的落点：人在缝里用保留的当前格；还没有当前格或配置无效时拒绝。</summary>
    public bool TryGetResetCenter(Vector2 point, out Vector2 center, out string error)
    {
        center = default;
        bool located = TryLocate(point, out MapCell cell, out error);
        // 安全：重叠、漏引用或失效格子不能改用旧格中心掩盖配置错误。
        if (!IsValid)
        {
            error = ConfigurationError;
            return false;
        }
        if (located)
        {
            center = cell.Center;
            error = null;
            return true;
        }
        // 实际逻辑：已经进入过格子后，缝隙和最外侧不含边界仍重置到保留格中心。
        if (CurrentCell != null)
        {
            center = CurrentCell.Center;
            error = null;
            return true;
        }
        error = "玩家尚未进入任何地图格子，不能重置。";
        return false;
    }

    public bool IsLit(MapCell cell) => cell != null && reserved.ContainsKey(cell);
    public float ReservedCognition(MapCell cell) => cell != null && reserved.TryGetValue(cell, out float value) ? value : 0f;

    /// <summary>只由正常探索移动采样调用；同格内移动、重复经过与暂停不重复占用。</summary>
    public bool SampleNormalMovement(Vector2 point)
    {
        if (!Finite(point)) return false;
        bool located = TryLocate(point, out MapCell next, out _);
        if (!IsValid) return false;
        // 实际逻辑：中心不落在任何格内时保留上一格，缝隙和最外侧不含边界都不算离格。
        if (!located) return false;
        if (blockedUntilExit != null && next != blockedUntilExit) blockedUntilExit = null;
        if (next == CurrentCell) return false;
        CurrentCell = next;
        if (next == null || next == blockedUntilExit || IsLit(next)) return false;
        float paid = cognition.OccupyMapCost(next.CognitionCost);
        // 实际逻辑：余额只够一部分时仍点亮并只记实付；余额已是零时，正费用新格不记成点亮。
        if (paid == 0f && next.CognitionCost != 0f) return false;
        reserved.Add(next, paid);
        return true;
    }

    /// <summary>显式安置只同步当前位置，不点亮；落在格子外则保留上一格。</summary>
    public void OnRelocated(Vector2 point)
    {
        if (!Finite(point)) return;
        // 实际逻辑：安置点不在任何格内时保留上一格，不把这次落点写成离格。
        if (!TryLocate(point, out MapCell next, out _)) return;
        CurrentCell = next;
        if (blockedUntilExit != null && next != blockedUntilExit) blockedUntilExit = null;
    }

    public bool TryCancel(MapCell cell)
    {
        // 安全：取消只能释放本关已经登记的实际预留，重复请求不退款。
        if (!IsValid || cell == null || !reserved.TryGetValue(cell, out float value)) return false;
        reserved.Remove(cell);
        cognition.ReleaseMapCost(value);
        if (cell == CurrentCell) blockedUntilExit = cell;
        return true;
    }

    /// <summary>清地图记录并释放仍占用的容量；新局的初始余额由规则重新创建，时间与转换费用不在这里退还。</summary>
    public void Reset()
    {
        foreach (float value in reserved.Values) cognition.ReleaseMapCost(value);
        reserved.Clear();
        CurrentCell = blockedUntilExit = null;
    }

    static bool Finite(Vector2 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
        && !float.IsNaN(point.y) && !float.IsInfinity(point.y);
}
