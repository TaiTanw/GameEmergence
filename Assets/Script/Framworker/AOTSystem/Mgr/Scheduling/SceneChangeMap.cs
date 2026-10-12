using UnityEngine;

/// <summary>本关地图的界面入口；世界刷新仍交给同一关卡规则。</summary>
public partial class SceneChangeMgr
{
    public LevelMapRecord CurrentMapRecord => levelRules?.MapRecord;
    public bool CanViewMap => !loading && !refreshing && !GameMenuOpen && CurrentMapRecord != null
        && (levelRules.Stage == PlayerGameplayStage.Exploration || levelRules.Stage == PlayerGameplayStage.Escape);
    public bool CanTeleportMap => CanViewMap && !levelRules.EscapeSelectionPending && Time.timeScale > 0f
        && levelRules.Stage == PlayerGameplayStage.Exploration && spirit != null
        && InputControlMgr.Instance.Player == spirit && CurrentMapRecord.IsValid;

    public bool TryTeleport(MapCell cell)
    {
        // 前置校验：未点亮、外关或无效地图不能先迁移角色。
        if (!CanTeleportMap || cell == null || !CurrentMapRecord.IsLit(cell)
            || !CurrentMapRecord.TryLocate(cell.Center, out MapCell found, out _) || found != cell) return false;
        InputControlMgr input = InputControlMgr.Instance;
        bool enabled = input.InputEnabled;
        if (enabled) input.InputOpenOrClose(false);
        // 只迁移角色与镜头；世界和资源由独立重置按钮负责。
        try
        {
            if (!levelRules.TryTeleportMap(cell.Center)) return false;
            input.SnapCameraToPlayer();
            return true;
        }
        finally { if (enabled) input.InputOpenOrClose(true); }
    }
}
