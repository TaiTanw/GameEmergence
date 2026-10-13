using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class BeginPanel : BasePanel
{
    UnityEngine.Video.VideoPlayer cover;
    UnityEngine.UI.RawImage picture;
    RenderTexture frame;

    protected override void OtherInit()
    {
        cover = GetComponentInChildren<UnityEngine.Video.VideoPlayer>(true);
        picture = GetComponentInChildren<UnityEngine.UI.RawImage>(true);
        // 安全：封面未配置时仍保留静态底图和三个入口。
        if (cover == null || picture == null) return;
        cover.prepareCompleted += PlayPreparedCover;
        cover.errorReceived += CoverError;
        picture.enabled = false;
    }

    protected override void OnClickButton(string name)
    {
        switch (name)
        {
            case "t1":
                SceneChangeMgr.Instance.EnterSavedLevel();
                // 加载受理后关闭封面；配置拒绝时入口留在原处。
                if (Time.timeScale == 0f) UIMgr.Instance.HideOneUI<BeginPanel>();
                break;
            case "t2": UIMgr.Instance.SceneUIShow(E_UI_Process.SetPanel); break;
            case "t3": Application.Quit(); break;
        }
    }

    public override void ShowMe()
    {
        base.ShowMe();
        // 安全：未配置视频时只使用预设上的静态封面。
        if (cover == null || cover.clip == null || picture == null || frame != null) return;
        frame = new RenderTexture((int)cover.clip.width, (int)cover.clip.height, 0, RenderTextureFormat.ARGB32)
        { name = "MenuCoverFrame", useMipMap = false, autoGenerateMips = false };
        frame.Create();
        cover.targetTexture = frame;
        picture.texture = frame;
        picture.enabled = false;
        cover.Prepare();
    }

    void PlayPreparedCover(UnityEngine.Video.VideoPlayer source)
    {
        // 停播后迟到的准备回调不能重新启动视频。
        if (!isActiveAndEnabled || frame == null) return;
        picture.enabled = true;
        source.Play();
    }

    void CoverError(UnityEngine.Video.VideoPlayer source, string message)
    {
        Debug.LogWarning("封面视频播放失败，保留静态封面：" + message);
        StopCover();
    }

    public override void HideMe() { StopCover(); base.HideMe(); }
    void OnDisable() => StopCover();
    void OnDestroy() => StopCover();

    void StopCover()
    {
        if (cover != null) { cover.Stop(); cover.targetTexture = null; }
        if (picture != null) { picture.texture = null; picture.enabled = false; }
        // 安全：重复隐藏／销毁不会重复释放画面缓冲。
        if (frame == null) return;
        frame.Release(); Destroy(frame); frame = null;
    }
}
