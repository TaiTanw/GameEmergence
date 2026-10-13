#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在 P6 真实换关夹具中检查渲染像素与演出时序，不修改保存的场景或预设。</summary>
public static class P66TransitionChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static bool pixelsChecked, menuDarkened, menuBrightened, levelDarkened, levelBrightened, animationAdvanced;
    static int serialBefore = -1;
    static Vector2 bodyPosition;
    static Player previousBody;
    static string previousScene;
    static float animationTime;
    static Animator observedAnimator;
    static bool previouslyPaused, settledWhileHidden, settledBeforeReveal;
    static Player settlingBody;
    static GameObject settleFloor;
    static float settleStartY;

    public static void Reset()
    {
        pixelsChecked = menuDarkened = menuBrightened = levelDarkened = levelBrightened = animationAdvanced = false;
        serialBefore = -1;
        previousBody = null;
        observedAnimator = null;
        previouslyPaused = settledWhileHidden = settledBeforeReveal = false;
        settlingBody = null;
        ReleaseFixture();
    }

    public static void ReleaseFixture()
    {
        if (settleFloor != null) Object.DestroyImmediate(settleFloor);
        settleFloor = null;
    }

    public static void Observe(List<string> passed)
    {
        SceneChangeMgr flow = SceneChangeMgr.Instance;
        if (!(bool)typeof(SceneChangeMgr).GetField("loading", Hidden).GetValue(flow))
        {
            Require(!Object.FindObjectsOfType<Camera>().Any(c => c.enabled && c.name == "PlayerPortraitCamera"),
                "演出结束或取消后关闭实体前景镜头");
            Require(Object.FindObjectsOfType<AudioListener>().Count(a => a.enabled) <= 1,
                "菜单与游戏镜头切换后只有一个音频监听器");
            return;
        }
        Require(!InputControlMgr.Instance.InputEnabled, "整个淡入淡出期间输入保持关闭");
        bool paused = Time.timeScale == 0f;
        Require(paused || Time.timeScale == 1f, "过场只在安置前暂停时间，安置后恢复正常速度");
        LoadingPanel panel = Object.FindObjectOfType<LoadingPanel>();
        if (panel == null) return;
        if (!pixelsChecked)
        {
            CheckCoverPixels(panel, passed);
            pixelsChecked = true;
        }
        bool fromMenu = (bool)typeof(SceneChangeMgr).GetField("enterFromMenu", Hidden).GetValue(flow);
        Player body = (Player)typeof(SceneChangeMgr).GetField("body", Hidden).GetValue(flow);
        int serial = (int)typeof(SceneChangeMgr).GetField("loadSerial", Hidden).GetValue(flow);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        float weight = panel.TransitionWeight;
        if (!paused && body != null)
        {
            SpriteRenderer sprite = body.GetComponentInChildren<SpriteRenderer>();
            if (weight == 1f)
            {
                Require(sprite.forceRenderingOff == fromMenu,
                    "菜单全黑缓冲隐藏实体，普通跨关持续显示前景实体");
                if (!settledBeforeReveal && settlingBody == null && fromMenu && SceneLevelSetup.Current.Teaching)
                    CreateRaisedSpawn(body);
                if (body == settlingBody && body.GetComponent<Rigidbody2D>().position.y < settleStartY - .05f)
                    settledWhileHidden = true;
            }
            else if (body == settlingBody && !settledBeforeReveal)
            {
                Require(settledWhileHidden && body.NowPhyData.isGrounded
                    && Mathf.Abs(body.GetComponent<Rigidbody2D>().velocity.y) < .01f && !sprite.forceRenderingOff,
                    "真实出生点高出地面 0.15 米时，先在全黑下落稳，再揭开画面");
                settledBeforeReveal = true;
                passed.Add("PASS P6.6 出生点高出地面 0.15 米：输入关闭时先恢复物理，全黑下落稳后再淡入");
            }
        }
        if (!fromMenu && body != null)
        {
            Rigidbody2D rb = body.GetComponent<Rigidbody2D>();
            if (paused && previouslyPaused && previousBody == body && previousScene == scene && serialBefore == serial)
                Require(rb.position == bodyPosition, "渐暗和安置前暂停刚体，不在旧场景中继续下落");
            bodyPosition = rb.position;
            previousBody = body;
            previousScene = scene;
            Animator animator = body.GetComponentInChildren<Animator>();
            Require(animator.updateMode == AnimatorUpdateMode.UnscaledTime, "实体动画独立于暂停时间播放");
            float time = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (!paused || !previouslyPaused || observedAnimator != animator || serialBefore != serial)
                animationTime = time;
            else if (time > animationTime + .01f) animationAdvanced = true;
            observedAnimator = animator;
            Player spirit = (Player)typeof(SceneChangeMgr).GetField("spirit", Hidden).GetValue(flow);
            if (spirit != null) Require(spirit.GetComponentInChildren<SpriteRenderer>().forceRenderingOff,
                "过场只展示实体，新旧精神体保持隐藏");
        }
        serialBefore = serial;
        previouslyPaused = paused;
        if (weight <= .2f || weight >= .8f) return;
        bool darkening = scene == (fromMenu ? "BeginScenes" : "TeskScenes1");
        if (fromMenu && darkening && !menuDarkened) { menuDarkened = true; Capture("menu-darkening.png"); }
        if (fromMenu && !darkening && !menuBrightened) { menuBrightened = true; Capture("menu-brightening.png"); }
        if (!fromMenu && darkening && !levelDarkened) { levelDarkened = true; Capture("level-darkening.png"); }
        if (!fromMenu && !darkening && !levelBrightened) { levelBrightened = true; Capture("level-brightening.png"); }
    }

    public static void Verify(List<string> passed)
    {
        Require(pixelsChecked && menuDarkened && menuBrightened && levelDarkened && levelBrightened,
            "真实菜单进关与关间切换均经历可见的渐暗、渐亮");
        Require(animationAdvanced, "物理暂停期间真实 Animator 的播放时间继续推进");
        Require(settledBeforeReveal, "实际教学进关在淡入前完成小幅出生下落");
        passed.Add("PASS P6.6 菜单/跨关渐暗渐亮、安置前暂停、安置后物理恢复、实体动画继续、精神体隐藏与取消清理");
    }

    static void CreateRaisedSpawn(Player body)
    {
        body.RefreshRuntime(new Vector2(9000f, 9000f));
        Bounds bounds = body.GetComponent<CharacterPhysics>().BodyBounds;
        settleFloor = new GameObject("P66 temporary spawn floor");
        settleFloor.layer = LayerMask.NameToLayer("Ground");
        settleFloor.transform.position = new Vector3(bounds.center.x, bounds.min.y - .15f - .05f, 0f);
        settleFloor.AddComponent<BoxCollider2D>().size = new Vector2(6f, .1f);
        Physics2D.SyncTransforms();
        body.RefreshRuntime(new Vector2(9000f, 9000f));
        settlingBody = body;
        settleStartY = body.GetComponent<Rigidbody2D>().position.y;
    }

    static void CheckCoverPixels(LoadingPanel panel, List<string> passed)
    {
        Camera ui = (Camera)typeof(UIMgr).GetField("uiCamera", Hidden).GetValue(UIMgr.Instance);
        CameraClearFlags flags = ui.clearFlags;
        Color background = ui.backgroundColor;
        Rect viewport = ui.rect;
        float weight = panel.TransitionWeight;
        BasePanel[] others = Object.FindObjectsOfType<BasePanel>().Where(p => p != panel && p.gameObject.activeSelf).ToArray();
        RenderTexture previous = ui.targetTexture, active = RenderTexture.active;
        var render = new RenderTexture(320, 180, 24);
        var texture = new Texture2D(320, 180, TextureFormat.RGB24, false);
        try
        {
            foreach (BasePanel other in others) other.gameObject.SetActive(false);
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.backgroundColor = Color.white;
            ui.targetTexture = render;
            ui.rect = new Rect(0f, 0f, 1f, 1f);
            float[][] brightness = new float[3][];
            for (int step = 0; step < 3; step++)
            {
                panel.SetTransitionWeight(step * .5f);
                Canvas.ForceUpdateCanvases();
                ui.Render();
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, 320, 180), 0, 0); texture.Apply();
                brightness[step] = new[] { texture.GetPixel(160, 90).grayscale, texture.GetPixel(5, 5).grayscale,
                    texture.GetPixel(315, 5).grayscale, texture.GetPixel(5, 175).grayscale, texture.GetPixel(315, 175).grayscale,
                    texture.GetPixel(0, 0).grayscale, texture.GetPixel(319, 0).grayscale,
                    texture.GetPixel(0, 179).grayscale, texture.GetPixel(319, 179).grayscale,
                    texture.GetPixel(0, 90).grayscale, texture.GetPixel(319, 90).grayscale,
                    texture.GetPixel(160, 0).grayscale, texture.GetPixel(160, 179).grayscale };
                Directory.CreateDirectory(".utmp/P66Validation");
                File.WriteAllBytes(".utmp/P66Validation/cover-" + step + ".png", texture.EncodeToPNG());
            }
            for (int point = 0; point < brightness[0].Length; point++)
                Require(brightness[0][point] > .98f && brightness[1][point] > .05f && brightness[1][point] < .95f
                    && brightness[2][point] < .02f, "真实加载预设的中心、四角及最外侧像素随权重从白经过灰变黑，点 " + point);
            passed.Add("PASS P6.6 真实加载预设像素验证：透明、中间值、全黑覆盖中心、四角与最外侧");
        }
        finally
        {
            panel.SetTransitionWeight(weight);
            ui.clearFlags = flags; ui.backgroundColor = background; ui.targetTexture = previous; ui.rect = viewport;
            RenderTexture.active = active;
            foreach (BasePanel other in others) if (other != null) other.gameObject.SetActive(true);
            Object.DestroyImmediate(texture); Object.DestroyImmediate(render);
        }
    }

    static void Capture(string name)
    {
        Camera[] cameras = Object.FindObjectsOfType<Camera>().Where(c => c.enabled).OrderBy(c => c.depth).ToArray();
        RenderTexture[] previous = cameras.Select(c => c.targetTexture).ToArray();
        RenderTexture active = RenderTexture.active;
        var render = new RenderTexture(960, 540, 24);
        var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try
        {
            Canvas.ForceUpdateCanvases();
            foreach (Camera camera in cameras) { camera.targetTexture = render; camera.Render(); }
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
            Directory.CreateDirectory(".utmp/P66Validation");
            File.WriteAllBytes(".utmp/P66Validation/" + name, texture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < cameras.Length; i++) cameras[i].targetTexture = previous[i];
            RenderTexture.active = active;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(render);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("P6.6: " + message);
    }
}
#endif
