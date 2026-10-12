using System;
using UnityEngine;

public enum EscapePenaltyKind { Movement, Jump, WaterRise }

/// <summary>本关人工配置的有限负面候选；只在本次逃生确认一次，不进入永久升级。</summary>
[Serializable]
public sealed class EscapePenaltyOffer
{
    [SerializeField] EscapePenaltyKind kind;
    [SerializeField] float multiplier = .8f;
    public EscapePenaltyKind Kind => kind;
    public float Multiplier => multiplier;
    public string Label => kind == EscapePenaltyKind.Movement ? "移速降低"
        : kind == EscapePenaltyKind.Jump ? "起跳力度降低" : "上涨水变快";
    public bool IsValid => Enum.IsDefined(typeof(EscapePenaltyKind), kind)
        && !float.IsNaN(multiplier) && !float.IsInfinity(multiplier)
        && (kind == EscapePenaltyKind.WaterRise ? multiplier > 1f : multiplier > 0f && multiplier < 1f);
    public EscapePenaltyOffer(EscapePenaltyKind kind, float multiplier)
    { this.kind = kind; this.multiplier = multiplier; }
}
