#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>P25 增量制作：只替换近景表现和说明，不重建关卡或保存场景。</summary>
public static class P25LevelVisualTools
{
    public const string Art = "Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/P25/";
    public const string DecorationRoot = "08_P25近景与装饰";
    static readonly string[] InteractiveNames =
    {
        "P25_Box", "P25_ElectricBox", "P25_Cart", "P25_PressurePlate",
        "P25_Gate", "P25_Signboard", "P25_Platform", "P25_Lamp"
    };
    static readonly string[] DecorationNames =
    {
        "P25_Railing", "P25_Pipes", "P25_Cabinet", "P25_Cables",
        "P25_Vines", "P25_StoneNiche", "P25_Warning", "P25_Debris"
    };

    [MenuItem("自定义工具/关卡/P25/导入特征图集")]
    public static void ImportAtlases()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请退出播放后导入关卡图集。");
        ImportAtlas(Art + "interactive-atlas-v1.png", InteractiveNames);
        ImportAtlas(Art + "decoration-atlas-v1.png", DecorationNames);
    }

    static void ImportAtlas(string path, string[] names)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("P25 图集尚未生成。", path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("图集不是可导入的纹理：" + path);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(path)))
                throw new InvalidOperationException("无法读取 P25 图集：" + path);
            if (source.width < 4 || source.height < 2)
                throw new InvalidOperationException("P25 图集尺寸不足四列两行：" + path);
            Color32[] pixels = source.GetPixels32();
            var slices = new SpriteMetaData[8];
            for (int index = 0; index < slices.Length; index++)
            {
                int col = index % 4, row = index / 4;
                int left = col * source.width / 4, right = (col + 1) * source.width / 4;
                // 行顺序从图像顶部开始，按完整尺寸的边界取整，不丢弃奇数尺寸的末行末列。
                int bottom = source.height - (row + 1) * source.height / 2;
                int top = source.height - row * source.height / 2;
                int w = right - left, h = top - bottom;
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (pixels[(bottom + y) * source.width + left + x].a <= 8) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                if (maxX < minX) throw new InvalidOperationException("图集格为空：" + names[index]);
                // 原图每格留有透明边距；紧切后再按实际主体尺寸制作，避免图片比碰撞框小一圈。
                minX = Mathf.Max(0, minX - 1); minY = Mathf.Max(0, minY - 1);
                maxX = Mathf.Min(w - 1, maxX + 1); maxY = Mathf.Min(h - 1, maxY + 1);
                slices[index] = new SpriteMetaData
                {
                    name = names[index],
                    rect = new Rect(left + minX, bottom + minY, maxX - minX + 1, maxY - minY + 1),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = names[index] == "P25_Signboard" ? new Vector2(.5f, 0f) : new Vector2(.5f, .5f),
                    border = Vector4.zero
                };
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.spritesheet = slices;
            importer.SaveAndReimport();
        }
        finally { Object.DestroyImmediate(source); }
    }

    [MenuItem("自定义工具/关卡/P25/应用当前场景近景与说明")]
    static void ApplyActiveScene() => Apply(SceneManager.GetActiveScene());

    public static void Apply(Scene scene)
    {
        if (Application.isPlaying || !scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("P25 视觉制作需要已打开且未播放的场景。");
        Dictionary<string, Sprite> sprites = LoadSprites();
        GameObject[] roots = scene.GetRootGameObjects();
        var boxes = All<PhysicalBox>(roots);
        foreach (var box in boxes)
            SkinBody(box.gameObject, sprites[box.IsElectrified ? "P25_ElectricBox" : "P25_Box"]);
        foreach (var cart in All<MechanismCart>(roots)) SkinBody(cart.gameObject, sprites["P25_Cart"]);
        foreach (var plate in All<MechanismPressurePlate>(roots))
        {
            SkinBody(plate.gameObject, sprites["P25_PressurePlate"]);
            if (plate.TargetComponent != null)
                SkinBody(plate.TargetComponent.gameObject, sprites["P25_Gate"]);
        }

        var fixedShapes = roots.Where(g => g.name == "01_固定结构与安全托台" || g.name == "09_P25关卡增量")
            .SelectMany(g => g.GetComponentsInChildren<BoxCollider2D>(true))
            .Where(c => c.enabled && !c.isTrigger && c.GetComponent<Rigidbody2D>() == null
                && c.GetComponent<BasicPhysicalObject>() == null && c.GetComponent<MechanismPressurePlate>() == null)
            .ToArray();
        SkinFixedGeometry(scene, fixedShapes, sprites["P25_Platform"]);

        foreach (var sign in All<Signboard>(roots))
        {
            SkinSign(sign, sprites["P25_Signboard"]);
            string message = NoticeText(sign.name);
            if (message != null) SetNotice(sign, message);
        }
        AddDecorations(scene, roots, fixedShapes, sprites);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static Dictionary<string, Sprite> LoadSprites()
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(Art + "interactive-atlas-v1.png").OfType<Sprite>()
            .Concat(AssetDatabase.LoadAllAssetsAtPath(Art + "decoration-atlas-v1.png").OfType<Sprite>())
            .ToDictionary(s => s.name);
        foreach (string name in InteractiveNames.Concat(DecorationNames))
            if (!sprites.ContainsKey(name)) throw new InvalidOperationException("请先导入 P25 图集，缺少精灵：" + name);
        return sprites;
    }

    static T[] All<T>(IEnumerable<GameObject> roots) where T : Component
        => roots.SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

    static void Record(Object value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }

    static void SkinBody(GameObject body, Sprite sprite)
    {
        var shape = body.GetComponent<BoxCollider2D>();
        var renderer = body.GetComponent<SpriteRenderer>();
        if (shape == null || renderer == null) return;
        var oldImage = body.transform.Find("image");
        var oldRenderer = oldImage != null ? oldImage.GetComponent<SpriteRenderer>() : null;
        var visible = renderer.sprite != null ? renderer : oldRenderer;
        Vector3 scale = body.transform.lossyScale;
        Vector2 size = visible != null && visible.sprite != null
            ? new Vector2(visible.bounds.size.x / Mathf.Abs(scale.x), visible.bounds.size.y / Mathf.Abs(scale.y))
            : shape.size;
        // 只改主体网格尺寸；根缩放、碰撞框、探针、材质与当前形态 Alpha 都保持原值。
        Color tint = renderer.color;
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        renderer.color = new Color(1f, 1f, 1f, tint.a);
        renderer.enabled = true;
        if (oldRenderer != null)
        {
            renderer.sortingOrder = oldRenderer.sortingOrder;
            renderer.sharedMaterial = oldRenderer.sharedMaterial;
            oldRenderer.enabled = false;
            Record(oldRenderer);
        }
        Record(renderer);
    }

    static void SkinFixedGeometry(Scene scene, BoxCollider2D[] shapes, Sprite platform)
    {
        string surfaceName = scene.name.StartsWith("L01", StringComparison.Ordinal) ? "GardenStone"
            : scene.name.StartsWith("L04", StringComparison.Ordinal) ? "Marble"
            : scene.name.StartsWith("L00", StringComparison.Ordinal) || scene.name.StartsWith("L02", StringComparison.Ordinal)
                ? "Concrete" : "Steel";
        Sprite surface = AssetDatabase.LoadAllAssetsAtPath("Assets/MyObjAssets/gameObject/Art/LevelAtmosphere/surface-atlas-v1.png")
            .OfType<Sprite>().Single(s => s.name == surfaceName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        foreach (var shape in shapes)
        {
            var renderer = shape.GetComponent<SpriteRenderer>();
            if (renderer == null) continue;
            if (shape.autoTiling)
                throw new InvalidOperationException(shape.name + " 的自动平铺会改动碰撞框，请先关闭后再换皮。");
            Bounds b = shape.bounds;
            if (b.size.x >= 2f && b.size.x <= 8f && b.size.y <= .8f)
                SkinBody(shape.gameObject, platform);
            else
            {
                // 宽地面与厚墙沿原尺寸平铺，避免把单个窄平台的铆钉横向拉长。
                float alpha = renderer.color.a;
                renderer.sprite = surface;
                renderer.drawMode = SpriteDrawMode.Tiled;
                renderer.tileMode = SpriteTileMode.Continuous;
                renderer.size = shape.size;
                renderer.color = new Color(.86f, .89f, .94f, alpha);
                renderer.enabled = true;
            }
            if (material != null) renderer.sharedMaterial = material;
            Record(renderer);
        }
    }

    static void SkinSign(Signboard sign, Sprite sprite)
    {
        var parts = sign.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(r => r.name == "Board" || r.name == "Post" || r.name.StartsWith("Mark", StringComparison.Ordinal)).ToArray();
        var renderer = sign.GetComponent<SpriteRenderer>();
        Vector2 size = renderer != null ? renderer.size : Vector2.one;
        if (renderer == null)
        {
            if (parts.Length == 0) return;
            Bounds visible = parts[0].bounds;
            foreach (var part in parts.Skip(1)) visible.Encapsulate(part.bounds);
            renderer = sign.gameObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = parts.Max(r => r.sortingOrder);
            renderer.sortingLayerID = parts[0].sortingLayerID;
            renderer.sharedMaterial = parts[0].sharedMaterial;
            Vector3 scale = sign.transform.lossyScale;
            size = new Vector2(visible.size.x / Mathf.Abs(scale.x), visible.size.y / Mathf.Abs(scale.y));
        }
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        renderer.color = Color.white;
        foreach (var part in parts) { part.enabled = false; Record(part); }
        Record(renderer);
    }

    /// <summary>几何制作工具可用同一个入口填写新牌；不修改阅读触发器与面板显示许可。</summary>
    public static void SetNotice(Signboard sign, string message)
    {
        if (sign == null) throw new ArgumentNullException(nameof(sign));
        var caption = sign.GetComponentInChildren<Text>(true);
        var panel = sign.transform.Find("NoticePanel") as RectTransform;
        if (caption == null || panel == null) throw new InvalidOperationException(sign.name + " 缺少原告示面板。");
        panel.sizeDelta = new Vector2(600f, 250f);
        caption.text = message;
        caption.fontSize = 24;
        caption.resizeTextForBestFit = false;
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        caption.verticalOverflow = VerticalWrapMode.Overflow;
        Record(panel); Record(caption);
    }

    internal static string NoticeText(string name)
    {
        if (name.StartsWith("P25_", StringComparison.Ordinal)) return null;
        switch (name)
        {
            case "移动与跳跃": return "A / D 移动，空格跳跃。\n空中还能再跳一次；Shift 疾跑。\n先试试右侧的矮台，再去看下一块牌。";
            case "普通箱操作": return "靠近普通箱按 E 选中，再用 A / D 推拉。\n再次点按 E 解除选中，跳跃也会松手。\n选中后长按 E 可以收纳箱子。\n退开原位置，再靠近按 E 取出。";
            case "小车与压板": return "靠近小车按 E 开关，它会沿固定方向走。\n小车能推箱子，普通压板能控制同号的门。\n压住时门打开，板上空了，门就慢慢关上。\n先看清车、箱、板和门，再开车跟上。";
            case "游泳与氧气": return "水里用 W / A / S / D 游动。\n整个头部进入水中，会慢慢消耗氧气。\n让头露出水面，就能恢复氧气。\n这里不会涨水，可以慢慢练习。";
            case "通关交互": return "靠近床、书或灯，按 E 离开这一关。\n下一关先用精神体看路、修改环境。\n准备好后，长按回车结束探索。\n中途松开即可取消；确认后用本体逃生。\n按 ESC，在“操作指南”里查看当前按键。";
            case "精神体操作说明": return "现在控制的是精神体，本体在起点等待。\nW / A / S / D 漫游：能穿墙，箱车暂停。\n按 Q 切到模拟：A / D 走，空格跳，E 交互。\n再按 Q 返回漫游，才能修改蓝框环境。\n模拟会消耗认知，遇险会自动返回漫游。";
            case "性质转移": return "只在漫游时，左键点击蓝框环境。\n抽出“触发器性质”并确认，水池就会变实心。\n把它放进金色墙顶部的独立槽，确认后能穿墙。\n“四向游泳”只管游泳，不能让墙消失。\n改动要点“确认”才会生效。";
            case "认知与试验说明": return "点亮的小地图格子可以点击传送。\n“试验重置”让箱子、小车和机关复位。\n确认过的环境修改保留，体力、氧气回满。\n认知不会返还，模拟也会慢慢消耗认知。\n长按回车结束探索，松开即可取消。\n开始逃生时，箱子和小车也会复位。\n按 ESC，在“操作指南”里查看当前按键。";
            case "探索说明": return "先看路，再决定要改哪一块环境。\n认知花得越少，逃生时越容易得到好能力。\n蓝框能修改，普通托台是安全落脚点。\n高处需要连跳；能力变弱时也有可走的路。";
            case "顺序机关": return "同号压板控制同号门：压住开，离开关。\n先看箱子离每块板有多远，再开启小车。\n距离越远，轮到那道门打开就越晚。\n门前托台可以停下来等待。\n模拟里摆过的箱车，开始逃生时会复位。";
            case "带电箱变体": return "带电箱碰不得，也不能直接选中。\n让小车推它，或隔着普通箱推动。\n紫色压板只认带电箱。\n别站到它顶上，所有表面都有危险。";
            case "电面关路线": return "绿色毒液一碰就会失败，变成地面也危险。\n先抽掉“触发器性质”，再把地面调滑。\n推箱加速，跳上箱顶，再左右跟住箱子。\n普通能力要靠箱子过第一槽，不能直接踩过去。\n后面的安全台可以停下来观察下一段。";
            case "折返廊提示": return "这条长廊要来回绕五次。\n等水灌满，满氧也游不到另一端。\n趁走廊没淹时通过，或先打开金色捷径墙。\n先在精神体阶段找好出口，再开始逃生。";
            case "双井路线": return "第一座井高处有一个出口。\n想去第二座井，要提前穿过折返走廊，\n或者用“触发器性质”打开金色捷径墙。\n别等所有地方都淹没后，才开始找路。";
            case "高层跳跃提示": return "这里是留给更强跳跃能力的近路。\n跳不上去就找较低的路线，不必硬跳。\n保留更多认知，更容易获得正面能力。";
        }
        if (name.StartsWith("滑箱提示_", StringComparison.Ordinal))
            return "绿色池子要先抽“触发器性质”，确认成地面。\n它仍然危险！把“地面消退倍率”调小。\n先推箱加速，再跳上箱顶，左右跟着箱子走。\n红色电面也不能踩，普通箱不会传电。\n模拟摆过的箱子，开始逃生时会回到原位。";
        return null;
    }

    static void AddDecorations(Scene scene, GameObject[] roots, BoxCollider2D[] fixedShapes, Dictionary<string, Sprite> sprites)
    {
        var root = roots.SingleOrDefault(g => g.name == DecorationRoot);
        if (root == null)
        {
            root = new GameObject(DecorationRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        if (root.GetComponentsInChildren<Collider2D>(true).Length > 0 || root.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
            throw new InvalidOperationException("P25 装饰根中出现了玩法组件，请先检查。" );
        foreach (Transform child in root.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);

        var reserved = new List<Bounds>();
        foreach (var hazard in All<FixedContactHazard>(roots))
        {
            var shape = hazard.GetComponent<Collider2D>();
            if (shape == null) continue;
            Bounds b = shape.bounds;
            // 毒槽和电面上方预留整个滑箱与登箱区，不用背景装饰覆盖危险边缘。
            b.SetMinMax(new Vector3(b.min.x - 1.5f, b.min.y - .5f, -1f),
                new Vector3(b.max.x + 1.5f, b.max.y + 4f, 1f));
            reserved.Add(b);
        }
        foreach (var sign in All<Signboard>(roots))
        {
            var range = sign.GetComponent<BoxCollider2D>();
            if (range != null) { Bounds b = range.bounds; b.Expand(.6f); reserved.Add(b); }
            var panel = sign.transform.Find("NoticePanel") as RectTransform;
            if (panel == null) continue;
            var corners = new Vector3[4]; panel.GetWorldCorners(corners);
            var panelBounds = new Bounds(corners[0], Vector3.zero);
            foreach (var corner in corners.Skip(1)) panelBounds.Encapsulate(corner);
            panelBounds.Expand(.6f); reserved.Add(panelBounds);
        }
        var legacy = roots.SingleOrDefault(g => g.name == "05_梦境场景美术");
        if (legacy != null)
        {
            int lamps = 0;
            foreach (var part in legacy.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (part.name == "装饰灯柱" || part.name == "检修灯座")
                {
                    Bounds b = part.bounds;
                    var point = new Vector2(b.center.x, part.name == "装饰灯柱" ? b.max.y : b.center.y);
                    var proposed = new Bounds(new Vector3(point.x, point.y, 0f), new Vector3(.95f, .55f, .1f));
                    if (!reserved.Any(r => OverlapsXY(r, proposed)))
                        AddSprite(root.transform, "P25_整图检修灯_" + lamps++, sprites["P25_Lamp"],
                            point, new Vector2(.95f, .55f), new Color(.76f, .80f, .86f, .8f), -6);
                }
                if (part.name == "背面支架" || part.name == "装饰灯柱" || part.name == "灯壳"
                    || part.name == "温弱灯芯" || part.name == "检修灯座" || part.name == "检修灯"
                    || part.name == "稀疏藤影" || part.name == "藤叶")
                { part.enabled = false; Record(part); }
            }
        }
        var ledges = fixedShapes.Where(c => c.bounds.size.x >= 7f && c.bounds.size.y < 1f)
            .OrderBy(c => c.bounds.max.y).ThenBy(c => c.bounds.center.x).ToArray();
        string[] details = scene.name.StartsWith("L04", StringComparison.Ordinal)
            ? new[] { "P25_StoneNiche", "P25_Vines", "P25_Railing", "P25_Cables", "P25_Debris" }
            : scene.name.StartsWith("L01", StringComparison.Ordinal)
                ? new[] { "P25_Railing", "P25_Vines", "P25_Pipes", "P25_Warning", "P25_Debris" }
                : new[] { "P25_Railing", "P25_Pipes", "P25_Cabinet", "P25_Cables", "P25_Warning", "P25_Debris" };
        int step = Mathf.Max(1, ledges.Length / 9), placed = 0;
        for (int index = 0; index < ledges.Length && placed < 10; index += step)
        {
            Bounds b = ledges[index].bounds;
            string kind = details[placed % details.Length];
            float width = kind == "P25_Cabinet" ? 1.55f : kind == "P25_Debris" ? 2f
                : kind == "P25_Warning" ? 1.2f : kind == "P25_Vines" || kind == "P25_StoneNiche" ? 1.7f : 4.5f;
            Sprite sprite = sprites[kind];
            float h = width * sprite.bounds.size.y / sprite.bounds.size.x;
            if (h > 3.6f) { width *= 3.6f / h; h = 3.6f; }
            bool hanging = kind == "P25_Cables";
            if (hanging && b.min.y < 4f) continue;
            for (int side = 0; side < 2; side++)
            {
                bool fromLeft = (placed + side) % 2 == 0;
                float x = fromLeft ? b.min.x + width / 2f + .6f : b.max.x - width / 2f - .6f;
                var point = new Vector2(x, hanging ? b.min.y - h / 2f - .15f : b.max.y + h / 2f);
                var proposed = new Bounds(new Vector3(point.x, point.y, 0f), new Vector3(width, h, .1f));
                if (reserved.Any(r => OverlapsXY(r, proposed))) continue;
                // 装饰只占背墙空间；危险扫掠和完整告示面板周边均不安放。
                AddSprite(root.transform, "P25_背墙细节_" + placed, sprite, point, new Vector2(width, h),
                    new Color(.62f, .68f, .77f, .5f), -18);
                proposed.Expand(.4f); reserved.Add(proposed);
                placed++;
                break;
            }
        }
    }

    static bool OverlapsXY(Bounds a, Bounds b)
        => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;

    static void AddSprite(Transform parent, string name, Sprite sprite, Vector2 point, Vector2 size, Color color, int order)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.position = new Vector3(point.x, point.y, 0f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = size;
        renderer.color = color;
        renderer.sortingOrder = order;
        var material = AssetDatabase.LoadAssetAtPath<Material>(SceneLightingTools.SpriteMaterial);
        if (material != null) renderer.sharedMaterial = material;
    }
}
#endif
