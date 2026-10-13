using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class SceneChangeMgr
{
    EndingSceneController endingHost;
    EndingSceneContext endingContext;
    public bool IsEnding => endingContext != null && endingContext.IsActive && endingContext.IsRevealed;

    static bool ValidateEndingTarget(string scene, out string error)
    {
        error = string.IsNullOrWhiteSpace(scene) || scene == MenuSceneName || !Application.CanStreamedLevelBeLoaded(scene)
            ? "结局场景不能加载：" + scene + "。请核对结局编排及构建设置。" : null;
        return error == null;
    }

    void EnterEnding(string scene, int lastLevelIndex, bool replay)
    {
        if (loading) return;
        if (!ValidateEndingTarget(scene, out string error)) { ShowGameplayMessage(error); return; }
        clearingLevel = false;
        CloseGameMenu();
        ReleaseEnding();
        refreshing = false; CancelEscapeHandoff();
        UIMgr.Instance.HideOneUI<GameplayPanel>();
        UIMgr.Instance.HideOneUI<FailureRecoveryPanel>();
        loading = true;
        int serial = ++loadSerial;
        bool fromMenu = SceneManager.GetActiveScene().name == MenuSceneName;
        InputControlMgr.Instance.InputOpenOrClose(false);
        InputControlMgr.Instance.EndTransitionView();
        body?.Presentation?.SetTransitionPlayback(true);
        levelRules?.Release();
        Time.timeScale = 0f;
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top, panel =>
        {
            if (serial != loadSerial || panel == null) return;
            MonoPublicMgr.Instance.StartCoroutine(DarkenThenEnterEnding(panel, scene, lastLevelIndex, replay, fromMenu, serial));
        });
    }

    IEnumerator DarkenThenEnterEnding(LoadingPanel panel, string scene, int lastIndex, bool replay, bool fromMenu, int serial)
    {
        yield return FadeTransition(panel, 0f, 1f, serial);
        if (serial != loadSerial) yield break;
        if (fromMenu) UIMgr.Instance.HideOneUI<BeginPanel>();
        ReleaseLevelRules();
        InputControlMgr.Instance.BindPlayer(null);
        InputControlMgr.Instance.BindEndingActor(null);
        DestroyActor(ref spirit); DestroyActor(ref body);
        ResetEscapePreparation();
        LoadSceneAsync(scene, () =>
        {
            if (serial != loadSerial) return;
            endingHost = EndingSceneController.FindFor(SceneManager.GetActiveScene(), out string error);
            if (endingHost == null) { FailEnding(error); return; }
            if (SceneLevelSetup.Current != null) { FailEnding("结局场景不能挂普通关 SceneLevelSetup。"); return; }
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.GetComponentInChildren<Player>(true) != null)
                { FailEnding("结局角色须使用专属预设，不能挂普通 Player。"); return; }
            if (!DataAndInitMgr.Instance.TryGetCandidateEndingVector(Vector2.zero, out Vector3 vector, out error))
            { FailEnding(error); return; }
            endingContext = new EndingSceneContext(new EndingSceneData(scene, vector, replay),
                actor => { if (serial == loadSerial) InputControlMgr.Instance.BindEndingActor(actor); },
                target => { if (serial == loadSerial) InputControlMgr.Instance.FollowEndingCamera(target); },
                point => { if (serial == loadSerial) InputControlMgr.Instance.PositionEndingCamera(point); },
                () => { if (serial == loadSerial) ReturnToMenu(); });
            try { endingHost.Initialize(endingContext); }
            catch (Exception exception) { FailEnding("结局准备失败：" + exception.Message); return; }
            MonoPublicMgr.Instance.StartCoroutine(PrepareAndRevealEnding(panel, lastIndex, replay, serial));
        });
    }

    IEnumerator PrepareAndRevealEnding(LoadingPanel panel, int lastIndex, bool replay, int serial)
    {
        float waiting = 0f;
        while (endingHost != null && !endingHost.Prepared && endingHost.PreparationError == null)
        {
            if (serial != loadSerial) yield break;
            waiting += Time.unscaledDeltaTime;
            if (waiting > endingHost.PreparationTimeout) { FailEnding("结局准备超时，请检查场景脚本。准备期间使用真实时间等待。"); yield break; }
            yield return null;
        }
        if (serial != loadSerial) yield break;
        if (endingHost == null || !endingHost.Prepared)
        { FailEnding("结局准备失败：" + endingHost?.PreparationError); yield break; }
        if (!replay)
        {
            int[] previousLevels = effects.CaptureConfirmedUpgradeLevels();
            PlayerEffectRecord[] previousEffects = effects.CaptureConfirmedEffects();
            effects.CommitLevel();
            if (!DataAndInitMgr.Instance.SaveEndingProgress(lastIndex, endingContext.Data.SceneName, effects))
            {
                effects.RestoreConfirmed(previousLevels, previousEffects);
                FailEnding("结局进度保存失败，已保留此前进度。"); yield break;
            }
        }
        // 准备和整体存档均完成后才揭幕，剧情及角色运动仍未开始。
        yield return FadeTransition(panel, 1f, 0f, serial);
        if (serial != loadSerial) yield break;
        loading = false;
        UIMgr.Instance.HideOneUI<LoadingPanel>();
        Time.timeScale = 1f;
        InputControlMgr.Instance.InputOpenOrClose(true);
        try { endingHost.Begin(); }
        catch (Exception error) { FailEnding("结局开演失败：" + error.Message); }
    }

    void FailEnding(string error)
    {
        Debug.LogError(error);
        ReturnToMenu();
        // 返回准备不会清掉这条提示；菜单有独立显示入口。
        ShowGameplayMessage(error);
        MenuMessage = error;
    }
    void ReleaseEnding()
    {
        endingHost?.Release();
        endingContext?.Release();
        endingHost = null; endingContext = null;
        InputControlMgr.Instance.BindEndingActor(null);
        InputControlMgr.Instance.EndEndingView();
    }
    public bool TryClearGameProgress(out string message)
    {
        if (loading || IsEnding || body != null || SceneManager.GetActiveScene().name != MenuSceneName)
        { message = "请返回主菜单后清空游戏进度。"; return false; }
        if (!DataAndInitMgr.Instance.TryClearPlayerProgress())
        { message = "清空游戏进度失败，请检查存档目录权限。"; return false; }
        effects.ResetAll(); ResetEscapePreparation();
        message = "游戏进度已清空，下次开始从第一关进入。";
        return true;
    }
}
