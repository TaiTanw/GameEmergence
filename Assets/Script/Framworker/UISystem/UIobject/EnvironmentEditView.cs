using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>漫游的环境编辑显示与点击选择；只保留选中来源，改值交给关卡规则。</summary>
public sealed class EnvironmentEditView : MonoBehaviour
{
    [SerializeField] Color availableFrameColor = new Color(.35f, .8f, .9f, .7f);
    [SerializeField] Color selectedFrameColor = new Color(1f, .8f, .2f, 1f);
    [SerializeField, Min(1f), Tooltip("屏幕像素线宽，不随镜头缩放改变。")]
    float availableFrameWidth = 2f, selectedFrameWidth = 4f;
    Text objectText, parameterText, hintText;
    InputField valueInput;
    Button applyButton, nextParameterButton;
    int parameterIndex;
    BasicPhysicalObject selected;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    readonly List<BasicPhysicalObject> frameSources = new List<BasicPhysicalObject>();
    EnvironmentSelectionFrames selectionFrames;
    float nextFrameScan;

    void Awake()
    {
        objectText = transform.Find("EnvironmentObjectText").GetComponent<Text>();
        parameterText = transform.Find("EnvironmentParameterText").GetComponent<Text>();
        hintText = transform.Find("EnvironmentEditHintText").GetComponent<Text>();
        valueInput = transform.Find("EnvironmentValueInput").GetComponent<InputField>();
        applyButton = transform.Find("ApplyEnvironmentButton").GetComponent<Button>();
        nextParameterButton = transform.Find("NextEnvironmentParameterButton").GetComponent<Button>();
        applyButton.onClick.AddListener(Apply);
        nextParameterButton.onClick.AddListener(NextParameter);
        valueInput.onValueChanged.AddListener(OnValueChanged);
        ClearSelection("点击环境物体，查看并调整参数。");
    }

    void OnEnable() => nextFrameScan = 0f;

    void OnDisable()
    {
        // 生命周期：绘制节点在整屏面板上，编辑区停用时须一同隐藏。
        if (selectionFrames != null) selectionFrames.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (selectionFrames != null) Destroy(selectionFrames.gameObject);
    }

    void LateUpdate()
    {
        // 资格：暂停、加载和离开漫游不留下可选提示，显示不授予编辑权限。
        bool allowed = SceneChangeMgr.Instance.CanEditEnvironment;
        if (selectionFrames != null) selectionFrames.gameObject.SetActive(allowed);
        if (!allowed) return;
        if (selectionFrames == null)
        {
            GameplayPanel panel = GetComponentInParent<GameplayPanel>();
            if (panel == null) return;
            var item = new GameObject("EnvironmentSelectionFrames", typeof(RectTransform), typeof(CanvasRenderer), typeof(EnvironmentSelectionFrames));
            item.layer = panel.gameObject.layer;
            item.transform.SetParent(panel.transform, false);
            item.transform.SetAsFirstSibling();
            var rect = (RectTransform)item.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            selectionFrames = item.GetComponent<EnvironmentSelectionFrames>();
            selectionFrames.raycastTarget = false;
        }
        // 发现：本关对象低频重扫；几何和选中色每帧重画，不给环境宿主增加登记关系。
        if (Time.unscaledTime >= nextFrameScan)
        {
            // 发现只遍历本关层级，运行时 DontSave 物体也能被选择，不扫描项目预设。
            ScanFrameSources();
        }
        selectionFrames.Display(frameSources, ValidSelection() ? selected : null, Camera.main,
            availableFrameColor, selectedFrameColor, availableFrameWidth, selectedFrameWidth);
    }

