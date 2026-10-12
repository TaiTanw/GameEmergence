using System.Collections;
using UnityEngine;

public partial class SceneChangeMgr
{
    bool escapeHandoff;
    public bool IsEscapeHandoff => escapeHandoff;

    bool ValidateEscapeHandoff(SceneLevelSetup setup)
    {
        // 安全：缺少角色、有效安置点或镜头时拒绝整次交接，不自动搜索替代对象。
        if (setup == null || body == null || spirit == null || !Finite(setup.BodyPoint)
            || !Finite(setup.BodyPose.z) || !InputControlMgr.Instance.HasTransitionCamera)
        { Debug.LogError("探索退场缺少角色、有效实体出生点或游戏镜头。", setup); return false; }
        string error = null;
        if (spirit.Presentation != null && spirit.Presentation.ValidateDeathPlayback(out error)) return true;
        Debug.LogError(spirit.Presentation == null ? "精神体缺少共用动画表现组件。" : error, spirit);
        return false;
    }

    /// <summary>全部准备确认后的唯一入口；当前购买／低预算路径调用，后续新面板复用。</summary>
    public bool CompleteEscapePreparation(bool preserveScene = false)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (loading || refreshing || GameMenuOpen || escapeHandoff || !EscapeSelectionPending
            || !preparationConfirmed || levelRules == null || !ValidateEscapeHandoff(setup)) return false;
        if (!spirit.Presentation.TryBeginDeathPlayback(out uint playback)) return false;
        escapeHandoff = true;
        int serial = ++loadSerial;
        InputControlMgr.Instance.InputOpenOrClose(false);
        MonoPublicMgr.Instance.StartCoroutine(FinishEscapeHandoff(setup, levelRules, spirit, playback, preserveScene, serial));
        return true;
    }

    IEnumerator FinishEscapeHandoff(SceneLevelSetup setup, PlayerLifecycleRules rules, Player departing,
        uint playback, bool preserveScene, int serial)
    {
        while (serial == loadSerial && escapeHandoff && rules == levelRules && departing != null)
        {
            PresentationLayer.DeathPlaybackStatus status = departing.Presentation != null
                ? departing.Presentation.GetDeathPlaybackStatus(playback)
                : PresentationLayer.DeathPlaybackStatus.Invalid;
            if (status == PresentationLayer.DeathPlaybackStatus.Invalid)
            {
                Debug.LogError("精神体退场动画被打断，返回菜单并撤销本关准备。");
                ReturnToMenu();
                yield break;
            }
            if (status == PresentationLayer.DeathPlaybackStatus.Completed) break;
            yield return null;
        }
        if (serial != loadSerial || !escapeHandoff || rules != levelRules) yield break;
        if (departing == null || setup == null)
        {
            Debug.LogError("探索退场对象被移除，返回菜单并撤销本关准备。");
            ReturnToMenu();
            yield break;
        }
        // 确认消散完成后才处理现场及实体；探索交接不发死亡请求、不收转换费。
        if (!preserveScene) PlayerLifecycleRules.RestoreWorldDefaults(setup.gameObject.scene);
        body.BindEffectLedger(effects);
        body.BindDeathReceiver(rules);
        body.RefreshRuntime(setup.BodyPoint);
        PlaceDepth(body, setup.BodyPose);
        body.Presentation.SetTransitionPlayback(true);
        InputControlMgr.Instance.BeginTransitionView(body.transform, false);
        while (!InputControlMgr.Instance.TransitionViewComplete)
        {
            if (serial != loadSerial || !escapeHandoff || rules != levelRules) yield break;
            yield return null;
        }
        if (serial != loadSerial || !escapeHandoff || rules != levelRules) yield break;
        InputControlMgr.Instance.BeginTransitionView(body.transform, true);
        InputControlMgr.Instance.BindPlayer(body);
        InputControlMgr.Instance.EndTransitionView();
        body.Presentation.SetTransitionPlayback(false);
        DestroyActor(ref spirit);
        rules.ConfigureStage(PlayerGameplayStage.Escape);
        PlayerLifecycleRules.SetWorldPaused(GameMenuOpen);
        Physics2D.SyncTransforms();
        LevelBoxContext.Resample(setup.gameObject.scene);
        body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        float waterMultiplier = escapePenalty != null && escapePenalty.Kind == EscapePenaltyKind.WaterRise
            ? escapePenalty.Multiplier : 1f;
        setup.RisingWater?.SetRunRiseSpeed(setup.WaterRiseSpeed * waterMultiplier);
        setup.RisingWater?.SetRisingAllowed(true);
        escapeHandoff = false;
        InputControlMgr.Instance.InputOpenOrClose(true);
    }

    void CancelEscapeHandoff()
    {
        if (!escapeHandoff) return;
        escapeHandoff = false;
        spirit?.Presentation?.CancelDeathPlayback();
        body?.Presentation?.SetTransitionPlayback(false);
        InputControlMgr.Instance.EndTransitionView();
    }
}
