#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>实际保存场景的镜头覆盖、层次顺序、生命周期及原生渲染定向检查。</summary>
public static class P26DepthChecks
{
    const string Output = ".utmp/P26Depth/";
    static readonly List<string> report = new List<string>();
    static int passed;

    [MenuItem("自定义工具/测试工具/验证/P26 验证检修站背景层次（Edit Mode）")]
    public static void RunPrototype() => Run(new[] { 4 });

    [MenuItem("自定义工具/测试工具/验证/P26 验证五关背景层次（Edit Mode）")]
    public static void RunAll() => Run(Enumerable.Range(0, 5));

    static void Run(IEnumerable<int> indices)
    {
        P26DepthTools.CheckEditState();
        Directory.CreateDirectory(Output); report.Clear(); passed = 0;
        string previous = SceneManager.GetActiveScene().path;
        try
        {
            foreach (int index in indices) CheckScene(index);
            report.Insert(0, "PASS: " + passed + " checks");
            report.Add("边界：通过 Edit Mode 调用正式更新／启停入口并由原生相机渲染；未签收人类连续试玩或 Windows 实包观感与帧率。");
            Debug.Log("P26_DEPTH_PASS " + passed);
        }
        finally
        {
            File.WriteAllLines(Output + "depth-checks.txt", report);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        }
    }

