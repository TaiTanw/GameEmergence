using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

public class Point : MonoBehaviour
{
    // Start is called before the first frame update
    /// <summary>
    /// 判定线位置
    /// </summary>
    public Transform atPoint;
    /// <summary>
    /// 透明度设置
    /// </summary>
    public SpriteRenderer sprite;
    /// <summary>
    /// 碰撞检测开始
    /// </summary>
    public BoxCollider2D BoxCollider2D;
    /// <summary>
    /// 间隔时长
    /// </summary>
    public float BPMTime = 0.5f;
    /// <summary>
    /// 音乐播放中
    /// </summary>
    public bool musicIson;
    /// <summary>
    /// 拍间隔时间(外部可配置
    /// </summary>
    public double beatInterval = 0.5;
    /// <summary>
    /// 间隔时间
    /// </summary>
    double nextBeat;
    void Start()
    {


    }

    // Update is called once per frame
    void Update()
    {
        //定时移动
        if (AudioSettings.dspTime >= nextBeat && musicIson)
        {
            transform.position += Vector3.right * 5;
            // 出场位置由生成者明确安置；平台移动入口只从实际位置去终点。
            Taijie platform = PoolMgr.Instance.GetPoolValue("Taijie").GetComponent<Taijie>();
            platform.transform.position = transform.position + Vector3.up * 5f;
            platform.ToMovePoint(transform.position);

            nextBeat += beatInterval;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        //开始设置拍子时间和打开开关
        nextBeat = AudioSettings.dspTime;
        musicIson = true;
        //显示透明
        sprite.color = new UnityEngine.Color(sprite.color.r, sprite.color.g, sprite.color.b, 0f);
        //关闭碰撞检测
        BoxCollider2D.enabled = false;
        //设置初始位置
        transform.position = new Vector3(0, atPoint.position.y, transform.position.z);
        //开始播放音乐
        MusicMgr.Instance.NoPauseBKMusic();
    }
}



