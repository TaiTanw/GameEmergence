using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景切换管理器
/// </summary>
public class SceneChangeMgr : BaseMgr<SceneChangeMgr>
{
    const string MenuSceneName = "BeginScenes";
    Player body;
    Player spirit;
    bool loading;
    int loadSerial;

    /// <summary>按已保存的关序号进入。没有存档时是第一关。</summary>
    public void EnterSavedLevel()
    {
        LevelOrderFile order = DataAndInitMgr.Instance.LoadLevelOrder();
        int index = DataAndInitMgr.Instance.LoadLevelIndex();
        // 安全：进度超出当前顺序时回到第一关。
        if (order.scenes == null || index < 0 || index >= order.scenes.Length) index = 0;
        EnterLevel(index, order);
    }

    /// <summary>进入顺序中的下一关。最后一关不再往后。</summary>
    public void EnterNextLevel()
    {
        LevelOrderFile order = DataAndInitMgr.Instance.LoadLevelOrder();
        EnterLevel(DataAndInitMgr.Instance.LoadLevelIndex() + 1, order);
    }

    /// <summary>还在关卡里就结束本局并载回开始场景。人已经在菜单时返回 false，让界面自己退一层。</summary>
    public bool ReturnToMenu()
    {
        int serial = ++loadSerial;
        loading = false;
        InputControlMgr.Instance.BindPlayer(null);
        InputControlMgr.Instance.InputOpenOrClose(false);
        // 实际逻辑：不在开始场景就结束本局。实体跨场景，必须显式销毁。
        bool inLevel = SceneManager.GetActiveScene().name != MenuSceneName;
        DestroyActor(ref spirit);
        DestroyActor(ref body);
        if (!inLevel)
        {
            UIMgr.Instance.HideOneUI<LoadingPanel>();
            return false;
        }
        UIMgr.Instance.HideOneUI<ChangePanel>();
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top);
        LoadSceneAsync(MenuSceneName, () =>
        {
            // 实际逻辑：加载被新的进入或返回作废时，不能关掉新的加载页。
            if (serial != loadSerial) return;
            UIMgr.Instance.HideOneUI<LoadingPanel>();
        });
        return true;
    }

    void EnterLevel(int index, LevelOrderFile order)
    {
        // 安全：没有顺序时不编造关卡。
        if (order.scenes == null || order.scenes.Length == 0)
        {
            Debug.LogError("关卡顺序还没生成，不能进入场景。");
            return;
        }
        // 安全：上一轮场景还没安置完时不开始下一次。
        if (loading)
        {
            Debug.Log("场景正在加载。");
            return;
        }
        if (index < 0 || index >= order.scenes.Length)
        {
            Debug.Log("已经是顺序中的最后一关。");
            return;
        }
        // 安全：失效的顺序文件不能关闭输入后再把加载流程卡住。
        if (string.IsNullOrWhiteSpace(order.scenes[index])
            || !Application.CanStreamedLevelBeLoaded(order.scenes[index]))
        {
            Debug.LogError($"关卡不能加载：{order.scenes[index]}，请重新生成关卡顺序并核对构建设置。");
            return;
        }
        loading = true;
        int serial = ++loadSerial;
        InputControlMgr.Instance.InputOpenOrClose(false);
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top);
        LoadSceneAsync(order.scenes[index], () =>
        {
            if (serial != loadSerial) return;
            PlaceBody(index, serial, placed => FinishEnter(index, serial, placed));
        });
    }

    void FinishEnter(int index, int serial, bool placed)
    {
        // 实际逻辑：返回或新的进入已经作废这一轮时，不再写进度、也不再打开输入。
        if (serial != loadSerial) return;
        loading = false;
        UIMgr.Instance.HideOneUI<LoadingPanel>();
        if (placed)
        {
            DataAndInitMgr.Instance.SaveLevelIndex(index);
            InputControlMgr.Instance.InputOpenOrClose(true);
        }
    }

    /// <summary>人工阅读入口：场景配置 → 留下的实体 → 本关精神体 → 唯一输入目标；成功后才保存进度。</summary>
    void PlaceBody(int index, int serial, UnityAction<bool> done)
    {
        SceneLevelSetup setup = SceneLevelSetup.Current;
        // 安全：场景没摆关卡配置时不创建或搬走实体，也不记进度。
        if (setup == null)
        {
            Debug.LogError("当前场景没有关卡配置。");
            done?.Invoke(false);
            return;
        }
        LevelStageMgr.Instance.Note(index, setup.Teaching);
        EnsureBody(serial, ok =>
        {
            if (serial != loadSerial) return;
            // 安全：实体没就绪时不安置，也不把控制权交给精神体。
            if (!ok || body == null)
            {
                done?.Invoke(false);
                return;
            }
            // 实际逻辑：先绑输入再刷新，状态机才能在 Start 之前拿到操作数据。
            InputControlMgr.Instance.BindPlayer(body);
            body.RefreshRuntime(setup.BodyPoint);
            if (setup.Teaching)
            {
                body.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
                done?.Invoke(true);
                return;
            }
            // 实际逻辑：非教学关实体停在出生点，控制交给新的精神体。不打开飞行。
            body.ApplyCapabilities(PlayerCapabilityPermissions.Standby);
            done?.Invoke(SpawnSpirit(setup));
        });
    }

    bool SpawnSpirit(SceneLevelSetup setup)
    {
        // 实际逻辑：上一只还在时先去掉，这一关再新建。
        DestroyActor(ref spirit);
        // 安全：没指定预设时不造精神体，实体保持待机。
        if (setup.SpiritPrefab == null)
        {
            Debug.LogError("非教学关没有指定精神体预设。", setup);
            return false;
        }
        GameObject created = Object.Instantiate(setup.SpiritPrefab, setup.SpiritPoint, Quaternion.identity);
        spirit = created.GetComponent<Player>();
        // 安全：预设身份不对时不把实体的控制权交出去。
        if (spirit == null || spirit.Identity != PlayerIdentity.Spirit)
        {
            Debug.LogError("精神体预设的身份不是精神体。", created);
            if (created != null) Object.Destroy(created);
            spirit = null;
            return false;
        }
        InputControlMgr.Instance.BindPlayer(spirit);
        spirit.RefreshRuntime(setup.SpiritPoint);
        spirit.ApplyCapabilities(PlayerCapabilityPermissions.Walking);
        return true;
    }

    static void DestroyActor(ref Player actor)
    {
        if (actor == null) return;
        Object.Destroy(actor.gameObject);
        actor = null;
    }

    void EnsureBody(int serial, UnityAction<bool> ready)
    {
        if (serial != loadSerial) return;
        if (body != null)
        {
            // 安全：已经留下的对象必须是实体，不能把精神体当成跨关角色。
            if (body.Identity != PlayerIdentity.Body)
            {
                Debug.LogError("已有角色的身份不是实体。", body);
                ready?.Invoke(false);
                return;
            }
            ready?.Invoke(true);
            return;
        }
        string model = InputControlMgr.Instance.PlayerModleName;
        // 安全：没有实体预设名时不能凭空造玩家。
        if (string.IsNullOrEmpty(model))
        {
            Debug.LogError("没有配置实体预设。");
            ready?.Invoke(false);
            return;
        }
        AddresableMge.Instance.LoadAssetAsyncI<GameObject>(model, handle =>
        {
            if (serial != loadSerial) return;
            // 安全：地址资源加载失败时保留菜单退出入口，不实例化空资源或写进度。
            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                || handle.Result == null)
            {
                Debug.LogError($"实体预设加载失败：{model}。");
                ready?.Invoke(false);
                return;
            }
            if (body == null)
            {
                GameObject created = Object.Instantiate(handle.Result);
                Object.DontDestroyOnLoad(created);
                Player player = created.GetComponent<Player>();
                // 安全：没有玩家组件，或身份不是实体时，不留下这个对象。
                if (player == null || player.Identity != PlayerIdentity.Body)
                {
                    Debug.LogError(player == null ? "实体预设上没有玩家组件。" : "实体预设的身份不是实体。", created);
                    Object.Destroy(created);
                    ready?.Invoke(false);
                    return;
                }
                body = player;
            }
            ready?.Invoke(true);
        });
    }

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
    /// <summary>
    /// 异步场景切换
    /// </summary>
    /// <param name="sceneName">场景名</param>
    /// <param name="callBack">回调</param>
    /// <param name="percentage">任务权重占比</param>
    public void LoadSceneAsync(string sceneName,UnityAction callBack,float percentage=1f)
    {
        AsyncOperation tion = SceneManager.LoadSceneAsync(sceneName);
        MonoPublicMgr.Instance.StartCoroutine(Load(tion,callBack,percentage));
    }

    IEnumerator Load(AsyncOperation ao,UnityAction action,float percentage)
    {
        while (!ao.isDone)
        {
            //事件分发，用于外部获取加载进度
            EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene,ao.progress* percentage);
            yield return 0;
        }

        EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene,1 * percentage);
        action?.Invoke();
    }
    /// <summary>
    /// 加载到游戏场景以及资源加载1
    /// </summary>
    /// <param name="name">场景名</param>
    /// <param name="resAndNum">加载资源名称和缓存池大小</param>

    /// <summary>旧入口会按名字预热缓存池再实例化玩家。新关卡流程不再调用；台阶物件留到逻辑收尾再清。</summary>
    public void LoadResToChangeSence(string name,Dictionary<string,int> resAndNum=null)
    {
        int task = 0;

        GameObject tobj = null;
        void ToStart()
        {
            if (task == 2)
            {

                GameObject created = GameObject.Instantiate(tobj);
                // 现有单角色场景由加载组装显式选目标，预设体 Awake 不再抢控制。
                InputControlMgr.Instance.BindPlayer(created.GetComponent<Player>());
                
                
                UIMgr.Instance.HideOneUI<LoadingPanel>();
                EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene, 0.1f);
            }
        }
        UIMgr.Instance.ShowOneUI<LoadingPanel>();

        LoadSceneAsync(name, () =>
        {
            //print("场景切换成功");
            //台阶对象预热

            if (resAndNum != null)
            {
                foreach (var e in resAndNum)
                {
                    PoolMgr.Instance.Preload(e.Key, e.Value);
                }

            }

            task += 1;
            ToStart();
        }, 0.4f);


        //玩家加载
        AddresableMge.Instance.LoadAssetAsyncI<GameObject>(InputControlMgr.Instance.PlayerModleName, (obj) =>
        {
            tobj = obj.Result;

            task += 1;
            EventCenterSystem.Instance.EventTrigger<float>(E_EventEnum.E_LoadScene, 0.4f);
            ToStart();
        });
    }
}
