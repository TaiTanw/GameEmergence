using System.Collections.Generic;
using UnityEngine;

public partial class SceneChangeMgr
{
    readonly List<PlayerUpgradeDefinition> escapeOffers = new List<PlayerUpgradeDefinition>();
    EscapePenaltyOffer escapePenalty;
    bool negativePending;
    bool preparationConfirmed;
    bool preserveEscapeScene;
    int preparationVersion;
    public IReadOnlyList<PlayerUpgradeDefinition> EscapeOffers => escapeOffers;
    public PlayerEffectLedger PlayerEffects => effects;
    public int EscapePreparationVersion => preparationVersion;
    public bool EscapeNegativePending => EscapeSelectionPending && negativePending;
    public EscapePenaltyOffer SelectedEscapePenalty => escapePenalty;
    public bool PreserveEscapeScene => preserveEscapeScene;

    bool EnsureUpgradeCatalog()
    {
        if (effects.Catalog != null) return true;
        PlayerUpgradeCatalog catalog = Resources.Load<PlayerUpgradeCatalog>("PlayerUpgradeCatalog");
        if (catalog == null || !effects.Configure(catalog))
        {
            Debug.LogError("永久升级配置缺失或无效，请在升级配置面板检查 PlayerUpgradeCatalog。");
            ShowGameplayMessage("升级配置缺失或无效，请检查制作配置。");
            return false;
        }
        return true;
    }

    bool CanPrepareEscape => EscapeSelectionPending && !loading && !refreshing && !GameMenuOpen
        && !escapeHandoff && Time.timeScale > 0f;

    void ResetEscapePreparation()
    {
        escapeOffers.Clear();
        escapePenalty = null;
        negativePending = preparationConfirmed = preserveEscapeScene = false;
        preparationVersion++;
    }

    /// <summary>只在一次准备开始时抽取；菜单开关、购买及界面逐帧更新不重新抽取。</summary>
    void DrawEscapeOffers()
    {
        var available = new List<PlayerUpgradeDefinition>();
        foreach (PlayerUpgradeDefinition item in effects.Catalog.Definitions)
            if (item.Enabled && effects.CanUpgrade(item.Kind)) available.Add(item);
        while (escapeOffers.Count < 3 && available.Count > 0)
        {
            int index = Random.Range(0, available.Count);
            escapeOffers.Add(available[index]);
            available.RemoveAt(index);
        }
    }