    static void CheckScene(int index)
    {
        string name = P26DepthTools.Scenes[index];
        Scene scene = EditorSceneManager.OpenScene(P26DepthTools.Folder + name + ".unity");
        var before = P26DepthTools.GameplaySnapshot(scene);
        var cameraStates = P26DepthTools.All<Camera>(scene).ToDictionary(c => c, c => c.enabled);
        var layers = P26DepthTools.All<SceneParallaxLayer>(scene).ToArray();
        var original = layers.ToDictionary(c => c, c => c.transform.position);
        GameObject temporary = new GameObject("P26 原生渲染检查临时镜头") { hideFlags = HideFlags.DontSave };
        Camera view = temporary.AddComponent<Camera>();
        temporary.tag = "MainCamera"; view.orthographic = true; view.orthographicSize = 5.3f;
        view.aspect = 16f / 9f; view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.04f, .05f, .07f);
        try
        {
            foreach (var pair in cameraStates) pair.Key.enabled = false;
            Check(layers.Length == 2, name + " 仅有远景／中景两个视差组件");
            Check(Camera.main == view, name + " 正式回调识别启用的主镜头，忽略已关闭的场景镜头");
            foreach (var layer in layers) Check(layer.ValidateConfiguration(out _), name + " " + layer.name + " 制作配置有效");
            var far = layers.Single(l => l.GetComponent<SpriteRenderer>() != null);
            var mid = layers.Single(l => l != far);
            SpriteRenderer farRenderer = far.GetComponent<SpriteRenderer>();
            var root = scene.GetRootGameObjects().Single(g => g.name == P26DepthTools.RootName);
            Check(root.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is SpriteRenderer || c is SceneParallaxLayer),
                name + " 新装饰根不含碰撞、刚体、灯或玩法脚本");
            Check(farRenderer.sortingOrder < mid.GetComponentsInChildren<SpriteRenderer>().Min(s => s.sortingOrder)
                && mid.GetComponentsInChildren<SpriteRenderer>().All(s => s.sortingOrder < -20), name + " 两层背景均排在可交互对象后面");
            foreach (SpriteRenderer front in root.transform.Find("静态边缘前景_无碰撞").GetComponentsInChildren<SpriteRenderer>())
                Check(front.bounds.max.x <= 0 || front.bounds.min.x >= 80, name + " " + front.name + " 完整轮廓不跨入玩家活动范围");
            var frontPositions = root.transform.Find("静态边缘前景_无碰撞").GetComponentsInChildren<Transform>().ToDictionary(t => t, t => t.position);
            foreach (float x in new[] { -4f, 0f, 40f, 80f, 84f })
            foreach (float y in new[] { -5f, 0f, P26DepthTools.Heights[index] / 2, P26DepthTools.Heights[index], P26DepthTools.Heights[index] + 5 })
            {
                view.transform.position = new Vector3(x, y, -10); Tick(layers);
                Bounds b = farRenderer.bounds; float halfX = view.orthographicSize * view.aspect;
                Check(b.min.x <= x - halfX && b.max.x >= x + halfX && b.min.y <= y - view.orthographicSize && b.max.y >= y + view.orthographicSize,
                    name + " 远景覆盖16:9镜头 (" + x + "," + y + ")，含场外余量");
            }
            Vector3 reference = new Vector3(40, P26DepthTools.Heights[index] / 2, -10);
            foreach (Vector2 outside in new[] { new Vector2(-200, -200), new Vector2(250, 300) })
            {
                view.transform.position = new Vector3(outside.x, outside.y, -10); Tick(layers);
                Bounds b = farRenderer.bounds; float halfX = view.orthographicSize * view.aspect;
                Check(b.min.x <= outside.x - halfX + .001f && b.max.x >= outside.x + halfX - .001f
                    && b.min.y <= outside.y - view.orthographicSize + .001f && b.max.y >= outside.y + view.orthographicSize - .001f,
                    name + " 远距离漫游仍有完整底图覆盖 " + outside);
            }
            view.transform.position = reference; Tick(layers);
            Check(layers.All(l => (l.transform.position - original[l]).sqrMagnitude < .000001f), name + " 返回制作基准后无漂移");
            view.transform.position = reference + new Vector3(8, 6); Tick(layers);
            Vector3 farDelta = far.transform.position - original[far], midDelta = mid.transform.position - original[mid];
            Check(farDelta.x > midDelta.x && midDelta.x > 0 && farDelta.x < 8
                && farDelta.y > midDelta.y && midDelta.y > 0 && farDelta.y < 6, name + " 远近层具有不同视深且未锁在画面上");
            var shifted = layers.ToDictionary(l => l, l => l.transform.position);
            for (int i = 0; i < 100; i++) Tick(layers);
            Check(layers.All(l => (l.transform.position - shifted[l]).sqrMagnitude < .000001f), name + " 镜头停住100帧不累计偏移");
            foreach (var layer in layers)
            {
                Invoke(layer, "OnDisable"); layer.enabled = false;
                Check(layer.transform.position == original[layer], name + " 关闭 " + layer.name + " 恢复制作位置");
                layer.enabled = true; Invoke(layer, "OnEnable"); Invoke(layer, "LateUpdate");
                Check((layer.transform.position - shifted[layer]).sqrMagnitude < .000001f, name + " 重新启用不把旧偏移当起点");
            }
            Check(frontPositions.All(p => p.Key.position == p.Value), name + " 静态前景不随镜头移动");
            foreach (var layer in layers) Check(layer.transform.position.z == original[layer].z, name + " 视差不改深度排序的Z");
            foreach (var setting in P26DepthTools.All<SceneLightingSettings>(scene)) setting.Apply();
            view.transform.position = new Vector3(72, new[] { 26.2f, 50.2f, 78.2f, 92.2f, 14f }[index], -10);
            Tick(layers); Render(view, Output + name + "_画幅.png");
            if (index == 4)
            {
                view.orthographicSize = 25.5f; view.transform.position = new Vector3(40, 22, -10);
                Tick(layers); Render(view, Output + name + "_全景.png");
                view.orthographicSize = 5.3f;
                for (int frame = 0; frame < 3; frame++)
                {
                    view.transform.position = new Vector3(44 + frame * 5, 14, -10); Tick(layers);
                    Render(view, Output + name + "_横移_" + frame + ".png");
                }
            }
        }
        finally
        {
            foreach (var layer in layers) if (layer != null) { layer.RestoreAuthoredPosition(); Invoke(layer, "OnDisable"); }
            Object.DestroyImmediate(temporary);
            foreach (var pair in cameraStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        }
        Check(before.OrderBy(p => p.Key).SequenceEqual(P26DepthTools.GameplaySnapshot(scene).OrderBy(p => p.Key)), name + " 检查与渲染后原玩法组件和位置未变");
    }

    static void Tick(IEnumerable<SceneParallaxLayer> layers)
    { foreach (var layer in layers) Invoke(layer, "LateUpdate"); }

    static void Invoke(SceneParallaxLayer layer, string method)
        => typeof(SceneParallaxLayer).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);

    static void Render(Camera view, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var rt = new RenderTexture(1600, 900, 24); var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            view.targetTexture = rt; view.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { view.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); Object.DestroyImmediate(rt); }
    }

    static void Check(bool condition, string message)
    {
        report.Add((condition ? "PASS: " : "FAIL: ") + message);
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }
}
#endif
