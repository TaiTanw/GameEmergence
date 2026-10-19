using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Small narrative slice. Content comes from the reviewed Strata manuscript.</summary>
[DefaultExecutionOrder(10000)]
public sealed partial class NarrativeFlow : MonoBehaviour
{
    [Serializable] public sealed class Node { public string id; public string[] pages; }
    [Serializable] public sealed class Catalog { public Node[] nodes; }
    static NarrativeFlow instance;
    public static NarrativeFlow Existing => instance;
    static int closedFrame = -1;
    public static bool BlocksMenu => instance != null && (instance.IsOpen || instance.endingFinished || Time.frameCount == closedFrame);
    public static NarrativeFlow Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("NarrativeFlow");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<NarrativeFlow>();
            }
            return instance;
        }
    }
    public bool IsOpen => active != null;
    public string ActiveId => active?.id;
    public int PageIndex => page;
    Catalog catalog;
    Node active;
    int page, openedFrame;
    GameObject shade, illustration, prompt;
    Text content, counter, nextLabel;
    Transform book;
    NarrativeAssets assets;
    string scene;
    bool endingFinished;
    Image roomImage;
    Sprite openingSprite, endingSprite;
    Camera overviewCamera;
    Vector3 overviewOriginalPosition, overviewPosition;
    float overviewOriginalSize, overviewSize;
    Rect overviewOriginalRect;
    public bool HasOverview => overviewCamera != null;
    GameObject skipButton, keyHint;

    public static string ClearNode(string sceneName)
    {
        switch(sceneName)
        {
            case "L00_Orientation": return "T05";
            case "L01_PropertyLabyrinth": return "A02";
            case "L02_SequentialGates": return "B04";
            case "L03_ElectricTraverse": return "C02";
            case "L04_FoldedWells": return "D03";
            case "L05_DepartureWorks": return "F01";
            default: return null;
        }
    }

    void Awake()
    {
        var json = Resources.Load<TextAsset>("NarrativeSlice");
        catalog = json != null ? JsonUtility.FromJson<Catalog>(json.text) : new Catalog { nodes = new Node[0] };
        assets = Resources.Load<NarrativeAssets>("NarrativeAssets");
        BuildUI();
        SceneManager.activeSceneChanged += SceneChanged;
    }

    void OnDestroy()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        if (instance == this) { Cancel(); instance = null; }
        if (openingSprite != null) Destroy(openingSprite);
        if (endingSprite != null) Destroy(endingSprite);
    }

    void SceneChanged(Scene previous, Scene next)
    {
        Cancel();
        if (book != null) Destroy(book.gameObject);
        scene = next.name;
        prompt.SetActive(false);
    }

    public IEnumerator EnterScene()
    {
        scene = SceneManager.GetActiveScene().name;
        ResetObservations();
        if (scene == "L00_Orientation")
        {
            PlaceBook();
            yield return Play("T00");
        }
        else if (scene == "L01_PropertyLabyrinth") yield return Play("A00");
        else if (scene == "L02_SequentialGates") yield return Play("B00");
        else if (scene == "L03_ElectricTraverse") yield return Play("C00");
        else if (scene == "L04_FoldedWells") yield return Play("D00");
        else if (scene == "L05_DepartureWorks") yield return Play("F00");
    }

    public IEnumerator Play(string id, bool repeat = false)
    {
        if (IsOpen || (!repeat && DataAndInitMgr.Instance.HasSeenNarrative(id))) yield break;
        Node node = Array.Find(catalog.nodes, n => n.id == id);
        if (node == null || node.pages == null || node.pages.Length == 0) yield break;
        if (!SceneChangeMgr.Instance.BeginStoryPause()) yield break;
        active = node;
        endingFinished = false;
        page = 0;
        openedFrame = Time.frameCount;
        shade.SetActive(true);
        bool ending = id == "E00" || id == "E01";
        illustration.SetActive((id == "T00" || ending) && assets != null && assets.room != null);
        roomImage.sprite = ending ? endingSprite : openingSprite;
        roomImage.color = id == "E01" ? new Color(.08f,.08f,.1f) : Color.white;
        shade.GetComponent<Image>().color = new Color(.025f,.035f,.07f, ending ? 1f : .8f);
        skipButton.SetActive(true); keyHint.SetActive(true);
        // Keep the current authored camera and parallax framing in the expanded levels.
        prompt.SetActive(false);
        RenderPage();
        while (active == node) yield return null;
    }

    public void Next()
    {
        if (endingFinished) { SceneChangeMgr.Instance.ReturnToMenu(); return; }
        if (!IsOpen) return;
        if (++page >= active.pages.Length) { Complete(); return; }
        RenderPage();
    }

    public void Complete()
    {
        if (!IsOpen) return;
        // A failed write leaves the node unread on disk; it never blocks gameplay.
        DataAndInitMgr.Instance.MarkNarrativeSeen(active.id);
        Cancel();
    }

    public void Cancel()
    {
        bool hadStory = IsOpen;
        active = null;
        endingFinished = false;
        ReleaseOverview();
        if (hadStory) closedFrame = Time.frameCount;
        if (shade != null) shade.SetActive(false);
        if (hadStory) SceneChangeMgr.Instance.EndStoryPause();
    }

    void RenderPage()
    {
        string text = active.pages[page];
        if (text.StartsWith("旁白：", StringComparison.Ordinal))
            content.text = text.Substring(3);
        else if (text.StartsWith("女孩：", StringComparison.Ordinal))
            content.text = "“" + text.Substring(3) + "”";
        else
            content.text = text;
        counter.text = (page + 1) + " / " + active.pages.Length;
        nextLabel.text = page + 1 == active.pages.Length ? "继续" : "下一页";
    }

    void PlaceBook()
    {
        if (book != null || SceneLevelSetup.Current == null || assets == null || assets.book == null) return;
        var go = new GameObject("NarrativeBook_T04");
        book = go.transform;
        // Decoration only: no collider, physics property, or level-clear interaction.
        var actor = InputControlMgr.Instance.Player;
        Vector3 origin = actor != null ? actor.transform.position : SceneLevelSetup.Current.BodyPose;
        book.position = origin + new Vector3(1.5f, -.25f, 0);
        var sprite = go.AddComponent<SpriteRenderer>();
        sprite.sprite = assets.book;
        sprite.sortingOrder = 35;
        book.localScale = Vector3.one * (.8f / Mathf.Max(.01f, sprite.sprite.bounds.size.y));
    }

    void Update()
    {
        if (endingFinished)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SceneChangeMgr.Instance.ReturnToMenu();
            return;
        }
        if (IsOpen)
        {
            // Avoid carrying the click/key which opened a page into its first advance.
            if (Time.frameCount > openedFrame + 1 && Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame) Complete();
                else if (Keyboard.current.spaceKey.wasPressedThisFrame) Next();
            }
            return;
        }
        var flow = SceneChangeMgr.Instance;
        var player = InputControlMgr.Instance.Player;
        bool near = scene == "L00_Orientation" && book != null && player != null
            && !flow.IsLoading && !flow.IsFailureRecovering && !flow.GameMenuOpen && !flow.IsLevelClearPending
            && InputControlMgr.Instance.InputEnabled && !player.PendingDeath.HasValue
            && Vector2.Distance(player.transform.position, book.position) < 2.5f;
        prompt.SetActive(near);
    }

    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        gameObject.AddComponent<GraphicRaycaster>();

        shade = Rect("StoryOverlay", transform, new Vector2(.5f,.5f), new Vector2(1280,720), Vector2.zero).gameObject;
        var backdrop = shade.AddComponent<Image>();
        backdrop.color = new Color(.025f,.035f,.07f,.8f);
        // Stretch overlay to every aspect ratio, while keeping the reading panel bounded.
        var rt = (RectTransform)shade.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

        var art = Rect("Room", shade.transform, new Vector2(.5f,1), new Vector2(1024,450), new Vector2(0,-225));
        illustration = art.gameObject;
        var room = roomImage = art.gameObject.AddComponent<Image>();
        if (assets != null && assets.room != null)
        {
            openingSprite = Sprite.Create(assets.room.texture, new Rect(0,0,1024,450), new Vector2(.5f,.5f),100);
            endingSprite = Sprite.Create(assets.room.texture, new Rect(45,145,350,210), new Vector2(.5f,.5f),100);
            room.sprite = openingSprite;
        }
        room.preserveAspect = true; room.raycastTarget = false;

        var panel = Rect("ReadingPanel", shade.transform, new Vector2(.5f,0), new Vector2(1120,290), new Vector2(0,165));
        panel.gameObject.AddComponent<Image>().color = new Color(.055f,.075f,.13f,.98f);
        content = Label("StoryText", panel, new Vector2(0,32), new Vector2(1000,170), 28);
        content.alignment = TextAnchor.UpperLeft;
        content.lineSpacing = 1.25f;
        counter = Label("PageCount", panel, new Vector2(-465,-105), new Vector2(90,36), 20);
        var next = Button("Next", panel, "下一页", new Vector2(410,-100), new Vector2(180,50), Next);
        nextLabel = next.GetComponentInChildren<Text>();
        skipButton = Button("Skip", panel, "跳过本段", new Vector2(200,-100), new Vector2(180,50), Complete).gameObject;
        var keys = Label("Keys", panel, new Vector2(-210,-105), new Vector2(390,36), 18);
        keys.text = "空格继续 · Esc 跳过本段"; keyHint = keys.gameObject;
        Button("ReturnMenu", shade.transform, "返回主菜单", new Vector2(480,310), new Vector2(190,44), () => SceneChangeMgr.Instance.ReturnToMenu());
        prompt = Button("ReadBook", transform, "阅读书页", new Vector2(475,280), new Vector2(190,52), () =>
        {
            if (prompt.activeSelf) StartCoroutine(Play("T04", true));
        }).gameObject;
        shade.SetActive(false); prompt.SetActive(false);
    }

    public void ShowEndingComplete()
    {
        endingFinished = true;
        shade.SetActive(true); illustration.SetActive(false); prompt.SetActive(false);
        shade.GetComponent<Image>().color = Color.black;
        content.text = ""; counter.text = ""; nextLabel.text = "返回主菜单";
        skipButton.SetActive(false); keyHint.SetActive(false);
    }

    void ShowOverview(string id)
    {
        ReleaseOverview();
        overviewCamera = InputControlMgr.Instance.GameCamera;
        if (overviewCamera == null) return;
        overviewOriginalPosition=overviewCamera.transform.position;
        overviewOriginalSize=overviewCamera.orthographicSize;
        overviewOriginalRect=overviewCamera.rect;
        float height = id == "B00" ? 48 : id == "C00" ? 24 : 120;
        overviewSize=height/2;
        overviewPosition=new Vector3(32,height/2,overviewOriginalPosition.z);
        shade.GetComponent<Image>().color=new Color(.025f,.035f,.07f,.25f);
        ApplyOverview();
    }

    void ApplyOverview()
    {
        if(overviewCamera == null) return;
        overviewCamera.orthographicSize=overviewSize;
        overviewCamera.transform.position=overviewPosition;
        overviewCamera.rect=new Rect(0,.43f,1,.57f);
    }

    void ReleaseOverview()
    {
        if(overviewCamera == null) return;
        overviewCamera.transform.position=overviewOriginalPosition;
        overviewCamera.orthographicSize=overviewOriginalSize;
        overviewCamera.rect=overviewOriginalRect;
        overviewCamera=null;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 pos)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = size; rt.anchoredPosition = pos; return rt;
    }
    static Text Label(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize)
    {
        var text = Rect(name,parent,new Vector2(.5f,.5f),size,pos).gameObject.AddComponent<Text>();
        text.font = GameUIAssets.Shared.font; text.fontSize = fontSize;
        text.color = new Color(.94f,.94f,.9f); text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false; text.raycastTarget = false;
        return text;
    }
    static Button Button(string name, Transform parent, string caption, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name,parent,new Vector2(.5f,.5f),size,pos);
        rect.gameObject.AddComponent<Image>().color = new Color(.19f,.25f,.36f);
        var button = rect.gameObject.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(action);
        Label("Caption",rect,Vector2.zero,size- new Vector2(10,4),23).text = caption;
        return button;
    }
}

