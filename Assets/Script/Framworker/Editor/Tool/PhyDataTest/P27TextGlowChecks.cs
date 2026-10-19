#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>结果页中文SDF、共享材质、柔光差分及原生遮罩／淡出；不修改场景与规则数据。</summary>
public static class P27TextGlowChecks
{
    const string Output = ".utmp/P273Validation/";
    const string Sample = "强化与削弱可能同时出现。移动速度 ×1.2 · 额外空中跳 +1 次\n移动速度 6.25 · 游泳速度 4.5 · 起跳初速度 9 · 额外空中跳 0 次";
    static readonly List<string> passed = new List<string>();
    [MenuItem("自定义工具/测试工具/验证/P27 验证结果页文字柔光（Edit Mode）")]
    public static void Run()
    {
        Directory.CreateDirectory(Output); passed.Clear();
        Transform source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/UIPanel/GameplayPanel.prefab").transform.Find("EscapeChoiceArea/Choices");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(P27AbilityTextStyle.FontPath);
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(P27AbilityTextStyle.GlowPath);
        Check(font != null && font.sourceFontFile == GameUIAssets.Shared.font && font.atlasPopulationMode == AtlasPopulationMode.Dynamic,
            "独立SDF沿用原字体，支持后续中文动态补字");
        Check(font.atlasTextures.Length == 1 && font.atlasTextures[0].width == 2048 && font.atlasPadding == 14,
            "当前字形预置在一张2048 Alpha8图集中，留14像素柔光边距");
        Check(!new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild").boolValue, "预置字形随资源包保存");
        var missingGlyphs = new HashSet<char>();
        foreach (char c in P27AbilityTextStyle.SeedText + Sample)
            if (!char.IsWhiteSpace(c) && !font.characterLookupTable.ContainsKey(c)) missingGlyphs.Add(c);
        Check(missingGlyphs.Count == 0, "当前菜单中文、数字和正负符号全部预置：" + string.Join("", missingGlyphs));
        Check(glow != null && glow.IsKeywordEnabled("GLOW_ON") && glow.shader.isSupported && !ShaderUtil.ShaderHasError(glow.shader),
            "局部共享SDF柔光材质及GLOW_ON编译通过");
        Check(source.Find("CostPage").GetComponentsInChildren<TMP_Text>(true).Length == 0
            && source.Find("EscapeBudgetText").GetComponent<Text>() != null, "第一页保持原生文字与纸样");
        foreach (string path in P27AbilityTextStyle.ResultPaths)
        {
            TMP_Text label = source.Find(path).GetComponent<TMP_Text>();
            Check(label != null && label.font == font && label.fontSharedMaterial == glow && label.color == Color.white && !label.raycastTarget,
                "第二页独立白字与共享柔光 " + path);
        }
        Scene preview = EditorSceneManager.NewPreviewScene();
        var plain = new Material(glow) { hideFlags = HideFlags.HideAndDontSave }; plain.DisableKeyword("GLOW_ON");
        try
        {
            var cameraObject = new GameObject("P273 camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var canvasObject = new GameObject("P273 canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            GameObject owner = new GameObject("SDF sample", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(CanvasGroup));
            owner.transform.SetParent(canvas.transform, false);
            TMP_Text text = owner.GetComponent<TMP_Text>(); text.font = font; text.color = Color.white; text.fontSize = 26;
            text.raycastTarget = false; text.richText = false; text.extraPadding = true;
            text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Truncate;
            text.rectTransform.sizeDelta = new Vector2(1300,160); text.text = Sample;
            foreach (int width in new[] { 1920,960 })
            {
                int height = width * 9 / 16;
                text.fontSharedMaterial = plain; text.UpdateMeshPadding(); text.ForceMeshUpdate();
                Texture2D before = Render(camera,width,height);
                text.fontSharedMaterial = glow; text.UpdateMeshPadding(); text.ForceMeshUpdate();
                Texture2D after = Render(camera,width,height);
                Check(!text.isTextOverflowing, width + " 中文长文与数字换行后不溢出");
                int visible = 0; foreach (var character in text.textInfo.characterInfo)
                    if (character.isVisible) visible++;
                Check(visible > 50, width + " 中文和正负数字符号生成实际字形网格");
                int expanded = 0, whiteCore = 0, core = 0;
                var a = before.GetPixels32(); var b = after.GetPixels32();
                for (int i=0;i<a.Length;i++)
                {
                    // TMP原生Glow采用加色：字外光可有RGB而alpha仍为零。
                    if (a[i].r+a[i].g+a[i].b < 6 && b[i].r+b[i].g+b[i].b > 12) expanded++;
                    if (a[i].a > 220) { core++; if (b[i].r+2 >= a[i].r && b[i].g+2 >= a[i].g && b[i].b+2 >= a[i].b) whiteCore++; }
                }
                Check(expanded > 100, width + " 柔光增加字外覆盖面积=" + expanded);
                Check(core > 0 && whiteCore > core * .95f, width + " 柔光保留白字亮度：核心="+core+"，保持="+whiteCore);
                camera.backgroundColor = new Color(.035f,.065f,.14f,1f);
                text.fontSharedMaterial = plain; text.UpdateMeshPadding(); text.ForceMeshUpdate();
                Texture2D plainPreview = Render(camera,width,height);
                text.fontSharedMaterial = glow; text.UpdateMeshPadding(); text.ForceMeshUpdate();
                Texture2D glowPreview = Render(camera,width,height);
                File.WriteAllBytes(Output + "text-plain-"+width+".png", plainPreview.EncodeToPNG());
                File.WriteAllBytes(Output + "text-glow-"+width+".png", glowPreview.EncodeToPNG());
                camera.backgroundColor = Color.clear;
                CanvasGroup group = owner.GetComponent<CanvasGroup>(); group.alpha = 0f;
                Texture2D hidden = Render(camera,width,height);
                Check(Area(hidden) == 0, width + " 字面与柔光一起淡出，不提前泄露文字");
                group.alpha = 1f;
                var maskObject = new GameObject("text mask", typeof(RectTransform), typeof(RectMask2D));
                maskObject.transform.SetParent(canvas.transform, false); ((RectTransform)maskObject.transform).sizeDelta = new Vector2(500,100);
                owner.transform.SetParent(maskObject.transform, false);
                Texture2D clipped = Render(camera,width,height);
                Check(Outside(clipped) == 0 && Area(clipped) > 0, width + " RectMask2D裁切文字与外扩柔光");
                ScrollRect scroll = maskObject.AddComponent<ScrollRect>();
                scroll.viewport = (RectTransform)maskObject.transform; scroll.content = text.rectTransform;
                scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
                Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 0f;
                Texture2D scrolled = Render(camera,width,height);
                Check(Outside(scrolled) == 0 && Difference(clipped, scrolled) > 100,
                    width + " 滚动后文字与柔光一起移动且不越过裁切边界");
                Object.DestroyImmediate(scroll); text.rectTransform.anchoredPosition = Vector2.zero;
                Object.DestroyImmediate(maskObject.GetComponent<RectMask2D>());
                maskObject.AddComponent<Image>(); maskObject.AddComponent<Mask>().showMaskGraphic = false;
                Texture2D stencil = Render(camera,width,height);
                Check(Outside(stencil) == 0 && Area(stencil) > 0, width + " Stencil Mask裁切文字与柔光");
                owner.transform.SetParent(canvas.transform, false); Object.DestroyImmediate(maskObject);
                foreach(var image in new[] {before,after,plainPreview,glowPreview,hidden,clipped,scrolled,stencil}) Object.DestroyImmediate(image);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); Object.DestroyImmediate(plain); }
        File.WriteAllLines(Output + "text-glow-checks.txt", passed);
        Debug.Log("P27.3 text glow: " + passed.Count + " checks passed");
    }
    static int Area(Texture2D image) { int n=0; foreach(var p in image.GetPixels32()) if(p.a>2||p.r>2||p.g>2||p.b>2)n++; return n; }
    static int Difference(Texture2D first, Texture2D second)
    { int n=0;var a=first.GetPixels32();var b=second.GetPixels32();for(int i=0;i<a.Length;i++)if(!a[i].Equals(b[i]))n++;return n; }
    static int Outside(Texture2D image)
    {
        int n=0; var pixels=image.GetPixels32(); float scale=image.width/1920f;
        for(int y=0;y<image.height;y++) for(int x=0;x<image.width;x++)
            if ((Mathf.Abs(x+.5f-image.width*.5f)>250*scale+1 || Mathf.Abs(y+.5f-image.height*.5f)>50*scale+1)
                && (pixels[y*image.width+x].a>2||pixels[y*image.width+x].r>2||pixels[y*image.width+x].g>2||pixels[y*image.width+x].b>2)) n++;
        return n;
    }
    static Texture2D Render(Camera camera,int width,int height)
    {
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); RenderTexture previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
            var result=new Texture2D(width,height,TextureFormat.RGBA32,false); result.ReadPixels(new Rect(0,0,width,height),0,0); result.Apply(); return result;
        }
        finally { camera.targetTexture=null; RenderTexture.active=previous; target.Release(); Object.DestroyImmediate(target); }
    }
    static void Check(bool condition,string message)
    { if(!condition)throw new InvalidOperationException("P27.3 FAILED: "+message); passed.Add("PASS "+message); }
}
#endif
