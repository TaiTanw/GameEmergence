public class GameMenuPanel : BasePanel
{
    protected override void OnClickButton(string uiName)
    {
        // 实际逻辑：游戏菜单只有返回主菜单；继续游戏由固定 ESC 关闭菜单完成。
        if (uiName == "ReturnToMenuButton") SceneChangeMgr.Instance.ReturnToMenu();
    }
}
