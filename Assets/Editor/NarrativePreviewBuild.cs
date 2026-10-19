using UnityEditor;

public static class NarrativePreviewBuild
{
    public static void Run()
    {
        NarrativeSliceAssets.Prepare();
        NarrativeSliceAssets.AuditNarrativeAnchors();
        string product = PlayerSettings.productName;
        try
        {
            // This review build gets its own save directory and PlayerPrefs.
            PlayerSettings.productName = "GameEmergence Narrative Preview";
            LocalWindowsBuild.Run();
        }
        finally { PlayerSettings.productName = product; AssetDatabase.SaveAssets(); }
    }
}
