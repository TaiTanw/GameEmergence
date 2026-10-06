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
    private Text sprintT;
    private Text conflictT;

    static readonly string[] KnownRowOrder = { "left", "right", "up", "down", "ff", "interact", "sprint" };
    const float FooterHeight = 168f;



    void Start()
    {
        //找到文本控件

        leftT = FindUIObj<Text>("leftT");
        rightT = FindUIObj<Text>("rightT");
        upT = FindUIObj<Text>("upT");
        downT = FindUIObj<Text>("downT");
        FT = FindUIObj<Text>("FT");
        interactT = FindUIObj<Text>("interactT");
        sprintT = FindUIObj<Text>("sprintT");
        conflictT = FindUIObj<Text>("conflictT");
        ArrangeRebindList();
        //初始化文本面板显示
        UpdateUIvalue();


    }

    /// <summary>
    /// 把键位行收进纵向列表。列表高度随行数增长，超出视口后滚动。
    /// 返回和冲突提示留在视口下方，不参与滚动。
    /// </summary>
    void ArrangeRebindList()
    {
        // 安全：重复打开时不再建一套滚动。
        if (transform.Find("rebindScroll") != null) return;

        RectTransform scrollRt = CreateScrollArea();
        RectTransform contentRt = scrollRt.Find("viewport/rebindContent") as RectTransform;
        List<Transform> rows = CollectKeyRows(scrollRt);
        rows.Sort(CompareRows);
        foreach (Transform row in rows)
        {
            row.SetParent(contentRt, false);
            LayoutElement element = row.GetComponent<LayoutElement>();
            if (element == null)
                element = row.gameObject.AddComponent<LayoutElement>();
            // 实际逻辑：每行只要固定高度。新增键位复制一行即可，不必再改其它行的坐标。
            element.minHeight = 88f;
            element.preferredHeight = 100f;
            element.flexibleHeight = 0f;
        }
        PinFooter();
        scrollRt.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
    }

    RectTransform CreateScrollArea()
    {
        GameObject scrollGo = new GameObject("rebindScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(transform, false);
        RectTransform scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(0f, FooterHeight);
        scrollRt.offsetMax = Vector2.zero;

        GameObject viewportGo = new GameObject("viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollRt, false);
        RectTransform viewportRt = (RectTransform)viewportGo.transform;
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        Image viewportImage = viewportGo.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0f);
        viewportImage.raycastTarget = true;

        GameObject contentGo = new GameObject("rebindContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportRt, false);
        RectTransform contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(80, 80, 24, 24);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.inertia = true;
        return scrollRt;
    }

    List<Transform> CollectKeyRows(RectTransform scrollRt)
    {
        List<Transform> rows = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            // 实际逻辑：返回和冲突提示固定在页脚。滚动容器自身也不能被收进行列表。
            if (child == scrollRt || child.name == "out" || child.name == "conflictT") continue;
            rows.Add(child);
        }
        return rows;
    }

    static int CompareRows(Transform a, Transform b)
    {
        int order = RowOrder(a).CompareTo(RowOrder(b));
        if (order != 0) return order;
        return a.GetSiblingIndex().CompareTo(b.GetSiblingIndex());
    }

    static int RowOrder(Transform row)
    {
        int best = 1000;
        foreach (Button button in row.GetComponentsInChildren<Button>(true))
        {
            int index = Array.IndexOf(KnownRowOrder, button.gameObject.name);
            if (index >= 0 && index < best)
                best = index;
        }
        return best;
    }

    void PinFooter()
    {
        Transform exit = transform.Find("out");
        if (exit is RectTransform exitRt)
        {
            exitRt.anchorMin = new Vector2(0.5f, 0f);
            exitRt.anchorMax = new Vector2(0.5f, 0f);
            exitRt.pivot = new Vector2(0.5f, 0f);
            exitRt.anchoredPosition = new Vector2(0f, 28f);
            exit.SetAsLastSibling();
        }
        if (conflictT == null) return;
        RectTransform conflictRt = conflictT.rectTransform;
        conflictRt.anchorMin = new Vector2(0.5f, 0f);
        conflictRt.anchorMax = new Vector2(0.5f, 0f);
        conflictRt.pivot = new Vector2(0.5f, 0f);
        conflictRt.anchoredPosition = new Vector2(0f, 112f);
        conflictRt.SetAsLastSibling();
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
            case "sprint":
                InputControlMgr.Instance.StartRebind("Sprint", 0, UpdateUIvalue);
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
        if (sprintT != null)
            sprintT.text = asset.FindAction("Sprint").bindings[0].effectivePath;
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
