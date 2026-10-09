using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 测试用例
/// </summary>
public class TaskSp : MonoBehaviour
{
    //需要注意public变量会被编辑器持久化，且编辑器有更高优先级
    //当UI数值与成员变量关联时，private更加合适

    //此处数据后续用于对接本地持久化


    void Start()
    {
        //UIMgr.Instance.Init();
        //print(DataAndInitMgr.Instance.asset.FindAction("Move").bindings[1].path);
        //BaseCollection a = new BaseCollection();
        //GameObjData d = new GameObjData();
    }

    // Update is called once per frame
    void Update()
    {
        

        
    }
    private void OnGUI()
    {



        #region 音效测试
        //if (GUILayout.Button("播放背景音乐"))
        //{
        //    MusicMgr.Instance.PlayBkMusic("BkMusic");
        //}
        //if (GUILayout.Button("暂停背景音乐"))
        //{
        //    MusicMgr.Instance.PauseBKMusic();
        //}
        //if (GUILayout.Button("停止背景音乐"))
        //{
        //    MusicMgr.Instance.StopBkMusic();
        //}
        //newBkValue = GUILayout.HorizontalSlider(newBkValue, 0f, 1f);
        //if(oldBkValue!= newBkValue)
        //{
        //    oldBkValue = newBkValue;
        //    MusicMgr.Instance.SetBkMusicVolume(oldBkValue);
        //}
        //if (GUILayout.Button("测试音效触发"))
        //{
        //    MusicMgr.Instance.StartSound("Tower");
        //}
        //if (GUILayout.Button("测试音效暂停开"))
        //{
        //    MusicMgr.Instance.PauseOrPlaySoundMusic(true);
        //}
        //if (GUILayout.Button("测试音效暂停关"))
        //{
        //    MusicMgr.Instance.PauseOrPlaySoundMusic(false);
        //}
        //newSoundValue = GUILayout.HorizontalSlider(newSoundValue, 0f, 1f);
        //if (oldSoundValue != newSoundValue)
        //{
        //    oldSoundValue = newSoundValue;
        //    MusicMgr.Instance.SetSoundVolume(oldSoundValue);
        //}
        #endregion

        #region UI测试
        //if (GUILayout.Button("显示开始面板"))
        //{
        //    UIMgr.Instance.ShowOneUI<BeginPanel>();
        //    UIMgr.Instance.HideOneUI<BeginPanel>();
        //    //UIMgr.Instance.ShowOneUI<BeginPanel>();
        //    //UIMgr.Instance.GetOnePanel<BeginPanel>((obj) =>
        //    //{
        //    //    obj.TeskPanel();

        //    //});
        //}
        #endregion

        //if (GUILayout.Button("显示改建面板"))
        //{
        //    UIMgr.Instance.ShowOneUI<InputChangePanel>();
        //}

        //if (GUILayout.Button("计时器测试"))
        //{
        //    int a = 0;
        //    a = TimerMgr.Instance.StartTimerDataObj(() =>
        //    {
        //        print("计时器结束"+a);
        //    }, 4500, () =>
        //    {
        //        print($"{a}计时器间隔执行,剩余{(float)(TimerMgr.Instance.keyDic[a].nowEndTime)/1000f}秒");
        //    }, 500);
        //    print(a+"计时器执行");
        //}

        if (GUILayout.Button("返回开始界面"))
        {
            SceneChangeMgr.Instance.ReturnToMenu();
        }

        // 当前测试场景沿用已有按钮入口，正式面板也调用同一个模式规则。
        if (LevelStageMgr.Instance.HasExploration)
            GUILayout.Label($"认知：{LevelStageMgr.Instance.CognitionRemaining:0.##}");
        if (LevelStageMgr.Instance.HasExploration
            && GUILayout.Button(LevelStageMgr.Instance.IsSimulation ? "返回漫游" : "进入模拟"))
            SceneChangeMgr.Instance.SetSimulation(!LevelStageMgr.Instance.IsSimulation);

        if (GUILayout.Button("打开对话框"))
        {
            UIMgr.Instance.ShowOneUI<DialogBoxPanel>(E_UILayer.Middle, async (T) =>
            {
                await T.PlayText("哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大苏打哇大");
                print("下一步");
            });
        }

    }

}
