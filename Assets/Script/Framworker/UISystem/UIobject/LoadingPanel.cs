using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class LoadingPanel : BasePanel
{
    /// <summary>0 为揭开，1 为全暗；同一权重同时驱动黑幕和当前转场的像素化。</summary>
    public float TransitionWeight { get; private set; }
    [SerializeField, Min(0f), Tooltip("旧场景逐渐变暗的时长，单位秒。")]
    float fadeOutSeconds = .7f;
    [SerializeField, Min(0f), Tooltip("新场景逐渐变亮的时长，单位秒。")]
    float fadeInSeconds = .7f;
    [SerializeField, Min(0f), Tooltip("安置完成后先恢复物理，在全黑下留给出生点小幅下落的缓冲秒数；设为 0 可直接淡入。")]
    float physicsSettleSeconds = .25f;
    [SerializeField, Range(1f, 128f), Tooltip("转场最粗像素块在 1080 高度画面中的边长；随分辨率等比变化，菜单不使用。")]
    float maxPixelBlockSize = 24f;
    bool pixelationEnabled;
    public float FadeOutDuration => Mathf.Max(0f, fadeOutSeconds);
    public float FadeInDuration => Mathf.Max(0f, fadeInSeconds);
    public float PhysicsSettleDuration => Mathf.Max(0f, physicsSettleSeconds);
    Image cover;

    protected override void Awake()
    {
        base.Awake();
        Slider slider = FindUIObj<Slider>("LoadingSlider");
        if (slider != null) slider.gameObject.SetActive(false);
        // 实际逻辑：预设根节点已经是全屏黑图，直接改变它，避免黑色底图挡住淡入淡出。
        cover = GetComponent<Image>();
        // 黑幕使用纯色矩形；内置背景贴图的透明边缘在全黑时仍会透出关卡。
        cover.sprite = null;
        cover.type = Image.Type.Simple;
    }
    private void OnEnable()
    {
        pixelationEnabled = false;
        SetTransitionWeight(0f);
    }

    void OnDisable()
    {
        pixelationEnabled = false;
        // 退出应用时不再访问自动创建的管理器。
        if (!MonoPublicMgr.IsQuitting) InputControlMgr.Instance.SetTransitionPixelation(0f, maxPixelBlockSize);
    }

    public void SetPixelationEnabled(bool on)
    {
        // 安全：非法制作参数拒绝像素化，不让错误数值进入相机采样。
        if (on && (float.IsNaN(maxPixelBlockSize) || float.IsInfinity(maxPixelBlockSize)
            || maxPixelBlockSize < 1f || maxPixelBlockSize > 128f))
        {
            Debug.LogError(name + "：转场像素块边长必须是 1–128 的有限数值。", this);
            on = false;
        }
        pixelationEnabled = on;
        InputControlMgr.Instance.SetTransitionPixelation(on ? TransitionWeight : 0f, maxPixelBlockSize);
    }

    /// <summary>面板只传表现参数；采样处理仍归游戏镜头，不处理 UI 或前景镜头。</summary>
    public void SetTransitionWeight(float weight)
    {
        TransitionWeight = Mathf.Clamp01(weight);
        InputControlMgr.Instance.SetTransitionPixelation(pixelationEnabled ? TransitionWeight : 0f, maxPixelBlockSize);
        // 实际逻辑：透明的首帧也拦住菜单点击，操作只在整个演出结束后恢复。
        cover.color = new Color(0f, 0f, 0f, TransitionWeight);
        cover.enabled = true;
        cover.raycastTarget = true;
    }
}
