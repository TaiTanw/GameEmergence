#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>正式认知条的几何、透明孔、独立裁切及两种画幅原生渲染；不保存临时场景。</summary>
public static class P27CognitionBarChecks
{
    const string Output = ".utmp/P27ArtValidation/";
    static readonly List<string> passed = new List<string>();
    static readonly FieldInfo glowEnabled = typeof(CognitionBarGraphic).GetField("glowEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo populate = typeof(CognitionBarGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
        null, new[] { typeof(VertexHelper) }, null);

    [MenuItem("自定义工具/测试工具/验证/P27 验证认知条美术（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 验证认知条美术。");
        Directory.CreateDirectory(Output); passed.Clear();
        File.WriteAllText(Output + "glow-pixel-audit.txt", "");
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/UIPanel/GameplayPanel.prefab");
        CognitionBarGraphic source = asset.GetComponentInChildren<CognitionBarGraphic>(true);
        Check(source != null && !source.raycastTarget && source.GetComponent<Image>() == null,
            "正式预设使用单个只读绘制组件，不留旧矩形底图或射线阻挡");
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var cameraObject = new GameObject("P27 preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.AddComponent<Camera>(); camera.scene = preview; camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.322f, .384f, .482f);
            var canvasObject = new GameObject("P27 preview canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            float[] cognition = { 1f, 0f, .5f }; float[] confirmation = { 1f, 1f, .5f };
            string[] labels = { "未消耗 · 认知 100%", "完全消耗 · 认知 0%", "部分消耗 · 认知 50% · 回车确认进行中" };
            for (int i = 0; i < 3; i++)
            {
                var root = new GameObject("state " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(CognitionBarGraphic));
                root.transform.SetParent(canvas.transform, false);
                RectTransform rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(1728, source.rectTransform.rect.height);
                rect.anchoredPosition = new Vector2(0, 300 - i * 300);
                CognitionBarGraphic bar = root.GetComponent<CognitionBarGraphic>();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), bar);
                bar.SetDisplay(cognition[i], confirmation[i], true);
                CheckMesh(bar, cognition[i], confirmation[i]);
                var textObject = new GameObject("label " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textObject.transform.SetParent(canvas.transform, false);
                RectTransform textRect = (RectTransform)textObject.transform; textRect.sizeDelta = new Vector2(1728, 70);
                textRect.anchoredPosition = new Vector2(0, 380 - i * 300);
                Text label = textObject.GetComponent<Text>(); label.font = GameUIAssets.Shared.font; label.fontSize = 32;
                label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.text = labels[i]; label.raycastTarget = false;
            }
            Render(camera, 1920, 1080); Render(camera, 960, 540);
            CheckMaskAndFade(camera, canvas, source);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); File.WriteAllLines(Output + "art-checks.txt", passed); }
        Debug.Log("P27_ART_PASS " + passed.Count);
    }

    static void CheckMesh(CognitionBarGraphic bar, float cognition, float confirmation)
    {
        Mesh mesh = new Mesh();
        try
        {
            using (var vertices = new VertexHelper()) { populate.Invoke(bar, new object[] { vertices }); vertices.FillMesh(mesh); }
            Vector3[] points = mesh.vertices; Color32[] colors = mesh.colors32;
            var uv = new List<Vector4>(); mesh.GetUVs(0, uv);
            float whiteEdge = 0f, inkEdge = 0f, navyLeft = float.PositiveInfinity, navyRight = float.NegativeInfinity;
            int whites = 0, inks = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (uv[i].z > 0f) continue;
                if (colors[i].r > 240 && colors[i].g > 240 && colors[i].b > 240) { whites++; whiteEdge = Mathf.Max(whiteEdge, Mathf.Abs(points[i].x)); }
                else if (colors[i].r < 5 && colors[i].g < 5 && colors[i].b < 5) { inks++; inkEdge = Mathf.Max(inkEdge, Mathf.Abs(points[i].x)); }
                else { navyLeft = Mathf.Min(navyLeft, points[i].x); navyRight = Mathf.Max(navyRight, points[i].x); }
            }
            float half = bar.rectTransform.rect.width * .5f;
            Check(navyLeft <= -half && navyRight >= half, "认知 " + cognition + " 的外轮廓保留完整左右端点");
            Check(cognition == 0f ? whites == 0 && inks == 0 : whites > 0 && whiteEdge <= half * cognition + .01f,
                "认知 " + cognition + " 的白底按真实比例裁切，耗尽后内部为空");
            Check(inkEdge <= half * cognition * confirmation + .01f, "确认刻度受认知及确认剩余量共同裁切");
            bar.SetDisplay(cognition, 0f, true);
            using (var vertices = new VertexHelper()) { populate.Invoke(bar, new object[] { vertices }); vertices.FillMesh(mesh); }
            bool blackRemains = Array.Exists(mesh.colors32, c => c.r < 5 && c.g < 5 && c.b < 5);
            Check(!blackRemains, "确认归零后没有黑线、刻度或黑色残留轮廓");
            bar.SetDisplay(cognition, confirmation, true);
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    static void Render(Camera camera, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture active = RenderTexture.active;
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            Color background = image.GetPixel(3, height / 2);
            for (int i = 0; i < 3; i++)
            {
                int centerY = Mathf.RoundToInt(height * (.5f + (300 - i * 300) / 1080f));
                Color hole = image.GetPixel(width / 2, centerY);
                Check(Vector3.Distance(new Vector3(hole.r, hole.g, hole.b), new Vector3(background.r, background.g, background.b)) < .025f,
                    width + "画幅状态" + i + "的中央菱形内部透明，透出真实背景");
            }
            File.WriteAllBytes(Output + "three-states-" + width + ".png", image.EncodeToPNG());
            Color[] withGlow = image.GetPixels();
            // 预览场景对象不混入实际游戏；关闭同一组预览的柔光，取得逐像素差分基线。
            var previewBars = new List<CognitionBarGraphic>();
            foreach (GameObject root in camera.scene.GetRootGameObjects())
                foreach (CognitionBarGraphic bar in root.GetComponentsInChildren<CognitionBarGraphic>())
                { previewBars.Add(bar); glowEnabled.SetValue(bar, false); bar.SetVerticesDirty(); }
            try
            {
                Canvas.ForceUpdateCanvases(); camera.Render(); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Color[] withoutGlow = image.GetPixels();
                int[] changed = new int[3];
                for (int row = 0; row < 3; row++)
                {
                    int centerY = Mathf.RoundToInt(height * (.5f + (300 - row * 300) / 1080f));
                    int band = Mathf.CeilToInt(28f * height / 1080f);
                    for (int y = centerY - band; y <= centerY + band; y++)
                        for (int x = 0; x < width; x++)
                            if (Difference(withGlow[y * width + x], withoutGlow[y * width + x]) > .01f) changed[row]++;
                }
                Check(changed[0] > changed[2] * 1.5f && changed[2] > 0,
                    width + "画幅光晕实际影响面积随满条到半条缩小：" + changed[0] + "/" + changed[2]);
                Check(changed[1] == 0, width + "画幅耗尽后没有残留柔光，金框沿原图显示");
                int sampleX = width / 2 + Mathf.RoundToInt(120f * width / 1920f);
                int offset = Mathf.RoundToInt(10f * height / 1080f);
                int fullY = Mathf.RoundToInt(height * (.5f + 300f / 1080f)) + offset;
                int halfY = Mathf.RoundToInt(height * (.5f - 300f / 1080f)) + offset;
                Color full = withGlow[fullY * width + sampleX], half = withGlow[halfY * width + sampleX];
                Check(Difference(full, withoutGlow[fullY * width + sampleX]) > .03f && Difference(full, half) < .01f,
                    width + "画幅满条与半条相同位置的柔光亮度保持一致");
                File.AppendAllText(Output + "glow-pixel-audit.txt", width + ": changed full=" + changed[0] + ", half=" + changed[2] + ", empty=" + changed[1]
                    + ", brightness difference=" + Difference(full, half) + "\n");
            }
            finally { foreach (CognitionBarGraphic bar in previewBars) { glowEnabled.SetValue(bar, true); bar.SetVerticesDirty(); } }
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = active;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("P27_ART_FAILED " + message); passed.Add("PASS " + message); }

    static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

    static void CheckMaskAndFade(Camera camera, Canvas canvas, CognitionBarGraphic source)
    {
        foreach (Transform child in canvas.transform) child.gameObject.SetActive(false);
        var holder = new GameObject("glow clipping fixture", typeof(RectTransform), typeof(RectMask2D), typeof(CanvasGroup));
        holder.transform.SetParent(canvas.transform, false); ((RectTransform)holder.transform).sizeDelta = new Vector2(600, 60);
        var item = new GameObject("masked cognition", typeof(RectTransform), typeof(CanvasRenderer), typeof(CognitionBarGraphic));
        item.transform.SetParent(holder.transform, false); ((RectTransform)item.transform).sizeDelta = new Vector2(1728, source.rectTransform.rect.height);
        var bar = item.GetComponent<CognitionBarGraphic>(); EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), bar); bar.SetDisplay(1f, 1f, true);
        var target = new RenderTexture(1920, 1080, 24); var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            Action capture = () => { Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); };
            capture(); Color background = image.GetPixel(3, 540), full = image.GetPixel(1080, 550);
            Check(Difference(image.GetPixel(1268, 550), background) < .01f && Difference(full, background) > .03f,
                "RectMask2D同时裁切白底与柔光，遮罩内仍有柔光");
            Color ink = image.GetPixel(1080, 540);
            Check(Mathf.Max(ink.r, ink.g, ink.b) < .02f, "柔光位于白底下方，不冲淡黑色确认线");
            CanvasGroup group = holder.GetComponent<CanvasGroup>(); group.alpha = .5f; capture();
            Color faded = image.GetPixel(1080, 550);
            Check(Difference(faded, background) > .01f && Difference(faded, background) < Difference(full, background),
                "CanvasGroup透明度正常淡出柔光，不改变剩余长度");
            group.alpha = 0f; capture();
            Check(Difference(image.GetPixel(1080, 550), background) < .01f && Difference(image.GetPixel(1080, 540), background) < .01f,
                "CanvasGroup隐藏后柔光与主体都无残留");
            group.alpha = 1f; Object.DestroyImmediate(holder.GetComponent<RectMask2D>());
            holder.AddComponent<Image>(); holder.AddComponent<Mask>().showMaskGraphic = false; capture();
            Check(Difference(image.GetPixel(1268, 550), background) < .01f && Difference(image.GetPixel(1080, 550), background) > .03f,
                "Stencil Mask保持柔光及主体裁切兼容");
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
#endif
