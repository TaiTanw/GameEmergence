#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>真实气体预设的 GPU 雾边、浓淡变化、形态切换和共享材质生命周期。</summary>
public static class GasVolumeVisualChecks
{
    const string Output = ".utmp/GasVolumeValidation/";
    const string Prefabs = "Assets/MyObjAssets/gameObject/Pf/SceneObj/NewPf/";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> passed = new List<string>();

    [MenuItem("自定义工具/测试工具/验证/P28 气体动态迷雾")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        passed.Clear();
        Scene scene = EditorSceneManager.NewPreviewScene();
        float lighting = Shader.GetGlobalFloat("_SceneLightingEnabled");
        Shader.SetGlobalFloat("_SceneLightingEnabled", 0f);
        try
        {
            var cameraObject = new GameObject("Gas preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true;
            camera.orthographicSize = 2.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            Material shared = null;
            foreach (string name in new[] { "气体团块", "有毒气体团块" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
                Check(prefab != null, name + " 正式预设可读取");
                GameObject instance = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance, scene);
                instance.transform.position = Vector3.zero;
                try
                {
                    var visual = instance.GetComponent<GasVolumeVisual>();
                    var sprite = instance.GetComponent<SpriteRenderer>();
                    var fogSprite = Read<SpriteRenderer>(visual,"fogSprite");
                    var box = instance.GetComponent<BoxCollider2D>();
                    var host = instance.GetComponent<ForceField>();
                    Check(visual != null && visual.ValidateConfiguration(out _), name + " 雾面内部关联有效");
                    Check(box.size == new Vector2(3,3) && box.isTrigger && box.offset == Vector2.zero
                        && instance.GetComponents<Collider2D>().Length == 1, name + " 原3×3唯一触发框保留");
                    Check(host != null && instance.GetComponents<BasicPhysicalObject>().Length == 1
                        && instance.GetComponentsInChildren<ParticleSystem>().Length == 0
                        && instance.GetComponent<Rigidbody2D>() == null, name + " 唯一环境宿主保留，无新增物理或粒子");
                    Check((instance.GetComponent<FixedContactHazard>() != null) == name.StartsWith("有毒"), name + " 原固定危险身份保留");
                    if (shared == null) shared = fogSprite.sharedMaterial;
                    Material originalMaterial = sprite.sharedMaterial;
                    Check(fogSprite.sharedMaterial == shared && shared.shader.name == "GameEmergence/GasVolume"
                        && originalMaterial.shader.name == "GameEmergence/AtmosphereSprite", name + " 雾面共用材质，根精灵保留原材质");
                    Color color = sprite.color;
                    Tick(visual, 0f);
                    Texture2D first = Render(camera,512,512);
                    File.WriteAllBytes(Output + name + "-t0.png", first.EncodeToPNG());
                    Color32[] pixels = first.GetPixels32();
                    int visible = 0, translucent = 0;
                    byte low = 255, high = 0;
                    for (int y=0;y<512;y++) for (int x=0;x<512;x++)
                    {
                        byte alpha = pixels[y*512+x].a;
                        if (alpha > 0) visible++;
                        if (alpha > 1 && alpha < 75) translucent++;
                        if (x>160 && x<352 && y>160 && y<352) { low = Math.Min(low,alpha); high = Math.Max(high,alpha); }
                        if ((x<41 || x>=471 || y<41 || y>=471) && alpha != 0)
                            throw new Exception(name + " 外扩雾面出现网格硬边或越出显示范围");
                    }
                    Check(visible > 95000 && visible < 210000 && translucent > 25000, name + " 实际GPU输出柔化透明雾面，覆盖=" + visible);
                    Check(high-low > 12 && high < 100, name + " 中央有多层浓淡而非均匀色块，alpha="+low+"–"+high);
                    Check(pixels[104*512+104].a > 10 && pixels[104*512+407].a > 10
                        && pixels[407*512+104].a > 10 && pixels[407*512+407].a > 10,
                        name + " 原判定框四角均被可见迷雾覆盖");
                    Check(pixels[256*512+40].a == 0 && pixels[256*512+80].a > 0,
                        name + " 雾边扩到判定框外，并在显示网格边缘前渐隐");
                    Tick(visual, 8f);
                    Texture2D later = Render(camera,512,512);
                    Check(Difference(first,later) > 12000 && SilhouetteDifference(first,later) > 1000,
                        name + " 内部浓淡和不规则轮廓随时间实际变化");
                    Object.DestroyImmediate(first); Object.DestroyImmediate(later);
                    var block = new MaterialPropertyBlock();
                    sprite.GetPropertyBlock(block); block.SetFloat("_GasTestSentinel",42f); sprite.SetPropertyBlock(block);
                    float clock = Read<float>(visual,"visualTime");
                    Tick(visual,0f); Tick(visual,0f);
                    Check(Read<float>(visual,"visualTime") == clock, name + " 零时间步保持雾面时钟");
                    Write(visual,"flowSpeed",0f); Tick(visual,2f);
                    Check(Read<float>(visual,"visualTime") == clock, name + " 流动速度0可制作静态迷雾");
                    Write(visual,"flowSpeed",.65f);
                    for (int i=0;i<100;i++) Tick(visual,.016f);
                    sprite.GetPropertyBlock(block);
                    Check(sprite.sharedMaterial == originalMaterial && fogSprite.sharedMaterial == shared && sprite.color == color && block.GetFloat("_GasTestSentinel") == 42f,
                        name + " 连续更新不克隆材质、不改精灵色、不覆盖其他渲染属性");
                    Write(visual,"density",0f); Tick(visual,0f);
                    Texture2D empty = Render(camera,512,512);
                    Check(Area(empty) == 0, name + " 雾面浓度0完全透明"); Object.DestroyImmediate(empty);
                    Write(visual,"density",1f);

                    Check(host.TryCommitEnvironmentEdit(host.CopyEnvironmentSlots(false), host.CopyEnvironmentSlots(true),false,out string error),
                        name + " 沿原环境确认入口转实心：" + error);
                    Tick(visual,0f);
                    Texture2D solid = Render(camera,512,512);
                    sprite.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/MyObjAssets/gameObject/Art/Lighting/AtmosphereSprite.mat");
                    Texture2D original = Render(camera,512,512);
                    Check(Difference(solid,original) < 10 && Area(solid) > 90000, name + " 实心GPU输出与原方块材质一致");
                    Object.DestroyImmediate(solid); Object.DestroyImmediate(original); sprite.sharedMaterial = originalMaterial;
                    host.RestoreEnvironmentDefaults(); Tick(visual,0f);
                    fogSprite.GetPropertyBlock(block);
                    Check(box.isTrigger && block.GetFloat("_GasEnabled") == 1f && box.size == new Vector2(3,3),
                        name + " 原重刷新恢复气体形态和雾面，尺寸不变");
                    visual.enabled = false; sprite.GetPropertyBlock(block);
                    Check(!fogSprite.enabled && !sprite.forceRenderingOff && block.GetFloat("_GasTestSentinel") == 42f,
                        name + " 停用只撤销雾面，保留其他属性");
                    visual.enabled = true; Tick(visual,0f); fogSprite.GetPropertyBlock(block);
                    Check(fogSprite.enabled && sprite.forceRenderingOff && block.GetFloat("_GasEnabled") == 1f, name + " 再启用正常恢复雾面");
                    instance.transform.localScale = new Vector3(1.8f,.65f,1f); Tick(visual,0f);
                    Vector2 spriteSize = sprite.size, fogSize = fogSprite.size;
                    for(int i=0;i<100;i++) Tick(visual,.016f);
                    Check(visual.ValidateConfiguration(out _) && box.size == new Vector2(3,3)
                        && sprite.size == spriteSize && fogSprite.size == fogSize, name + " 非等比缩放不回写根尺寸，外扩不逐帧累积");
                }
                finally { Object.DestroyImmediate(instance); }
            }
            Check(shared.shader.isSupported && !ShaderUtil.ShaderHasError(shared.shader), "实际渲染后Shader支持且无编译错误");
            CheckLighting(scene,camera);
            Preview(scene,camera);
            File.WriteAllLines(Output + "checks.txt",passed);
            Debug.Log("P28 gas visual: " + passed.Count + " checks passed.");
        }
        finally
        {
            Shader.SetGlobalFloat("_SceneLightingEnabled",lighting);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void CheckLighting(Scene scene, Camera camera)
    {
        float previousTransition = Shader.GetGlobalFloat("_SceneLightingTransition");
        float previousLocal = Shader.GetGlobalFloat("_SceneLightingLocal");
        Vector4 previousAmbient = Shader.GetGlobalVector("_SceneLightingAmbient");
        GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "气体团块.prefab"));
        SceneManager.MoveGameObjectToScene(instance,scene); instance.transform.position = Vector3.zero;
        var visual = instance.GetComponent<GasVolumeVisual>();
        GameObject lamp = new GameObject("Native light sample",typeof(Light));
        SceneManager.MoveGameObjectToScene(lamp,scene);
        lamp.transform.position = new Vector3(0,0,-2);
        Light light = lamp.GetComponent<Light>(); light.type = LightType.Point;
        light.range = 6f; light.intensity = 2f; light.color = new Color(1f,.68f,.38f);
        light.renderMode = LightRenderMode.ForcePixel; light.enabled = false;
        Tick(visual,0f);
        Texture2D plain = Render(camera,512,512), dark = null, lit = null, transition = null;
        try
        {
            Shader.SetGlobalFloat("_SceneLightingEnabled",1f);
            Shader.SetGlobalFloat("_SceneLightingTransition",0f);
            Shader.SetGlobalFloat("_SceneLightingLocal",1f);
            Shader.SetGlobalVector("_SceneLightingAmbient",new Vector4(.22f,.28f,.4f,1f));
            dark = Render(camera,512,512);
            Check(Difference(plain,dark)>20000 && Area(plain)==Area(dark), "原环境照度影响雾色并保持透明轮廓");
            light.enabled = true; lit = Render(camera,512,512);
            Check(Difference(dark,lit)>10000, "原生点光实际照亮雾团，无新增光照系统");
            Shader.SetGlobalFloat("_SceneLightingTransition",1f); transition = Render(camera,512,512);
            Check(Difference(plain,transition)<10, "原转场权重渐退受光，雾形与原色保留");
        }
        finally
        {
            Shader.SetGlobalFloat("_SceneLightingEnabled",0f);
            Shader.SetGlobalFloat("_SceneLightingTransition",previousTransition);
            Shader.SetGlobalFloat("_SceneLightingLocal",previousLocal);
            Shader.SetGlobalVector("_SceneLightingAmbient",previousAmbient);
            foreach(Texture2D texture in new[] {plain,dark,lit,transition}) if(texture!=null) Object.DestroyImmediate(texture);
            Object.DestroyImmediate(lamp); Object.DestroyImmediate(instance);
        }
    }

    static void Preview(Scene scene, Camera camera)
    {
        Directory.CreateDirectory(Output + "frames");
        var backdrop = new GameObject("Contrast backdrop",typeof(SpriteRenderer));
        SceneManager.MoveGameObjectToScene(backdrop,scene);
        var texture = new Texture2D(8,8);
        for (int y=0;y<8;y++) for(int x=0;x<8;x++)
            texture.SetPixel(x,y,(x+y)%2==0 ? new Color(.13f,.14f,.19f) : new Color(.20f,.21f,.26f));
        texture.filterMode = FilterMode.Point; texture.Apply();
        var backdropSprite = Sprite.Create(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f),1f);
        var background = backdrop.GetComponent<SpriteRenderer>(); background.sprite = backdropSprite;
        background.sortingOrder = -100; backdrop.transform.localScale = new Vector3(1.5f,1f,1f);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "气体团块.prefab");
        GameObject before = Object.Instantiate(prefab), after = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(before,scene); SceneManager.MoveGameObjectToScene(after,scene);
        before.transform.position = new Vector3(-2f,0,0); after.transform.position = new Vector3(2f,0,0);
        before.GetComponent<GasVolumeVisual>().enabled = false;
        var fog = after.GetComponent<GasVolumeVisual>();
        camera.orthographicSize = 2.4f;
        camera.backgroundColor = new Color(.13f,.14f,.19f,1f);
        for (int i=0;i<48;i++)
        {
            Tick(fog,i==0 ? 0f : .16f);
            Texture2D frame = Render(camera,960,540);
            File.WriteAllBytes(Output + "frames/" + i.ToString("D3") + ".png",frame.EncodeToPNG());
            if (i==0) File.WriteAllBytes(Output + "气体-原方块与动态迷雾.png",frame.EncodeToPNG());
            Object.DestroyImmediate(frame);
        }
        Object.DestroyImmediate(before); Object.DestroyImmediate(after);
        Object.DestroyImmediate(backdrop); Object.DestroyImmediate(backdropSprite); Object.DestroyImmediate(texture);
    }

    static void Tick(GasVolumeVisual visual,float delta) => typeof(GasVolumeVisual).GetMethod("RefreshVisual",Hidden).Invoke(visual,new object[] {delta});
    static T Read<T>(GasVolumeVisual visual,string field) => (T)typeof(GasVolumeVisual).GetField(field,Hidden).GetValue(visual);
    static void Write(GasVolumeVisual visual,string field,object value) => typeof(GasVolumeVisual).GetField(field,Hidden).SetValue(visual,value);
    static Texture2D Render(Camera camera,int width,int height)
    {
        RenderTexture target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        var texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
        texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
        camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
        return texture;
    }
    static int Area(Texture2D texture) { int n=0; foreach(Color32 p in texture.GetPixels32()) if(p.a>0) n++; return n; }
    static int Difference(Texture2D a,Texture2D b)
    {
        Color32[] x=a.GetPixels32(),y=b.GetPixels32(); int n=0;
        for(int i=0;i<x.Length;i++) if(Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b)+Math.Abs(x[i].a-y[i].a)>5) n++;
        return n;
    }
    static int SilhouetteDifference(Texture2D a,Texture2D b)
    {
        Color32[] x=a.GetPixels32(),y=b.GetPixels32(); int n=0;
        for(int i=0;i<x.Length;i++) if((x[i].a>5)!=(y[i].a>5)) n++;
        return n;
    }
    static void Check(bool condition,string message)
    {
        if(!condition) throw new Exception("P28 FAIL: " + message);
        passed.Add("PASS " + message);
    }
    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void RunRegressionForBatch()
    {
        try
        {
            Run();
            EditorSceneManager.OpenScene("Assets/MyObjAssets/Scenes/BeginScenes.unity",OpenSceneMode.Single);
            Directory.CreateDirectory(".utmp/P14Validation");
            EnvironmentBackpackChecks.Run();
            EditorApplication.Exit(0);
        }
        catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