    void Update()
    {
        bool allowed = SceneChangeMgr.Instance.CanEditEnvironment;
        // 资格：菜单、加载和离开漫游均禁止应用，不以面板是否显示代替规则判断。
        valueInput.interactable = allowed && ValidParameter();
        nextParameterButton.interactable = allowed && ValidParameter() && selected.EnvironmentParameterCount > 1;
        applyButton.interactable = valueInput.interactable && TryReadValue(out _);
        if (!allowed) return;
        // 生命周期：来源被销毁、禁用或不属于本关时，不继续显示旧对象。
        if (!object.ReferenceEquals(selected, null) && !ValidSelection())
            ClearSelection("点击环境物体，查看并调整参数。");
        RefreshSelectionDisplay();
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        Vector2 pointer = mouse.position.ReadValue();
        if (HitsUI(pointer)) return;
        Camera camera = Camera.main;
        // 依赖：没有游戏镜头时不把屏幕坐标误当作场景坐标。
        if (camera == null) return;
        Ray ray = camera.ScreenPointToRay(pointer);
        if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance)) return;
        SelectAt(ray.GetPoint(distance));
    }

    bool HitsUI(Vector2 pointer)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        uiHits.Clear();
        // 即时查询 UI，避免脚本更新顺序使本次点击穿透到场景。
        events.RaycastAll(new PointerEventData(events) { position = pointer }, uiHits);
        foreach (RaycastResult hit in uiHits) if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    void SelectAt(Vector2 point)
    {
        BasicPhysicalObject candidate = null;
        foreach (Collider2D collider in Physics2D.OverlapPointAll(point))
        {
            BasicPhysicalObject[] sources = collider.GetComponents<BasicPhysicalObject>();
            if (sources.Length != 1 || !EditableSource(sources[0])) continue;
            // 同一来源的多个碰撞框合并；不同来源重叠时交给玩家重新点，不暗中排序。
            if (candidate != null && candidate != sources[0])
            {
                ClearSelection("这里有多个可调整的物体，请点击更清晰的位置。");
                return;
            }
            candidate = sources[0];
        }
        // 暂停箱子仍可编辑；只补矩形几何读取，不临时打开刚体来做查询。
        ScanFrameSources();
        foreach (BasicPhysicalObject source in frameSources)
        {
            if (!EnvironmentSelectionFrames.ContainsPausedBox(source, point)) continue;
            if (candidate != null && candidate != source)
            {
                ClearSelection("这里有多个可调整的物体，请点击更清晰的位置。");
                return;
            }
            candidate = source;
        }
        if (candidate == null)
        {
            ClearSelection("这里没有可调整的物体，请点击其它位置。");
            return;
        }
        selected = candidate;
        parameterIndex = 0;
        bool hasParameter = selected.TryGetEnvironmentParameter(parameterIndex, out EnvironmentParameter parameter);
        valueInput.text = hasParameter ? Format(parameter.Value) : "";
        hintText.text = hasParameter ? "输入新的倍率后点击应用。" : "该物体暂无可调整的数值参数。";
        valueInput.interactable = hasParameter;
        applyButton.interactable = hasParameter && TryReadValue(out _);
        RefreshSelectionDisplay();
    }

    internal static bool EditableSource(BasicPhysicalObject source) => source != null && source.IsEnvironmentActive
        && SceneLevelSetup.Current != null && source.gameObject.scene == SceneLevelSetup.Current.gameObject.scene
        && source.GetComponents<BasicPhysicalObject>().Length == 1
        && EnvironmentSelectionFrames.TryGetBounds(source, out _);

    void ScanFrameSources()
    {
        nextFrameScan = Time.unscaledTime + .25f;
        frameSources.Clear();
        if (SceneLevelSetup.Current == null) return;
        foreach (GameObject root in SceneLevelSetup.Current.gameObject.scene.GetRootGameObjects())
            foreach (BasicPhysicalObject source in root.GetComponentsInChildren<BasicPhysicalObject>())
                if (EditableSource(source)) frameSources.Add(source);
    }

    bool ValidSelection() => EditableSource(selected);
    bool ValidParameter() => ValidSelection() && selected.TryGetEnvironmentParameter(parameterIndex, out _);

    void NextParameter()
    {
        if (!SceneChangeMgr.Instance.CanEditEnvironment || !ValidParameter()) return;
        parameterIndex = (parameterIndex + 1) % selected.EnvironmentParameterCount;
        selected.TryGetEnvironmentParameter(parameterIndex, out EnvironmentParameter parameter);
        valueInput.text = Format(parameter.Value);
        RefreshSelectionDisplay();
    }

    void RefreshSelectionDisplay()
    {
        if (!ValidSelection()) return;
        objectText.text = selected.name;
        if (!selected.TryGetEnvironmentParameter(parameterIndex, out EnvironmentParameter parameter))
        {
            parameterText.text = "暂无可调整的数值参数。";
            return;
        }
        parameterText.text = $"{parameter.DisplayName}   当前 {Format(parameter.Value)}   默认 {Format(parameter.DefaultValue)}";
    }

    void ClearSelection(string message)
    {
        selected = null;
        parameterIndex = 0;
        objectText.text = "环境参数";
        parameterText.text = "选择物体后显示可调整参数。";
        valueInput.text = "";
        valueInput.interactable = applyButton.interactable = nextParameterButton.interactable = false;
        hintText.text = message;
    }

    void OnValueChanged(string text)
    {
        applyButton.interactable = SceneChangeMgr.Instance.CanEditEnvironment && ValidParameter() && TryReadValue(out _);
        if (ValidParameter()) hintText.text = TryReadValue(out _)
            ? "输入新的倍率后点击应用。" : "请输入有限且不小于 0 的数。";
    }

    bool TryReadValue(out float value) => float.TryParse(valueInput.text, NumberStyles.Float,
        CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

    void Apply()
    {
        // 安全：所有资格、来源和输入须在提交前仍有效，界面不直接改来源或玩家效果。
        if (!SceneChangeMgr.Instance.CanEditEnvironment || !ValidParameter() || !TryReadValue(out float value)) return;
        hintText.text = SceneChangeMgr.Instance.EditEnvironmentParameter(selected, parameterIndex, value)
            ? "已应用。" : "未能应用，请重新选择物体。";
        RefreshSelectionDisplay();
    }

    static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
