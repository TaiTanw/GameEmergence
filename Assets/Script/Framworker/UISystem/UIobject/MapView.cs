using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>按手工地图矩形显示本关记录；点击与取消均调用场景规则，不持有认知副本。</summary>
public sealed class MapView : MonoBehaviour
{
    RectTransform cellsArea;
    Text selectedText, hintText;
    Button cancelButton;
    LevelMapRecord record;
    MapCell selected;
    readonly Dictionary<MapCell, Image> tiles = new Dictionary<MapCell, Image>();
    static readonly Color Recorded = new Color(.13f, .32f, .37f);
    static readonly Color Unrecorded = new Color(.075f, .12f, .16f);

    void Awake()
    {
        cellsArea = transform.Find("MapCellsArea").GetComponent<RectTransform>();
        selectedText = transform.Find("MapSelectedText").GetComponent<Text>();
        hintText = transform.Find("MapHintText").GetComponent<Text>();
        cancelButton = transform.Find("CancelMapCellButton").GetComponent<Button>();
        cancelButton.onClick.AddListener(CancelSelected);
        Refresh();
    }

    void Update() => Refresh();

    void Refresh()
    {
        SceneChangeMgr scene = SceneChangeMgr.Instance;
        LevelMapRecord current = scene.CurrentMapRecord;
        if (record != current) Rebuild(current);
        // 配置失效时只显示问题，不画部分地图或放行取消／传送。
        if (record == null || !record.IsValid)
        {
            cellsArea.gameObject.SetActive(false);
            cancelButton.interactable = false;
            selectedText.text = "地图暂不可用";
            hintText.text = "请检查本关地图格子配置。";
            return;
        }
        cellsArea.gameObject.SetActive(true);
        foreach (KeyValuePair<MapCell, Image> tile in tiles)
        {
            if (tile.Key == null) continue;
            tile.Value.color = record.IsLit(tile.Key) ? Recorded : Unrecorded;
            Outline outline = tile.Value.GetComponent<Outline>();
            outline.effectColor = tile.Key == selected ? new Color(.88f, .75f, .4f)
                : LevelStageMgr.Instance.HasExploration && tile.Key == record.CurrentCell
                    ? new Color(.55f, .86f, .92f) : new Color(0f, 0f, 0f, .6f);
        }
        cancelButton.interactable = scene.CanCancelMapCell && selected != null && record.IsLit(selected);
        selectedText.text = selected == null ? "选择格子查看记录" : record.IsLit(selected)
            ? $"{selected.name} · 已记录 · 占用 {Format(record.ReservedCognition(selected))}"
            : $"{selected.name} · 未记录";
        hintText.text = scene.EscapeSelectionPending ? "请先完成逃生能力选择。"
            : LevelStageMgr.Instance.Stage == PlayerGameplayStage.Escape ? "逃生中只读查看探索记录。"
            : scene.CanTeleportMap ? "点击点亮格可重置并传送；\n选中后可取消记录。"
            : "点击格子查看；选中后可取消记录。";
    }

    void Rebuild(LevelMapRecord current)
    {
        foreach (Image tile in tiles.Values)
        {
            tile.gameObject.SetActive(false);
            Destroy(tile.gameObject);
        }
        tiles.Clear(); selected = null; record = current;
        if (record == null || !record.IsValid) return;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (MapCell cell in record.Cells)
        {
            Rect bounds = cell.WorldRect;
            minX = Mathf.Min(minX, bounds.xMin); minY = Mathf.Min(minY, bounds.yMin);
            maxX = Mathf.Max(maxX, bounds.xMax); maxY = Mathf.Max(maxY, bounds.yMax);
        }
        float scale = Mathf.Min((cellsArea.rect.width - 8f) / (maxX - minX), (cellsArea.rect.height - 8f) / (maxY - minY));
        Vector2 center = new Vector2(minX + (maxX - minX) * .5f, minY + (maxY - minY) * .5f);
        foreach (MapCell cell in record.Cells)
        {
            var item = new GameObject("MapCell_" + cell.GetInstanceID(), typeof(RectTransform), typeof(Image), typeof(Button));
            item.layer = gameObject.layer;
            RectTransform rect = item.GetComponent<RectTransform>(); rect.SetParent(cellsArea, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = (cell.Center - center) * scale;
            rect.sizeDelta = new Vector2(cell.Width, cell.Height) * scale;
            Image image = item.GetComponent<Image>(); image.color = Unrecorded;
            item.AddComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);
            Button button = item.GetComponent<Button>(); button.targetGraphic = image;
            MapCell target = cell;
            button.onClick.AddListener(() => ClickCell(target));
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelObject.layer = gameObject.layer;
            RectTransform labelRect = labelObject.GetComponent<RectTransform>(); labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            Text label = labelObject.GetComponent<Text>(); label.font = selectedText.font; label.fontSize = 18;
            label.color = new Color(.88f, .94f, .96f); label.alignment = TextAnchor.MiddleCenter;
            label.text = cell.Marker ?? ""; label.raycastTarget = false;
            tiles.Add(cell, image);
        }
    }

    void ClickCell(MapCell cell)
    {
        // 只在当前关卡仍可查看时接受选择；模拟点击已亮格直接传送，不增加确认按钮。
        SceneChangeMgr scene = SceneChangeMgr.Instance;
        if (!scene.CanViewMap || record != scene.CurrentMapRecord || !record.IsValid || cell == null) return;
        selected = cell;
        bool teleport = scene.CanTeleportMap && record.IsLit(cell);
        bool completed = !teleport || scene.TryTeleport(cell);
        Refresh();
        if (!completed) hintText.text = "未能传送，请检查格子配置与当前状态。";
    }

    void CancelSelected()
    {
        if (selected == null) return;
        bool completed = SceneChangeMgr.Instance.CancelMapCell(selected);
        Refresh();
        if (!completed) hintText.text = "当前无法取消这条记录。";
    }

    static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
