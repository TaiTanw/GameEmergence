using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
/// <summary>
/// 数据与初始化管理器
/// </summary>
public class DataAndInitMgr 
{
    private static DataAndInitMgr instance = new DataAndInitMgr();
    public static DataAndInitMgr Instance => instance;

    /// <summary>
    /// 必备资源同步动态加载路径，框架依赖
    /// </summary>
    public string resourcesNecessaryAssetsPath = "NecessaryAssets/";
    /// <summary>
    /// 同步动态加载默认路径
    /// </summary>
    public string defaultResourcesPath = "default/";

    private DataAndInitMgr()
    {
        //初始化
        LoadRebind();
        musicData = new();
        //Debug.Log("数据管理器加载");

    }
    public void Init()
    {
        // 编辑器关闭域重载时，编辑态创建的输入资产可能已被销毁；沿原入口重建一次。
        LoadRebind();
        musicData = LoadDataBinary<MusicInfo>();
        //Debug.Log("数据管理器执行");
    }

    #region 持久化数据结构类
    /// <summary>
    /// 音乐数据
    /// </summary>
    public class MusicInfo
    {
        [FieldOrder(0)] public bool bkMusicOpen = true;
        [FieldOrder(1)] public bool soundOpen = true;
        [FieldOrder(2)] public float bkMusicValue = 0.5f;
        [FieldOrder(3)] public float soundValue = 0.5f;
    }
    #endregion

    #region 音乐数据相关
    public MusicInfo musicData;
    /// <summary>
    /// 保存音乐数据
    /// </summary>
    public void SaveMusicData()
    {
        SaveDataBinary(musicData);
    }
    #endregion

    #region 改键相关
    // 基础动作资产只加载一次。默认键位写在 Resources/player.json，改键只持久化绑定覆盖。
    private InputActionAsset assetin;
    public InputActionAsset asset => assetin;
    static string BindingOverridePath => Application.persistentDataPath + "/InputBindingOverrides.json";

    /// <summary>把当前资产上的绑定覆盖写入持久化。不重新生成基础资产。</summary>
    public void SaveBindingOverrides()
    {
        if (assetin == null) return;
        File.WriteAllText(BindingOverridePath, assetin.SaveBindingOverridesAsJson());
    }

    /// <summary>
    /// 加载基础动作资产，并套用已有绑定覆盖。
    /// 不读取旧的 InputInfo.json，也不再替换整份 JSON。
    /// </summary>
    public void LoadRebind()
    {
        // 实际逻辑：资产已在内存时不重新 FromJson，避免换掉已订上的动作实例。
        if (assetin == null)
        {
            TextAsset source = Resources.Load<TextAsset>("player");
            assetin = InputActionAsset.FromJson(source.text);
        }
        // 安全：没有覆盖文件时用资产里的默认键位。
        if (!File.Exists(BindingOverridePath)) return;
        string overrides = File.ReadAllText(BindingOverridePath);
        if (string.IsNullOrEmpty(overrides)) return;
        assetin.LoadBindingOverridesFromJson(overrides);
    }
    #endregion

    #region 二进制数据相关
    /// <summary>
    /// 二进制配置文件存储路径
    /// </summary>
    public static string binaryDataPath = UnityEngine.Application.streamingAssetsPath + "/BinaryData/";
    /// <summary>
    /// 默认二进制数据持久化路径
    /// </summary>
    public static string usingBinaryDataPath =  UnityEngine.Application.persistentDataPath + "/";
    /// <summary>
    /// 容器类的容器，配置文件信息
    /// 键名表示容器类名
    /// </summary>
    public static Dictionary<string,BaseCollection> ininDataDic = new Dictionary<string,BaseCollection>();

