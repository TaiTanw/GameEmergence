using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Experimental.GlobalIllumination;
using static Unity.VisualScripting.Member;

public class MusicMgr : BaseMgr<MusicMgr>
{
    public MusicMgr()
    {
        //构造函数只保证数据结构存在
    }
    /// <summary>
    /// 循环删除非循环已结束音效
    /// </summary>
    private void UpdateT()
    {
        //需要逆向遍历删除
        if(isDelete)
        {
            for(int i = ListSoundMusics.Count-1; i >=0; i--)
            {
                if (!ListSoundMusics[i].isPlaying)
                {
                    ListSoundMusics[i].clip = null;
                    PoolMgr.Instance.PushInObj(ListSoundMusics[i].gameObject);
                    ListSoundMusics.Remove(ListSoundMusics[i]);
                }
            }
        }
    }

    private AudioSource BkMusic;
    private AudioSource buttonClickAudio;
    /// <summary>
    /// 用于配置默认的开始背景音效名称，用于Addressable加载的资源名=========================================================
    /// </summary>
    private string BkDefaultMusicName = "BkMusic";

    public float BkVolume ;
    public bool BknoMute ;

    private List<AudioSource> ListSoundMusics=new List<AudioSource>();

    public float SoundVolume ;
    public bool SoundnoMute;

    /// <summary>
    /// 删除准许，默认为true，暂停时为false
    /// </summary>
    private bool isDelete=true;


    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        if(BkMusic==null)
        {
            MonoPublicMgr.Instance.AddFixedUpdateEvent(UpdateT);
            //Debug.Log("音乐管理器加载成功");
            //关联背景音乐的主要组件和脚本
            GameObject obj = new GameObject("BkMusicAudio");
            BkMusic = obj.AddComponent<AudioSource>();
            // 点击声常驻且使用二维音源，关闭面板或暂停游戏都不截断这次反馈。
            buttonClickAudio = obj.AddComponent<AudioSource>();
            buttonClickAudio.playOnAwake = false;
            buttonClickAudio.spatialBlend = 0f;
            buttonClickAudio.ignoreListenerPause = true;
            GameObject.DontDestroyOnLoad(obj);//过场景不移除
        }

        GameUIAssets uiAssets = GameUIAssets.Shared;
        // 制作资源缺失时明确报错，不让每次点击重新加载或寻找替代音效。
        if (uiAssets == null || uiAssets.buttonClickSound == null)
            Debug.LogError("GameUIAssets 未关联按钮点击音效。");
        else buttonClickAudio.clip = uiAssets.buttonClickSound;

        //读取本地音乐设置文件
        BknoMute = DataAndInitMgr.Instance.musicData.bkMusicOpen;
        SoundnoMute = DataAndInitMgr.Instance.musicData.soundOpen;
        BkVolume = DataAndInitMgr.Instance.musicData.bkMusicValue;
        SoundVolume = DataAndInitMgr.Instance.musicData.soundValue;

        //配置名要在第一次加载前生效，否则会按字段初值去找旧地址
        BkDefaultMusicName=DataConfigurationMgr.Instance.StartRes.BkDefaultMusicName;
        PlayBkMusic(BkDefaultMusicName);
        SetBkMusicVolume(BkVolume);
        PauseOrPlaySoundMusic(SoundnoMute);
        SetSoundVolume(SoundVolume);

