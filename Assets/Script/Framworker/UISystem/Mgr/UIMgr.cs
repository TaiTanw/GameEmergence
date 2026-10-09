using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;//注意避免命名空间污染
public enum E_UILayer
{
    /// <summary>
    /// 最底层
    /// </summary>
    Bottom,
    /// <summary>
    /// 中层
    /// </summary>
    Middle,
    /// <summary>
    /// 高层
    /// </summary>
    Top,
    /// <summary>
    /// 系统层 最高层
    /// </summary>
    System,
    
}
public class UIMgr : BaseMgr<UIMgr>
{
    //UI管理器，显示面板分为单选显示和多选显示
    //单选显示：唯一的UI面板，由UIMgr管理,dic字段
    //多项显示：不唯一，统一由缓存池管理

    /// <summary>
    /// 防止重复初始化
    /// </summary>
    bool isInit=false;
    private abstract class BaseUIInfo { }

    private class UIInfo<T> : BaseUIInfo where T : BasePanel
    {
        public T value;

        public bool isOnShow = false;

        public bool onHide = false;

        public bool onDestroy = false;

        public UnityAction<T> action;

        /// <summary>
        /// 只要有一个为true,属性返回true，或运算
        /// </summary>
        public bool isOut => onHide || onDestroy;

        public void ShowOne()
        {
            isOnShow = true;
            onDestroy = false;
            onHide = false;
        }
        public UIInfo() { }

    }

    private Dictionary<string, BaseUIInfo> onlyShowUIDic = new Dictionary<string, BaseUIInfo>();

    private Camera uiCamera;
    private Canvas uiCanvas;
    private EventSystem uiEventSystem;

    //层级父对象
    private Transform bottomLayer;
    private Transform middleLayer;
    private Transform topLayer;
    private Transform systemLayer;
  
    /// <summary>
    /// 初始化，因为涉及到mono相关，所以放在构造函数可能有时序问题，则通过显示调用初始化
    /// 原则上不可重复初始化
    /// </summary>
    public void Init()
    {
        if(isInit)
        {
            return;
        }
        //加载资源后实例化
        uiCamera = GameObject.Instantiate
            (Resources.Load<GameObject>(DataAndInitMgr.Instance.resourcesNecessaryAssetsPath+"UICamera")).GetComponent<Camera>();
        //过场景不移除
        GameObject.DontDestroyOnLoad(uiCamera.gameObject);

        uiCanvas = GameObject.Instantiate
            (Resources.Load<GameObject>(DataAndInitMgr.Instance.resourcesNecessaryAssetsPath+"Canvas")).GetComponent<Canvas>();
        uiCanvas.worldCamera = uiCamera;
        GameObject.DontDestroyOnLoad(uiCanvas.gameObject);


        uiEventSystem = GameObject.Instantiate
            (Resources.Load<GameObject>(DataAndInitMgr.Instance.resourcesNecessaryAssetsPath + "EventSystem")).GetComponent<EventSystem>();
        GameObject.DontDestroyOnLoad(uiEventSystem.gameObject);

        bottomLayer = uiCanvas.transform.Find("Bottom");
        middleLayer = uiCanvas.transform.Find("Middle");
        topLayer = uiCanvas.transform.Find("Top");
        systemLayer = uiCanvas.transform.Find("System");

        isInit = true;
        //Debug.Log("1，UIMgr初始化成功");
    }

    public void SetEventSystemOpen(bool isOpen)
    {
        uiEventSystem.gameObject.SetActive(isOpen);
    }
    public Transform GetUILayer(E_UILayer layer)
    {
        switch (layer)
        {
            case E_UILayer.Bottom:
                return bottomLayer;
            case E_UILayer.Middle:
                return middleLayer;
            case E_UILayer.Top:
                return topLayer;
            case E_UILayer.System:
                return systemLayer;
        }
        return null;
    }

