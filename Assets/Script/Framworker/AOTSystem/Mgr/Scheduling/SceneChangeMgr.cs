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
