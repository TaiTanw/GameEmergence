using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public enum PlayerEffectKind { MovementMultiplier, JumpMultiplier }
// Permanent 保留旧存档标识；新的运动授予只允许 CurrentLevel。
public enum PlayerEffectScope { CurrentLevel, Permanent }

public readonly struct PlayerEffectRecord
{
    public PlayerEffectKind Kind { get; }
    public float Multiplier { get; }
    public PlayerEffectScope Scope { get; }
    public PlayerEffectRecord(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope)
    { Kind = kind; Multiplier = multiplier; Scope = scope; }
}

/// <summary>本体和精神体共用：本场运动记录与三项降费等级；不持基础值或物理状态。</summary>
public sealed class PlayerEffectLedger
{
    readonly List<PlayerEffectRecord> currentLevel = new List<PlayerEffectRecord>();
    readonly List<PlayerEffectRecord> legacyPermanent = new List<PlayerEffectRecord>();
    readonly int[] confirmedUpgradeLevels = new int[PlayerUpgradeCatalog.KindCount];
    readonly int[] pendingUpgradeLevels = new int[PlayerUpgradeCatalog.KindCount];
    public PlayerUpgradeCatalog Catalog { get; private set; }
    public ReadOnlyCollection<PlayerEffectRecord> CurrentLevelEffects { get; }
    public ReadOnlyCollection<PlayerEffectRecord> ConfirmedPermanentEffects { get; }
    public float MovementMultiplier { get; private set; } = 1f;
    public float JumpMultiplier { get; private set; } = 1f;
    public bool AutomaticResultLocked { get; private set; }
    public PlayerAbilityEffect? PositiveAbility { get; private set; }
    public PlayerAbilityEffect? NegativeAbility { get; private set; }

    public PlayerEffectLedger()
    {
        CurrentLevelEffects = currentLevel.AsReadOnly();
        ConfirmedPermanentEffects = legacyPermanent.AsReadOnly();
    }

    public bool Configure(PlayerUpgradeCatalog catalog)
    {
        // 安全：只验证正在消费的降费配置，停用的运动配置不阻止旧进度续关。
        if (catalog == null || !catalog.Validate(out _) || !ValidateLevels(catalog, confirmedUpgradeLevels, pendingUpgradeLevels)) return false;
        Catalog = catalog;
        return true;
    }

    public int GetUpgradeLevel(PlayerUpgradeKind kind) => PlayerUpgradeCatalog.IsCost(kind)
        ? confirmedUpgradeLevels[(int)kind] + pendingUpgradeLevels[(int)kind] : 0;

    public bool CanUpgrade(PlayerUpgradeKind kind)
    {
        if (!PlayerUpgradeCatalog.IsCost(kind)) return false;
        PlayerUpgradeDefinition item = Catalog != null ? Catalog.Get(kind) : null;
        if (item == null || !item.Enabled || !item.Validate(out _)) return false;
        int level = GetUpgradeLevel(kind);
        return level < item.MaxLevel && level < int.MaxValue && PlayerUpgradeDefinition.Finite(item.Evaluate(1f, level + 1));
    }

    public float GetUpgradedValue(PlayerUpgradeKind kind, float basis)
    {
        if (!PlayerUpgradeCatalog.IsKnown(kind) || !PlayerUpgradeDefinition.Finite(basis) || basis < 0f)
            throw new InvalidOperationException("能力基础值或标识无效。" + kind);
        // 旧运动编号只保留兼容身份，不再消费永久等级。
        if (!PlayerUpgradeCatalog.IsCost(kind)) return basis;
        PlayerUpgradeDefinition item = Catalog != null ? Catalog.Get(kind) : null;
        float value = item != null ? item.Evaluate(basis, GetUpgradeLevel(kind)) : basis;
        if (!PlayerUpgradeDefinition.Finite(value) || value < 0f)
            throw new InvalidOperationException("降费等级结果无效或溢出。" + kind);
        return value;
    }

    public bool TryUpgrade(PlayerUpgradeKind kind, LevelCognition cognition)
    {
        if (cognition == null || !CanUpgrade(kind)) return false;
        // 支付成功只增一次；失败保持余额和等级。
        if (!cognition.TryPayAbilityCost(Catalog.Get(kind).Cost)) return false;
        pendingUpgradeLevels[(int)kind]++;
        return true;
    }

    public int[] CaptureConfirmedUpgradeLevels() => (int[])confirmedUpgradeLevels.Clone();
    public PlayerEffectRecord[] CaptureConfirmedEffects() => Array.Empty<PlayerEffectRecord>();

