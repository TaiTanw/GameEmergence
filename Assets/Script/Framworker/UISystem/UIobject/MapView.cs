using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只呈现已点亮的手工地图格；探索点击只传送，不持有认知或现场副本。</summary>
public sealed class MapView : MonoBehaviour
{
    RectTransform cellsArea;
    Text selectedText, hintText;
    LevelMapRecord record;
    MapCell selected;
    readonly Dictionary<MapCell, Image> tiles = new Dictionary<MapCell, Image>();
    static readonly Color Recorded = new Color(.13f, .32f, .37f);
    Vector2 layoutSize;

    void Awake()
    {
        cellsArea = transform.Find("MapCellsArea").GetComponent<RectTransform>();
        selectedText = transform.Find("MapSelectedText").GetComponent<Text>();
        hintText = transform.Find("MapHintText").GetComponent<Text>();
        // 旧预设中的取消按钮暂不开放；新制作入口不再生成它。
        Transform oldCancel = transform.Find("CancelMapCellButton");
        if (oldCancel != null) oldCancel.gameObject.SetActive(false);
        Refresh();
    }

    void Update() => Refresh();

    void Refresh()
    {
        SceneChangeMgr scene = SceneChangeMgr.Instance;
        LevelMapRecord current = scene.CurrentMapRecord;
        bool changed = record != current || layoutSize != cellsArea.rect.size;
        int litCount = 0;
        if (current != null && current.IsValid)
        {
            foreach (MapCell cell in current.Cells) if (current.IsLit(cell)) litCount++;
            foreach (MapCell cell in tiles.Keys) if (!current.IsLit(cell)) changed = true;
        }
        if (changed || tiles.Count != litCount) Rebuild(current);
        // 配置失效时只显示问题，不画部分地图或放行取消／传送。
        if (record == null || !record.IsValid)
        {
            cellsArea.gameObject.SetActive(false);
            selectedText.text = "地图暂不可用";
            hintText.text = "请检查本关地图格子配置。";
            return;
        }
        cellsArea.gameObject.SetActive(true);
        foreach (KeyValuePair<MapCell, Image> tile in tiles)
        {
            if (tile.Key == null) continue;
            tile.Value.GetComponent<Button>().interactable = scene.CanViewMap && !scene.EscapeSelectionPending;
            Outline outline = tile.Value.GetComponent<Outline>();
            outline.effectColor = tile.Key == selected ? new Color(.88f, .75f, .4f)
                : LevelStageMgr.Instance.HasExploration && tile.Key == record.CurrentCell
                    ? new Color(.55f, .86f, .92f) : new Color(0f, 0f, 0f, .6f);
        }
        selectedText.text = selected == null ? "仅显示已点亮的地图"
            : $"{selected.name} · 已记录 · 占用 {Format(record.ReservedCognition(selected))}";
        hintText.text = scene.EscapeSelectionPending ? "请先完成逃生能力选择。"
            : LevelStageMgr.Instance.Stage == PlayerGameplayStage.Escape ? "逃生中只读查看探索记录。"
            : "点击已点亮格子传送；\n试验重置使用独立按钮。";
    }

    void Rebuild(LevelMapRecord current)
    {
        foreach (Image tile in tiles.Values)
        {
            tile.gameObject.SetActive(false);
            tile.gameObject.name = "RetiredMapCell";
            Destroy(tile.gameObject);
        }
        tiles.Clear();
        if (record != current || current == null || !current.IsLit(selected)) selected = null;
        record = current;
        layoutSize = cellsArea.rect.size;
        if (record == null || !record.IsValid) return;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (MapCell cell in record.Cells)
        {
            // 显示范围也只读已亮格，未知布局不影响缩放和居中。
            if (!record.IsLit(cell)) continue;
            Rect bounds = cell.WorldRect;
            minX = Mathf.Min(minX, bounds.xMin); minY = Mathf.Min(minY, bounds.yMin);
            maxX = Mathf.Max(maxX, bounds.xMax); maxY = Mathf.Max(maxY, bounds.yMax);
        }
        if (float.IsPositiveInfinity(minX)) return;
        float scale = Mathf.Min((cellsArea.rect.width - 8f) / (maxX - minX), (cellsArea.rect.height - 8f) / (maxY - minY));
        Vector2 center = new Vector2(minX + (maxX - minX) * .5f, minY + (maxY - minY) * .5f);
        foreach (MapCell cell in record.Cells)
        {
            // 不创建未知格的图像、标记、边框或点击热区。
            if (!record.IsLit(cell)) continue;
            var item = new GameObject("MapCell_" + cell.GetInstanceID(), typeof(RectTransform), typeof(Image), typeof(Button));
            item.layer = gameObject.layer;
            RectTransform rect = item.GetComponent<RectTransform>(); rect.SetParent(cellsArea, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = (cell.Center - center) * scale;
            rect.sizeDelta = new Vector2(cell.Width, cell.Height) * scale;
            Image image = item.GetComponent<Image>(); image.color = Recorded;
            item.AddComponent<Outline>().effectDistance = new Vector2(1.5f, -1.5f);
            Button button = item.GetComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
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
        // 只接受当前记录的已亮格；两个探索模式均传送，逃生只读选择。
        SceneChangeMgr scene = SceneChangeMgr.Instance;
        if (!scene.CanViewMap || record != scene.CurrentMapRecord || !record.IsValid || !record.IsLit(cell)) return;
        selected = cell;
        bool teleport = scene.CanTeleportMap && record.IsLit(cell);
        bool completed = !teleport || scene.TryTeleport(cell);
        Refresh();
        if (!completed) hintText.text = "未能传送，请检查格子配置与当前状态。";
    }

    static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
