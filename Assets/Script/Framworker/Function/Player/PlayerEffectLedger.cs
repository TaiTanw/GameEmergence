using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum PlayerEffectKind { MovementMultiplier, JumpMultiplier }
public enum PlayerEffectScope { CurrentLevel, Permanent }

/// <summary>一份已经领取的有限数值效果；价格与选项池属于关卡配置，不写进领取记录。</summary>
public readonly struct PlayerEffectRecord
{
    public PlayerEffectKind Kind { get; }
    public float Multiplier { get; }
    public PlayerEffectScope Scope { get; }

    public PlayerEffectRecord(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope)
    { Kind = kind; Multiplier = multiplier; Scope = scope; }
}

/// <summary>
/// 本体与精神体共读的一份效果记录；新永久效果先在本关生效，通关才确认跨关保留。
/// 只汇总有限倍率，不保存角色基础配置、环境参数或累计速度；磁盘持久化后续由 P4 接入。
/// </summary>
public sealed class PlayerEffectLedger
{
    private readonly List<PlayerEffectRecord> currentLevel = new List<PlayerEffectRecord>();
    private readonly List<PlayerEffectRecord> confirmedPermanent = new List<PlayerEffectRecord>();
    public ReadOnlyCollection<PlayerEffectRecord> CurrentLevelEffects { get; }
    public ReadOnlyCollection<PlayerEffectRecord> ConfirmedPermanentEffects { get; }
    public float MovementMultiplier { get; private set; } = 1f;
    public float JumpMultiplier { get; private set; } = 1f;

    public PlayerEffectLedger()
    {
        CurrentLevelEffects = currentLevel.AsReadOnly();
        ConfirmedPermanentEffects = confirmedPermanent.AsReadOnly();
    }

    /// <summary>领取立即生效；重复领取资格由领取规则判断，本记录不假定同类效果只能有一份。</summary>
    public bool GrantEffect(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope = PlayerEffectScope.CurrentLevel)
    {
        // 安全：非法配置不能产生部分领取记录或无穷的有效倍率。
        if ((kind != PlayerEffectKind.MovementMultiplier && kind != PlayerEffectKind.JumpMultiplier)
            || (scope != PlayerEffectScope.CurrentLevel && scope != PlayerEffectScope.Permanent)
            || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return false;
        float current = kind == PlayerEffectKind.MovementMultiplier ? MovementMultiplier : JumpMultiplier;
        if (float.IsInfinity(current * multiplier)) return false;
        // 本局负面倍率结束后，永久倍率也必须仍是有限值，不能靠临时减益掩盖溢出。
        if (scope == PlayerEffectScope.Permanent && float.IsInfinity(ResolvePermanent(kind) * multiplier)) return false;
        currentLevel.Add(new PlayerEffectRecord(kind, multiplier, scope));
        ResolveEffects();
        return true;
    }

    /// <summary>失败清所有本关新增，包括已生效、尚未通关确认的永久效果。</summary>
    public void ResetCurrentLevel()
    {
        currentLevel.Clear();
        ResolveEffects();
    }

    /// <summary>下一关安置成功后确认通关：只移动新增永久记录，不再领取或叠乘一次；本局项到此结束。</summary>
    public void CommitLevel()
    {
        foreach (PlayerEffectRecord effect in currentLevel)
            if (effect.Scope == PlayerEffectScope.Permanent) confirmedPermanent.Add(effect);
        currentLevel.Clear();
        ResolveEffects();
    }

    private float ResolvePermanent(PlayerEffectKind kind)
    {
        float value = 1f;
        foreach (PlayerEffectRecord effect in confirmedPermanent)
            if (effect.Kind == kind) value *= effect.Multiplier;
        foreach (PlayerEffectRecord effect in currentLevel)
            if (effect.Kind == kind && effect.Scope == PlayerEffectScope.Permanent) value *= effect.Multiplier;
        return value;
    }

    private void ResolveEffects()
    {
        float movement = 1f, jump = 1f;
        foreach (PlayerEffectRecord effect in confirmedPermanent) Apply(effect, ref movement, ref jump);
        foreach (PlayerEffectRecord effect in currentLevel) Apply(effect, ref movement, ref jump);
        MovementMultiplier = movement;
        JumpMultiplier = jump;
    }

    private static void Apply(PlayerEffectRecord effect, ref float movement, ref float jump)
    {
        if (effect.Kind == PlayerEffectKind.MovementMultiplier) movement *= effect.Multiplier;
        else jump *= effect.Multiplier;
    }
}
