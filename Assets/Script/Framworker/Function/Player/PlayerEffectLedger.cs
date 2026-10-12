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
/// 普通道具保存有限倍率，玩家升级保存等级；不保存角色基础配置、环境参数或累计速度。
/// </summary>
public sealed class PlayerEffectLedger
{
    private readonly List<PlayerEffectRecord> currentLevel = new List<PlayerEffectRecord>();
    private readonly List<PlayerEffectRecord> confirmedPermanent = new List<PlayerEffectRecord>();
    private readonly int[] confirmedUpgradeLevels = new int[PlayerUpgradeCatalog.KindCount];
    private readonly int[] pendingUpgradeLevels = new int[PlayerUpgradeCatalog.KindCount];
    public PlayerUpgradeCatalog Catalog { get; private set; }
    public ReadOnlyCollection<PlayerEffectRecord> CurrentLevelEffects { get; }
    public ReadOnlyCollection<PlayerEffectRecord> ConfirmedPermanentEffects { get; }
    public float MovementMultiplier { get; private set; } = 1f;
    public float JumpMultiplier { get; private set; } = 1f;

    public PlayerEffectLedger()
    {
        CurrentLevelEffects = currentLevel.AsReadOnly();
        ConfirmedPermanentEffects = confirmedPermanent.AsReadOnly();
    }

    /// <summary>同一场游戏共用一份制作配置；失效配置不替换已绑定的记录。</summary>
    public bool Configure(PlayerUpgradeCatalog catalog)
    {
        if (catalog == null || !catalog.Validate(out _)) return false;
        if (!ValidateUpgradeValues(catalog, confirmedUpgradeLevels, pendingUpgradeLevels,
            MovementMultiplier, JumpMultiplier)) return false;
        // 本局减益不能掩盖已确认和待确认永久项在撤销减益后的溢出。
        if (!ValidateUpgradeValues(catalog, confirmedUpgradeLevels, pendingUpgradeLevels,
            ResolvePermanent(PlayerEffectKind.MovementMultiplier), ResolvePermanent(PlayerEffectKind.JumpMultiplier))) return false;
        Catalog = catalog;
        return true;
    }

    public int GetUpgradeLevel(PlayerUpgradeKind kind) => PlayerUpgradeCatalog.IsKnown(kind)
        ? confirmedUpgradeLevels[(int)kind] + pendingUpgradeLevels[(int)kind] : 0;

    public bool CanUpgrade(PlayerUpgradeKind kind)
    {
        PlayerUpgradeDefinition definition = Catalog != null ? Catalog.Get(kind) : null;
        if (definition == null || !definition.Enabled || !definition.Validate(out _)) return false;
        int level = GetUpgradeLevel(kind);
        // 购买次数不受关卡限制；上限只比较总等级，历史高等级仍保留效果。
        if (level >= definition.MaxLevel || level == int.MaxValue) return false;
        float value = definition.Evaluate(1f, level + 1);
        if (!PlayerUpgradeDefinition.Finite(value)) return false;
        if (kind == PlayerUpgradeKind.MovementSpeed)
            return FiniteProduct(value, MovementMultiplier) && FiniteProduct(value, ResolvePermanent(PlayerEffectKind.MovementMultiplier));
        if (kind == PlayerUpgradeKind.JumpStrength)
            return FiniteProduct(value, JumpMultiplier) && FiniteProduct(value, ResolvePermanent(PlayerEffectKind.JumpMultiplier));
        return true;
    }

    public float GetUpgradedValue(PlayerUpgradeKind kind, float baseValue)
    {
        PlayerUpgradeDefinition definition = Catalog != null ? Catalog.Get(kind) : null;
        int level = GetUpgradeLevel(kind);
        float value = definition != null ? definition.Evaluate(baseValue, level) : baseValue;
        // 安全：配置被运行中改坏或实际基础值导致溢出时，明确拒绝而不把 NaN 写进刚体或费用。
        if (!PlayerUpgradeCatalog.IsKnown(kind) || !PlayerUpgradeDefinition.Finite(value) || value < 0f)
            throw new InvalidOperationException($"能力 {kind} 的基础值／等级运算无效或溢出。");
        return value;
    }