    /// <summary>
    /// 存储二进制数据，固定路径，文件名为类名
    /// </summary>
    /// <typeparam name="T">纯数据类</typeparam>
    /// <param name="obj">实例对象</param>
    public void SaveDataBinary<T>(T obj) where T : class
    {
        //获取类型
        Type type = typeof(T);
        //获取所有变量
        FieldInfo[] fields = type.GetFields();

        string DataPath = usingBinaryDataPath + typeof(T).Name + ".bitto";

        if(!Directory.Exists(usingBinaryDataPath))
            Directory.CreateDirectory(usingBinaryDataPath);

        using(FileStream fs=new FileStream(DataPath, FileMode.Create,FileAccess.Write))
        {
            //按序写入内容
            foreach (FieldInfo fi in fields)
            {
                if (fi.FieldType == typeof(int))
                {
                    int intValue = (int)fi.GetValue(obj);
                    fs.Write(BitConverter.GetBytes(intValue), 0, 4);
                }
                else if (fi.FieldType == typeof(float))
                {
                    float floatValue= (float)fi.GetValue(obj);
                    fs.Write(BitConverter.GetBytes(floatValue), 0, 4);
                }
                else if(fi.FieldType == typeof(bool))
                {
                    bool boolValue= (bool)fi.GetValue(obj);
                    fs.Write(BitConverter.GetBytes(boolValue), 0, 1);
                }
                else if (fi.FieldType == typeof(string))
                {
                    string str= (string)fi.GetValue(obj);
                    fs.Write(BitConverter.GetBytes(str.Length), 0, 4);
                    fs.Write(Encoding.UTF8.GetBytes(str),0,str.Length);
                }
                else
                {
                    Debug.LogError($"非法字段,类型{obj.GetType().Name}为非纯数据类");
                    fs.Close();
                    File.Delete(DataPath);
                    return;
                }
            }

            fs.Close ();
        }
    }
    /// <summary>
    /// 读取二进制文件,未找到文件时候返回new对象
    /// </summary>
    /// <typeparam name="T">类型</typeparam>
    /// <returns></returns>
    public T LoadDataBinary<T>() where T : class, new()
    {
        //获取类型
        Type type = typeof(T);
        //获取所有变量
        FieldInfo[] fields = type.GetFields();
        //创建
        T data = new T();
        string DataPath = usingBinaryDataPath + typeof(T).Name + ".bitto";

        if(!File.Exists(DataPath))
        {
            return data;
        }

        using(FileStream fs=File.Open(DataPath, FileMode.Open, FileAccess.Read))
        {
            //读取全部字节内容
            byte[] buffer = new byte[fs.Length];
            fs.Read(buffer, 0, buffer.Length);
            fs.Close();
            int index = 0;

            foreach(FieldInfo fi in fields)
            {
                if(fi.FieldType == typeof(int))
                {
                    int num = BitConverter.ToInt32(buffer, index);
                    index += 4;
                    fi.SetValue(data, num);
                }
                else if(fi.FieldType == typeof(float))
                {
                    float num= BitConverter.ToSingle(buffer, index); 
                    index += 4;
                    fi.SetValue(data, num);
                }
                else if(fi.FieldType == typeof(bool))
                {
                    bool va= BitConverter.ToBoolean(buffer, index);
                    index += 1;
                    fi.SetValue(data, va);
                }
                else if (fi.FieldType == typeof(string))
                {
                    int leng= BitConverter.ToInt32(buffer, index);
                    index += 4;
                    string str=Encoding.UTF8.GetString(buffer, index, leng);
                    index += leng;
                    fi.SetValue(data, str);
                }
                else
                {
                    Debug.LogError($"非法字段,类型{typeof(T)}为非纯数据类");
                    return null;
                }
            }
        }

        return data;
    }
    #endregion

