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

public static class NarrativeIntegrationChecks
{
    static readonly List<string> results = new List<string>();
    static IEnumerator run;
    static string output, oldDirectory, error;
    static bool done, oldOptionsEnabled;
    static EnterPlayModeOptions oldOptions;
    static double deadline;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunForBatch()
    {
        if (!Application.isBatchMode) throw new Exception("Use a separate batch editor");
        NarrativeSliceAssets.Prepare();
        output = Path.GetFullPath(".utmp/NarrativeIntegration");
        Directory.CreateDirectory(output);
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
        flow.EnterSavedLevel();
        while (NarrativeFlow.Existing == null || NarrativeFlow.Existing.ActiveId != "T00") yield return null;
        var story = NarrativeFlow.Instance;
        Check(Time.timeScale == 0 && BasicEntity.IsWorldPaused && !InputControlMgr.Instance.InputEnabled,"opening pauses time, world and input");
        Check(!flow.ToggleGameMenu(),"menu cannot overlap dialogue");
        Check(!DataAndInitMgr.Instance.HasSeenNarrative("T00"),"unread opening is not prematurely saved");
        Vector3 position = InputControlMgr.Instance.Player.transform.position;
        float until = Time.realtimeSinceStartup + .3f;
        while(Time.realtimeSinceStartup < until) yield return null;
        Check(Vector3.Distance(position,InputControlMgr.Instance.Player.transform.position)<.001f,"paused actor does not move across frames");
        foreach (var t in story.GetComponentsInChildren<Text>())
            if(t.name == "StoryText") Check(t.preferredHeight <= t.rectTransform.rect.height,"opening text fits its panel");
        Capture("01-opening.png");
        story.Next();
        Check(story.PageIndex == 1 && story.IsOpen,"next page does not finish early");
        flow.ReturnToMenu();
        while(flow.IsLoading) yield return null;
        Check(!story.IsOpen && !flow.StoryActive && !DataAndInitMgr.Instance.HasSeenNarrative("T00"),"return during reading cancels without marking seen");
        flow.EnterSavedLevel();
        while(story.ActiveId != "T00") yield return null;
        story.Complete();
        Check(DataAndInitMgr.Instance.HasSeenNarrative("T00"),"skip marks complete in player save");
        Check(Time.timeScale == 1 && !BasicEntity.IsWorldPaused && InputControlMgr.Instance.InputEnabled,"tutorial state restored");
        yield return null;
        // Open the actual UI button; the book marker has no world sprite.

        var button = Array.Find(story.GetComponentsInChildren<Button>(true), b=>b.name=="ReadBook");
        Check(button != null && button.gameObject.activeSelf,"book is readable at safe tutorial spawn");
        button.onClick.Invoke();
        Check(story.ActiveId == "T04","book button opens optional text");
        Capture("02-book.png");
        story.Next();
        Check(!story.IsOpen && DataAndInitMgr.Instance.HasSeenNarrative("T04"),"book completion persists");
        yield return null;
        button.onClick.Invoke();
        Check(story.ActiveId=="T04","optional book can be reread");
        story.Complete();
        yield return null;
        flow.ReturnToMenu();
        while(flow.IsLoading) yield return null;
        flow.EnterSavedLevel();
        while(flow.IsLoading) yield return null;
        yield return null; yield return null;
        Check(!story.IsOpen && InputControlMgr.Instance.InputEnabled,"continue does not replay read opening");
        var body = (Player)typeof(SceneChangeMgr).GetField("body",Hidden).GetValue(flow);
        int setupId = SceneLevelSetup.Current.GetInstanceID();
        Check(body.TryRequestDeath(PlayerDeathCause.Abyss),"tutorial death uses existing lifecycle");
        while(SceneLevelSetup.Current == null || SceneLevelSetup.Current.GetInstanceID() == setupId || flow.IsLoading) yield return null;
        yield return null;
        while(story.ActiveId != "R00") yield return null;
        story.Complete();
        Check(!story.IsOpen && InputControlMgr.Instance.InputEnabled,"death retry restores control without repeating opening");
        // Direct lifecycle request tests the transition, not the obstacle course traversal.
        Check(flow.TryClearLevel(body),"real tutorial clear playback accepted");
        while(story.ActiveId != "T05") yield return null;
        Check(flow.IsLevelClearPending && !flow.IsLoading,"post-clear text precedes next scene load");
        story.Complete();
        while(story.ActiveId != "A00") yield return null;
        Check(SceneManager.GetActiveScene().name=="L01_PropertyLabyrinth","tutorial advances to actual first level");
        Check(DataAndInitMgr.Instance.HasSeenNarrative("T05"),"cross-level save retains story flags");
        Capture("03-first-level.png");
        story.Complete();
        Check(Time.timeScale == 1 && BasicEntity.IsWorldPaused && InputControlMgr.Instance.InputEnabled,"roaming restores paused world with active spirit input");
        yield return null;

        Check(flow.SetSimulation(true),"simulation can start after story");

        Check(!BasicEntity.IsWorldPaused,"simulation world runs");
        var rules = (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules",Hidden).GetValue(flow);
        float cognition = rules.Cognition.Remaining;
        story.StartCoroutine(story.Play("T04",true));
        until=Time.realtimeSinceStartup+.3f;
        while(Time.realtimeSinceStartup<until) yield return null;
        Check(rules.Cognition.Remaining==cognition,"narrative pause stops simulation cognition spending");
        story.Complete();
        Check(Time.timeScale==1 && !BasicEntity.IsWorldPaused,"simulation pause restores original world state");
        flow.ReturnToMenu();
        while(flow.IsLoading) yield return null;
        Check(flow.TryClearGameProgress(out _),"clear progress succeeds in isolated test save");
        Check(!DataAndInitMgr.Instance.HasSeenNarrative("T00") && !DataAndInitMgr.Instance.HasSeenNarrative("A00"),"clear progress resets narrative flags");
        flow.EnterSavedLevel();
        while(story.ActiveId!="T00") yield return null;
        story.Complete(); yield return null;
        body = (Player)typeof(SceneChangeMgr).GetField("body",Hidden).GetValue(flow);
        Check(flow.TryClearLevel(body),"tutorial can clear without reading optional book");
        while(story.ActiveId!="T05") yield return null;
        story.Complete();
        while(story.ActiveId!="A00") yield return null;
        Check(!DataAndInitMgr.Instance.HasSeenNarrative("T04"),"unread branch reaches first-level narrative without fabricated book knowledge");
        story.Complete();
        string[] entry = { "A00", "B00", "C00", "D00", "F00" };
        string[] clear = { "A02", "B04", "C02", "D03", "F01" };
        for(int level=1;level<=5;level++)
        {
            yield return null;
            Check(flow.StartEscape(),"start escape in level "+level);
            if(level==2)
            {
                while(story.ActiveId!="B02") yield return null;
                Check(!flow.NextEscapeStep(),"preparation cannot bypass active story");
                story.Complete();
                Check(!DataAndInitMgr.Instance.HasSeenNarrative("B03"),"manual branch excludes exhaustion text");
            }
            Check(flow.NextEscapeStep() && flow.BeginEscapeFromAbilities(),"abilities hand off in level "+level);
            while(flow.IsEscapeHandoff) yield return null;
            if(level==4)
            {
                while(story.ActiveId!="D02") yield return null;
                story.Complete();
            }
            Check(LevelStageMgr.Instance.Stage==PlayerGameplayStage.Escape,"real escape stage in level "+level);
            var water=SceneLevelSetup.Current.RisingWater;
            body=(Player)typeof(SceneChangeMgr).GetField("body",Hidden).GetValue(flow);
            if(level==1)
            {
                Check(water.ConfigureWaterLevel(-20,body.transform.position.y+5,.45f,true),"create submerged pause fixture");
                until=Time.realtimeSinceStartup+.2f;
                while(Time.realtimeSinceStartup<until) yield return null;
                Check(body.ResourceFacts.submerged,"fixture actor is actually submerged");
            }
            float y=water.SurfaceY;
            float oxygen=body.ResourceFacts.oxygen;
            story.StartCoroutine(story.Play("T04",true));
            until=Time.realtimeSinceStartup+.25f;
            while(Time.realtimeSinceStartup<until) yield return null;
            Check(Mathf.Abs(water.SurfaceY-y)<.001f,"rising water frozen by dialogue in level "+level);
            if(level==1) Check(body.ResourceFacts.oxygen==oxygen,"submerged dialogue freezes oxygen drain");
            story.Complete();
            body=(Player)typeof(SceneChangeMgr).GetField("body",Hidden).GetValue(flow);
            Check(flow.TryClearLevel(body),"level clear accepted "+level);
            while(story.ActiveId!=clear[level-1]) yield return null;
            Check(!flow.IsLoading,"clear text precedes transition "+level);
            story.Complete();
            if(level<5) { while(story.ActiveId!=entry[level]) yield return null; story.Complete(); }
            else while(!flow.IsEnding) yield return null;
        }
        Check(flow.IsEnding && Object.FindObjectOfType<OrimeEnding>()!=null,"fifth level reaches current three-ending host");
        Check(!story.IsOpen,"legacy bed ending does not override current ending");
        flow.ReturnToMenu(); while(flow.IsLoading) yield return null;
        foreach(string ending in new[]{"E01_StillRaining","E02_OtherSide","E03_Unfolded"})
        {
            Check(DataAndInitMgr.Instance.SaveEndingProgress(5,ending,flow.PlayerEffects),"save ending replay "+ending);
            flow.EnterSavedLevel(); while(!flow.IsEnding || flow.IsLoading) yield return null;
            Check(SceneManager.GetActiveScene().name==ending && Object.FindObjectOfType<OrimeEnding>()!=null,"load actual ending "+ending);
            Check(!story.IsOpen,"ending retains native text "+ending);
            Check(flow.ToggleGameMenu() && Time.timeScale==0,"ending pause "+ending);
            Check(flow.ToggleGameMenu() && Time.timeScale==1,"ending resume "+ending);
            flow.ReturnToMenu(); while(flow.IsLoading) yield return null;
        }
        Check(flow.TryClearGameProgress(out _),"reset isolated save for exhausted branch");
        Check(DataAndInitMgr.Instance.SavePlayerProgress(2,flow.PlayerEffects),"prepare second-level branch fixture");
        flow.EnterSavedLevel();
        while(story.ActiveId!="B00") yield return null;
        story.Complete(); yield return null;
        Check(flow.SetSimulation(true),"enter simulation for exhaustion branch");
        rules=(PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules",Hidden).GetValue(flow);
        Check(rules.Cognition.TryPayAbilityCost(rules.Cognition.Remaining),"consume remaining cognition in test fixture");
        while(story.ActiveId!="B03") yield return null;
        story.Complete();
        Check(!DataAndInitMgr.Instance.HasSeenNarrative("B02"),"exhaustion branch excludes manual text");
        flow.ReturnToMenu();
        while(flow.IsLoading) yield return null;
    }

    static void Capture(string file) { /* Headless checks intentionally do not render or capture a desktop. */ }
    static void Check(bool ok,string name)
    { if(!ok) throw new Exception(name); results.Add("PASS "+name); File.WriteAllLines(Path.Combine(output,"progress.txt"),results); }
    static void Error(string text,string trace,LogType type)
    { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) error ??= text+"\n"+trace; }
}
