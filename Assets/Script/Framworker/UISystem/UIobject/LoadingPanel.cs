using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class LoadingPanel : BasePanel
{
    /// <summary>0 为揭开，1 为全暗。像素化以后读这个权重，当前只改遮罩透明度。</summary>
    public float TransitionWeight { get; private set; }
    [SerializeField, Min(0f), Tooltip("旧场景逐渐变暗的时长，单位秒。")]
    float fadeOutSeconds = .7f;
    [SerializeField, Min(0f), Tooltip("新场景逐渐变亮的时长，单位秒。")]
    float fadeInSeconds = .7f;
    [SerializeField, Min(0f), Tooltip("安置完成后先恢复物理，在全黑下留给出生点小幅下落的缓冲秒数；设为 0 可直接淡入。")]
    float physicsSettleSeconds = .25f;
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
    }
    private void OnEnable()
    {
        SetTransitionWeight(0f);
    }

    /// <summary>换关明暗。权重变化时遮罩透明度跟着变，不在这里做后处理。</summary>
    public void SetTransitionWeight(float weight)
    {
        TransitionWeight = Mathf.Clamp01(weight);
        // 实际逻辑：透明的首帧也拦住菜单点击，操作只在整个演出结束后恢复。
        cover.color = new Color(0f, 0f, 0f, TransitionWeight);
        cover.enabled = true;
        cover.raycastTarget = true;
    }
}
