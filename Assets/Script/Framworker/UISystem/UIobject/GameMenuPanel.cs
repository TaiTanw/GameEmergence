using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameMenuPanel : BasePanel
{
    GameObject menuCard;
    GameObject guideCard;
    Text bindingsText;

    protected override void OtherInit()
    {
        menuCard = transform.Find("MenuCard").gameObject;
        guideCard = transform.Find("OperationGuideCard").gameObject;
        bindingsText = guideCard.transform.Find("GuideBindingsText").GetComponent<Text>();
    }

    public override void ShowMe()
    {
        base.ShowMe();
        // 每次重新打开暂停菜单都从首页开始，指南不持有关卡或输入状态。
        guideCard.SetActive(false);
        menuCard.SetActive(true);
    }

    protected override void OnClickButton(string uiName)
    {
        if (uiName == "ReturnToMenuButton") SceneChangeMgr.Instance.ReturnToMenu();
        if (uiName == "OperationGuideButton")
        {
            // 打开时读取有效覆盖；暂停中的动作图仍保留绑定，不需要启用或复制输入。
            bindingsText.text = BindingSummary(DataAndInitMgr.Instance.asset);
            menuCard.SetActive(false);
            guideCard.SetActive(true);
        }
        if (uiName == "GuideBackButton") ReturnFromGuide();
    }

    /// <summary>指南内的 ESC 只退一层，游戏继续沿原菜单入口保持暂停。</summary>
    public bool ReturnFromGuide()
    {
        // 首页或未就绪时，交回原有关闭菜单流程。
        if (guideCard == null || !guideCard.activeSelf) return false;
        guideCard.SetActive(false);
        menuCard.SetActive(true);
        return true;
    }

    public static string BindingSummary(InputActionAsset asset)
    {
        return "向左　" + BindingCaption(asset, "Move", "negative")
            + "\n向右　" + BindingCaption(asset, "Move", "positive")
            + "\n向上／上浮　" + BindingCaption(asset, "Vertical", "positive")
            + "\n向下／下潜　" + BindingCaption(asset, "Vertical", "negative")
            + "\n跳跃　" + BindingCaption(asset, "Jump")
            + "\n疾跑（按住）　" + BindingCaption(asset, "Sprint")
            + "\n交互（点按／长按）　" + BindingCaption(asset, "Interact")
            + "\n漫游／模拟切换　" + BindingCaption(asset, "FormSwitch");
    }

    static string BindingCaption(InputActionAsset asset, string actionName, string part = null)
    {
        InputAction action = asset != null ? asset.FindAction(actionName) : null;
        // 缺失动作或主动清空的绑定照实显示，不回退成默认按键。
        if (action == null) return "未绑定";
        var captions = new List<string>();
        foreach (InputBinding binding in action.bindings)
        {
            // 方向读取指定复合部分，按钮读取自身绑定；忽略复合根节点。
            if (binding.isComposite || (part != null ? binding.name != part : binding.isPartOfComposite)) continue;
            if (string.IsNullOrEmpty(binding.effectivePath)) continue;
            string caption = InputControlPath.ToHumanReadableString(binding.effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (!captions.Contains(caption)) captions.Add(caption);
        }
        return captions.Count == 0 ? "未绑定" : string.Join(" / ", captions);
    }
}
