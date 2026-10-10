using UnityEngine;

/// <summary>本关地图的界面入口；世界刷新仍交给同一关卡规则。</summary>
public partial class SceneChangeMgr
{
    public LevelMapRecord CurrentMapRecord => levelRules?.MapRecord;
    public bool CanViewMap => !loading && !GameMenuOpen && CurrentMapRecord != null
        && (levelRules.Stage == PlayerGameplayStage.Exploration || levelRules.Stage == PlayerGameplayStage.Escape);
    public bool CanCancelMapCell => CanViewMap && !EscapeSelectionPending && Time.timeScale > 0f
        && levelRules.Stage == PlayerGameplayStage.Exploration && spirit != null
        && InputControlMgr.Instance.Player == spirit && CurrentMapRecord.IsValid;
    public bool CanTeleportMap => CanResetTrial && CurrentMapRecord != null && CurrentMapRecord.IsValid;

    public bool CancelMapCell(MapCell cell)
    {
        // 安全：取消前核对整份地图与选中格身份，逃生及待选择期间只读。
        if (!CanCancelMapCell || cell == null || !CurrentMapRecord.IsLit(cell)
            || !CurrentMapRecord.TryLocate(cell.Center, out MapCell found, out _) || found != cell) return false;
        return CurrentMapRecord.TryCancel(cell);
    }

    public bool TryTeleport(MapCell cell)
    {
        // 前置校验：未点亮、已取消、外关或无效地图不能先恢复世界的一部分。
        if (!CanTeleportMap || cell == null || !CurrentMapRecord.IsLit(cell)
            || !CurrentMapRecord.TryLocate(cell.Center, out MapCell found, out _) || found != cell) return false;
        InputControlMgr input = InputControlMgr.Instance;
        bool enabled = input.InputEnabled;
        if (enabled) input.InputOpenOrClose(false);
        // 统一轻刷新：全关箱子、角色资源和安置仍只执行一次，地图状态由已有通知保持。
        try { return levelRules.TryResetTrial(cell.Center); }
        finally { if (enabled) input.InputOpenOrClose(true); }
    }
}
