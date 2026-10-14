using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>P4.1 真实文件读写检查；仅在独立临时目录中操作，不触碰玩家存档。</summary>
public static class PlayerProgressChecks
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly List<string> passed = new List<string>();

    public static void RunForBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    [MenuItem("自定义工具/测试工具/验证/P4.1 验证进度与降费存档（Edit Mode）")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("请在 Edit Mode 执行验证。");
        passed.Clear();
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.GetFullPath(Path.Combine(temporaryRoot, "GameEmergenceP4Progress-" + Guid.NewGuid().ToString("N")));
        string previousDirectory = DataAndInitMgr.debugPlayerProgressDirectory;
        DataAndInitMgr.debugPlayerProgressDirectory = directory;
        Directory.CreateDirectory(directory);
        try
        {
            CheckMissingAndLegacy(directory);
            CheckConfirmedRoundTrip(directory);
            CheckEndingVector(directory);
            CheckEndingDestination(directory);
            CheckInvalidInput(directory);
            CheckWriteFailureAndClear(directory);
            EndingConfigurationChecks.Run();
            ScenePropInteractionChecks.Run();
            Debug.Log($"Player progress: {passed.Count} checks passed. Expected invalid-input diagnostics were exercised.");
        }
        finally
        {
            DataAndInitMgr.debugPlayerProgressDirectory = previousDirectory;
            // 临时目录由本次检查独占；删除前核对它仍是系统临时目录的直接子目录。
            if (Path.GetDirectoryName(directory) != temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                throw new InvalidOperationException("拒绝删除检查范围外的目录。");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(".utmp/PlayerProgressValidation");
            File.WriteAllLines(".utmp/PlayerProgressValidation/checks.txt", passed);
        }
    }

    private static void CheckMissingAndLegacy(string directory)
    {
        var ledger = new PlayerEffectLedger();
        Check(Load(directory, ledger, out int missingIndex) && missingIndex == 0, "无存档从首关及空永久数据开始");
        Check(Directory.GetFiles(directory).Length == 0, "无存档读取不创建文件");

        string legacyPath = Path.Combine(directory, "LevelProgress.txt");
        const string oldContent = " 2\r\n";
        File.WriteAllText(legacyPath, oldContent);
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f);
        ledger.CommitLevel();
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 0.8f);
        Check(Load(directory, ledger, out int index) && index == 2, "旧纯关序可读");
        Check(ledger.CaptureConfirmedEffects().Length == 0 && ledger.CurrentLevelEffects.Count == 0
            && AllZero(ledger.CaptureConfirmedUpgradeLevels()), "旧档读取恢复空永久记录并清本关新增");
        Check(File.ReadAllText(legacyPath) == oldContent && !File.Exists(Path.Combine(directory, "PlayerProgress.json")),
            "旧档加载只在内存迁移且保持原始文件");
    }

    private static void CheckConfirmedRoundTrip(string directory)
    {
        string path = Path.Combine(directory, "PlayerProgress.json");
        // 模拟真实旧 JSON；读取不应为迁移而重写原始文件。
        const string legacy = "{\"version\":1,\"levelIndex\":3,\"confirmedUpgradeLevels\":[1,2,3,4,5,6],\"confirmedEffects\":[{\"kind\":0,\"multiplier\":1.5}]}";
        File.WriteAllText(path, legacy);
        var ledger = new PlayerEffectLedger();
        Check(Load(directory, ledger, out int index) && index == 3 && File.ReadAllText(path) == legacy, "旧能力档只读迁移不改文件");
        int[] levels = { 0, 0, 0, 4, 5, 6 };
        Check(Same(levels, ledger.CaptureConfirmedUpgradeLevels()) && ledger.CaptureConfirmedEffects().Length == 0 && ledger.MovementMultiplier == 1f,
            "旧运动等级与永久倍率忽略，降费等级完整保留");
        ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.4f);
        ledger.TryGrantAutomaticAbilities(new PlayerAbilityEffect(new PlayerAbilityOffer(PlayerAbilityKind.AirJumps, true, 1f)), null);
        Check(Save(directory, 3, ledger), "已确认降费与关序整体写入");
        string json = File.ReadAllText(path);
        Check(json.Contains("\"version\": 3") && json.Contains("\"levelIndex\": 3") && !File.Exists(path + ".tmp"), "完整替换保存且无暂存残留");
        var restored = new PlayerEffectLedger();
        Check(Load(directory, restored, out index) && index == 3 && Same(levels, restored.CaptureConfirmedUpgradeLevels())
            && restored.CaptureConfirmedEffects().Length == 0 && !restored.AutomaticResultLocked, "临时普通项与自动结果均不存盘");
        Check(Load(directory, restored, out _) && Same(levels, restored.CaptureConfirmedUpgradeLevels()), "重复读档不叠加降费等级");
        Check(Save(directory, 4, restored) && Load(directory, restored, out index) && index == 4, "已有存档完整替换");
        DataAndInitMgr.Instance.SaveLevelIndex(4);
        Check(Load(directory, restored, out index) && index == 4 && Same(levels, restored.CaptureConfirmedUpgradeLevels()), "旧关序保存入口保留降费等级");
    }

    private static void CheckInvalidInput(string directory)
    {
        string path = Path.Combine(directory, "PlayerProgress.json");
        string goodJson = File.ReadAllText(path);
        var ledger = new PlayerEffectLedger();
        Check(Load(directory, ledger, out _), "保留有效记录作为拒绝坏档的基准");
        int[] oldLevels = ledger.CaptureConfirmedUpgradeLevels();
        string[] invalid =
        {
            "{ definitely invalid json",
            "{}",
            "{\"version\":1,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[]}",
            goodJson.Replace("\"version\": 3", "\"version\": 99"),
            goodJson.Replace("\"levelIndex\": 4", "\"levelIndex\": -1"),
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[1,2],\"confirmedEffects\":[]}",
            "{\"version\":2,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[]}",
            "{\"version\":2,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[],\"confirmedEndingVector\":{\"x\":1,\"y\":2}}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,-1,0,0],\"confirmedEffects\":[]}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,-1,0],\"confirmedEffects\":[]}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0,0],\"confirmedEffects\":[]}"
        };
        for (int i = 0; i < invalid.Length; i++)
        {
            File.WriteAllText(path, invalid[i]);
            // 此处的 Error 日志是预期诊断；坏的新档不能偷偷回退旧关序或覆盖原文件。
            Check(!Load(directory, ledger, out _) && File.ReadAllText(path) == invalid[i]
                && Same(oldLevels, ledger.CaptureConfirmedUpgradeLevels()) && ledger.CaptureConfirmedEffects().Length == 0,
                "无效进度 " + i + " 拒绝读取并保留已有记录与文件");
        }
        File.WriteAllText(path, goodJson);
    }

    private static void CheckWriteFailureAndClear(string directory)
    {
        string path = Path.Combine(directory, "PlayerProgress.json");
        string json = File.ReadAllText(path);
        var ledger = new PlayerEffectLedger();
        Check(Load(directory, ledger, out _), "写入失败检查恢复有效永久数据");
        // 独占暂存路径模拟无法写入，避免依赖平台文件锁语义。
        Directory.CreateDirectory(path + ".tmp");
        try { Check(!Save(directory, 9, ledger) && File.ReadAllText(path) == json, "暂存写入失败不破坏此前完整进度"); }
        finally { Directory.Delete(path + ".tmp"); }
        File.WriteAllText(path + ".tmp", "incomplete");
        Check((bool)Invoke("ClearPlayerProgressFiles", new object[] { directory }), "清除进度执行成功");
        Check(!File.Exists(path) && !File.Exists(Path.Combine(directory, "LevelProgress.txt")) && !File.Exists(path + ".tmp"),
            "清进度同时删除新永久存档、旧关序与暂存文件");
        Check(Load(directory, ledger, out int index) && index == 0 && AllZero(ledger.CaptureConfirmedUpgradeLevels())
            && ledger.CaptureConfirmedEffects().Length == 0, "清除后重新开始空永久进度");
    }

    private static bool Load(string directory, PlayerEffectLedger ledger, out int index)
    {
        object[] arguments = { directory, ledger, 0, Vector3.zero };
        bool result = (bool)Invoke("LoadPlayerProgressFromDirectory", arguments);
        index = (int)arguments[2];
        return result;
    }

    private static bool Save(string directory, int index, PlayerEffectLedger ledger)
    { return (bool)Invoke("SavePlayerProgressToDirectory", new object[] { directory, index, ledger, Vector3.zero }); }

    private static void CheckEndingVector(string directory)
    {
        DataAndInitMgr data = DataAndInitMgr.Instance;
        Vector3 previousConfirmed = data.ConfirmedEndingVector, previousPending = data.CurrentLevelEndingVector;
        var ledger = new PlayerEffectLedger();
        string path = Path.Combine(directory, "PlayerProgress.json");
        string originalJson = File.ReadAllText(path);
        try
        {
            Check(data.LoadPlayerProgress(ledger, out int index) && index == 4
                && data.ConfirmedEndingVector == Vector3.zero, "二维向量随进度整体恢复");
            const string versionOne = "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[]}";
            File.WriteAllText(path, versionOne);
            Check(data.LoadPlayerProgress(ledger, out _) && data.ConfirmedEndingVector == Vector3.zero
                && File.ReadAllText(path) == versionOne, "版本一旧档向量按零迁移，读取不改文件");
            var reserved = new Vector3(2, -3, 17);
            Check((bool)Invoke("SavePlayerProgressToDirectory", new object[] { directory, 4, ledger, reserved })
                && data.LoadPlayerProgress(ledger, out _) && data.ConfirmedEndingVector == reserved,
                "二维累计与预留 Z 在同一存档往返");
            string beforePickup = File.ReadAllText(path);
            Check(data.TryAddEndingContribution(new Vector2(3, 5), out _)
                && data.CurrentLevelEndingVector == new Vector3(3, 5, 0) && File.ReadAllText(path) == beforePickup,
                "领取只登记本关二维贡献，不写磁盘");
            Check(data.TryGetCandidateEndingVector(new Vector2(-1, 2), out Vector3 candidate, out _)
                && candidate == new Vector3(4, 4, 17) && data.CurrentLevelEndingVector == new Vector3(3, 5, 0),
                "最后贡献查询包含已确认和本关累计且无副作用");
            Check(!data.TryAddEndingContribution(new Vector2(float.NaN, 0), out _)
                && data.CurrentLevelEndingVector == new Vector3(3, 5, 0), "非法贡献拒绝且保持原累计");
            Directory.CreateDirectory(path + ".tmp");
            try
            {
                Check(!data.SavePlayerProgress(5, ledger) && data.ConfirmedEndingVector == reserved
                    && data.CurrentLevelEndingVector == new Vector3(3, 5, 0) && File.ReadAllText(path) == beforePickup,
                    "写盘失败不确认向量或改动此前文件");
            }
            finally { Directory.Delete(path + ".tmp"); }
            Check(data.SavePlayerProgress(5, ledger) && data.ConfirmedEndingVector == new Vector3(5, 2, 17)
                && data.CurrentLevelEndingVector == Vector3.zero, "安置后一次保存整体确认向量并清本关累计");
            Check(data.LoadPlayerProgress(ledger, out index) && index == 5
                && data.ConfirmedEndingVector == new Vector3(5, 2, 17), "重新读取不重复累计贡献");
            data.TryAddEndingContribution(new Vector2(9, 9), out _);
            data.DiscardCurrentLevelEndingVector();
            Check(data.CurrentLevelEndingVector == Vector3.zero && data.ConfirmedEndingVector == new Vector3(5, 2, 17),
                "丢弃本关贡献保留已确认向量");
            RestoreVector(data, new Vector3(float.MaxValue, 0, 0));
            Check(!data.TryAddEndingContribution(new Vector2(float.MaxValue, 0), out _), "累计溢出明确拒绝");
            data.ClearLevelProgress();
            Check(data.ConfirmedEndingVector == Vector3.zero && data.CurrentLevelEndingVector == Vector3.zero
                && !File.Exists(path), "清进度同步清向量磁盘和内存");
        }
        finally
        {
            typeof(DataAndInitMgr).GetProperty("ConfirmedEndingVector").SetValue(data, previousConfirmed);
            RestoreVector(data, previousPending);
            File.WriteAllText(path, originalJson);
        }
    }

    private static void RestoreVector(DataAndInitMgr data, Vector3 vector)
        => typeof(DataAndInitMgr).GetMethod("RestoreCurrentLevelEndingVector", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(data, new object[] { vector });

    private static void CheckEndingDestination(string directory)
    {
        var data = DataAndInitMgr.Instance;
        var ledger = new PlayerEffectLedger();
        string path = Path.Combine(directory, "PlayerProgress.json"), original = File.ReadAllText(path);
        Vector3 confirmed = data.ConfirmedEndingVector, pending = data.CurrentLevelEndingVector;
        string oldEnding = data.SavedEndingScene;
        try
        {
            Check(data.LoadPlayerProgress(ledger, out _) && data.SavedEndingScene == null, "旧普通目的地读取没有结局标记");
            string v2 = original.Replace("\"version\": 3", "\"version\": 2");
            File.WriteAllText(path, v2);
            Check(data.LoadPlayerProgress(ledger, out _) && data.SavedEndingScene == null
                && File.ReadAllText(path) == v2, "版本二只在内存迁移为普通目的地，保留向量与原文件");
            File.WriteAllText(path, original); data.LoadPlayerProgress(ledger, out _);
            data.TryAddEndingContribution(new Vector2(2, -3), out _);
            Directory.CreateDirectory(path + ".tmp");
            try { Check(!data.SaveEndingProgress(4, "EndingExample", ledger) && data.SavedEndingScene == null
                && data.CurrentLevelEndingVector == new Vector3(2, -3, 0) && File.ReadAllText(path) == original,
                "结局写盘失败不修改原目的地或确认贡献"); }
            finally { Directory.Delete(path + ".tmp"); }
            Check(data.SaveEndingProgress(4, "EndingExample", ledger) && data.SavedEndingScene == "EndingExample"
                && data.ConfirmedEndingVector == new Vector3(2, -3, 0) && data.CurrentLevelEndingVector == Vector3.zero,
                "同一份存档确认最后贡献并保存结局场景");
            string endingJson = File.ReadAllText(path);
            Check(data.LoadPlayerProgress(ledger, out int index) && index == 4 && data.SavedEndingScene == "EndingExample"
                && File.ReadAllText(path) == endingJson && data.ConfirmedEndingVector == new Vector3(2, -3, 0),
                "重读保存结局不写盘、不重复累计");
            string[] invalid = {
                endingJson.Replace("\"destinationKind\": 1", "\"destinationKind\": 9"),
                endingJson.Replace("\"destinationKind\": 1,", ""),
                endingJson.Replace("EndingExample", " "),
                endingJson.Replace("\"destinationKind\": 1", "\"destinationKind\": 0")
            };
            foreach (string bad in invalid)
            {
                File.WriteAllText(path, bad);
                Check(!data.LoadPlayerProgress(ledger, out _) && data.SavedEndingScene == "EndingExample"
                    && data.ConfirmedEndingVector == new Vector3(2, -3, 0) && File.ReadAllText(path) == bad,
                    "无效结局目的地保留已有状态和原文件");
            }
            File.WriteAllText(path, endingJson);
            Check(data.TryClearPlayerProgress() && data.SavedEndingScene == null && !File.Exists(path),
                "清进度同时清除保存的结局目的地");
        }
        finally
        {
            typeof(DataAndInitMgr).GetProperty("ConfirmedEndingVector").SetValue(data, confirmed);
            typeof(DataAndInitMgr).GetProperty("SavedEndingScene").SetValue(data, oldEnding);
            RestoreVector(data, pending); File.WriteAllText(path, original);
        }
    }

    private static object Invoke(string method, object[] arguments)
    { return typeof(DataAndInitMgr).GetMethod(method, HiddenStatic).Invoke(null, arguments); }

    private static bool Same(int[] left, int[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static bool AllZero(int[] values)
    {
        foreach (int value in values) if (value != 0) return false;
        return true;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        passed.Add("PASS " + label);
    }
}