public sealed partial class NarrativeFlow
{
    sealed class PlateObservation
    {
        public MechanismPressurePlate plate;
        public Vector3 rest;
        public bool sawBox, sawRaised, wasPressed, releasedFromBox;
    }
    PhysicalBox[] boxes = new PhysicalBox[0];
    FixedContactHazard[] electric = new FixedContactHazard[0];
    readonly List<PlateObservation> plates = new List<PlateObservation>();
    readonly HashSet<MapCell> inspectedCorridor = new HashSet<MapCell>();
    readonly Dictionary<MapCell,int> corridorCoverage = new Dictionary<MapCell,int>();
    readonly HashSet<string> attempted = new HashSet<string>();
    readonly List<MapCell> corridor = new List<MapCell>();
    PhysicalBox practiceBox, crossingBox;
    Vector3 practiceOrigin, lastPosition, pushedOrigin;
    Collider2D crossingHazard;
    Player observedPlayer;
    bool wasSwimming, wasGrounded, simJump, pushed, rodeOverHazard, adjusted;
    float jumpBase;

    public void ResetObservations()
    {
        boxes = FindObjectsOfType<PhysicalBox>();
        electric = FindObjectsOfType<FixedContactHazard>();
        plates.Clear(); corridor.Clear(); inspectedCorridor.Clear(); corridorCoverage.Clear(); attempted.Clear();
        foreach (var plate in FindObjectsOfType<MechanismPressurePlate>())
            if (plate.TargetComponent != null)
                plates.Add(new PlateObservation {plate=plate,rest=plate.TargetComponent.transform.position});
        practiceBox = null;
        foreach (var box in boxes)
            if(practiceBox == null || box.transform.position.x < practiceBox.transform.position.x) practiceBox=box;
        if(practiceBox != null) practiceOrigin=practiceBox.transform.position;
        if(SceneLevelSetup.Current != null)
            foreach(var cell in SceneLevelSetup.Current.MapCells)
                if(cell != null && cell.WorldRect.height <= 3.3f && cell.WorldRect.width >= 10f)
                    corridor.Add(cell);
        observedPlayer=null; crossingBox=null; crossingHazard=null;
        pushed=rodeOverHazard=adjusted=wasSwimming=wasGrounded=simJump=emerged=false;
    }