    /// <summary>
    /// 加载并显示面板，只在首次加载时候设置层级有用,默认中层
    /// </summary>
    /// <typeparam name="T">面板类型（预设体名与类名必须一致）</typeparam>
    /// <param name="layer">父对象层级</param>
    /// <param name="callBack">函数回调</param>
    /// 如果需要，可使用函数回调获取显示的面板类
    public void ShowOneUI<T>(E_UILayer layer = E_UILayer.Middle, UnityAction<T> callBack = null) where T : BasePanel
    {
        if (onlyShowUIDic.ContainsKey(typeof(T).Name))
        {
            UIInfo<T> info = onlyShowUIDic[typeof(T).Name] as UIInfo<T>;
            info.ShowOne();
            info.action += callBack;
            T UIobj = info.value;
            if (UIobj != null)
            {
                UIobj.ShowMe();

                info.action?.Invoke(UIobj);
                info.action = null;
            }
        }
        else
        {
            onlyShowUIDic.Add(typeof(T).Name, new UIInfo<T>());
            UIInfo<T> info = onlyShowUIDic[typeof(T).Name] as UIInfo<T>;
            info.action += callBack;
            T UIobj = info.value;
            AddresableMge.Instance.LoadAssetAsyncI<GameObject>(typeof(T).Name, (hande) =>
            {
                UIobj = GameObject.Instantiate(hande.Result, GetUILayer(layer), false).GetComponent<T>();
                if (info.onDestroy)
                {
                    UIobj.HideMe();
                    //制空引用，临时变量不需要
                    onlyShowUIDic.Remove(typeof(T).Name);
                    //删除对象且释放加载内存(卸载资源
                    GameObject.Destroy(UIobj.gameObject);
                    AddresableMge.Instance.Release<GameObject>(UIobj.gameObject.name);

                    return;
                }
                if (info.onHide)
                {
                    // 隐藏发生在加载完成前时，仍缓存已生成的面板，下一次显示可直接复用。
                    info.value = UIobj;
                    info.action = null;
                    UIobj.HideMe();
                    return;
                }
                info.ShowOne();
                UIobj.ShowMe();
                info.value = UIobj;
                info.action?.Invoke(UIobj);

                //执行完毕回调后制空，避免长期引用
                info.action = null;
            });
        }
    }

    
    public void HideOneUI<T>() where T : BasePanel
    {
        if (onlyShowUIDic.ContainsKey(typeof(T).Name))
        {
            if ((onlyShowUIDic[typeof(T).Name] as UIInfo<T>).value == null)
            {
                (onlyShowUIDic[typeof(T).Name] as UIInfo<T>).onHide = true;
            }
            else
            {
                (onlyShowUIDic[typeof(T).Name] as UIInfo<T>).value.HideMe();
            }
            
        }
    }
    public void DistoryOneUI<T>() where T : BasePanel
    {
        if (onlyShowUIDic.ContainsKey(typeof(T).Name))
        {
            if(onlyShowUIDic[typeof(T).Name] == null)
            {
                (onlyShowUIDic[typeof(T).Name] as UIInfo<T>).onDestroy = true;
            }
            else
            {
                T UIobj= (onlyShowUIDic[typeof(T).Name] as UIInfo<T>).value;
                //制空引用，临时变量不需要
                onlyShowUIDic.Remove(typeof(T).Name);
                //删除对象且释放加载内存(卸载资源
                GameObject.Destroy(UIobj.gameObject);
                AddresableMge.Instance.Release<GameObject>(UIobj.gameObject.name);
            }
        }
        
    }
    /// <summary>
    ///一键显示隐藏所有 UI
    /// </summary>
    /// <param name="isopen"></param>
    public void HideAllPanel(bool isopen)
    {
        // 一键显示隐藏所有 UI
        uiCamera.enabled =isopen;

    }
    /// <summary>
    /// 获取面板
    /// </summary>
    /// <typeparam name="T">面板类型</typeparam>
    /// <param name="callBack">回调</param>
    public void GetOnePanel<T>(UnityAction<T> callBack) where T : BasePanel
    {
        if (onlyShowUIDic.ContainsKey(typeof(T).Name))
        {
            UIInfo<T> info = onlyShowUIDic[(typeof(T).Name)] as UIInfo<T>;
            T obj = info.value;
            if (obj == null)//此面板正在加载中
            {
                info.action += callBack;
            }
            else
            {
                callBack(obj);
            }
        }
    }


    /// <summary>
    /// 添加自定义UI事件（备用，传入UI控件）
    /// </summary>
    /// <param name="uiObj">控件本身</param>
    /// <param name="evType">事件类型</param>
    /// <param name="callBack">函数回调</param>
    public static void AddEventTriggerListener(UIBehaviour uiObj, EventTriggerType evType, UnityAction<BaseEventData> callBack)
    {

        EventTrigger evTrigger = uiObj.gameObject.GetComponent<EventTrigger>();
        if (evTrigger == null)
            evTrigger = uiObj.gameObject.AddComponent<EventTrigger>();
        //设置某一自定义事件
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = evType;
        entry.callback.AddListener(callBack);

        //添加此自定义事件
        evTrigger.triggers.Add(entry);
    }

    /// <summary>
    /// 场景UI流程控制器
    /// </summary>
    BaseStartSceneUIFlow bssUI;
    /// <summary>
    /// 场景UI调度
    /// </summary>
    /// <param name="state"></param>
    public void SceneUIShow(E_UI_Process state)
    {
        bssUI.ChangeState(state);
    }
    public void UIBack()
    {
        bssUI.ToBack();
    }
    /// <summary>
    ///绑定当前场景UI控制器
    /// </summary>
    /// <param name="obj"></param>
    public void SetSceneUIFlow(BaseStartSceneUIFlow obj)
    {
        bssUI = obj;
    }
}
