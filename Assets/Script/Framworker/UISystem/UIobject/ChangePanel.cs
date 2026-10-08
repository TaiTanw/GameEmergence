using UnityEngine;

public class ChangePanel : BasePanel
{
    protected override void OnClickButton(string UIname)
    {
        base.OnClickButton(UIname);

        switch (UIname)
        {
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
