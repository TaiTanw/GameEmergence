using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputControlMgr : BaseAutoMonoMgr<InputControlMgr>
{
    #region 玩家相关
    /// <summary>
    /// 运行时输入信息
    /// </summary>
    PlayerInputData inputData;
    public PlayerInputData PlayerInputData=>inputData;
    /// <summary>
    /// 配置时输入绑定
    /// </summary>
    PlayerInput input;
    /// <summary>
    /// 所控制玩家的实例
    /// </summary>
    Player player;
    public Player Player => player;
    /// <summary>
    /// 玩家美术资源所指向路径（名称）此处已经配置===========================================================
    /// </summary>
    string playerModle;
    public string PlayerModleName=>playerModle;

    #endregion

    /// <summary>
    /// 主摄像机
    /// </summary>
    MainCamera mainCamera;
    #region 动作图
    InputActionMap player1;
    InputAction moveAction;
    InputAction jumpAction;
    InputAction interactAction;
    bool gameplayActionsBound;
    InputActionRebindingExtensions.RebindingOperation rebindOperation;
    bool interactHolding;
    bool interactLongFired;
    float interactHoldStart;
    const float InteractLongPressSeconds = 0.4f;
    #endregion
    /// <summary>
    /// 把基础动作资产接到 PlayerInput，并订上移动、跳跃和交互。
    /// 资产对象不变时重复调用会先卸订阅再订上，避免改键后再绑一次就触发两次。
    /// </summary>
    public void BindInputAsset()
    {
        bool wasEnabled = input.currentActionMap != null && input.currentActionMap.enabled;
        UnbindGameplayActions();
        input.actions = DataAndInitMgr.Instance.asset;

        player1 = input.actions.FindActionMap("player");
        moveAction = player1["Move"];
        jumpAction = player1["Jump"];
        interactAction = player1["Interact"];

        moveAction.performed += OnMovePerformed;
        moveAction.canceled += OnMoveCanceled;
        jumpAction.started += OnJumpStarted;
        jumpAction.canceled += OnJumpCanceled;
        interactAction.started += OnInteractStarted;
        interactAction.canceled += OnInteractCanceled;
        gameplayActionsBound = true;

        input.SwitchCurrentActionMap("player");
        // 实际逻辑：已经在接收输入时，换绑后保持开启。第一次还没有动作图，由 Init 关闭。
        if (wasEnabled)
            InputOpenOrClose(true);
    }

    /// <summary>
    /// 交互式改某一条绑定。不重建资产，完成后只保存绑定覆盖。
    /// </summary>
    public void StartRebind(string actionName, int bindingIndex, Action onComplete)
    {
        // 安全：上一轮还在听按键时不再开一轮。
        if (rebindOperation != null || player1 == null) return;
        bool mapWasEnabled = input.currentActionMap != null && input.currentActionMap.enabled;
        InputAction action = player1[actionName];
        bool actionWasEnabled = action.enabled;
        // 实际逻辑：Input System 1.11.2 在动作仍启用时启动交互改键会抛异常。先停用该动作。
        // 改键过程可能关掉动作图，按住的交互会收到 canceled，所以这里先中止计时。
        AbortInteractHold();
        action.Disable();
        rebindOperation = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnComplete(operation => FinishRebind(operation, action, actionWasEnabled, mapWasEnabled, onComplete))
            .OnCancel(operation => FinishRebind(operation, action, actionWasEnabled, mapWasEnabled, null))
            .Start();
    }

    void FinishRebind(InputActionRebindingExtensions.RebindingOperation operation, InputAction action,
        bool actionWasEnabled, bool mapWasEnabled, Action onComplete)
    {
        operation.Dispose();
        rebindOperation = null;
        // 实际逻辑：取消不写入新键，完成才保存覆盖。两种结束都恢复改键前的开关。
        if (onComplete != null)
            DataAndInitMgr.Instance.SaveBindingOverrides();
        if (actionWasEnabled)
            action.Enable();
        if (mapWasEnabled && input.currentActionMap != null && !input.currentActionMap.enabled)
            InputOpenOrClose(true);
        onComplete?.Invoke();
    }

    /// <summary>
    /// 扫描 player 动作图里的实际按键。复合轴本身不参与。两条绑定路径相同即冲突。
    /// </summary>
    public bool HasPlayerBindingConflict(out string message)
    {
        message = null;
        if (player1 == null) return false;
        var used = new Dictionary<string, string>();
        foreach (InputAction action in player1.actions)
        {
            foreach (InputBinding binding in action.bindings)
            {
                // 实际逻辑：1D 轴的父绑定不是按键。
                if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath)) continue;
                string path = binding.effectivePath;
                string label = string.IsNullOrEmpty(binding.name) ? action.name : action.name + "/" + binding.name;
                if (used.TryGetValue(path, out string other))
                {
                    message = other + " 与 " + label + " 使用了相同按键";
                    return true;
                }
                used.Add(path, label);
            }
        }
        return false;
    }

    void UnbindGameplayActions()
    {
        // 安全：还没订过，或动作已随资产销毁。
        if (!gameplayActionsBound) return;
        if (moveAction != null)
        {
            moveAction.performed -= OnMovePerformed;
            moveAction.canceled -= OnMoveCanceled;
        }
        if (jumpAction != null)
        {
            jumpAction.started -= OnJumpStarted;
            jumpAction.canceled -= OnJumpCanceled;
        }
        if (interactAction != null)
        {
            interactAction.started -= OnInteractStarted;
            interactAction.canceled -= OnInteractCanceled;
        }
        interactHolding = false;
        interactLongFired = false;
        gameplayActionsBound = false;
    }

    void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        inputData.moveInput = ctx.ReadValue<float>();
    }

    void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        inputData.moveInput = 0;
    }

    void OnJumpStarted(InputAction.CallbackContext ctx)
    {
        inputData.jumpPressed = true;
    }

    void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        inputData.jumpRelease = true;
    }

    void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        interactHolding = true;
        interactLongFired = false;
        interactHoldStart = Time.time;
    }

    void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        // 实际逻辑：一次按下只产生一种结果。已发出长按，或松开时时长已过阈值，都算长按。
        if (!interactHolding) return;
        bool longEnough = interactLongFired || Time.time - interactHoldStart >= InteractLongPressSeconds;
        interactHolding = false;
        if (longEnough)
        {
            EmitInteractLongPress();
            return;
        }
        inputData.interactTapped = true;
        print("交互点按");
    }

    void UpdateInteractHold()
    {
        // 实际逻辑：按住超过阈值只产生一次长按。
        if (!interactHolding || interactLongFired) return;
        if (Time.time - interactHoldStart < InteractLongPressSeconds) return;
        EmitInteractLongPress();
    }

    void EmitInteractLongPress()
    {
        if (interactLongFired) return;
        interactLongFired = true;
        inputData.interactLongPressed = true;
        print("交互长按");
    }

    /// <summary>关闭输入或改交互键前中止按住计时，并清掉还没被状态机消费的交互边沿。</summary>
    void AbortInteractHold()
    {
        interactHolding = false;
        interactLongFired = false;
        if (inputData == null) return;
        inputData.interactTapped = false;
        inputData.interactLongPressed = false;
    }
    /// <summary>
    /// Awake保证数据结构存在
    /// </summary>
    private void Awake()
    {
        // 获取或添加 PlayerInput 组件
        //input = GetComponent<PlayerInput>();

        if (input == null)
        {
            input = gameObject.AddComponent<PlayerInput>();
        }

        // 设置 Behavior 为 Invoke CSharp Events
        input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

        //创建运行时输入信息
        inputData = new PlayerInputData();
    }
    
    /// <summary>
    /// 显式初始化，精准控制
    /// </summary>
    public void Init()
    {
        //玩家模型数据配置指向
        playerModle = DataConfigurationMgr.Instance.StartRes.playerModleName;

        BindInputAsset();//关联action唯一入口,关联数据管理器内已经初始化完成的数据
        //切换当前输入地图
        input.SwitchCurrentActionMap("player");
        //绑定后关闭输入响应，等待正式游戏后开启
        InputOpenOrClose(false);
    }
    /// <summary>
    /// 输入控制开关
    /// </summary>
    /// <param name="isOpen"></param>
    public void InputOpenOrClose(bool isOpen)
    {
        if (isOpen)
        {
            input.currentActionMap.Enable();
            return;
        }
        // 实际逻辑：Disable 会对仍按住的动作发 canceled。先中止交互，避免被当成点按。
        AbortInteractHold();
        input.currentActionMap.Disable();
    }
    /// <summary>
    /// 关联玩家,由玩家主动拉取(核心数据提前初始化完成，待到玩家创建时主动获取）
    /// </summary>
    /// <param name="player"></param>
    public void BindPlayer(Player player)
    {
        // 实际逻辑：先解除已经在控制的角色。传入的新角色还没接上，不能对它清输入。
        if (this.player != null && this.player != player)
            this.player.RemoveInput();
        this.player = player;
        // 安全：空目标只表示当前无人受控。
        if (this.player == null) return;
        this.player.ChangeInputAsset(inputData);
    }

    /// <summary>
    /// 设置玩家模型指向，选择角色时使用
    /// </summary>
    /// <param name="name"></param>
    public void SetPlayerModle(string name)
    {
        playerModle = name;
    }
    /// <summary>
    /// 设置主摄像机
    /// </summary>
    /// <param name="camera"></param>
    public void SetMainCamera(MainCamera camera)
    {
        mainCamera = camera;
    }
    void Update()
    {
        //摄像机与玩家均不为空
        if (mainCamera != null && player != null)
        {
            mainCamera.SetPoint(player.transform.position);

        }
        UpdateInteractHold();
    }
}
