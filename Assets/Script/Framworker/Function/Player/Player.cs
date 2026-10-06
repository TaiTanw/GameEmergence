using PhyData;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


/// <summary>
/// 输入解析结果
/// </summary>
public class PlayerInputData
{
    public float moveInput;     //水平输入
    public float verticalInput; // 上下，负数为下
    public bool jumpPressed;     // 这一帧是否按下
    public bool jumpRelease;       //跳跃是否长按中
    public bool interactTapped;    // 交互短按，与长按互斥
    public bool interactLongPressed; // 交互长按，只成立一次
}
/// <summary>
/// 持续性动作执行数据（瞬时动作靠事件触发
/// </summary>
public class MovementData 
{
    /// <summary>
    /// 移动方向，-1到1，最终速度由物理组件计算
    /// </summary>
    public float onMove;
    /// <summary>
    /// 上下输入。负数为下，由状态机每帧写入，物理只读。
    /// </summary>
    public float verticalInput;
    /// <summary>
    /// 当前状态
    /// </summary>
    public PlayerStateMachine.E_playerState nowState; 
}

/// <summary>
/// 只读包装类（仅包装需要读的属性）
/// </summary>
public class ReadOnly_ActionData
{
    private readonly MovementData _data;
    public ReadOnly_ActionData(MovementData data) => _data = data;
    //只读属性
    public float onMove => _data.onMove;
    public float verticalInput => _data.verticalInput;
    public PlayerStateMachine.E_playerState NowState =>_data.nowState;

}

public class Player : MonoBehaviour
{
    /// <summary>
    /// 物理组件
    /// </summary>
    CharacterPhysics playPDate;
    /// <summary>
    /// 物理碰撞信息
    /// </summary>
    public ReadOnly_GeometryPhysicsData NowPhyData => playPDate.ReadOnly_GeometryPhysicsData;

    public ReadOnly_PlayerPhysicsData NowSpeedData => playPDate.ReadOnly_PlayersPhysicsData;
    /// <summary>
    /// 玩家当前输入数据,初始化交给外部
    /// </summary>
    PlayerInputData inputData;
    /// <summary>
    /// 玩家当前所执行动作
    /// </summary>
    MovementData actionData;
    public ReadOnly_ActionData _ActionData;

    /// <summary>
    /// 玩家逻辑状态机对象
    /// </summary>
    PlayerStateMachine fsm;
    /// <summary>
    /// 表现层状态机
    /// </summary>
    PresentationLayer animatorFSM;

    void Awake()
    {

        //玩家主动拉取输入控制权限，调用方法保证唯一玩家控制权,且便于控制时序，保证先于状态机初始化
        InputControlMgr.Instance.BindPlayer(this);
        //可执行动作数据初始化
        actionData = new MovementData();
        _ActionData = new ReadOnly_ActionData(actionData);
        //逻辑状态机/物理状态/表现层初始化（保证存在
        fsm = new PlayerStateMachine();
        playPDate = gameObject.GetComponentInChildren<CharacterPhysics>();
        animatorFSM = gameObject.GetComponentInChildren<PresentationLayer>();

    }
    private void Start()
    {
        //保证可用
        fsm.InitData(playPDate.ReadOnly_GeometryPhysicsData, inputData, actionData);
        playPDate.Init(_ActionData, fsm.EventSystem);
        animatorFSM.Init(playPDate.ReadOnly_PlayersPhysicsData,playPDate.ReadOnly_GeometryPhysicsData, _ActionData);
    }
    /// <summary>
    /// 注入玩家按键数据行为
    /// </summary>
    /// <param name="data"></param>
    public void ChangeInputAsset(PlayerInputData data)
    {
        inputData = data;
    }
    public void RemoveInput()
    {
        inputData=null;
    }
    //float time;
    void Update()
    {
        //逻辑状态机更新，接收输入信息发送动作数据
        fsm.Update(inputData);
    }

}

