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
    private Text FT;
    private Text interactT;



    void Start()
    {
        //找到文本控件

        leftT = FindUIObj<Text>("leftT");
        rightT = FindUIObj<Text>("rightT");
        FT = FindUIObj<Text>("FT");
        interactT = FindUIObj<Text>("interactT");
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

            case "ff":
                print("跳跃修改");
                InputControlMgr.Instance.StartRebind("Jump", 0, UpdateUIvalue);
                break;
            case "interact":
                print("交互修改");
                InputControlMgr.Instance.StartRebind("Interact", 0, UpdateUIvalue);
                break;
            case "out":
                //print("退出改建面板");
                //存入数据(可放入面板退出按钮）
                //DataAndInitMgr.Instance.SeveInputValue();
                //隐藏
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
        FT.text = asset.FindAction("Jump").bindings[0].effectivePath;
        interactT.text = asset.FindAction("Interact").bindings[0].effectivePath;
    }   
}
