#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>已落地单元的定向与相关物理回归入口；不保存场景或修改关卡配置。</summary>
public static class P8P9UnitChecks
{
    public static void RunForBatch()
    {
        try
        {
            P8CognitionChecks.Run();
            P8MapChecks.Run();
            TrialRefreshChecks.Run();
            ScenePropInteractionChecks.Run();
            EnvironmentParameterChecks.Run();
            PlayerEffectChecks.Run();
            SpiritFlightChecks.Run();
            EnvironmentIntegrationChecks.Run();
            BoxInteractionChecks.Run();
            PlayerDeathCycleChecks.Run();
            Debug.Log("P8/P9/K7 unit and regression checks passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
