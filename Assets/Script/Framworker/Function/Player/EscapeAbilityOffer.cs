using System;
using UnityEngine;

/// <summary>固定逃生能力池的一项制作配置；购买记录归共用效果记录，配置本身不保存领取状态。</summary>
[Serializable]
public sealed class EscapeAbilityOffer
{
    [SerializeField] string label;
    [SerializeField] PlayerEffectKind kind;
    [SerializeField, Min(0f)] float cost = 20f;
    [SerializeField, Min(0.01f)] float multiplier = 1.2f;
    public string Label => label;
    public PlayerEffectKind Kind => kind;
    public float Cost => cost;
    public float Multiplier => multiplier;
    public bool IsValid => !string.IsNullOrWhiteSpace(label) && Enum.IsDefined(typeof(PlayerEffectKind), kind)
        && !float.IsNaN(cost) && !float.IsInfinity(cost) && cost >= 0f
        && !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f;
    public EscapeAbilityOffer(string label, PlayerEffectKind kind, float cost, float multiplier)
    { this.label = label; this.kind = kind; this.cost = cost; this.multiplier = multiplier; }
}
