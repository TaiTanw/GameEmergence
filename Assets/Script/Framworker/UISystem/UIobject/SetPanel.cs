using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class SetPanel : BasePanel
{
    BasePanel page;
    string opening;
    int request;

    protected override void OnClickButton(string name)
    {
        if (name == "hui")
        {
            // 子页返回先检查改键冲突；不把重复键带回开始界面。
            if (page is InputChangePanel keys && !keys.CanLeave()) return;
            ++request;
            UIMgr.Instance.HideOneUI<MusicSetPanel>();
            UIMgr.Instance.HideOneUI<InputChangePanel>();
            if (page != null || opening != null)
            { page = null; opening = null; ShowEntrances(true); }
            else UIMgr.Instance.UIBack();
            return;
        }
        // 等待地址资源加载时不重复打开另一子页。
        if (page != null || opening != null) return;
        int serial = ++request;
        if (name == "mu")
        { opening = name; ShowEntrances(false); UIMgr.Instance.ShowOneUI<MusicSetPanel>(callBack: child => AttachPage(child, serial)); }
        if (name == "ke")
        { opening = name; ShowEntrances(false); UIMgr.Instance.ShowOneUI<InputChangePanel>(callBack: child => AttachPage(child, serial)); }
    }

    void AttachPage(BasePanel child, int serial)
    {
        // 安全：返回／隐藏之后完成的加载不能重新盖住入口。
        if (!isActiveAndEnabled || serial != request) { child.HideMe(); return; }
        page = child; opening = null;
        child.transform.SetParent(transform.Find("SettingsContent"), false);
        child.transform.localScale = Vector3.one;
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        child.ShowMe();
    }

    void ShowEntrances(bool show)
    { transform.Find("mu").gameObject.SetActive(show); transform.Find("ke").gameObject.SetActive(show); }

    public override void ShowMe() { base.ShowMe(); ShowEntrances(true); }
    public override void HideMe()
    {
        ++request;
        UIMgr.Instance.HideOneUI<MusicSetPanel>(); UIMgr.Instance.HideOneUI<InputChangePanel>();
        page = null; opening = null;
        base.HideMe();
    }
}
