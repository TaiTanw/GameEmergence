using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerAbilityKind { MovementSpeed, JumpStrength, AirJumps }

/// <summary>全局池的一条制作配置；不保存是否已抽中或角色有效值。</summary>
[Serializable]
public sealed class PlayerAbilityOffer
{
    public PlayerAbilityKind Kind;
    public bool Positive = true;
    public float Weight = 1f;
    public float Value = 1.2f;

    public PlayerAbilityOffer() { }
    public PlayerAbilityOffer(PlayerAbilityKind kind, bool positive, float value, float weight = 1f)
    { Kind = kind; Positive = positive; Value = value; Weight = weight; }

    public bool Validate(out string error)
    {
        error = null;
        // 安全：制作数值必须有限，零权重只关闭这一条，不代表一个可抽出的空效果。
        if (!Enum.IsDefined(typeof(PlayerAbilityKind), Kind) || !PlayerUpgradeDefinition.Finite(Weight)
            || Weight < 0f || !PlayerUpgradeDefinition.Finite(Value))
            error = "能力类型、权重或数值无效。";
        else if (Kind == PlayerAbilityKind.AirJumps)
        {
            if (Value != Math.Truncate(Value) || Math.Abs((double)Value) > int.MaxValue
                || (Positive ? Value <= 0f : Value >= 0f))
                error = "空中跳必须填写符合正负标记的整数增减。";
        }
        else if (Positive ? Value <= 1f : Value < 0f || Value >= 1f)
            error = "移速／起跳正面倍率须大于 1，负面倍率须在 0（含）与 1 之间。";
        return error == null;
    }
}

/// <summary>锁存抽取时的值，制作资产后来变化不改写本次结果。</summary>
public readonly struct PlayerAbilityEffect
{
    public PlayerAbilityKind Kind { get; }
    public bool Positive { get; }
    public float Value { get; }
    public PlayerAbilityEffect(PlayerAbilityOffer offer)
    { Kind = offer.Kind; Positive = offer.Positive; Value = offer.Value; }
    public string Description => Name(Kind) + (Kind == PlayerAbilityKind.AirJumps
        ? $" {Value:+0;-0;0} 次" : $" ×{Value:0.###}");
    public static string Name(PlayerAbilityKind kind) => kind == PlayerAbilityKind.MovementSpeed ? "移动速度"
        : kind == PlayerAbilityKind.JumpStrength ? "起跳强度" : "额外空中跳";

    internal bool IsValid => new PlayerAbilityOffer(Kind, Positive, Value).Validate(out _);
    internal double Apply(PlayerAbilityKind kind, double value)
        => Kind != kind ? value : Kind == PlayerAbilityKind.AirJumps ? value + Value : value * Value;
}

[CreateAssetMenu(menuName = "GameEmergence/全局自动能力池", fileName = "PlayerAbilityPool")]
public sealed class PlayerAbilityPool : ScriptableObject
{
    public const string ResourceName = "PlayerAbilityPool";
    public List<PlayerAbilityOffer> Entries = new List<PlayerAbilityOffer>
    {
        new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, true, 1.2f),
        new PlayerAbilityOffer(PlayerAbilityKind.JumpStrength, true, 1.2f),
        new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, true, 1f),
        new PlayerAbilityOffer(PlayerAbilityKind.MovementSpeed, false, .8f),
        new PlayerAbilityOffer(PlayerAbilityKind.JumpStrength, false, .9f),
        new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, false, -1f)
    };

    public bool Validate(out string error)
    {
        error = null;
        if (Entries == null || Entries.Count == 0) { error = "自动能力池不能为空。"; return false; }
        double positive = 0d, negative = 0d;
        for (int i = 0; i < Entries.Count; i++)
        {
            PlayerAbilityOffer item = Entries[i];
            if (item == null || !item.Validate(out error))
            { error = "自动能力池第 " + (i + 1) + " 条：" + (error ?? "条目为空。"); return false; }
            if (item.Positive) positive += item.Weight;
            else negative += item.Weight;
        }
        // 正负分别抽取；任一分组没有可抽权重都属于制作错误。
        if (positive <= 0d || negative <= 0d || double.IsInfinity(positive) || double.IsInfinity(negative))
            error = "自动能力池的正、负分组均须有条目，且各自总权重大于 0。";
        return error == null;
    }

    public PlayerAbilityEffect Draw(bool positive, float sample)
    {
        if (!Validate(out string error)) throw new InvalidOperationException(error);
        if (!PlayerUpgradeDefinition.Finite(sample) || sample < 0f || sample > 1f)
            throw new ArgumentOutOfRangeException(nameof(sample));
        double total = 0d;
        foreach (PlayerAbilityOffer item in Entries) if (item.Positive == positive) total += item.Weight;
        double target = sample * total;
        PlayerAbilityOffer last = null;
        foreach (PlayerAbilityOffer item in Entries)
        {
            if (item.Positive != positive || item.Weight <= 0f) continue;
            last = item;
            target -= item.Weight;
            if (target < 0d) return new PlayerAbilityEffect(item);
        }
        // Random.value 可取 1；这个端点属于最后一个有权重的条目。
        return new PlayerAbilityEffect(last);
    }

    public static float PositiveProbability(float ratio) => Mathf.Clamp01(2f * ratio - .5f);
    public static float NegativeProbability(float ratio) => Mathf.Clamp01(1f - 2f * ratio);
    public static bool Triggers(float probability, float sample) => probability >= 1f || probability > 0f && sample < probability;
}
