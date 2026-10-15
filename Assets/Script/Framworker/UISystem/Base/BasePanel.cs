using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class BasePanel : MonoBehaviour
{
    //容器存储控件
    protected Dictionary<string, Dictionary<Type, UIBehaviour>> keyValueDic = new Dictionary<string, Dictionary<Type, UIBehaviour>>();

    /// <summary>
    /// 默认名称组件，只有非默认名称才视作需监听组件
    /// </summary>
    private static List<string> defaultNameList = new List<string>() 
    { 
        "Image",
        "Text (TMP)",
        "RawImage",
        "Background",
        "Checkmark",
        "Label",
        "Text (Legacy)",
        "Arrow",
        "Placeholder",
        "Fill",
        "Handle",
        "Viewport",
        "Scrollbar Horizontal",
        "Scrollbar Vertical"
    };

    

    protected virtual void Awake()
    {
        //重写子类方法，用于自定义需要监听的UI控件
        InitAll();
    }

    /// <summary>
    /// 将函数传出，由子类重写
    /// </summary>
    /// <param name="UIname">用于区分同类组件不同名称</param>
    protected virtual void OnClickButton(string UIname)
    {

    }

    /// <summary>预设按钮与动态按钮共用的点击反馈；重复接线先撤再加，避免一次点击叠音。</summary>
    public static void BindButtonSound(Button button)
    {
        button.onClick.RemoveListener(PlayButtonSound);
        button.onClick.AddListener(PlayButtonSound);
    }

    static void PlayButtonSound() => MusicMgr.Instance.PlayButtonClick();
    protected virtual void SliderValueChange(string UIname,float value)
    {

    }
    protected virtual void ToggleValueChange(string sliderName, bool value)
    {

    }

    public virtual void ShowMe() 
    {
        this.gameObject.SetActive(true);
    }
    public virtual void HideMe() 
    {
        this.gameObject.SetActive(false);
    }


    /// <summary>
    /// 自动注册一类UI事件关联
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    public void SetListenIn<T>() where T : UIBehaviour
    {
        T[] values = GetComponentsInChildren<T>(true);//true表示失活对象也能检测
        foreach (T t in values)
        {
            //不能是默认名称
            if (!defaultNameList.Contains(t.gameObject.name))
            {
                //字典无值
                if (!keyValueDic.ContainsKey(t.gameObject.name))
                {
                    keyValueDic.Add(t.gameObject.name, new Dictionary<Type, UIBehaviour>());
                }
                
                if (t is Button button)
                {
                    BindButtonSound(button);
                    button.onClick.AddListener(() =>
                    {
                        OnClickButton(t.gameObject.name);
                    });
                }
                else if (t is Slider)
                {
                    (t as Slider).onValueChanged.AddListener((value) =>
                    {
                        SliderValueChange(t.gameObject.name, value);
                    });
                }
                else if (t is Toggle)
                {
                    (t as Toggle).onValueChanged.AddListener((value) =>
                    {
                        ToggleValueChange(t.gameObject.name, value);
                    });
                }
                //同一对象可能挂载多个组件
                keyValueDic[t.gameObject.name].Add(typeof(T), t);
            }
        }
        
    }


    /// <summary>
    /// 初始化UI组件内容，默认不填即为监听常见组件
    /// </summary>
    /// <param name="types"></param>
    public void InitAll(params Type[] types)
    {
        List<Type> list = new List<Type>(types);
        if (list.Count == 0)
        {
            SetListenIn<Button>();
            SetListenIn<Slider>();
            SetListenIn<Toggle>();
            SetListenIn<InputField>();
            //根据组件使用频率，可适当增减注释
            //SetListenIn<ScrollRect>();
            //SetListenIn<Dropdown>();

            SetListenIn<Text>();
            SetListenIn<TextMeshProUGUI>();
            SetListenIn<Image>();
        }
        else
        {
            foreach (Type t in list)
            {
                // 使用反射调用泛型方法
                var method = this.GetType().GetMethod("SetListenIn",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (method != null)
                {
                    var genericMethod = method.MakeGenericMethod(t);
                    genericMethod.Invoke(this, null);
                }
            }
        }
        OtherInit();
    }

    protected virtual void OtherInit() { }//提供子类重写的用于添加自定义函数

    /// <summary>
    /// 找到具体控件
    /// </summary>
    /// <typeparam name="T">控件类型</typeparam>
    /// <param name="name">对象名称</param>
    public T FindUIObj<T>(string name) where T : UIBehaviour
    {
        if (keyValueDic.ContainsKey(name))
        {
            if(keyValueDic[name].ContainsKey(typeof(T)))
            {
                return (keyValueDic[name][typeof(T)] as T);
            }
            else
            {
                print($"所指定的控件没有{typeof(T)}的相关组件");
            }
        }
        else
        {
            print("未找到相关控件");
        }
        return null;

    }

    /// <summary>
    /// 添加自定义UI事件
    /// </summary>
    /// <param name="uiName">控件名称</param>
    /// <param name="evType">事件类型</param>
    /// <param name="callBack">函数回调</param>
    public void AddEventTriggerListener<T>(string uiName, EventTriggerType evType, UnityAction<BaseEventData> callBack) where T : UIBehaviour
    {
        T uiobj = FindUIObj<T>(uiName);
        if (uiobj == null)
        {
            print("未找到控件");
            return;
        }

        EventTrigger evTrigger = uiobj.gameObject.GetComponent<EventTrigger>();
        if (evTrigger == null)
            evTrigger = uiobj.gameObject.AddComponent<EventTrigger>();

        //设置某一自定义事件
        EventTrigger.Entry entry=new EventTrigger.Entry();
        entry.eventID = evType;
        entry.callback.AddListener(callBack);

        //添加此自定义事件
        evTrigger.triggers.Add(entry);
    }

    //说明：若某个会多次实例化的小型面板，不需要关联太多组件，重写了基类的awake方法且移除base，那么在使用添加自定义事件的时候
    //一定注意传入的控件名称必须在之前已经用SetListenIn关联过
}