    bool Visible(Vector3 position)
    {
        var camera=InputControlMgr.Instance.GameCamera;
        if(camera == null) return false;
        Vector3 p=camera.WorldToViewportPoint(position);
        return p.z>0 && p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1;
    }

    bool TryObservation(string id)
    {
        if(!attempted.Add(id) || DataAndInitMgr.Instance.HasSeenNarrative(id)) return false;
        StartCoroutine(Play(id));
        return IsOpen;
    }

    bool GateVisible(MonoBehaviour gate)
    {
        var shape=gate.GetComponent<Collider2D>();
        var camera=InputControlMgr.Instance.GameCamera;
        if(shape==null || camera==null) return Visible(gate.transform.position);
        var a=camera.WorldToViewportPoint(shape.bounds.min);
        var b=camera.WorldToViewportPoint(shape.bounds.max);
        return a.z>0 && b.x>=0 && a.x<=1 && b.y>=0 && a.y<=1;
    }

    void LateUpdate()
    {
        if(IsOpen) { ApplyOverview(); return; }
        var flow=SceneChangeMgr.Instance;
        var player=InputControlMgr.Instance.Player;
        if(IsOpen || endingFinished || player==null || flow.IsLoading || flow.GameMenuOpen || flow.IsFailureRecovering
            || flow.IsEscapeHandoff || flow.IsLevelClearPending || !InputControlMgr.Instance.InputEnabled || Time.timeScale<=0) return;
        if(player.PendingDeath.HasValue) { observedPlayer=null; crossingBox=null; return; }
        Vector3 position=player.transform.position;
        bool grounded=player.NowPhyData.isGrounded;
        if(observedPlayer!=player || Vector3.Distance(lastPosition,position)>5f)
        {
            observedPlayer=player; lastPosition=position; wasGrounded=grounded;
            wasSwimming=player._ActionData.isSwimming; simJump=false; crossingBox=null;
        }
        bool safe=grounded && !player._ActionData.isSwimming;
        if(scene=="L00_Orientation")
        {
            if(safe && practiceBox!=null && Vector3.Distance(practiceBox.transform.position,practiceOrigin)<.1f
                && position.x<practiceBox.transform.position.x && Vector2.Distance(position,practiceBox.transform.position)<3f
                && Visible(practiceBox.transform.position) && TryObservation("T01")) return;
            if(wasSwimming && !player._ActionData.isSwimming) emerged=true;
            if(player._ActionData.isSwimming) emerged=false;
            if(emerged && safe && TryObservation("T03")) { emerged=false; return; }
        }
        if(scene=="L00_Orientation" || scene=="L02_SequentialGates")
        {
            foreach(var observed in plates)
            {
                var plate=observed.plate;
                if(plate==null || plate.TargetComponent==null) continue;
                Vector3 gate=plate.TargetComponent.transform.position;
                if(plate.IsPressed)
                {
                    if(!observed.wasPressed) observed.sawRaised=false;
                    observed.wasPressed=true; observed.sawBox=plate.HasBoxOccupant;
                    if(Vector3.Distance(gate,observed.rest)>1.8f && GateVisible(plate.TargetComponent))
                    {
                        observed.sawRaised=true;
                        if(scene=="L00_Orientation" && safe && TryObservation("T02")) return;
                    }
                }
                else
                {
                    if(observed.wasPressed) observed.releasedFromBox=observed.sawBox;
                    observed.wasPressed=false;
                    if(observed.releasedFromBox && observed.sawRaised && Vector3.Distance(gate,observed.rest)<.05f
                        && GateVisible(plate.TargetComponent) && safe && scene=="L02_SequentialGates" && TryObservation("B01")) return;
                }
            }
        }
        if(scene=="L01_PropertyLabyrinth" && LevelStageMgr.Instance.IsSimulation)
        {
            if(wasGrounded && !grounded) jumpBase=lastPosition.y;
            if(!grounded && !player._ActionData.isSwimming && position.y>jumpBase+.25f && position.y>lastPosition.y) simJump=true;
            if(simJump && grounded && !wasGrounded && safe && TryObservation("A01")) { simJump=false; return; }
        }
        else simJump=false;
        if(scene=="L03_ElectricTraverse" && LevelStageMgr.Instance.Stage==PlayerGameplayStage.Escape)
            ObserveCrossing(player,position,safe);
        if(scene=="L04_FoldedWells" && LevelStageMgr.Instance.HasExploration && corridor.Count>0)
        {
            foreach(var cell in corridor)
            {
                var r=cell.WorldRect;
                corridorCoverage.TryGetValue(cell,out int coverage);
                // Remember the floor and ceiling along each section while the player pans.
                // A section can be wider than the viewport and need not fit in one frame.
                for(int x=0;x<5;x++) for(int y=0;y<2;y++)
                    if(Visible(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x/4f),y==0?r.yMin:r.yMax,0)))
                        coverage |= 1 << (x*2+y);
                corridorCoverage[cell]=coverage;
                if(coverage==1023) inspectedCorridor.Add(cell);
            }
            if(inspectedCorridor.Count==corridor.Count && TryObservation("D01")) return;
        }
        lastPosition=position; wasGrounded=grounded; wasSwimming=player._ActionData.isSwimming;
    }
    bool emerged;

    void ObserveCrossing(Player player,Vector3 position,bool safe)
    {
        PhysicalBox candidate=player._ActionData.PushPullBox;
        Bounds playerBounds=player.NowPhyData.RegionBodyBounds;
        float move=InputControlMgr.Instance.PlayerInputData.moveInput;
        if(candidate==null && safe && Mathf.Abs(move)>.1f)
            foreach(var box in boxes)
            {
                if(box==null) continue;
                var bounds=box.BodyBounds;
                float sideGap=move>0 ? bounds.min.x-playerBounds.max.x : playerBounds.min.x-bounds.max.x;
                if(sideGap>=-.05f && sideGap<.2f && playerBounds.min.y<bounds.max.y-.1f && playerBounds.max.y>bounds.min.y+.1f)
                { candidate=box; break; }
            }
        if(candidate!=null)
        {
            if(crossingBox!=candidate)
            {
                crossingBox=candidate; pushedOrigin=crossingBox.transform.position;
                pushed=rodeOverHazard=adjusted=false; crossingHazard=null;
            }
        }
        if(crossingBox!=null && Mathf.Abs(crossingBox.transform.position.x-pushedOrigin.x)>.5f) pushed=true;
        if(crossingBox==null || !pushed) return;
        Bounds boxBounds=crossingBox.BodyBounds;
        Bounds bodyBounds=player.NowPhyData.RegionBodyBounds;
        bool onBox=player.NowPhyData.isGrounded && Mathf.Abs(bodyBounds.min.y-boxBounds.max.y)<.2f
            && bodyBounds.max.x>boxBounds.min.x && bodyBounds.min.x<boxBounds.max.x;
        if(onBox)
        {
            foreach(var hazard in electric)
            {
                if(hazard==null) continue;
                var shape=hazard.GetComponent<Collider2D>();
                if(shape==null) continue;
                var bounds=shape.bounds;
                if(boxBounds.center.x>bounds.min.x && boxBounds.center.x<bounds.max.x
                    && Mathf.Abs(boxBounds.min.y-bounds.max.y)<.3f)
                {
                    crossingHazard=shape; rodeOverHazard=true;
                    if(Mathf.Abs(InputControlMgr.Instance.PlayerInputData.moveInput)>.1f) adjusted=true;
                }
            }
        }
        if(safe && !onBox && rodeOverHazard && adjusted && crossingHazard!=null)
        {
            var bounds=crossingHazard.bounds;
            bool across=pushedOrigin.x<bounds.min.x ? bodyBounds.min.x>bounds.max.x :
                pushedOrigin.x>bounds.max.x && bodyBounds.max.x<bounds.min.x;
            if(across) TryObservation("C01");
        }
    }
}