    public bool TryUpgrade(PlayerUpgradeKind kind, LevelCognition cognition)
    {
        if (cognition == null || !CanUpgrade(kind)) return false;
        PlayerUpgradeDefinition definition = Catalog.Get(kind);
        // 所有等级与价格检查已完成；支付成功后只增一次，无失败回调或重复施加效果。
        if (!cognition.TryPayAbilityCost(definition.Cost)) return false;
        pendingUpgradeLevels[(int)kind]++;
        return true;
    }

    public int[] CaptureConfirmedUpgradeLevels() => (int[])confirmedUpgradeLevels.Clone();
    public PlayerEffectRecord[] CaptureConfirmedEffects() => confirmedPermanent.ToArray();

    /// <summary>读档先完整检查，再替换已确认记录；失败不破坏原状态，也不重复领取。</summary>
    public bool RestoreConfirmed(int[] levels, PlayerEffectRecord[] effects)
    {
        if (levels == null || levels.Length != PlayerUpgradeCatalog.KindCount || effects == null) return false;
        foreach (int level in levels) if (level < 0) return false;
        double movement = 1d, jump = 1d;
        foreach (PlayerEffectRecord effect in effects)
        {
            if (!ValidEffect(effect.Kind, effect.Multiplier, effect.Scope) || effect.Scope != PlayerEffectScope.Permanent) return false;
            if (effect.Kind == PlayerEffectKind.MovementMultiplier) movement *= effect.Multiplier;
            else jump *= effect.Multiplier;
            if (!FiniteDouble(movement) || !FiniteDouble(jump)) return false;
        }
        if (Catalog != null && !ValidateUpgradeValues(Catalog, levels, new int[PlayerUpgradeCatalog.KindCount],
            (float)movement, (float)jump)) return false;
        Array.Copy(levels, confirmedUpgradeLevels, levels.Length);
        Array.Clear(pendingUpgradeLevels, 0, pendingUpgradeLevels.Length);
        confirmedPermanent.Clear();
        confirmedPermanent.AddRange(effects);
        currentLevel.Clear();
        ResolveEffects();
        return true;
    }

    public void ResetAll()
    {
        currentLevel.Clear();
        confirmedPermanent.Clear();
        Array.Clear(confirmedUpgradeLevels, 0, confirmedUpgradeLevels.Length);
        Array.Clear(pendingUpgradeLevels, 0, pendingUpgradeLevels.Length);
        ResolveEffects();
    }

    /// <summary>领取立即生效；重复领取资格由领取规则判断，本记录不假定同类效果只能有一份。</summary>
    public bool GrantEffect(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope = PlayerEffectScope.CurrentLevel)
    {
        // 安全：非法配置不能产生部分领取记录或无穷的有效倍率。
        if (!ValidEffect(kind, multiplier, scope)) return false;
        float current = kind == PlayerEffectKind.MovementMultiplier ? MovementMultiplier : JumpMultiplier;
        if (!FiniteProduct(current, multiplier)) return false;
        // 本局负面倍率结束后，永久倍率也必须仍是有限值，不能靠临时减益掩盖溢出。
        if (scope == PlayerEffectScope.Permanent && !FiniteProduct(ResolvePermanent(kind), multiplier)) return false;
        PlayerUpgradeKind upgrade = kind == PlayerEffectKind.MovementMultiplier ? PlayerUpgradeKind.MovementSpeed : PlayerUpgradeKind.JumpStrength;
        float upgraded = GetUpgradedValue(upgrade, 1f);
        if (!FiniteProduct(current * multiplier, upgraded)
            || (scope == PlayerEffectScope.Permanent && !FiniteProduct(ResolvePermanent(kind) * multiplier, upgraded))) return false;
        currentLevel.Add(new PlayerEffectRecord(kind, multiplier, scope));
        ResolveEffects();
        return true;
    }