    public bool RestoreConfirmed(int[] levels, PlayerEffectRecord[] effects)
    {
        // 安全：保留旧六槽位格式，结构损坏仍拒绝；旧运动值不参与当前运算。
        if (levels == null || levels.Length != PlayerUpgradeCatalog.KindCount || effects == null) return false;
        var restored = new int[PlayerUpgradeCatalog.KindCount];
        for (int i = 0; i < restored.Length; i++)
        {
            if (!PlayerUpgradeCatalog.IsCost((PlayerUpgradeKind)i)) continue;
            if (levels[i] < 0) return false;
            restored[i] = levels[i];
        }
        if (Catalog != null && !ValidateLevels(Catalog, restored, new int[restored.Length])) return false;
        Array.Copy(restored, confirmedUpgradeLevels, restored.Length);
        ResetCurrentLevel();
        return true;
    }

    public void ResetAll()
    {
        Array.Clear(confirmedUpgradeLevels, 0, confirmedUpgradeLevels.Length);
        ResetCurrentLevel();
    }

    public bool GrantEffect(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope = PlayerEffectScope.CurrentLevel)
    {
        // 普通运动效果只有本场寿命；Permanent 只用于旧文件读取，不暗中改换寿命。
        if ((kind != PlayerEffectKind.MovementMultiplier && kind != PlayerEffectKind.JumpMultiplier)
            || scope != PlayerEffectScope.CurrentLevel || !PlayerUpgradeDefinition.Finite(multiplier) || multiplier < 0f) return false;
        double value = (kind == PlayerEffectKind.MovementMultiplier ? MovementMultiplier : JumpMultiplier) * (double)multiplier;
        if (!Finite(value) || value > float.MaxValue) return false;
        currentLevel.Add(new PlayerEffectRecord(kind, multiplier, scope));
        ResolveOrdinaryEffects();
        return true;
    }

    public bool TryGrantAutomaticAbilities(PlayerAbilityEffect? positive, PlayerAbilityEffect? negative)
    {
        // 无命中也锁存一次结果；成对检查先于任何记录修改。
        if (AutomaticResultLocked || (positive.HasValue && (!positive.Value.Positive || !positive.Value.IsValid))
            || (negative.HasValue && (negative.Value.Positive || !negative.Value.IsValid))) return false;
        foreach (PlayerAbilityKind kind in Enum.GetValues(typeof(PlayerAbilityKind)))
            if (!Finite(ResolveMotion(kind, 1d, positive, negative))) return false;
        PositiveAbility = positive;
        NegativeAbility = negative;
        AutomaticResultLocked = true;
        return true;
    }

    public double ResolveMotionAttribute(PlayerAbilityKind kind, double basis)
    {
        double value = ResolveMotion(kind, basis, PositiveAbility, NegativeAbility);
        if (!Finite(value)) throw new InvalidOperationException("本场能力属性运算无效或溢出。" + kind);
        return value;
    }

    double ResolveMotion(PlayerAbilityKind kind, double basis, PlayerAbilityEffect? positive, PlayerAbilityEffect? negative)
    {
        double value = kind == PlayerAbilityKind.MovementSpeed ? basis * MovementMultiplier
            : kind == PlayerAbilityKind.JumpStrength ? basis * JumpMultiplier : basis;
        // 中间不截断，正面完整算完后再计算负面，角色消费者只限界最终值。
        if (positive.HasValue) value = positive.Value.Apply(kind, value);
        if (negative.HasValue) value = negative.Value.Apply(kind, value);
        return value;
    }

    public void ClearAutomaticAbilities()
    { PositiveAbility = NegativeAbility = null; AutomaticResultLocked = false; }

    public void ResetCurrentLevel()
    {
        currentLevel.Clear();
        Array.Clear(pendingUpgradeLevels, 0, pendingUpgradeLevels.Length);
        ClearAutomaticAbilities();
        ResolveOrdinaryEffects();
    }

    public void CommitLevel()
    {
        // 只有三项降费能写永久槽；运动记录及自动结果到此清理。
        for (int i = 0; i < confirmedUpgradeLevels.Length; i++)
            if (PlayerUpgradeCatalog.IsCost((PlayerUpgradeKind)i)) confirmedUpgradeLevels[i] += pendingUpgradeLevels[i];
        ResetCurrentLevel();
    }

    static bool ValidateLevels(PlayerUpgradeCatalog catalog, int[] confirmed, int[] pending)
    {
        if (!catalog.Validate(out _)) return false;
        for (int i = 0; i < confirmed.Length; i++)
        {
            PlayerUpgradeKind kind = (PlayerUpgradeKind)i;
            if (!PlayerUpgradeCatalog.IsCost(kind)) continue;
            if (confirmed[i] < 0 || pending[i] < 0 || confirmed[i] > int.MaxValue - pending[i]
                || !PlayerUpgradeDefinition.Finite(catalog.Get(kind).Evaluate(1f, confirmed[i] + pending[i]))) return false;
        }
        return true;
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    void ResolveOrdinaryEffects()
    {
        double movement = 1d, jump = 1d;
        foreach (PlayerEffectRecord effect in currentLevel)
            if (effect.Kind == PlayerEffectKind.MovementMultiplier) movement *= effect.Multiplier;
            else jump *= effect.Multiplier;
        MovementMultiplier = (float)movement;
        JumpMultiplier = (float)jump;
    }
}
