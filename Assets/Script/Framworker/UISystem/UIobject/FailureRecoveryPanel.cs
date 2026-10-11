using UnityEngine;
using UnityEngine.UI;

/// <summary>逃生失败的独立全屏遮罩；场景宿主决定何时复位，不承接关卡加载。</summary>
[RequireComponent(typeof(Image))]
public sealed class FailureRecoveryPanel : BasePanel
{
    [SerializeField, Tooltip("死亡后全屏逐渐变暗的秒数，使用真实时间。")]
    float darkenSeconds = .7f;
    [SerializeField, Tooltip("全黑复位完成后逐渐变亮的秒数，使用真实时间。")]
    float brightenSeconds = .7f;
    public float DarkenSeconds => darkenSeconds;
    public float BrightenSeconds => brightenSeconds;
    public float Weight { get; private set; }
    Image cover;

    protected override void Awake() { base.Awake(); cover = GetComponent<Image>(); }
    void OnEnable() => SetWeight(0f);

    public bool ValidateSettings(out string error)
    {
        error = null;
        if (!ValidDuration(darkenSeconds) || !ValidDuration(brightenSeconds))
            error = $"{name}：失败渐暗／渐亮时长必须是有限非负数值。";
        return error == null;
    }
    static bool ValidDuration(float seconds) => !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= 0f;

    public void SetWeight(float weight)
    {
        Weight = weight;
        // 演出从透明首帧就阻挡点击；输入只由宿主在渐亮结束后恢复。
        cover.color = new Color(0f, 0f, 0f, weight);
        cover.raycastTarget = true;
    }
}