    #region 关卡进度
    public const string LevelOrderResource = "LevelOrder";
    static string LevelProgressPath => Path.Combine(PlayerProgressDirectory, "LevelProgress.txt");
    static string PlayerProgressPath => Path.Combine(PlayerProgressDirectory, "PlayerProgress.json");
#if UNITY_EDITOR
    // 编辑器验证使用独立目录；正式构建没有这个入口，不改玩家实际存档位置。
    public static string debugPlayerProgressDirectory;
#endif
    private static string PlayerProgressDirectory
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(debugPlayerProgressDirectory)) return debugPlayerProgressDirectory;
#endif
            return Application.persistentDataPath;
        }
    }
    private const int PlayerProgressVersion = 1;
    private const int UpgradeKindCount = 6;

    // 只记录已确认永久数据；关序和能力使用同一份文件，避免分别写入留下半份进度。
    [Serializable]
    private sealed class PlayerProgressFile
    {
        public int version;
        public int levelIndex;
        public int[] confirmedUpgradeLevels;
        public PermanentEffectFile[] confirmedEffects;
    }

    // PlayerEffectRecord 是只读运行记录，JSON 使用有限字段而不改变其运行时接口。
    [Serializable]
    private sealed class PermanentEffectFile
    {
        public int kind;
        public float multiplier;
    }

    public LevelOrderFile LoadLevelOrder()
    {
        TextAsset asset = Resources.Load<TextAsset>(LevelOrderResource);
        // 安全：还没生成顺序文件时不编造关卡。
        if (asset == null || string.IsNullOrEmpty(asset.text)) return new LevelOrderFile();
        LevelOrderFile file = JsonUtility.FromJson<LevelOrderFile>(asset.text);
        if (file == null || file.scenes == null) return new LevelOrderFile();
        return file;
    }

    public int LoadLevelIndex()
    {
        return TryReadPlayerProgress(PlayerProgressDirectory, out PlayerProgressFile progress)
            ? progress.levelIndex : 0;
    }

    public void SaveLevelIndex(int index)
    {
        // 安全：旧入口也不能覆盖已确认能力，或用一次关序写入修复损坏的新存档。
        if (index < 0)
        {
            Debug.LogError($"不能保存负数关序：{index}。");
            return;
        }
        if (!TryReadPlayerProgress(PlayerProgressDirectory, out PlayerProgressFile progress)) return;
        progress.levelIndex = index;
        WritePlayerProgress(PlayerProgressDirectory, progress);
    }

    /// <summary>读取关序及已确认永久数据；损坏／未知版本时拒绝读取且不改变已有记录。</summary>
    public bool LoadPlayerProgress(PlayerEffectLedger ledger, out int levelIndex)
    {
        return LoadPlayerProgressFromDirectory(PlayerProgressDirectory, ledger, out levelIndex);
    }

    /// <summary>仅下一关安置成功后调用；本关临时和待确认项不进入磁盘。</summary>
    public bool SavePlayerProgress(int levelIndex, PlayerEffectLedger ledger)
    {
        return SavePlayerProgressToDirectory(PlayerProgressDirectory, levelIndex, ledger);
    }

    public void ClearLevelProgress()
    {
        ClearPlayerProgressFiles(PlayerProgressDirectory);
    }

    private static bool LoadPlayerProgressFromDirectory(string directory, PlayerEffectLedger ledger, out int levelIndex)
    {
        levelIndex = 0;
        // 安全：没有目标记录时不能只恢复关序。
        if (ledger == null)
        {
            Debug.LogError("读取玩家进度失败：效果记录为空。");
            return false;
        }
        if (!TryReadPlayerProgress(directory, out PlayerProgressFile progress)) return false;
        PlayerEffectRecord[] effects = RestoreEffectRecords(progress.confirmedEffects);
        // 整份记录经校验后再替换；RestoreConfirmed 本身也保证失败不产生部分恢复。
        if (!ledger.RestoreConfirmed(progress.confirmedUpgradeLevels, effects))
        {
            Debug.LogError($"读取玩家进度失败：{Path.Combine(directory, "PlayerProgress.json")} 的永久数据与能力配置不兼容。");
            return false;
        }
        levelIndex = progress.levelIndex;
        return true;
    }

    private static bool SavePlayerProgressToDirectory(string directory, int levelIndex, PlayerEffectLedger ledger)
    {
        // 安全：缺少记录或非法关序时不创建／覆盖进度。
        if (ledger == null || levelIndex < 0)
        {
            Debug.LogError("保存玩家进度失败：效果记录为空或关序为负数。");
            return false;
        }
        PlayerEffectRecord[] effects = ledger.CaptureConfirmedEffects();
        var progress = new PlayerProgressFile
        {
            version = PlayerProgressVersion,
            levelIndex = levelIndex,
            confirmedUpgradeLevels = ledger.CaptureConfirmedUpgradeLevels(),
            confirmedEffects = new PermanentEffectFile[effects.Length]
        };
        for (int i = 0; i < effects.Length; i++)
            progress.confirmedEffects[i] = new PermanentEffectFile { kind = (int)effects[i].Kind, multiplier = effects[i].Multiplier };
        // 已确认数据也先整体校验；无效快照不会替换此前有效存档。
        if (!ValidatePlayerProgress(progress))
        {
            Debug.LogError("保存玩家进度失败：已确认永久记录无效。");
            return false;
        }
        return WritePlayerProgress(directory, progress);
    }

    private static bool TryReadPlayerProgress(string directory, out PlayerProgressFile progress)
    {
        progress = null;
        string path = Path.Combine(directory, "PlayerProgress.json");
        try
        {
            // 新存档优先；新文件损坏时不回退到旧关序，避免回到另一套永久数据。
            if (File.Exists(path))
            {
                // 必填标量先放非法哨值，缺关序不能被 JsonUtility 默认成合法的第一关。
                progress = new PlayerProgressFile { version = -1, levelIndex = -1 };
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), progress);
                if (!ValidatePlayerProgress(progress))
                {
                    Debug.LogError($"读取玩家进度失败：{path} 版本或永久数据无效；文件未修改。");
                    progress = null;
                    return false;
                }
                return true;
            }
            int index = 0;
            string legacyPath = Path.Combine(directory, "LevelProgress.txt");
            // 旧纯关序只在内存中迁移；读档本身不创建 JSON，也不删除旧文件。
            if (File.Exists(legacyPath)
                && (!int.TryParse(File.ReadAllText(legacyPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0))
            {
                Debug.LogError($"读取玩家进度失败：{legacyPath} 关序无效；文件未修改。");
                return false;
            }
            progress = new PlayerProgressFile
            {
                version = PlayerProgressVersion,
                levelIndex = index,
                confirmedUpgradeLevels = new int[UpgradeKindCount],
                confirmedEffects = new PermanentEffectFile[0]
            };
            return true;
        }
        catch (Exception error) when (IsProgressFileError(error))
        {
            Debug.LogError($"读取玩家进度失败：{path}；{error.Message}");
            progress = null;
            return false;
        }
    }

    private static bool ValidatePlayerProgress(PlayerProgressFile progress)
    {
        // 安全：缺字段、未知版本和非法关序都不能被解释成一个全新的进度。
        if (progress == null || progress.version != PlayerProgressVersion || progress.levelIndex < 0
            || progress.confirmedUpgradeLevels == null || progress.confirmedUpgradeLevels.Length != UpgradeKindCount
            || progress.confirmedEffects == null) return false;
        for (int i = 0; i < progress.confirmedEffects.Length; i++)
            if (progress.confirmedEffects[i] == null) return false;
        // 用独立记录验证整组倍率与等级，避免读关序的兼容入口绕过永久效果校验。
        return new PlayerEffectLedger().RestoreConfirmed(progress.confirmedUpgradeLevels, RestoreEffectRecords(progress.confirmedEffects));
    }

    private static PlayerEffectRecord[] RestoreEffectRecords(PermanentEffectFile[] effects)
    {
        var restored = new PlayerEffectRecord[effects.Length];
        for (int i = 0; i < effects.Length; i++)
            restored[i] = new PlayerEffectRecord((PlayerEffectKind)effects[i].kind, effects[i].multiplier, PlayerEffectScope.Permanent);
        return restored;
    }

    private static bool WritePlayerProgress(string directory, PlayerProgressFile progress)
    {
        string path = Path.Combine(directory, "PlayerProgress.json");
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            // 写完并刷盘后才同目录原子替换，写入失败保留上一份完整进度。
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                {
                    writer.Write(JsonUtility.ToJson(progress, true));
                    writer.Flush();
                    stream.Flush(true);
                }
            }
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
            return true;
        }
        catch (Exception error) when (IsProgressFileError(error))
        {
            Debug.LogError($"保存玩家进度失败：{path}；{error.Message}");
            return false;
        }
        finally
        {
            // 安全：失败留下的临时文件不参与读档，不误删此前有效的正式文件。
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) when (IsProgressFileError(error))
            { Debug.LogError($"清理玩家进度临时文件失败：{temporaryPath}；{error.Message}"); }
        }
    }

    private static bool ClearPlayerProgressFiles(string directory)
    {
        try
        {
            // 清进度同时清关序、永久能力与未提交临时文件；不存在的文件无需处理。
            foreach (string name in new[] { "PlayerProgress.json", "LevelProgress.txt", "PlayerProgress.json.tmp" })
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) File.Delete(path);
            }
            return true;
        }
        catch (Exception error) when (IsProgressFileError(error))
        {
            Debug.LogError($"清除玩家进度失败：{directory}；{error.Message}");
            return false;
        }
    }

    private static bool IsProgressFileError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException || error is ArgumentException
            || error is NotSupportedException || error is System.Security.SecurityException;
    }
    #endregion
}
