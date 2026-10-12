using UnityEngine;

public partial class PresentationLayer
{
    public enum LevelClearPlaybackStatus { Invalid, Playing, Completed, Looping }
    bool levelClearPlaybackActive, levelClearLooping, levelClearPlaybackFailed;
    uint levelClearPlaybackSequence;
    int levelClearStateHash, levelClearLoopStateHash;
    AnimationClip levelClearClip, levelClearLoopClip;
    RuntimeAnimatorController levelClearController;
    AnimatorUpdateMode updateModeBeforeLevelClear;
    AnimatorCullingMode cullingModeBeforeLevelClear;

    /// <summary>编号是道具制作参数；每个编号提供独立的单次前段和加载循环，不向精神体要求这些状态。</summary>
    public bool ValidateLevelClearPlayback(int animationId, out string error)
    {
        error = null;
        // 安全：必须已组装且能实际播放，不能以等待固定秒数替代缺失动画。
        if (animationId < 0 || !isInitialized || !isActiveAndEnabled || animator == null
            || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        { error = $"{name}：通关动画编号或表现组件无效。"; return false; }
        string start = $"LevelClear_{animationId}", loop = $"LevelClearLoop_{animationId}";
        if (!animator.HasState(BaseLayerIndex, Animator.StringToHash("Base Layer." + start))
            || !animator.HasState(BaseLayerIndex, Animator.StringToHash("Base Layer." + loop))
            || !FindLevelClearClip(start, false, out _) || !FindLevelClearClip(loop, true, out _)
            || !PositivePlaybackSpeed(animator.speed))
        { error = $"{name}：需要 {start} 单次片段、{loop} 循环片段及同名 Base Layer 状态和有效播放速度。"; return false; }
        return true;
    }

    bool FindLevelClearClip(string clipName, bool looping, out AnimationClip result)
    {
        result = null;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null || clip.name != clipName) continue;
            // 制作约束：同名片段只有一份，循环标记和时长必须符合当前动作。
            if (result != null || clip.isLooping != looping || !PositivePlaybackSpeed(clip.length)) return false;
            result = clip;
        }
        return result != null;
    }

    static bool PositivePlaybackSpeed(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    public bool TryBeginLevelClearPlayback(int animationId, out uint playback)
    {
        playback = 0;
        if (deathPlaybackActive || levelClearPlaybackActive) return false;
        if (!ValidateLevelClearPlayback(animationId, out string error)) { Debug.LogError(error, this); return false; }
        FindLevelClearClip($"LevelClear_{animationId}", false, out levelClearClip);
        FindLevelClearClip($"LevelClearLoop_{animationId}", true, out levelClearLoopClip);
        levelClearStateHash = Animator.StringToHash($"Base Layer.LevelClear_{animationId}");
        levelClearLoopStateHash = Animator.StringToHash($"Base Layer.LevelClearLoop_{animationId}");
        levelClearController = animator.runtimeAnimatorController;
        updateModeBeforeLevelClear = transitionPlayback ? updateModeBeforeTransition : animator.updateMode;
        cullingModeBeforeLevelClear = animator.cullingMode;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        levelClearPlaybackActive = true;
        levelClearLooping = levelClearPlaybackFailed = false;
        if (++levelClearPlaybackSequence == 0) ++levelClearPlaybackSequence;
        playback = levelClearPlaybackSequence;
        interactHintView.Hide();
        animator.Play(levelClearStateHash, BaseLayerIndex, 0f);
        animator.Update(0f);
        // 实际播放不匹配时拒绝整次动作，不能让错误状态伪造完成。
        if (GetLevelClearPlaybackStatus(playback) != LevelClearPlaybackStatus.Invalid) return true;
        CancelLevelClearPlayback();
        return false;
    }

    public LevelClearPlaybackStatus GetLevelClearPlaybackStatus(uint playback)
    {
        // 生命周期：释放、失败和旧编号都不能完成新的换关。
        if (!levelClearPlaybackActive || playback == 0 || playback != levelClearPlaybackSequence || levelClearPlaybackFailed)
            return LevelClearPlaybackStatus.Invalid;
        if (!isActiveAndEnabled || animator == null || !animator.isActiveAndEnabled
            || animator.runtimeAnimatorController != levelClearController) return FailLevelClearPlayback();
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(BaseLayerIndex);
        AnimationClip expected = levelClearLooping ? levelClearLoopClip : levelClearClip;
        if (state.fullPathHash != (levelClearLooping ? levelClearLoopStateHash : levelClearStateHash)
            || state.loop != levelClearLooping || animator.IsInTransition(BaseLayerIndex)
            || clips.Length != 1 || clips[0].clip != expected || expected.isLooping != levelClearLooping
            || !PositivePlaybackSpeed(state.speed) || !PositivePlaybackSpeed(state.speedMultiplier)
            || !PositivePlaybackSpeed(animator.speed) || float.IsNaN(state.normalizedTime) || float.IsInfinity(state.normalizedTime))
            return FailLevelClearPlayback();
        if (levelClearLooping) return LevelClearPlaybackStatus.Looping;
        return state.normalizedTime >= 1f ? LevelClearPlaybackStatus.Completed : LevelClearPlaybackStatus.Playing;
    }

    public bool TryBeginLevelClearLoop(uint playback)
    {
        // 前段真正完成才切到循环；重复请求不能重置加载动画。
        if (GetLevelClearPlaybackStatus(playback) != LevelClearPlaybackStatus.Completed) return false;
        levelClearLooping = true;
        animator.Play(levelClearLoopStateHash, BaseLayerIndex, 0f);
        animator.Update(0f);
        return GetLevelClearPlaybackStatus(playback) == LevelClearPlaybackStatus.Looping;
    }

    LevelClearPlaybackStatus FailLevelClearPlayback()
    {
        levelClearPlaybackFailed = true;
        Debug.LogError($"{name}：通关动画配置无效或被打断，本次加载等待作废。", this);
        return LevelClearPlaybackStatus.Invalid;
    }

    public void CancelLevelClearPlayback()
    {
        if (!levelClearPlaybackActive) return;
        levelClearPlaybackActive = false;
        if (animator != null)
        {
            animator.updateMode = transitionPlayback ? AnimatorUpdateMode.UnscaledTime : updateModeBeforeLevelClear;
            animator.cullingMode = cullingModeBeforeLevelClear;
        }
        currentAnimation = AnimationState.None;
        if (isInitialized) ResetMotionHistory();
    }
}