        //数据读取校对
        //Debug.Log(BkMute+""+ SoundMute+ BkVolume+ SoundVolume);
    }

    #region 背景音乐相关
    /// <summary>
    /// 播放背景音乐
    /// </summary>
    /// <param name="name">资源名称</param>
    /// <param name="callBack">特殊设置，回调剩下逻辑将跳过</param>
    public void PlayBkMusic(string name,UnityAction<AudioSource> callBack=null)//重复调用会使背景音乐重新播放，需注意
    {
        
        AddresableMge.Instance.LoadAssetAsyncI<AudioClip>(name, (hande) =>
        {
            //回调传入加载好的资源，是否成功加载会在内部自动判断
            BkMusic.clip = hande.Result;
            BkMusic.volume = BkVolume;//音量大小
            if (callBack != null )//若有额外设置，则只执行此处部分
            {
                callBack?.Invoke(BkMusic);
                return;
            }
            BkMusic.loop=true;//循环

            StartBkMusic();
        });
    }
    public void StartBkMusic()//开启背景音乐
    {
        if (BkMusic == null)
        {
            Debug.LogWarning("音乐资源为空！！！");
            return;
        }
        if (BknoMute)
        {
            BkMusic.Play();
        }
        else
        {
            StopBkMusic();
        }

    }
    /// <summary>
    /// 停止背景音乐
    /// </summary>
    /// <param name="toDelete">默认为false表示停止但不卸载资源</param>
    /// <param name="assetName">资源名称</param>
    public void StopBkMusic(bool toDelete=false,string assetName="")
    {
        if (BkMusic != null)
        {
            BkMusic.Stop();
            if (toDelete)
            {
                if(AddresableMge.Instance.FindNameConfirmexistence(assetName))
                    AddresableMge.Instance.Release<AudioClip>(assetName);
            }
                
        }
    }

    /// <summary>
    /// 背景音乐暂停
    /// </summary>
    public void PauseBKMusic()
    {
        if (BkMusic != null)
        {
            BkMusic.Pause();
        }
    }
    /// <summary>
    /// 解除背景暂停
    /// </summary>
    public void NoPauseBKMusic()
    {
        if (BkMusic != null)
        {
            BkMusic.UnPause();
        }
    }
    //设置背景音乐大小
    public void SetBkMusicVolume(float value)
    {
        //if (BkMusic != null) 
        //    BkMusic.volume=value;//此写法只能在背景音乐存在时候修改，缺乏灵活
        BkVolume = value;
        if (BkMusic != null)
        {
            BkMusic.volume = BkVolume;
        }
    }
    #endregion

    #region 音效相关
    /// <summary>共用按钮点击声；复用音效音量与开关，不经过异步加载或受固定步清理影响的音效池。</summary>
    public void PlayButtonClick()
    {
        // 尚未初始化的界面或缺失制作资源不能发声。
        if (buttonClickAudio == null || buttonClickAudio.clip == null) return;
        // 用户关闭音效时不积压请求，开启后只播放新的点击。
        if (!SoundnoMute) return;
        buttonClickAudio.PlayOneShot(buttonClickAudio.clip);
    }

    /// <summary>
    /// 播放声效,addressable异步加载
    /// </summary>
    /// <param name="name">音效名称</param>
    /// <param name="isLoop">是否循环</param>
    /// <param name="callBack">函数回调，只在循环音效使用</param>
    public void StartSound(string name, bool isLoop = false, UnityAction<AudioSource> callBack = null,Vector3 vector3=new Vector3())
    {
        if (SoundnoMute == false)
        {
            return;
        }
        //此处已经设置音效最大同时存在15个
        AudioSource sound = PoolMgr.Instance.
            GetPoolValue("MusicPlayObj",15,DataAndInitMgr.Instance.resourcesNecessaryAssetsPath).GetComponent<AudioSource>();
        
        AddresableMge.Instance.LoadAssetAsyncI<AudioClip>(name, (hande) =>
        {
            //防止出错，先停止播放
            sound.Stop();

            sound.clip = hande.Result;
            sound.loop = isLoop;
            sound.volume = SoundVolume;
            //设置位置，若有3D音源需求
            sound.transform.position = vector3;
            sound.Play();

            //由于从缓存池中取出对象 有可能取出一个之前正在使用的（超上限时）
            //所以我们需要判断 容器中没有记录再去记录 不要重复去添加即可
            if (!ListSoundMusics.Contains(sound))
                ListSoundMusics.Add(sound);
            callBack?.Invoke(sound);
        });

    }


    /// <summary>
    /// 设置音效大小
    /// </summary>
    /// <param name="value"></param>
    public void SetSoundVolume(float value)
    {
        SoundVolume = value;
        if (buttonClickAudio != null) buttonClickAudio.volume = value;
        foreach(AudioSource sound in ListSoundMusics)
        {
            sound.volume = SoundVolume;
        }
    }
    /// <summary>
    /// 停止播放指定音效
    /// </summary>
    /// <param name="sound">要停止的音效组件</param>
    public void StopSoundMusic(AudioSource sound)
    {
        if (ListSoundMusics.Contains(sound))
        {
            sound.Stop();
            //清除文件引用
            sound.clip = null;
            //从音效容器中移除
            ListSoundMusics.Remove(sound);
            //依附对象放入缓存池
            PoolMgr.Instance.PushInObj(sound.gameObject);
        }
    }
    /// <summary>
    /// 音效暂停开关
    /// </summary>
    /// <param name="isPlay">true为播放</param>
    public void PauseOrPlaySoundMusic(bool isPlay)
    {
        SoundnoMute = isPlay;
        // 短点击关闭即停止，不在重新开启时重播旧反馈；其它音效沿原暂停语义。
        if (buttonClickAudio != null)
        {
            buttonClickAudio.mute = !isPlay;
            if (!isPlay) buttonClickAudio.Stop();
        }
        isDelete=isPlay;
        foreach(AudioSource sound in ListSoundMusics)
        {
            if (isPlay)
            {
                sound.Play();
            }
            else
            {
                sound.Pause();
            }
        }
    }
    #endregion
}
