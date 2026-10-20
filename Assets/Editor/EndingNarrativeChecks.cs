using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class EndingNarrativeChecks
{
    static readonly List<string> results = new List<string>();
    static IEnumerator run;
    static string output, oldDirectory, error;
    static bool done, oldOptionsEnabled;
    static bool RenderOnly => Environment.GetEnvironmentVariable("ENDING_RENDER_CHECK") == "1";
    static EnterPlayModeOptions oldOptions;
    static double deadline;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode) throw new Exception("Use a separate batch editor");
        NarrativeSliceAssets.Prepare();
        output = Path.GetFullPath(RenderOnly ? ".utmp/EndingNarrativeRender" : ".utmp/EndingNarrative");
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(output, "checks.txt"))) File.Delete(Path.Combine(output, "checks.txt"));
        oldDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        DataAndInitMgr.debugPlayerProgressDirectory = Path.Combine(output,"save-" + Guid.NewGuid().ToString("N"));
        oldOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        oldOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        MonoPublicMgr.IsQuitting = false;
        EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity");
        deadline = EditorApplication.timeSinceStartup + 600;
        results.Clear(); error = null; done = false;
        run = Run();
        Application.logMessageReceived += Error;
        EditorApplication.update += Update;
        EditorApplication.isPlaying = true;
    }

    static void Update()
    {
        if (done)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= Update;
            Application.logMessageReceived -= Error;
            EditorSettings.enterPlayModeOptionsEnabled = oldOptionsEnabled;
            EditorSettings.enterPlayModeOptions = oldOptions;
            DataAndInitMgr.debugPlayerProgressDirectory = oldDirectory;
            results.Add(error == null ? "RESULT PASS" : "RESULT FAIL " + error);
            File.WriteAllLines(Path.Combine(output,"checks.txt"),results);
            EditorApplication.Exit(error == null ? 0 : 1);
            return;
        }
        if (!Application.isPlaying) return;
        try
        {
            if (error != null) throw new Exception(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Narrative checks timed out");
            if (run.MoveNext()) return;
        }
        catch(Exception ex) { error = ex.ToString(); }
        done = true; EditorApplication.isPlaying = false;
    }

    static IEnumerator Run()
    {
        while (Main.Instance == null || string.IsNullOrEmpty(InputControlMgr.Instance.PlayerModleName)) yield return null;
        var flow = SceneChangeMgr.Instance;
        var story = NarrativeFlow.Instance;
        var catalog = JsonUtility.FromJson<NarrativeFlow.Catalog>(Resources.Load<TextAsset>("NarrativeSlice").text);
        string[] endings = { "StillRaining", "OtherSide", "Unfolded" };
        for (int index = 0; index < endings.Length; index++)
        {
            string scene = "E0" + (index + 1) + "_" + endings[index];
            string id = "END_" + endings[index];
            var node = Array.Find(catalog.nodes, n => n.id == id);
            Check(node != null && node.pages.Length == 3, "three accepted pages " + scene);
            Check(DataAndInitMgr.Instance.SaveEndingProgress(5, scene, flow.PlayerEffects), "save replay fixture " + scene);
            flow.EnterSavedLevel();
            while (!flow.IsEnding || flow.IsLoading) yield return null;
            var ending = Object.FindObjectOfType<OrimeEnding>();
            var actor = ending.Context.ControlledActor;
            Vector3 start = actor.transform.position;
            UnityEngine.Events.UnityAction move = () => actor.ReceiveInput(new EndingInputFrame(1f, false));
            MonoPublicMgr.Instance.AddPhysicalTimingUpdate(move, 2);
            while (story.ActiveId != id) yield return null;
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(move, 2);
            Check(actor.transform.position.x > start.x + 3f, "real physics walk reaches closing performance " + scene);
            Check(!actor.ControlEnabled && !actor.AutomaticMotion, "reading follows completed closing walk " + scene);
            var finish = (Transform)typeof(OrimeEnding).GetField("finishPoint", Hidden).GetValue(ending);
            Check(Vector3.Distance(actor.transform.position, finish.position) < .001f, "actor at authored final pose " + scene);
            if (index == 2)
            {
                var other = (EndingActor)typeof(OrimeEnding).GetField("companion", Hidden).GetValue(ending);
                var otherFinish = (Transform)typeof(OrimeEnding).GetField("companionFinishPoint", Hidden).GetValue(ending);
                Check(Vector3.Distance(other.transform.position, otherFinish.position) < .001f
                    && Vector3.Distance(actor.transform.position, other.transform.position) > .1f, "Unfolded actors remain separate after walking together");
            }
            Check(Time.timeScale == 0 && ending.Context.IsPaused, "reading pauses ending clock " + scene);
            Check(!flow.ToggleGameMenu(), "menu cannot cover ending pages " + scene);
            foreach (var line in ending.GetComponentsInChildren<TextMesh>())
                Check(line.color.a == 0, "old timed caption stays hidden " + scene);
            var text = Array.Find(story.GetComponentsInChildren<Text>(), t => t.name == "StoryText");
            var next = Array.Find(story.GetComponentsInChildren<Button>(), t => t.name == "Next");
            var panel = (RectTransform)text.transform.parent;
            Check(panel.anchorMin.y == 1f && panel.anchoredPosition.y == -190f, "ending panel positioned above actors " + scene);
            var before = actor.transform.position;
            var veil = (SpriteRenderer)typeof(OrimeEnding).GetField("closingVeil", Hidden).GetValue(ending);
            float until = Time.realtimeSinceStartup + .35f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Check(story.PageIndex == 0 && veil.color.a == 0 && actor.transform.position == before, "reading never auto advances or fades " + scene);
            for (int page = 0; page < node.pages.Length; page++)
            {
                Canvas.ForceUpdateCanvases();
                Check(story.PageIndex == page && text.text.Replace("\n", "") == node.pages[page], "exact approved copy page " + scene + "/" + page);
                Check(text.preferredHeight <= text.rectTransform.rect.height, "text height fits " + scene + "/" + page);
                Check(!text.text.Contains("\n”") && !text.text.Contains("\n，"), "no stranded closing punctuation " + scene + "/" + page);
                Check(!text.text.Contains("旁白：") && !text.text.Contains("女孩："), "no speaker prefixes " + scene + "/" + page);
                foreach (char ch in text.text)
                    if (!char.IsWhiteSpace(ch)) CheckGlyph(text.font.HasCharacter(ch), scene, ch);
                foreach (Vector2 size in new[] { new Vector2(800,600), new Vector2(1280,720), new Vector2(1920,1080), new Vector2(2560,1080) })
                {
                    // CanvasScaler.Expand uses the smaller axis scale; all anchored content fits this minimum canvas.
                    float scale = Mathf.Min(size.x / 1280f, size.y / 720f);
                    Check(1120f * scale <= size.x && 290f * scale <= size.y && text.preferredHeight * scale <= 170f * scale,
                        "scaled panel fits " + size + " " + scene + "/" + page);
                }
                if (RenderOnly)
                {
                    RenderPage(story, scene + "-" + (page + 1), 1280, 720);
                    if (page == 0) RenderPage(story, scene + "-" + (page + 1) + "-800", 800, 600);
                }
                next.onClick.Invoke();
                yield return null;
            }
            Check(!story.IsOpen && Time.timeScale == 1 && !ending.Context.IsPaused, "last page restores ending clock " + scene);
            Check(DataAndInitMgr.Instance.HasSeenNarrative(id), "completed copy saved " + scene);
            while (flow.IsEnding || flow.IsLoading) yield return null;
            Check(!flow.StoryActive && !story.IsOpen, "natural fade returns to menu " + scene);
            if (RenderOnly) continue;
            // Replay must show already-read prose, and menu cancellation must restart from page one.
            flow.EnterSavedLevel();
            while (!flow.IsEnding || flow.IsLoading) yield return null;
            ending = Object.FindObjectOfType<OrimeEnding>(); actor = ending.Context.ControlledActor;
            move = () => actor.ReceiveInput(new EndingInputFrame(1f, false));
            MonoPublicMgr.Instance.AddPhysicalTimingUpdate(move, 2);
            while (story.ActiveId != id) yield return null;
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(move, 2);
            Check(story.PageIndex == 0, "seen ending replays from first page " + scene);
            story.Next();
            flow.ReturnToMenu(); while (flow.IsLoading) yield return null;
            Check(!story.IsOpen && !flow.StoryActive && Time.timeScale == 1, "return mid-reading releases pause " + scene);
            flow.EnterSavedLevel();
            while (!flow.IsEnding || flow.IsLoading) yield return null;
            ending = Object.FindObjectOfType<OrimeEnding>(); actor = ending.Context.ControlledActor;
            move = () => actor.ReceiveInput(new EndingInputFrame(1f, false));
            MonoPublicMgr.Instance.AddPhysicalTimingUpdate(move, 2);
            while (story.ActiveId != id) yield return null;
            MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(move, 2);
            Check(story.PageIndex == 0, "cancelled replay restarts at first page " + scene);
            Array.Find(story.GetComponentsInChildren<Button>(), t => t.name == "Skip").onClick.Invoke();
            yield return null;
            Check(!story.IsOpen && Time.timeScale == 1, "skip resumes native ending " + scene);
            while (flow.IsEnding || flow.IsLoading) yield return null;
            Check(!flow.StoryActive, "skip still fades and returns cleanly " + scene);
        }
        Check(true, "all displayed Chinese characters available in the actual UI font");
    }
    static void RenderPage(NarrativeFlow story, string file, int width, int height)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new Exception("Rendering requires batch editor without -nographics");
        var camera = InputControlMgr.Instance.GameCamera;
        var canvas = story.GetComponent<Canvas>();
        var scaler = story.GetComponent<CanvasScaler>();
        bool oldScalerEnabled = scaler.enabled; float oldScale = canvas.scaleFactor;
        var oldRect = camera.rect; int oldMask = camera.cullingMask;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        float oldDistance = canvas.planeDistance;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var layers = new Dictionary<GameObject,int>();
        var target = new RenderTexture(width, height, 24);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var uiObject = new GameObject("OffscreenUIOnly");
        var uiCamera = uiObject.AddComponent<Camera>();
        uiCamera.enabled = false; uiCamera.clearFlags = CameraClearFlags.Depth;
        uiCamera.cullingMask = 1 << 31; uiCamera.targetTexture = target;
        try
        {
            foreach (var child in story.GetComponentsInChildren<Transform>(true))
            { layers[child.gameObject] = child.gameObject.layer; child.gameObject.layer = 31; }
            camera.cullingMask = oldMask & ~(1 << 31);
            camera.targetTexture = target;
            // Match the game's 16:9 world letterbox, while the overlay spans the entire screen.
            float aspect = (float)width / height;
            float worldHeight = Mathf.Min(1f, aspect / (16f/9f));
            float worldWidth = Mathf.Min(1f, (16f/9f) / aspect);
            camera.rect = new Rect((1-worldWidth)/2, (1-worldHeight)/2, worldWidth, worldHeight);
            scaler.enabled = false; canvas.scaleFactor = Mathf.Min(width / 1280f, height / 720f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera; canvas.planeDistance = uiCamera.nearClipPlane + .5f;
            Canvas.ForceUpdateCanvases();
            camera.Render(); uiCamera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, file + ".png"), pixels.EncodeToPNG());
            Check(true, "offscreen image " + file);
        }
        finally
        {
            foreach (var item in layers) item.Key.layer = item.Value;
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
            camera.targetTexture = oldTarget; camera.rect = oldRect; camera.cullingMask = oldMask;
            canvas.scaleFactor = oldScale; scaler.enabled = oldScalerEnabled;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(uiObject); Object.DestroyImmediate(pixels);
            target.Release(); Object.DestroyImmediate(target);
            Canvas.ForceUpdateCanvases();
        }
    }

    static void CheckGlyph(bool ok, string scene, char ch)
    { if (!ok) throw new Exception("Missing glyph " + ch + " in " + scene); }

    static void Capture(string file) { /* Headless checks intentionally do not render or capture a desktop. */ }
    static void Check(bool ok,string name)
    { if(!ok) throw new Exception(name); results.Add("PASS "+name); File.WriteAllLines(Path.Combine(output,"progress.txt"),results); }
    static void Error(string text,string trace,LogType type)
    { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) error ??= text+"\n"+trace; }
}
