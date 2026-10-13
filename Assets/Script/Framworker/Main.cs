using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 主入口
/// </summary>
public class Main : MonoBehaviour
{

    public static Main Instance;


    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<DisplaySettings>();

        DataConfigurationMgr.Instance.Init();
        DataAndInitMgr.Instance.Init();

        MonoPublicMgr.Instance.Init();

        UIMgr.Instance.Init();
        MusicMgr.Instance.Init();

        InputControlMgr.Instance.Init();
        //缓存加载界面
        UIMgr.Instance.ShowOneUI<LoadingPanel>(E_UILayer.Top, (UI) =>
        {
            // 预热回调若晚于开始点击，遮罩已被过场接管，不能再将它关闭。
            if (!SceneChangeMgr.Instance.IsLoading) UI.gameObject.SetActive(false);
        });
        //print(Application.persistentDataPath);
    }
    void Start()
    {
        //UIMgr.Instance.ShowOneUI<BeginPanel>();
        //MusicMgr.Instance.PlayBkMusic("BkMusic");

        //GameObject.DontDestroyOnLoad(this.gameObject);
        //GameObjData data = new GameObjData();
        //print(data.TestInfoDic[2].name);
        //读表功能测试
        //yyydddd yyydddd = new();
        //print(yyydddd.Sheet1Dic[2].name);
        //print("asssssssssss");
    }
}
