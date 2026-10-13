using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class BeginPanel : BasePanel
{
    [SerializeField, Min(0f), Tooltip("开始面板淡入秒数，视频第一帧就绪后开始。")]
    float fadeInSeconds = .3f;
    [SerializeField, Min(0f), Tooltip("离开开始面板的淡出秒数；加载时全黑后才隐藏。")]
    float fadeOutSeconds = .2f;
    UnityEngine.Video.VideoPlayer cover;
    UnityEngine.UI.RawImage picture;
    RenderTexture frame;
    CanvasGroup opacity;
    Coroutine fading;
    bool shown, coverReady;
    public bool IsFullyShown => shown && coverReady && fading == null && opacity.alpha >= .999f;

    protected override void OtherInit()
    {
        opacity = GetComponent<CanvasGroup>();
        if (opacity == null) opacity = gameObject.AddComponent<CanvasGroup>();
        cover = GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true);
        picture = GetComponentInChildren<UnityEngine.UI.RawImage>(true);
        // 安全：封面未配置时仍保留静态底图和三个入口。
        if (cover == null || picture == null) return;
        cover.prepareCompleted += PlayPreparedCover;
        cover.sendFrameReadyEvents = true;
        cover.frameReady += FirstCoverFrame;
        cover.errorReceived += CoverError;
        picture.enabled = false;
    }

    protected override void OnClickButton(string name)
    {
        switch (name)
        {
            case "t1":
                SceneChangeMgr.Instance.EnterSavedLevel();
                // 加载受理后保留封面直到全黑，资源首次加载期间也不能露出场景底色。
                if (SceneChangeMgr.Instance.IsLoading) opacity.interactable = false;
                break;
            case "t2": UIMgr.Instance.SceneUIShow(E_UI_Process.SetPanel); break;
            case "t3": Application.Quit(); break;
        }
    }

    public override void ShowMe()
    {
        // 已显示或正在准备的视频不因重复显示请求重新淡入。
        if (shown) return;
        base.ShowMe();
        shown = true;
        if (fading != null) StopCoroutine(fading);
        opacity.alpha = 0f;
        opacity.interactable = false;
        opacity.blocksRaycasts = true;
        coverReady = (frame != null && picture != null && picture.enabled) || cover == null || cover.clip == null || picture == null;
        fading = StartCoroutine(Reveal());
        // 安全：未配置视频时只使用预设上的静态封面。
        if (cover == null || cover.clip == null || picture == null || frame != null) return;
        frame = new RenderTexture((int)cover.clip.width, (int)cover.clip.height, 0, RenderTextureFormat.ARGB32)
        { name = "MenuCoverFrame", useMipMap = false, autoGenerateMips = false };
        frame.Create();
        cover.targetTexture = frame;
        picture.texture = frame;
        picture.enabled = false;
        cover.sendFrameReadyEvents = true;
        cover.Prepare();
    }

    IEnumerator Reveal()
    {
        float waiting = 0f;
        // 视频解码失败或迟迟没有首帧时保留静态封面，不无限等待。
        while (!coverReady && waiting < 5f)
        { waiting += Time.unscaledDeltaTime; yield return null; }
        if (!coverReady) { StopCover(); coverReady = true; }
        yield return FadeOpacity(1f, fadeInSeconds);
        opacity.interactable = !SceneChangeMgr.Instance.IsLoading;
        fading = null;
    }

    IEnumerator FadeOpacity(float target, float seconds)
    {
        float from = opacity.alpha, elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            opacity.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }
        opacity.alpha = target;
    }

    void PlayPreparedCover(UnityEngine.Video.VideoPlayer source)
    {
        // 停播后迟到的准备回调不能重新启动视频。
        if (!isActiveAndEnabled || frame == null) return;
        source.Play();
    }

    void FirstCoverFrame(UnityEngine.Video.VideoPlayer source, long index)
    {
        // 安全：停止后的迟到帧不能重新显示已释放的缓冲。
        if (!shown || frame == null) return;
        picture.enabled = true;
        coverReady = true;
        source.sendFrameReadyEvents = false;
    }

    void CoverError(UnityEngine.Video.VideoPlayer source, string message)
    {
        Debug.LogWarning("封面视频播放失败，保留静态封面：" + message);
        StopCover();
        coverReady = true;
    }

    public override void HideMe()
    {
        // 安全：隐藏缓存中的失活面板不启动协程。
        if (!gameObject.activeInHierarchy) return;
        shown = false;
        opacity.interactable = opacity.blocksRaycasts = false;
        if (fading != null) StopCoroutine(fading);
        fading = StartCoroutine(Conceal());
    }
    IEnumerator Conceal()
    {
        yield return FadeOpacity(0f, fadeOutSeconds);
        StopCover();
        fading = null;
        base.HideMe();
    }
    void OnDisable() { shown = false; fading = null; StopCover(); }
    void OnDestroy() => StopCover();

    void Update()
    {
        Text status = FindUIObj<Text>("BeginStatus");
        if (status != null) status.text = SceneChangeMgr.Instance.MenuMessage ?? "";
    }

    void StopCover()
    {
        if (cover != null) { cover.Stop(); cover.targetTexture = null; }
        if (picture != null) { picture.texture = null; picture.enabled = false; }
        // 安全：重复隐藏／销毁不会重复释放画面缓冲。
        if (frame == null) return;
        frame.Release(); Destroy(frame); frame = null;
    }
}
