using UnityEngine;
using UnityEngine.UI;

/// <summary>两页只展示规则结果；静态文案和布局留在预设，不在运行时补建或排版。</summary>
public sealed class EscapeChoiceView : MonoBehaviour
{
    GameObject costPage, abilityPage, emptyResult, positiveRow, negativeRow, lockedNote;
    Text budget, summary, positiveText, negativeText, finalValues;
    Button costTab, abilityTab, next, begin;
    Button[] purchases;
    Text[] levels, states;
    int displayedVersion = -1;
    bool showingAbilities;

    void Awake()
    {
        Transform card = transform.Find("Choices");
        costPage = card.Find("CostPage").gameObject;
        abilityPage = card.Find("AbilityPage").gameObject;
        budget = card.Find("EscapeBudgetText").GetComponent<Text>();
        summary = costPage.transform.Find("Content/EscapeCostText").GetComponent<Text>();
        lockedNote = costPage.transform.Find("Content/LockedPurchaseText").gameObject;
        costTab = card.Find("Tabs/CostTabButton").GetComponent<Button>();
        abilityTab = card.Find("Tabs/AbilityTabButton").GetComponent<Button>();
        next = card.Find("NextEscapeButton").GetComponent<Button>();
        begin = card.Find("BeginEscapeButton").GetComponent<Button>();
        emptyResult = abilityPage.transform.Find("EmptyResultText").gameObject;
        positiveRow = abilityPage.transform.Find("PositiveResult").gameObject;
        negativeRow = abilityPage.transform.Find("NegativeResult").gameObject;
        positiveText = positiveRow.transform.Find("PositiveResultValueText").GetComponent<Text>();
        negativeText = negativeRow.transform.Find("NegativeResultValueText").GetComponent<Text>();
        finalValues = abilityPage.transform.Find("FinalValuesText").GetComponent<Text>();
        purchases = new Button[3]; levels = new Text[3]; states = new Text[3];
        string[] names = { "MapCostRow", "BackpackCostRow", "FormCostRow" };
        for (int i = 0; i < purchases.Length; i++)
        {
            int index = i;
            Transform row = costPage.transform.Find("Content/" + names[i]);
            purchases[i] = row.Find(names[i] + "PurchaseButton").GetComponent<Button>();
            levels[i] = row.Find(names[i] + "ValuesText").GetComponent<Text>();
            states[i] = row.Find(names[i] + "StateText").GetComponent<Text>();
            purchases[i].onClick.AddListener(() => SceneChangeMgr.Instance.PurchaseEscapeAbility(index));
        }
        costTab.onClick.AddListener(() => { showingAbilities = false; Refresh(); });
        abilityTab.onClick.AddListener(() => { if (SceneChangeMgr.Instance.EscapeAutomaticReady) { showingAbilities = true; Refresh(); } });
        next.onClick.AddListener(() => { if (SceneChangeMgr.Instance.NextEscapeStep()) { showingAbilities = true; Refresh(); } });
        begin.onClick.AddListener(() => SceneChangeMgr.Instance.BeginEscapeFromAbilities());
    }

    void OnEnable() { if (budget != null) Refresh(); }
    void Update() => Refresh();

    void Refresh()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        if (SceneLevelSetup.Current == null) return;
        bool ready = host.EscapeAutomaticReady;
        if (displayedVersion != host.EscapePreparationVersion)
        { displayedVersion = host.EscapePreparationVersion; showingAbilities = ready; }
        bool active = host.EscapeSelectionPending && !host.IsEscapeHandoff && !host.GameMenuOpen && Time.timeScale > 0f;
        float remaining = LevelStageMgr.Instance.CognitionRemaining;
        SetText(budget, $"剩余认知 {remaining:0.##}");
        SetText(summary, host.EscapeCostSummary());
        costPage.SetActive(!showingAbilities);
        abilityPage.SetActive(showingAbilities);
        lockedNote.SetActive(ready);
        abilityTab.interactable = active && ready;
        costTab.interactable = active;
        next.gameObject.SetActive(!showingAbilities && !ready);
        begin.gameObject.SetActive(showingAbilities && ready);
        next.interactable = begin.interactable = active;
        for (int i = 0; i < purchases.Length; i++)
        {
            if (i >= host.EscapeOffers.Count) { purchases[i].interactable = false; continue; }
            PlayerUpgradeDefinition item = host.EscapeOffers[i];
            int level = host.PlayerEffects.GetUpgradeLevel(item.Kind);
            bool full = level >= item.MaxLevel;
            bool affordable = remaining >= item.Cost;
            float basis = host.EscapeUpgradeBaseValue(item.Kind);
            float current = item.Evaluate(basis, level), upgraded = full ? current : item.Evaluate(basis, level + 1);
            string step = item.Operation == PlayerUpgradeOperation.Multiply ? $"每级 ×{item.Step:0.###}" : $"每级 {item.Step:+0.###;-0.###;0}";
            SetText(levels[i], $"等级 {level}/{item.MaxLevel} · {current:0.###} → {upgraded:0.###}\n{step} · 每次 {item.Cost:0.##} 认知");
            string state = ready ? "购买已关闭" : !item.Enabled ? "未开放" : full ? "已满级" : !affordable ? "认知不足" : "可重复购买";
            SetText(states[i], state);
            purchases[i].interactable = active && !ready && item.Enabled && !full && affordable;
        }
        PlayerAbilityEffect? positive = host.PlayerEffects.PositiveAbility, negative = host.PlayerEffects.NegativeAbility;
        positiveRow.SetActive(positive.HasValue);
        negativeRow.SetActive(negative.HasValue);
        emptyResult.SetActive(!positive.HasValue && !negative.HasValue);
        if (positive.HasValue) SetText(positiveText, positive.Value.Description);
        if (negative.HasValue) SetText(negativeText, negative.Value.Description);
        if (ready) SetText(finalValues, host.AutomaticAbilityPreview());
    }

    static void SetText(Text target, string value)
    { if (target.text != value) target.text = value; }
}
