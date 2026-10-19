using System.Collections;
using UnityEngine;

public partial class SceneChangeMgr
{
    bool clearingLevel;
    public bool IsLevelClearPending => clearingLevel;

    /// <summary>通关道具的统一受理入口；编号仅选择表现，阶段、进度与效果仍归原规则及加载流程。</summary>
    public bool TryClearLevel(Player actor, int animationId = 0, SceneProp source = null)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        // 安全：同一次只能有一个终结结果，精神体和旧控制对象不能受理。
        if (loading || refreshing || clearingLevel || escapeHandoff || GameMenuOpen || StoryActive
            || actor == null || actor != body || actor != InputControlMgr.Instance.Player
            || actor.Identity != PlayerIdentity.Body || actor.IsLevelClearing || actor.PendingDeath.HasValue
            || setup == null || levelRules == null || levelRules.DeathPending) return false;
        // 阶段资格仍按教学或正式逃生，不另建一份阶段开关。
        if (!setup.Teaching && levelRules.Stage != PlayerGameplayStage.Escape) return false;
        LevelOrderFile order = DataAndInitMgr.Instance.LoadLevelOrder();
        int next = DataAndInitMgr.Instance.LoadLevelIndex() + 1;
        string endingTarget = null;
        // 下一关和动画都先核对，失败时保留可操作的角色与未领取道具。
        if (order.scenes == null || next < 0) return false;
        if (next >= order.scenes.Length)
        {
            if (order.endings == null || order.endings.Length == 0)
            { ShowGameplayMessage("已经是顺序中的最后一关。"); return false; }
            if (!DataAndInitMgr.Instance.TryPreviewEnding(source != null ? source.EndingContribution : Vector2.zero,
                out endingTarget, out string selectionError))
            { ShowGameplayMessage(selectionError); return false; }
            if (!ValidateEndingTarget(endingTarget, out string targetError))
            { ShowGameplayMessage(targetError); return false; }
        }
        else if (string.IsNullOrWhiteSpace(order.scenes[next]) || !Application.CanStreamedLevelBeLoaded(order.scenes[next]))
        { Debug.LogError($"关卡不能加载：{order.scenes[next]}，请核对顺序及构建设置。"); return false; }
        if (actor.Presentation == null) { Debug.LogError("实体缺少共用动画表现组件。", actor); return false; }
        DataAndInitMgr data = DataAndInitMgr.Instance;
        Vector3 vectorBeforeClear = data.CurrentLevelEndingVector;
        // 受理前检查贡献；动画被拒绝时只恢复本次快照，保留其它已领取贡献。
        if (!data.TryAddEndingContribution(source != null ? source.EndingContribution : Vector2.zero, out string error))
        { ShowGameplayMessage(error); return false; }
        if (!actor.Presentation.TryBeginLevelClearPlayback(animationId, out uint playback))
        { data.RestoreCurrentLevelEndingVector(vectorBeforeClear); return false; }
        PlayerCapabilityPermissions permissionsBeforeClear = actor.Permissions;
        bool inputBeforeClear = InputControlMgr.Instance.InputEnabled;
        clearingLevel = true;
        int serial = ++loadSerial;
        actor.BeginLevelClear();
        MonoPublicMgr.Instance.StartCoroutine(FinishLevelClear(actor, playback, next, order, serial,
            source, permissionsBeforeClear, inputBeforeClear, vectorBeforeClear, endingTarget));
        return true;
    }

    IEnumerator FinishLevelClear(Player actor, uint playback, int next, LevelOrderFile order, int serial,
        SceneProp source, PlayerCapabilityPermissions permissionsBeforeClear, bool inputBeforeClear, Vector3 vectorBeforeClear, string endingTarget)
    {
        try
        {
            while (serial == loadSerial && clearingLevel && actor != null && actor.IsLevelClearing)
            {
                // 安全：表现组件被移除也按未交接退出，不能留下角色及领取锁。
                PresentationLayer presentation = actor.Presentation;
                if (presentation == null) yield break;
                PresentationLayer.LevelClearPlaybackStatus status = presentation.GetLevelClearPlaybackStatus(playback);
                if (status == PresentationLayer.LevelClearPlaybackStatus.Invalid) yield break;
                if (status == PresentationLayer.LevelClearPlaybackStatus.Completed)
                {
                    string storyNode = NarrativeFlow.ClearNode(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    if (storyNode != null)
                    {
                        yield return NarrativeFlow.Instance.Play(storyNode);
                        if (serial != loadSerial || !clearingLevel || actor == null) yield break;
                    }
                    // 最后关只保留完整前段，随后角色也被全屏黑幕覆盖，不启动前景加载循环。
                    if (endingTarget != null) EnterEnding(endingTarget, next - 1, false);
                    // 加载循环和原场景加载同帧受理；只有真实完成事实能启动过场。
                    else if (presentation.TryBeginLevelClearLoop(playback)) EnterLevel(next, order, true, true);
                    yield break;
                }
                yield return null;
            }
        }
        finally
        {
            // 成功加载已接管，或退出／其它换关已作废本轮时，旧等待不能归还道具或开放角色。
            if (serial == loadSerial && clearingLevel)
            {
                bool resumeActor = actor != null && actor.IsLevelClearing;
                CancelLevelClear();
                DataAndInitMgr.Instance.RestoreCurrentLevelEndingVector(vectorBeforeClear);
                if (source != null) source.CancelLevelClearTake();
                // 普通刷新已有自己的许可恢复；只有仍由本次通关锁住的原控制对象回滚旧许可。
                if (resumeActor && actor == body && actor.isActiveAndEnabled
                    && actor == InputControlMgr.Instance.Player && !loading && !refreshing)
                {
                    actor.ApplyCapabilities(permissionsBeforeClear);
                    InputControlMgr.Instance.InputOpenOrClose(inputBeforeClear);
                }
            }
        }
    }

    void CancelLevelClear()
    {
        clearingLevel = false;
        body?.EndLevelClear();
    }
}
