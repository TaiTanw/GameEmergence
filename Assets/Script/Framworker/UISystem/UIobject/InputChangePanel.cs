using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

public class InputChangePanel : BasePanel
{

    private Text leftT;
    private Text rightT;
    private Text upT;
    private Text downT;
    private Text FT;
    private Text interactT;
    private Text conflictT;



    void Start()
    {
        //找到文本控件

        leftT = FindUIObj<Text>("leftT");
        rightT = FindUIObj<Text>("rightT");
        upT = FindUIObj<Text>("upT");
        downT = FindUIObj<Text>("downT");
        FT = FindUIObj<Text>("FT");
        interactT = FindUIObj<Text>("interactT");
        conflictT = FindUIObj<Text>("conflictT");
        //初始化文本面板显示
        UpdateUIvalue();


    }
    public override void HideMe()
    {
        base.HideMe();
        //print("隐藏面板");
        this.gameObject.SetActive(false);
    }

    protected override void OnClickButton(string UIname)
    {
        base.OnClickButton(UIname);
        switch (UIname)
        {

            case "left":
                print("左键修改");
                InputControlMgr.Instance.StartRebind("Move", 1, UpdateUIvalue);
                break;

            case "right":
                print("右键修改");
                InputControlMgr.Instance.StartRebind("Move", 2, UpdateUIvalue);
                break;
            case "up":
                StartCompositeRebind("Vertical", "positive");
                break;
            case "down":
                StartCompositeRebind("Vertical", "negative");
                break;

            case "ff":
                print("跳跃修改");
                InputControlMgr.Instance.StartRebind("Jump", 0, UpdateUIvalue);
                break;
            case "interact":
                print("交互修改");
                InputControlMgr.Instance.StartRebind("Interact", 0, UpdateUIvalue);
                break;
            case "out":
                if (InputControlMgr.Instance.HasPlayerBindingConflict(out string conflict))
                {
                    // 实际逻辑：有冲突时留在面板，等玩家改开重复键。
                    if (conflictT != null)
                        conflictT.text = conflict;
                    return;
                }
                if (conflictT != null)
                    conflictT.text = "";
                UIMgr.Instance.UIBack();
                break;
        }
       
    }
    
    /// <summary>
    /// 面板更新。显示覆盖之后的有效路径。
    /// </summary>
    public void UpdateUIvalue()
    {
        InputActionAsset asset = DataAndInitMgr.Instance.asset;
        leftT.text = asset.FindAction("Move").bindings[1].effectivePath;
        rightT.text = asset.FindAction("Move").bindings[2].effectivePath;
        if (upT != null)
            upT.text = CompositePartPath(asset, "Vertical", "positive");
        if (downT != null)
            downT.text = CompositePartPath(asset, "Vertical", "negative");
        FT.text = asset.FindAction("Jump").bindings[0].effectivePath;
        interactT.text = asset.FindAction("Interact").bindings[0].effectivePath;
    }

    void StartCompositeRebind(string actionName, string partName)
    {
        InputAction action = DataAndInitMgr.Instance.asset.FindAction(actionName);
        int index = CompositePartIndex(action, partName);
        // 安全：动作或方向绑定位缺失时不开始改键。
        if (index < 0) return;
        InputControlMgr.Instance.StartRebind(actionName, index, UpdateUIvalue);
    }

    static string CompositePartPath(InputActionAsset asset, string actionName, string partName)
    {
        InputAction action = asset.FindAction(actionName);
        int index = CompositePartIndex(action, partName);
        if (index < 0) return "";
        return action.bindings[index].effectivePath;
    }

    static int CompositePartIndex(InputAction action, string partName)
    {
        if (action == null) return -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            // 实际逻辑：只认复合轴上的这一侧，不把父绑定当成按键。
            if (action.bindings[i].isPartOfComposite && action.bindings[i].name == partName)
                return i;
        }
        return -1;
    }
}
