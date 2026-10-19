using UnityEngine;

public partial class SceneChangeMgr
{
    public bool StoryActive { get; private set; }
    float storyTimeScale;
    bool storyWorldPaused;

    public bool BeginStoryPause()
    {
        if (StoryActive || GameMenuOpen || loading || refreshing || escapeHandoff) return false;
        StoryActive = true;
        storyTimeScale = Time.timeScale;
        storyWorldPaused = BasicEntity.IsWorldPaused;
        // Menu and narrative are mutually exclusive; reuse the input release-edge guard.
        InputControlMgr.Instance.SetMenuInputPaused(true);
        Time.timeScale = 0;
        PlayerLifecycleRules.SetWorldPaused(true);
        if (IsEnding) endingHost.SetPaused(true);
        return true;
    }

    public void EndStoryPause()
    {
        if (!StoryActive) return;
        StoryActive = false;
        Time.timeScale = storyTimeScale;
        PlayerLifecycleRules.SetWorldPaused(storyWorldPaused);
        if (IsEnding) endingHost.SetPaused(false);
        InputControlMgr.Instance.SetMenuInputPaused(false);
    }
}
