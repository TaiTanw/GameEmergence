using UnityEngine;
using UnityEngine.UI;

/// <summary>两页只展示规则结果；静态文案和布局留在预设，不在运行时补建或排版。</summary>
public sealed class EscapeChoiceView : MonoBehaviour
{
    GameObject costPage, abilityPage, emptyResult, positiveRow, negativeRow, lockedNote;
    Text budget, summary, positiveText, negativeText, finalValues;
    Button next, begin;
    Button[] purchases;
    Text[] levels, states;
    int displayedVersion = -1;
    bool showingAbilities;
    [Header("菜单显现（真实秒数）"), Min(.01f)] public float paperFadeSeconds = .18f;
    [Min(.01f)] public float fogExpandSeconds = .45f;
    [Min(.01f)] public float textFadeSeconds = .3f;
    [Min(.01f)] public float exitFadeSeconds = .16f;
    CanvasGroup contents;
    Image paper, backdrop;
    EscapeFogGraphic fog;
    float pageTime, flowTime, exitTime;
    bool pageInitialized, presentedAbilities, closing;
    bool PageRevealed => pageTime >= (showingAbilities ? fogExpandSeconds + textFadeSeconds : paperFadeSeconds);

    void Awake()
    {
        Transform card = transform.Find("Choices");
        contents = card.GetComponent<CanvasGroup>();
        paper = card.GetComponent<Image>();
        backdrop = GetComponent<Image>();
        fog = transform.Find("AbilityFog").GetComponent<EscapeFogGraphic>();
        costPage = card.Find("CostPage").gameObject;
        abilityPage = card.Find("AbilityPage").gameObject;
        budget = card.Find("EscapeBudgetText").GetComponent<Text>();
        summary = costPage.transform.Find("Content/EscapeCostText").GetComponent<Text>();
        lockedNote = costPage.transform.Find("Content/LockedPurchaseText").gameObject;
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
            purchases[i].onClick.AddListener(() => {
                // 淡入期间即使收到程序化点击，也不提交购买。
                if (purchases[index].interactable) SceneChangeMgr.Instance.PurchaseEscapeAbility(index);
            });
        }
        next.onClick.AddListener(() => {
            // 下一步仍由原规则一次性锁存结果，表现层不抽取能力。
            if (next.interactable && SceneChangeMgr.Instance.NextEscapeStep()) { showingAbilities = true; Refresh(); }
        });
        begin.onClick.AddListener(() => {
            // 只允许完整显字后确认，淡出完成再交给原消散流程。
            if (begin.interactable) { closing = true; exitTime = 0f; Refresh(); }
        });
    }

    void OnEnable() { if (budget != null) { Refresh(); Present(); } }
    void OnDisable() { closing = false; exitTime = 0f; }
    void Update()
    {
        Refresh();
        pageTime += Time.unscaledDeltaTime;
        flowTime += Time.unscaledDeltaTime;
        if (closing) exitTime += Time.unscaledDeltaTime;
        Present();
        // 暂停不冻结 UI 动画；交接仍要等原规则允许，关闭菜单后才消费这次确认。
        SceneChangeMgr host = SceneChangeMgr.Instance;
        if (closing && exitTime >= exitFadeSeconds && !host.GameMenuOpen && Time.timeScale > 0f)
        {
            closing = false;
            host.BeginEscapeFromAbilities();
        }
    }

    void Present()
    {
        float fade = 1f - Mathf.Clamp01(exitTime / Mathf.Max(.01f, exitFadeSeconds));
        float textProgress = showingAbilities
            ? (pageTime - fogExpandSeconds) / Mathf.Max(.01f, textFadeSeconds)
            : pageTime / Mathf.Max(.01f, paperFadeSeconds);
        contents.alpha = Mathf.SmoothStep(0f, 1f, textProgress) * fade;
        contents.interactable = PageRevealed && !closing;
        // 整屏底层持续拦截射线，透明显现时也不能点到场景或背后的界面。
        contents.blocksRaycasts = true;
        backdrop.color = new Color(.015f, .025f, .04f, (showingAbilities ? .32f : .72f) * fade);
        fog.color = new Color(1f, 1f, 1f, fade);
        fog.SetPresentation(Mathf.SmoothStep(0f, 1f, pageTime / Mathf.Max(.01f, fogExpandSeconds)), flowTime);
    }

    void Refresh()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        if (SceneLevelSetup.Current == null) return;
        bool ready = host.EscapeAutomaticReady;
        if (displayedVersion != host.EscapePreparationVersion)
        { displayedVersion = host.EscapePreparationVersion; showingAbilities = ready; pageInitialized = false; }
        // 只在换页或新一轮准备时重播显现；刷新文案与暂停返回不重新计时。
        if (!pageInitialized || presentedAbilities != showingAbilities)
        {
            pageInitialized = true; presentedAbilities = showingAbilities;
            pageTime = exitTime = 0f; closing = false;
            paper.enabled = !showingAbilities;
            fog.gameObject.SetActive(showingAbilities);
            Present();
        }
        bool active = host.EscapeSelectionPending && !host.IsEscapeHandoff && !host.GameMenuOpen
            && Time.timeScale > 0f && PageRevealed && !closing;
        float remaining = LevelStageMgr.Instance.CognitionRemaining;
        SetText(budget, showingAbilities ? "本场体能效果" : $"剩余认知 {remaining:0.##}");
        budget.color = showingAbilities ? Color.white : new Color(.18f, .14f, .20f);
        SetText(summary, host.EscapeCostSummary());
        costPage.SetActive(!showingAbilities);
        abilityPage.SetActive(showingAbilities);
        lockedNote.SetActive(ready);
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
