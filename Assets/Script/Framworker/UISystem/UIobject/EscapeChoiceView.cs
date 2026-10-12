using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只显示本次锁存候选并发送购买／下一步请求，等级、付款和阶段归规则侧。</summary>
public sealed class EscapeChoiceView : MonoBehaviour
{
    readonly List<Button> choices = new List<Button>();
    int displayedVersion = -1;
    Text budget, costSummary, empty, penalty;
    Button preserve, next;
    Transform content, template;
    GameObject keepTitle, offerTitle;
    void Awake()
    {
        budget = transform.Find("Choices/EscapeBudgetText").GetComponent<Text>();
        content = transform.Find("Choices/Viewport/Content");
        template = content.Find("ChoiceTemplate");
        costSummary = content.Find("EscapeCostText").GetComponent<Text>();
        empty = content.Find("EmptyPoolText").GetComponent<Text>();
        penalty = content.Find("PenaltyText").GetComponent<Text>();
        keepTitle = content.Find("KeepTitle").gameObject;
        offerTitle = content.Find("OfferTitle").gameObject;
        preserve = content.Find("PreserveSceneButton").GetComponent<Button>();
        next = transform.Find("Choices/NextEscapeButton").GetComponent<Button>();
        preserve.onClick.AddListener(() => SceneChangeMgr.Instance.PurchasePreserveScene());
        next.onClick.AddListener(() => {
            SceneChangeMgr host = SceneChangeMgr.Instance;
            if (host.EscapeNegativePending) host.ConfirmEscapePenalty();
            else host.NextEscapeStep();
        });
    }
    void OnEnable() { if (budget != null) Refresh(); }
    void Update() => Refresh();
    void Refresh()
    {
        SceneChangeMgr host = SceneChangeMgr.Instance;
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (setup == null) return;
        // 同次准备候选不变；重建由规则版本驱动，菜单关闭不会重新抽取。
        if (displayedVersion != host.EscapePreparationVersion)
        {
            foreach (Button choice in choices) { choice.gameObject.SetActive(false); Destroy(choice.gameObject); }
            choices.Clear();
            displayedVersion = host.EscapePreparationVersion;
            for (int i = 0; i < host.EscapeOffers.Count; i++)
            {
                int index = i;
                Button button = Instantiate(template.gameObject, content).GetComponent<Button>();
                button.name = "EscapeChoice" + i;
                button.onClick.AddListener(() => host.PurchaseEscapeAbility(index));
                button.transform.SetSiblingIndex(template.GetSiblingIndex() + i + 1);
                button.gameObject.SetActive(true);
                choices.Add(button);
            }
        }
        float remaining = LevelStageMgr.Instance.CognitionRemaining;
        bool negative = host.EscapeNegativePending;
        bool active = host.EscapeSelectionPending && !host.GameMenuOpen && Time.timeScale > 0f;
        budget.text = $"逃生准备 · 剩余认知 {remaining:0.##}";
        keepTitle.SetActive(!negative); offerTitle.SetActive(!negative);
        preserve.gameObject.SetActive(!negative); costSummary.gameObject.SetActive(!negative);
        empty.gameObject.SetActive(!negative && choices.Count == 0);
        penalty.gameObject.SetActive(negative);
        empty.text = "本次没有可升级项目，可以直接下一步。";
        costSummary.text = host.EscapeCostSummary();
        preserve.interactable = active && !host.PreserveEscapeScene && remaining >= setup.PreserveSceneCost;
        preserve.GetComponentInChildren<Text>().text = host.PreserveEscapeScene ? "已购买 · 保留当前现场"
            : $"保留现场 · {setup.PreserveSceneCost:0.##} 认知" + (remaining < setup.PreserveSceneCost ? " · 认知不足" : "");
        for (int i = 0; i < choices.Count; i++)
        {
            PlayerUpgradeDefinition item = host.EscapeOffers[i];
            int level = host.PlayerEffects.GetUpgradeLevel(item.Kind);
            bool full = !host.PlayerEffects.CanUpgrade(item.Kind);
            float basis = host.EscapeUpgradeBaseValue(item.Kind);
            float current = item.Evaluate(basis, level);
            float upgraded = item.Evaluate(basis, level + 1);
            string operation = item.Operation == PlayerUpgradeOperation.Multiply ? $"每级 ×{item.Step:0.###}"
                : $"每级 {item.Step:+0.###;-0.###;0}";
            string values = $"{current:0.##} → {upgraded:0.##} · {operation}";
            if (item.Kind == PlayerUpgradeKind.MapCost || item.Kind == PlayerUpgradeKind.BackpackCost)
                values = item.Operation == PlayerUpgradeOperation.Multiply
                    ? $"费用倍率 ×{current:0.###} → ×{upgraded:0.###} · {operation}"
                    : $"累计减费 {(-item.Step * level):0.##} → {(-item.Step * (level + 1)):0.##} · {operation}";
            choices[i].gameObject.SetActive(!negative);
            choices[i].interactable = active && !negative && !full && remaining >= item.Cost;
            choices[i].GetComponentInChildren<Text>().text = $"{item.Label} · 等级 {level}/{item.MaxLevel}\n"
                + (full ? "已满级" : $"{values} · 升级 {item.Cost:0.##} 认知" + (remaining < item.Cost ? " · 认知不足" : ""));
        }
        if (negative && host.SelectedEscapePenalty != null)
        {
            EscapePenaltyOffer result = host.SelectedEscapePenalty;
            penalty.text = $"本次逃生负面\n\n{result.Label} · ×{result.Multiplier:0.##}\n\n确认后开始逃生，仅本局生效。";
        }
        next.interactable = active;
        next.GetComponentInChildren<Text>().text = negative ? "确认负面并开始逃生" : "下一步";
    }
}
