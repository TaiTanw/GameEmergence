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

    [MenuItem("自定义工具/测试工具/验证/P4.1 验证进度与永久能力存档（Edit Mode）")]
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
            CheckInvalidInput(directory);
            CheckWriteFailureAndClear(directory);
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
        ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 1.2f, PlayerEffectScope.Permanent);
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
        var ledger = new PlayerEffectLedger();
        int[] levels = { 1, 2, 3, 4, 5, 6 };
        var confirmed = new[] { new PlayerEffectRecord(PlayerEffectKind.MovementMultiplier, 1.5f, PlayerEffectScope.Permanent) };
        Check(ledger.RestoreConfirmed(levels, confirmed), "准备六类已确认等级及普通永久效果");
        Check(ledger.GrantEffect(PlayerEffectKind.JumpMultiplier, 1.4f, PlayerEffectScope.Permanent)
            && ledger.GrantEffect(PlayerEffectKind.MovementMultiplier, 0.5f), "准备未确认永久项及本局负面");
        Check(Save(directory, 3, ledger), "关序及已确认永久数据整体写入");
        string path = Path.Combine(directory, "PlayerProgress.json");
        string json = File.ReadAllText(path);
        Check(json.Contains("\"version\": 1") && json.Contains("\"levelIndex\": 3"), "存档包含版本与同份关序");
        Check(!File.Exists(path + ".tmp"), "成功写入不留下暂存文件");

        var restored = new PlayerEffectLedger();
        Check(Load(directory, restored, out int index) && index == 3, "新档优先于旧关序");
        Check(Same(levels, restored.CaptureConfirmedUpgradeLevels()), "六类已确认等级完整往返");
        PlayerEffectRecord[] effects = restored.CaptureConfirmedEffects();
        Check(effects.Length == 1 && effects[0].Kind == PlayerEffectKind.MovementMultiplier
            && effects[0].Scope == PlayerEffectScope.Permanent && effects[0].Multiplier == 1.5f,
            "只保存已确认普通永久效果，未确认永久项和本局负面不存盘");
        Check(Load(directory, restored, out int repeatIndex) && repeatIndex == 3
            && restored.CaptureConfirmedEffects().Length == 1 && Same(levels, restored.CaptureConfirmedUpgradeLevels()),
            "重复加载不重复叠加效果或等级");
        Check(Save(directory, 4, restored) && Load(directory, restored, out int nextIndex) && nextIndex == 4,
            "已有存档采用完整替换且仍可读取");
        DataAndInitMgr.Instance.SaveLevelIndex(4);
        Check(DataAndInitMgr.Instance.LoadLevelIndex() == 4 && Load(directory, restored, out _)
            && Same(levels, restored.CaptureConfirmedUpgradeLevels()) && restored.CaptureConfirmedEffects().Length == 1,
            "旧关序保存入口保留整份已确认等级与效果");
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
            goodJson.Replace("\"version\": 1", "\"version\": 99"),
            goodJson.Replace("\"levelIndex\": 4", "\"levelIndex\": -1"),
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[1,2],\"confirmedEffects\":[]}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[-1,0,0,0,0,0],\"confirmedEffects\":[]}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[{\"kind\":99,\"multiplier\":1.2}]}",
            "{\"version\":1,\"levelIndex\":4,\"confirmedUpgradeLevels\":[0,0,0,0,0,0],\"confirmedEffects\":[{\"kind\":0,\"multiplier\":3.4e38},{\"kind\":0,\"multiplier\":2}]}"
        };
        for (int i = 0; i < invalid.Length; i++)
        {
            File.WriteAllText(path, invalid[i]);
            // 此处的 Error 日志是预期诊断；坏的新档不能偷偷回退旧关序或覆盖原文件。
            Check(!Load(directory, ledger, out _) && File.ReadAllText(path) == invalid[i]
                && Same(oldLevels, ledger.CaptureConfirmedUpgradeLevels()) && ledger.CaptureConfirmedEffects().Length == 1,
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
        object[] arguments = { directory, ledger, 0 };
        bool result = (bool)Invoke("LoadPlayerProgressFromDirectory", arguments);
        index = (int)arguments[2];
        return result;
    }

    private static bool Save(string directory, int index, PlayerEffectLedger ledger)
    { return (bool)Invoke("SavePlayerProgressToDirectory", new object[] { directory, index, ledger }); }

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