    /// <summary>失败清所有本关新增，包括已生效、尚未通关确认的永久效果。</summary>
    public void ResetCurrentLevel()
    {
        currentLevel.Clear();
        Array.Clear(pendingUpgradeLevels, 0, pendingUpgradeLevels.Length);
        ResolveEffects();
    }

    /// <summary>下一关安置成功后确认通关：只移动新增永久记录，不再领取或叠乘一次；本局项到此结束。</summary>
    public void CommitLevel()
    {
        foreach (PlayerEffectRecord effect in currentLevel)
            if (effect.Scope == PlayerEffectScope.Permanent) confirmedPermanent.Add(effect);
        currentLevel.Clear();
        for (int i = 0; i < confirmedUpgradeLevels.Length; i++)
        {
            confirmedUpgradeLevels[i] += pendingUpgradeLevels[i];
            pendingUpgradeLevels[i] = 0;
        }
        ResolveEffects();
    }

    private static bool ValidEffect(PlayerEffectKind kind, float multiplier, PlayerEffectScope scope) =>
        (kind == PlayerEffectKind.MovementMultiplier || kind == PlayerEffectKind.JumpMultiplier)
        && (scope == PlayerEffectScope.CurrentLevel || scope == PlayerEffectScope.Permanent)
        && PlayerUpgradeDefinition.Finite(multiplier) && multiplier >= 0f;

    private static bool FiniteDouble(double value) => !double.IsNaN(value) && !double.IsInfinity(value)
        && value >= 0d && value <= float.MaxValue;
    private static bool FiniteProduct(float value, float multiplier) => FiniteDouble((double)value * multiplier);

    private static bool ValidateUpgradeValues(PlayerUpgradeCatalog catalog, int[] confirmed, int[] pending,
        float movement, float jump)
    {
        if (!catalog.Validate(out _)) return false;
        for (int i = 0; i < PlayerUpgradeCatalog.KindCount; i++)
        {
            if (confirmed[i] < 0 || pending[i] < 0 || confirmed[i] > int.MaxValue - pending[i]) return false;
            PlayerUpgradeKind kind = (PlayerUpgradeKind)i;
            float value = catalog.Get(kind).Evaluate(1f, confirmed[i] + pending[i]);
            if (!PlayerUpgradeDefinition.Finite(value)) return false;
            if (kind == PlayerUpgradeKind.MovementSpeed && !FiniteProduct(value, movement)) return false;
            if (kind == PlayerUpgradeKind.JumpStrength && !FiniteProduct(value, jump)) return false;
        }
        return true;
    }

    private float ResolvePermanent(PlayerEffectKind kind)
    {
        double value = 1d;
        foreach (PlayerEffectRecord effect in confirmedPermanent)
            if (effect.Kind == kind) value *= effect.Multiplier;
        foreach (PlayerEffectRecord effect in currentLevel)
            if (effect.Kind == kind && effect.Scope == PlayerEffectScope.Permanent) value *= effect.Multiplier;
        return (float)value;
    }

    private void ResolveEffects()
    {
        double movement = 1d, jump = 1d;
        foreach (PlayerEffectRecord effect in confirmedPermanent) Apply(effect, ref movement, ref jump);
        foreach (PlayerEffectRecord effect in currentLevel) Apply(effect, ref movement, ref jump);
        MovementMultiplier = (float)movement;
        JumpMultiplier = (float)jump;
    }

    private static void Apply(PlayerEffectRecord effect, ref double movement, ref double jump)
    {
        if (effect.Kind == PlayerEffectKind.MovementMultiplier) movement *= effect.Multiplier;
        else jump *= effect.Multiplier;
    }
}