    public bool PurchasePreserveScene()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        // 安全：负面确认或已购买时不再次付款；保留费不经过认知降费消费者。
        if (!CanPrepareEscape || negativePending || preparationConfirmed || preserveEscapeScene || setup == null)
            return false;
        if (!levelRules.Cognition.TryPayAbilityCost(setup.PreserveSceneCost)) return false;
        preserveEscapeScene = true;
        return true;
    }

    public bool NextEscapeStep()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (!CanPrepareEscape || negativePending || preparationConfirmed || setup == null) return false;
        // 实际逻辑：购买耗尽仍留在面板，只有下一步按本关初始容量检查。
        if (RequiresEscapePenalty(levelRules.Cognition.Remaining, setup.InitialCognition))
        {
            LockEscapePenalty(setup);
            return true;
        }
        preparationConfirmed = true;
        if (CompleteEscapePreparation(preserveEscapeScene)) return true;
        preparationConfirmed = false;
        return false;
    }

    public static bool RequiresEscapePenalty(float remaining, float initialCapacity) =>
        remaining <= 0f || remaining < initialCapacity / 10f;

    void LockEscapePenalty(SceneLevelSetup setup)
    {
        if (escapePenalty == null)
            escapePenalty = setup.EscapePenalties[Random.Range(0, setup.EscapePenalties.Count)];
        negativePending = true;
    }

    public bool ConfirmEscapePenalty()
    {
        if (!CanPrepareEscape || !negativePending || preparationConfirmed || escapePenalty == null) return false;
        preparationConfirmed = true;
        // 交接受理成功后才提交一次本局效果；负面候选已在入场前验证。
        if (!CompleteEscapePreparation(preserveEscapeScene)) { preparationConfirmed = false; return false; }
        if (escapePenalty.Kind == EscapePenaltyKind.Movement)
            effects.GrantEffect(PlayerEffectKind.MovementMultiplier, escapePenalty.Multiplier);
        else if (escapePenalty.Kind == EscapePenaltyKind.Jump)
            effects.GrantEffect(PlayerEffectKind.JumpMultiplier, escapePenalty.Multiplier);
        negativePending = false;
        return true;
    }

    /// <summary>制作安置与购买共用同一有限检查；四向移动使用自己的基础值，仍属移速能力。</summary>
    bool ValidateActorUpgradeValues(Player actor, out string error, PlayerUpgradeKind? nextKind = null, int nextLevel = 0)
    {
        error = null;
        CharacterPhysics physics = actor != null ? actor.GetComponentInChildren<CharacterPhysics>(true) : null;
        // 安全：缺角色或物理组件时不把基础值当成零继续购买／安置。
        if (actor == null || physics == null)
        { error = "角色或物理组件缺失，不能购买升级或安置角色。"; return false; }

        double permanentMovement = 1d, permanentJump = 1d;
        foreach (PlayerEffectRecord effect in effects.ConfirmedPermanentEffects)
        {
            if (effect.Kind == PlayerEffectKind.MovementMultiplier) permanentMovement *= effect.Multiplier;
            else permanentJump *= effect.Multiplier;
        }
        foreach (PlayerEffectRecord effect in effects.CurrentLevelEffects)
        {
            if (effect.Scope != PlayerEffectScope.Permanent) continue;
            if (effect.Kind == PlayerEffectKind.MovementMultiplier) permanentMovement *= effect.Multiplier;
            else permanentJump *= effect.Multiplier;
        }

        PlayerUpgradeKind[] kinds = { PlayerUpgradeKind.MovementSpeed, PlayerUpgradeKind.MovementSpeed,
            PlayerUpgradeKind.JumpStrength, PlayerUpgradeKind.AirJumps };
        float[] bases = { physics.speed, physics.BaseFourWaySpeed, physics.upSpeed, actor.BaseAirJumps };
        string[] labels = { "水平移速", "四向移速", "起跳强度", "额外空中跳次数" };
        for (int i = 0; i < kinds.Length; i++)
        {
            PlayerUpgradeKind kind = kinds[i];
            int level = nextKind == kind ? nextLevel : effects.GetUpgradeLevel(kind);
            PlayerUpgradeDefinition definition = effects.Catalog != null ? effects.Catalog.Get(kind) : null;
            if (!Finite(bases[i]) || bases[i] < 0f || (definition == null && level != 0)
                || (definition != null && !definition.Validate(out _)))
            { error = $"{actor.name} 的{labels[i]}基础值或升级配置无效，请检查制作配置。"; return false; }
            float value = definition != null ? definition.Evaluate(bases[i], level) : bases[i];
            float ordinary = kind == PlayerUpgradeKind.MovementSpeed ? effects.MovementMultiplier
                : kind == PlayerUpgradeKind.JumpStrength ? effects.JumpMultiplier : 1f;
            double permanent = kind == PlayerUpgradeKind.MovementSpeed ? permanentMovement
                : kind == PlayerUpgradeKind.JumpStrength ? permanentJump : 1d;
            // 本局减益不能掩盖确认通关、清除减益之后的溢出；拒绝先于任何扣款或安置。
            if (!Finite(value) || value < 0f || !ValidAttributeProduct(value, ordinary)
                || !ValidAttributeProduct(value, permanent))
            { error = $"{actor.name} 的{labels[i]}等级结果无效或溢出，请检查基础属性和升级步长。"; return false; }
        }
        return true;
    }

    static bool ValidAttributeProduct(float value, double multiplier)
    {
        double result = value * multiplier;
        return !double.IsNaN(result) && !double.IsInfinity(result) && result >= 0d && result <= float.MaxValue;
    }

    public float EscapeUpgradeBaseValue(PlayerUpgradeKind kind)
    {
        CharacterPhysics physics = body != null ? body.GetComponentInChildren<CharacterPhysics>(true) : null;
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (kind == PlayerUpgradeKind.MovementSpeed) return physics != null ? physics.speed : 0f;
        if (kind == PlayerUpgradeKind.JumpStrength) return physics != null ? physics.upSpeed : 0f;
        if (kind == PlayerUpgradeKind.AirJumps) return body != null ? body.BaseAirJumps : 0f;
        if (kind == PlayerUpgradeKind.FormCost) return setup != null ? setup.FormChangeCost : 0f;
        // 地图与入包可有多个基础价；界面另列本关实际费用，不用一个假统一价代替。
        return 1f;
    }

    public string EscapeCostSummary()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (setup == null) return "";
        float property = effects.GetUpgradedValue(PlayerUpgradeKind.BackpackCost, setup.CognitionPropertyCost);
        float calculation = effects.GetUpgradedValue(PlayerUpgradeKind.BackpackCost, setup.CognitionCalculationCost);
        float form = effects.GetUpgradedValue(PlayerUpgradeKind.FormCost, setup.FormChangeCost);
        float occupied = levelRules.MapRecord != null ? levelRules.MapRecord.TotalOccupiedCost : 0f;
        return $"本关入包：性质 {property:0.##}／计算点 {calculation:0.##} · 转换 {form:0.##}\n"
            + $"已点亮地图实际占用 {occupied:0.##} · 模拟 {setup.SimulationCognitionPerSecond:0.##}/秒\n"
            + "降费用于此后发生的费用，既有地图占用不重算。";
    }
}
