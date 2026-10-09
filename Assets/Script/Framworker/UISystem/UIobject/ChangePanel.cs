using UnityEngine;
using UnityEngine.UI;

public class ChangePanel : BasePanel
{
    Button simulationButton;

    protected override void OtherInit()
    {
        // 实际逻辑：复用当前面板的按钮样式，不另做形态按键或并行控制入口。
        Button template = FindUIObj<Button>("t2");
        if (template == null) return;
        simulationButton = Instantiate(template, template.transform.parent);
        simulationButton.name = "simulation";
        ((RectTransform)simulationButton.transform).anchoredPosition += Vector2.down * 130f;
        simulationButton.onClick.RemoveAllListeners();
        simulationButton.onClick.AddListener(() => OnClickButton("simulation"));
    }

    public override void ShowMe()
    {
        base.ShowMe();
        if (simulationButton == null) return;
        simulationButton.gameObject.SetActive(LevelStageMgr.Instance.HasExploration);
        Text label = simulationButton.GetComponentInChildren<Text>();
        if (label != null) label.text = LevelStageMgr.Instance.IsSimulation ? "返回漫游" : "进入模拟";
    }
    protected override void OnClickButton(string UIname)
    {
        base.OnClickButton(UIname);

        switch (UIname)
        {
            case "simulation":
                // 实际逻辑：安全退出拒绝时留在当前页；具体原因暂由统一转换入口打印。
                if (SceneChangeMgr.Instance.SetSimulation(!LevelStageMgr.Instance.IsSimulation))
                    UIMgr.Instance.HideOneUI<ChangePanel>();
                break;
            case "t1":
                // 实际逻辑：进入已保存的关。关掉本页，不在关卡里再打开。
                SceneChangeMgr.Instance.EnterSavedLevel();
                UIMgr.Instance.HideOneUI<ChangePanel>();
                break;
            case "t2":
                // 实际逻辑：没有通关道具时，用这个按钮走进顺序中的下一关。
                SceneChangeMgr.Instance.EnterNextLevel();
                UIMgr.Instance.HideOneUI<ChangePanel>();
                break;
            case "fan":
                // 实际逻辑：人还在开始场景时，返回只退界面栈。已经进入关卡才载回开始场景。
                if (!SceneChangeMgr.Instance.ReturnToMenu())
                    UIMgr.Instance.UIBack();
                break;
        }
    }
}
