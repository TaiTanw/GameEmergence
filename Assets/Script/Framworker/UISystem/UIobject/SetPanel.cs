using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class SetPanel : BasePanel
{
    BasePanel page;
    string opening;
    int request;
    bool displayOpen, selectedWindowed;
    int selectedResolution;

    protected override void OtherInit()
    { transform.Find("SettingsContent/DisplaySettingsArea")?.gameObject.SetActive(false); }

    protected override void OnClickButton(string name)
    {
        if (name == "hui")
        {
            // 显示子页使用同一个外层返回，未应用的选择直接放弃。
            if (displayOpen)
            { displayOpen = false; transform.Find("SettingsContent/DisplaySettingsArea").gameObject.SetActive(false); ShowEntrances(true); return; }
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
        if (displayOpen)
        {
            if (name == "DisplayMode") selectedWindowed = !selectedWindowed;
            if (name == "DisplayResolution") selectedResolution = (selectedResolution + 1) % DisplaySettings.Instance.Resolutions.Count;
            if (name == "DisplayApply") DisplaySettings.Instance.Apply(selectedResolution, selectedWindowed);
            RefreshDisplayChoices();
            return;
        }
        // 等待地址资源加载时不重复打开另一子页。
        if (page != null || opening != null) return;
        int serial = ++request;
        if (name == "di")
        {
            displayOpen = true; ShowEntrances(false);
            selectedResolution = DisplaySettings.Instance.ResolutionIndex;
            selectedWindowed = DisplaySettings.Instance.Windowed;
            transform.Find("SettingsContent/DisplaySettingsArea").gameObject.SetActive(true);
            RefreshDisplayChoices();
        }
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
    { transform.Find("mu").gameObject.SetActive(show); transform.Find("ke").gameObject.SetActive(show); transform.Find("di")?.gameObject.SetActive(show); }

    void RefreshDisplayChoices()
    {
        Transform area = transform.Find("SettingsContent/DisplaySettingsArea");
        Vector2Int size = DisplaySettings.Instance.Resolutions[selectedResolution];
        area.Find("DisplayResolution").GetComponentInChildren<UnityEngine.UI.Text>().text = $"分辨率：{size.x} × {size.y}　›";
        area.Find("DisplayMode").GetComponentInChildren<UnityEngine.UI.Text>().text = "模式：" + (selectedWindowed ? "窗口　›" : "全屏　›");
        bool applied = selectedResolution == DisplaySettings.Instance.ResolutionIndex && selectedWindowed == DisplaySettings.Instance.Windowed;
        area.Find("DisplayApply").GetComponentInChildren<UnityEngine.UI.Text>().text = applied ? "已应用" : "应用";
    }

    public override void ShowMe() { base.ShowMe(); ShowEntrances(true); }
    public override void HideMe()
    {
        ++request;
        displayOpen = false;
        transform.Find("SettingsContent/DisplaySettingsArea")?.gameObject.SetActive(false);
        UIMgr.Instance.HideOneUI<MusicSetPanel>(); UIMgr.Instance.HideOneUI<InputChangePanel>();
        page = null; opening = null;
        base.HideMe();
    }
}
