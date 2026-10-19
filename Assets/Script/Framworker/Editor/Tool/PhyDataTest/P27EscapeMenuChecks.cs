#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>正式两页预设与雾面的 GPU 检查；只在预览场景制作示意，不修改玩法数据。</summary>
public static class P27EscapeMenuChecks
{
    const string Output = ".utmp/P272Validation/";
    static readonly List<string> passed = new List<string>();
    [MenuItem("自定义工具/测试工具/验证/P27 验证纸样与迷雾（Edit Mode）")]
    public static void Run()
    {
        Directory.CreateDirectory(Output); passed.Clear();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/UIPanel/GameplayPanel.prefab").transform.Find("EscapeChoiceArea");
        Check(source.Find("Choices/Tabs") == null, "正式预设移除编号页签");
        Check(source.Find("Choices").GetComponent<Image>().sprite == GameUIAssets.Shared.environmentPanel, "第一页沿用少花纹纸张");
        foreach (Text text in source.Find("Choices/AbilityPage").GetComponentsInChildren<Text>(true))
            Check(text.color == Color.white, "二页白字 " + text.name);
        Material material = source.Find("AbilityFog").GetComponent<EscapeFogGraphic>().material;
        Check(material != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "迷雾材质编译且受当前显卡支持");
        Scene preview = EditorSceneManager.NewPreviewScene();
        Texture2D backdrop = null;
        try
        {
            var cameraObject = new GameObject("P272 camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var canvasObject = new GameObject("P272 canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            // 背景只用于观察 UI 半透明；不是新关卡或本轮实机截图。
            string currentBackground = ".utmp/PlayerUpgradeValidation/p27-before-preparation.png";
            string backgroundPath = File.Exists(currentBackground) ? currentBackground : "../P27Validation/p27-integrated-hold.png";
            GameObject background = new GameObject("Static gameplay reference", typeof(RectTransform), typeof(RawImage));
            background.transform.SetParent(canvas.transform, false); Stretch((RectTransform)background.transform);
            if (File.Exists(backgroundPath))
            {
                backdrop = new Texture2D(2, 2); backdrop.LoadImage(File.ReadAllBytes(backgroundPath));
                background.GetComponent<RawImage>().texture = backdrop;
            }
            background.GetComponent<RawImage>().color = backdrop != null ? Color.white : new Color(.22f, .27f, .34f);
            GameObject root = Object.Instantiate(source.gameObject, canvas.transform, false);
            Object.DestroyImmediate(root.GetComponent<EscapeChoiceView>());
            root.SetActive(true);
            Transform card = root.transform.Find("Choices");
            CanvasGroup group = card.GetComponent<CanvasGroup>();
            EscapeFogGraphic fog = root.transform.Find("AbilityFog").GetComponent<EscapeFogGraphic>();
            Text budget = card.Find("EscapeBudgetText").GetComponent<Text>(); budget.text = "剩余认知 62.5";
            string[] rows = { "MapCostRow", "BackpackCostRow", "FormCostRow" };
            foreach (string row in rows)
            {
                Transform item = card.Find("CostPage/Content/" + row);
                item.Find(row + "ValuesText").GetComponent<Text>().text = "等级 0/3 · 1 → 0.8\n每级 ×0.8 · 每次 5 认知";
                item.Find(row + "StateText").GetComponent<Text>().text = "可重复购买";
            }
            card.Find("CostPage/Content/EscapeCostText").GetComponent<Text>().text = "性质 2／计算点 1 · 转换 5\n地图实际占用 12 · 模拟 0.5/秒";
            group.alpha = 1;
            Save(camera, 1920, 1080, "paper-1920.png"); Save(camera, 960, 540, "paper-960.png");
            card.GetComponent<Image>().enabled = false;
            card.Find("CostPage").gameObject.SetActive(false); card.Find("NextEscapeButton").gameObject.SetActive(false);
            card.Find("AbilityPage").gameObject.SetActive(true); card.Find("BeginEscapeButton").gameObject.SetActive(true);
            card.Find("AbilityPage/EmptyResultText").gameObject.SetActive(false);
            card.Find("AbilityPage/PositiveResult/PositiveResultValueText").GetComponent<Text>().text = "移动速度 ×1.2";
            card.Find("AbilityPage/NegativeResult/NegativeResultValueText").GetComponent<Text>().text = "起跳强度 ×0.9";
            card.Find("AbilityPage/FinalValuesText").GetComponent<Text>().text = "移动速度 6 · 游泳速度 4\n起跳初速度 9 · 额外空中跳 1 次";
            budget.text = "本场体能效果"; budget.color = Color.white;
            root.GetComponent<Image>().color = new Color(.015f, .025f, .04f, .32f);
            fog.gameObject.SetActive(true);
            float[] reveal = { .38f, 1f, 1f }; float[] alpha = { 0f, 0f, 1f };
            string[] names = { "fog-center", "fog-expanded", "abilities" };
            for (int i = 0; i < reveal.Length; i++)
            {
                group.alpha = alpha[i]; fog.SetPresentation(reveal[i], 0f);
                Save(camera, 1920, 1080, names[i] + "-1920.png");
                Save(camera, 960, 540, names[i] + "-960.png");
            }
            // 隔离雾面以读取真正 alpha，避免背景和纸页干扰判断。
            background.SetActive(false); card.gameObject.SetActive(false); root.GetComponent<Image>().enabled = false;
            fog.color = Color.white;
            foreach (int width in new[] { 1920, 960 })
            {
                int height = width * 9 / 16;
                fog.SetPresentation(0f, 0f); Texture2D zero = Render(camera, width, height);
                fog.SetPresentation(.38f, 0f); Texture2D center = Render(camera, width, height);
                fog.SetPresentation(1f, 0f); Texture2D full = Render(camera, width, height);
                Check(Area(zero) == 0, width + " 零进度无残留");
                Check(Area(center) > 0 && Area(center) < Area(full) * .5f, width + " 从中心扩大覆盖区域");
                Check(center.GetPixel(width/2, height/2).a > .01f && center.GetPixel(width/2, height/5).a < .01f,
                    width + " 早期中心有雾而外侧透明");
                Check(full.GetPixel(width/2 + width*3/8 - 1,height/2).a < .01f,
                    width + " 最终外沿透明，不显露矩形边界");
                float maximum = 0f; foreach (Color pixel in full.GetPixels()) maximum = Mathf.Max(maximum, pixel.a);
                Check(full.GetPixel(width/2,height/2).a > .15f && full.GetPixel(width/2,height/2).a < .95f,
                    width + " 雾中央保持半透明");
                Check(maximum > .99f, width + " 部分浓厚雾边达到不透明，最大alpha=" + maximum.ToString("0.###"));
                float narrowest = 1f, widest = 0f;
                for (int ray = 0; ray < 64; ray++)
                {
                    float angle = ray * Mathf.PI * 2f / 64f, extent = 0f;
                    for (float r = .4f; r < .98f; r += .005f)
                    {
                        int x = Mathf.RoundToInt(width * (.5f + .375f * Mathf.Cos(angle) * r));
                        int y = Mathf.RoundToInt(height * (.5f + .5f * Mathf.Sin(angle) * r));
                        if (full.GetPixel(x,y).a > .05f) extent = r;
                    }
                    narrowest = Mathf.Min(narrowest, extent); widest = Mathf.Max(widest, extent);
                }
                Check(widest - narrowest > .10f, width + " 边缘轮廓不规则，半径起伏=" + (widest-narrowest).ToString("0.###"));
                fog.SetPresentation(1f, 6f); Texture2D moving = Render(camera, width, height);
                Check(Difference(full, moving) > 100, width + " 稳定显现后雾仍缓慢流动");
                CanvasGroup fogGroup = fog.gameObject.AddComponent<CanvasGroup>(); fogGroup.alpha = 0f;
                Texture2D faded = Render(camera, width, height); Check(Area(faded) == 0, width + " CanvasGroup淡出无残留");
                Object.DestroyImmediate(fogGroup);
                var maskObject = new GameObject("Fog mask", typeof(RectTransform), typeof(RectMask2D));
                maskObject.transform.SetParent(root.transform, false); ((RectTransform)maskObject.transform).sizeDelta = new Vector2(500,300);
                fog.transform.SetParent(maskObject.transform, false);
                Texture2D clipped = Render(camera, width, height);
                Check(clipped.GetPixel(width/2,height/2).a > .01f && clipped.GetPixel(width/2 + width/5,height/2).a == 0f,
                    width + " RectMask2D局部裁切");
                Object.DestroyImmediate(maskObject.GetComponent<RectMask2D>());
                maskObject.AddComponent<Image>(); maskObject.AddComponent<Mask>().showMaskGraphic = false;
                Texture2D stencil = Render(camera, width, height);
                Check(stencil.GetPixel(width/2,height/2).a > .01f && stencil.GetPixel(width/2 + width/5,height/2).a == 0f,
                    width + " Stencil Mask局部裁切");
                fog.transform.SetParent(root.transform, false); Object.DestroyImmediate(maskObject);
                foreach (Texture2D texture in new[] { zero, center, full, moving, faded, clipped, stencil }) Object.DestroyImmediate(texture);
            }
            Check(fog.material == material && !material.name.Contains("Instance"), "所有预览共用同一雾材质，无运行时材质实例");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); if (backdrop != null) Object.DestroyImmediate(backdrop); }
        File.WriteAllLines(Output + "render-checks.txt", passed);
    }
    static int Area(Texture2D image) { int count = 0; foreach (Color32 p in image.GetPixels32()) if (p.a > 2) count++; return count; }
    static int Difference(Texture2D first, Texture2D second)
    { int count = 0; var a = first.GetPixels32(); var b = second.GetPixels32(); for (int i=0;i<a.Length;i++) if (!a[i].Equals(b[i])) count++; return count; }
    static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero; }
    static void Save(Camera camera, int width, int height, string name)
    { Texture2D texture = Render(camera, width, height); File.WriteAllBytes(Output + name, texture.EncodeToPNG()); Object.DestroyImmediate(texture); }
    static Texture2D Render(Camera camera, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); return texture;
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); }
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("P27.2 FAILED: " + message); passed.Add("PASS " + message); }
}
#endif
