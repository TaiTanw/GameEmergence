#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>检查真实 GPU 采样，以及现有 Play Mode 夹具中的镜头启停；不保存制作资产。</summary>
public static class TransitionPixelationChecks
{
    public static void RunForBatch()
    {
        RunRasterChecks();
        P6LevelFlowChecks.RunForBatch();
    }

    public static void AssertState(bool expected, string label)
    {
        Camera camera = InputControlMgr.Instance.GameCamera;
        TransitionPixelation effect = camera != null ? camera.GetComponent<TransitionPixelation>() : null;
        Require((effect != null && effect.enabled) == expected,
            label + "；expected=" + expected + "，effect=" + (effect != null && effect.enabled));
        foreach (Camera other in Object.FindObjectsOfType<Camera>())
            if (other != camera) Require(other.GetComponent<TransitionPixelation>() == null,
                "UI 与角色前景镜头不挂像素化");
    }

    public static void RunRasterChecks()
    {
        Directory.CreateDirectory(".utmp/P19Validation");
        Shader shader = Resources.Load<Shader>("NecessaryAssets/TransitionPixelation");
        Require(shader != null && shader.isSupported, "Resources 中的真实 Shader 可加载且支持当前图形设备");
        CheckRaster(shader, 192, 108, 6);
        CheckRaster(shader, 384, 216, 12);
        var root = new GameObject("P19 temporary effect camera");
        try
        {
            var effect = root.AddComponent<TransitionPixelation>();
            MethodInfo set = typeof(TransitionPixelation).GetMethod("SetWeight", BindingFlags.Instance | BindingFlags.NonPublic);
            set.Invoke(effect, new object[] { .5f, 24f });
            Require(effect.enabled, "有转场权重才启用");
            set.Invoke(effect, new object[] { 0f, 24f });
            Require(!effect.enabled, "零权重停止全屏后处理");
        }
        finally { Object.DestroyImmediate(root); }
        File.WriteAllText(".utmp/P19Validation/raster-checks.txt",
            "PASS Shader 加载；两种分辨率正方形块采样；单位块完整还原；四边采样；启用／结束关闭。");
        Debug.Log("P19 raster checks: PASS");
    }

    static void CheckRaster(Shader shader, int width, int height, int block)
    {
        var source = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var render = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var material = new Material(shader);
        RenderTexture previous = RenderTexture.active;
        bool previousSRGB = GL.sRGBWrite;
        try
        {
            var colors = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                colors[y * width + x] = new Color(x / (float)(width - 1), y / (float)(height - 1), ((x * 7 + y * 11) % 17) / 16f, 1f);
            source.SetPixels(colors); source.Apply();
            GL.sRGBWrite = false;
            material.SetFloat("_BlockPixels", 1f);
            Read(source, material, render, output);
            foreach (Vector2Int point in new[] { Vector2Int.zero, new Vector2Int(width - 1, height - 1), new Vector2Int(width / 2, height / 2) })
                Require(Difference(output.GetPixel(point.x, point.y), source.GetPixel(point.x, point.y)) < .015f,
                    "单位像素块还原原图及方向");
            material.SetFloat("_BlockPixels", block);
            Read(source, material, render, output);
            Color center = output.GetPixel(block + 1, block + 1);
            for (int y = block; y < block * 2; y++) for (int x = block; x < block * 2; x++)
                Require(Difference(center, output.GetPixel(x, y)) < .01f, "正方形块内部颜色一致");
            Require(Difference(center, output.GetPixel(block * 2 + 1, block + 1)) > .01f
                && Difference(center, output.GetPixel(block + 1, block * 2 + 1)) > .01f,
                "水平与竖直相邻方块仍包含不同场景信息");
            foreach (Vector2Int point in new[] { Vector2Int.zero, new Vector2Int(width - 1, 0), new Vector2Int(0, height - 1), new Vector2Int(width - 1, height - 1) })
                Require(output.GetPixel(point.x, point.y).a > .98f, "边缘采样保持完整输出");
            File.WriteAllBytes(".utmp/P19Validation/blocks-" + width + ".png", output.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = previousSRGB;
            Object.DestroyImmediate(source); Object.DestroyImmediate(output);
            Object.DestroyImmediate(render); Object.DestroyImmediate(material);
        }
    }

    static void Read(Texture source, Material material, RenderTexture render, Texture2D output)
    {
        Graphics.Blit(source, render, material);
        RenderTexture.active = render;
        output.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); output.Apply();
    }
    static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("P19: " + message); }
}
#endif
