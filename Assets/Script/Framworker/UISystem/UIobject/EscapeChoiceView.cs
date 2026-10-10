using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>固定池只读显示与购买请求；预算及效果始终交给关卡规则结算。</summary>
public sealed class EscapeChoiceView : MonoBehaviour
{
    readonly List<Button> choices = new List<Button>();
    SceneLevelSetup displayedSetup;
    Text budget;

    void Awake() => budget = transform.Find("EscapeBudgetText").GetComponent<Text>();

    void OnEnable() => Refresh();
    void Update() => Refresh();

    void Refresh()
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        if (setup == null) return;
        if (setup != displayedSetup)
        {
            foreach (Button choice in choices) Destroy(choice.gameObject);
            choices.Clear();
            displayedSetup = setup;
            Transform template = transform.Find("Choices/ChoiceTemplate");
            for (int i = 0; i < setup.EscapeAbilities.Count; i++)
            {
                int index = i;
                EscapeAbilityOffer offer = setup.EscapeAbilities[i];
                Button button = Instantiate(template.gameObject, template.parent).GetComponent<Button>();
                button.name = "EscapeChoice" + i;
                button.GetComponentInChildren<Text>().text = $"{offer.Label}   ×{offer.Multiplier:0.##}   ·   {offer.Cost:0.##} 认知";
                button.onClick.AddListener(() => SceneChangeMgr.Instance.PurchaseEscapeAbility(index));
                button.gameObject.SetActive(true);
                choices.Add(button);
            }
        }
        float remaining = LevelStageMgr.Instance.CognitionRemaining;
        budget.text = $"选择一项能力开始逃生 · 可用认知 {Mathf.Floor(remaining):0}";
        for (int i = 0; i < choices.Count; i++)
        {
            EscapeAbilityOffer offer = setup.EscapeAbilities[i];
            choices[i].interactable = SceneChangeMgr.Instance.EscapeSelectionPending
                && !SceneChangeMgr.Instance.GameMenuOpen && Time.timeScale > 0f
                && remaining >= offer.Cost;
            choices[i].GetComponentInChildren<Text>().text = $"{offer.Label}   ×{offer.Multiplier:0.##}   ·   {offer.Cost:0.##} 认知"
                + (remaining < offer.Cost ? " · 认知不足" : "");
        }
    }
}
