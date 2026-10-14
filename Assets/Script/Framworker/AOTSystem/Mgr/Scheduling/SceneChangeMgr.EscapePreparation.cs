using System.Collections.Generic;
using UnityEngine;

public partial class SceneChangeMgr
{
    readonly List<PlayerUpgradeDefinition> escapeOffers = new List<PlayerUpgradeDefinition>();
    PlayerAbilityPool abilityPool;
    bool preparationConfirmed;
    int preparationVersion;
    public IReadOnlyList<PlayerUpgradeDefinition> EscapeOffers => escapeOffers;
    public PlayerEffectLedger PlayerEffects => effects;
    public int EscapePreparationVersion => preparationVersion;
    public bool EscapeAutomaticReady => EscapeSelectionPending && effects.AutomaticResultLocked;

    bool EnsureUpgradeCatalog()
    {
        if (effects.Catalog != null) return true;
        PlayerUpgradeCatalog catalog = Resources.Load<PlayerUpgradeCatalog>("PlayerUpgradeCatalog");
        if (catalog != null && effects.Configure(catalog)) return true;
        ShowGameplayMessage("认知降费配置缺失或无效，请检查制作配置。");
        Debug.LogError("认知降费配置缺失或无效：PlayerUpgradeCatalog。");
        return false;
    }

    bool CanPrepareEscape => EscapeSelectionPending && !loading && !refreshing && !GameMenuOpen
        && !escapeHandoff && Time.timeScale > 0f;

    void ResetEscapePreparation()
    {
        escapeOffers.Clear();
        effects.ClearAutomaticAbilities();
        preparationConfirmed = false;
        preparationVersion++;
    }

    void ListEscapeCostOffers()
    {
        // 固定列全三项，未开放／满级项由界面说明，不随机挑选或补位。
        foreach (PlayerUpgradeKind kind in new[] { PlayerUpgradeKind.MapCost, PlayerUpgradeKind.BackpackCost, PlayerUpgradeKind.FormCost })
            escapeOffers.Add(effects.Catalog.Get(kind));
    }

    bool ValidateAutomaticPool(SceneLevelSetup setup)
    {
        if (setup == null || setup.Teaching) return false;
        string error = null;
        abilityPool = Resources.Load<PlayerAbilityPool>(PlayerAbilityPool.ResourceName);
        if (abilityPool == null) error = "全局自动能力池缺失：PlayerAbilityPool。";
        else abilityPool.Validate(out error);
        if (error == null) return true;
        ShowGameplayMessage(error);
        Debug.LogError(error, setup);
        return false;
    }

    public bool NextEscapeStep()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (!CanPrepareEscape || effects.AutomaticResultLocked || preparationConfirmed || setup == null) return false;
        // 配置先完整检查，不在失败后重抽或制造部分授予。
        if (!ValidEscapeSetup(setup) || !ValidateAutomaticPool(setup)) return false;
        float ratio = levelRules.Cognition.Remaining / setup.InitialCognition;
        PlayerAbilityEffect? positive = null, negative = null;
        bool hasPositive = PlayerAbilityPool.Triggers(PlayerAbilityPool.PositiveProbability(ratio), Random.value);
        bool hasNegative = PlayerAbilityPool.Triggers(PlayerAbilityPool.NegativeProbability(ratio), Random.value);
        if (hasPositive) positive = abilityPool.Draw(true, Random.value);
        if (hasNegative) negative = abilityPool.Draw(false, Random.value);
        if (!effects.TryGrantAutomaticAbilities(positive, negative))
        { ShowGameplayMessage("本次自动能力数值无效，未授予效果。"); return false; }
        preparationVersion++;
        return true;
    }

    public bool BeginEscapeFromAbilities()
    {
        if (!CanPrepareEscape || !EscapeAutomaticReady || preparationConfirmed) return false;
        preparationConfirmed = true;
        if (CompleteEscapePreparation()) return true;
        preparationConfirmed = false;
        return false;
    }

    bool ValidateActorUpgradeValues(Player actor, out string error)
    {
        CharacterPhysics[] components = actor != null ? actor.GetComponentsInChildren<CharacterPhysics>(true) : null;
        if (components == null || components.Length != 1)
        { error = "角色或物理组件缺失，不能安置角色。"; return false; }
        CharacterPhysics physics = components[0];
        if (!physics.ValidateMotionConfiguration(out error) || !actor.AirJumpLimits.Validate(actor.BaseAirJumps, true, out error))
        { error = actor.name + "：" + error; return false; }
        return true;
    }

    public float EscapeUpgradeBaseValue(PlayerUpgradeKind kind)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        return kind == PlayerUpgradeKind.FormCost && setup != null ? setup.FormChangeCost : 1f;
    }

    public string EscapeCostSummary()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (setup == null) return "";
        float property = effects.GetUpgradedValue(PlayerUpgradeKind.BackpackCost, setup.CognitionPropertyCost);
        float calculation = effects.GetUpgradedValue(PlayerUpgradeKind.BackpackCost, setup.CognitionCalculationCost);
        float form = effects.GetUpgradedValue(PlayerUpgradeKind.FormCost, setup.FormChangeCost);
        float occupied = levelRules.MapRecord != null ? levelRules.MapRecord.TotalOccupiedCost : 0f;
        return $"性质 {property:0.##}／计算点 {calculation:0.##} · 转换 {form:0.##}\n"
            + $"地图实际占用 {occupied:0.##} · 模拟 {setup.SimulationCognitionPerSecond:0.##}/秒";
    }

    public string AutomaticAbilityPreview()
    {
        CharacterPhysics physics = body != null ? body.GetComponentInChildren<CharacterPhysics>(true) : null;
        if (physics == null) return "";
        double jumps = effects.ResolveMotionAttribute(PlayerAbilityKind.AirJumps, body.BaseAirJumps);
        return $"移动速度 {physics.EffectiveMovementSpeed:0.##} · 游泳速度 {physics.EffectiveFourWaySpeed:0.##}\n"
            + $"起跳初速度 {physics.EffectiveJumpSpeed:0.##} · 额外空中跳 {(int)body.AirJumpLimits.Limit(jumps)} 次";
    }
}
