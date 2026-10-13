using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

public partial class InputControlMgr : BaseAutoMonoMgr<InputControlMgr>
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
    public bool InputEnabled => input != null && input.currentActionMap != null && input.currentActionMap.enabled;
    public bool IsRebinding => rebindOperation != null;
    public Camera GameCamera => mainCamera != null ? mainCamera.GetComponent<Camera>() : null;
    float escapeHoldProgress;
    bool escapeHoldNeedsRelease;
    public float EscapeHoldProgress => escapeHoldProgress;
    public bool EscapeKeyConflict => EscapeKeyIsBound(out _);
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
    InputAction verticalAction;
    InputAction jumpAction;
    InputAction interactAction;
    InputAction sprintAction;
    InputAction formSwitchAction;
    bool gameplayActionsBound;
    InputActionRebindingExtensions.RebindingOperation rebindOperation;
    bool rebindMapWasEnabled;
    int rebindFinishedFrame = -1;
    bool menuInputPaused;
    bool inputBeforeMenu;
    bool interactHolding;
    bool interactLongFired;
    float interactHoldStart;
    const float InteractLongPressSeconds = 0.4f;
    #endregion
    /// <summary>
    /// 把基础动作资产接到 PlayerInput，并订上运动、交互和形态切换。
    /// 资产对象不变时重复调用会先卸订阅再订上，避免改键后再绑一次就触发两次。
    /// </summary>
    public void BindInputAsset()
    {
        bool wasEnabled = input.currentActionMap != null && input.currentActionMap.enabled;
        UnbindGameplayActions();
        input.actions = DataAndInitMgr.Instance.asset;

        player1 = input.actions.FindActionMap("player");
        moveAction = player1["Move"];
        verticalAction = player1["Vertical"];
        jumpAction = player1["Jump"];
        interactAction = player1["Interact"];
        sprintAction = player1["Sprint"];
        formSwitchAction = player1["FormSwitch"];

        moveAction.performed += OnMovePerformed;
        moveAction.canceled += OnMoveCanceled;
        verticalAction.performed += OnVerticalPerformed;
        verticalAction.canceled += OnVerticalCanceled;
        jumpAction.started += OnJumpStarted;
        jumpAction.canceled += OnJumpCanceled;
        interactAction.started += OnInteractStarted;
        interactAction.canceled += OnInteractCanceled;
        sprintAction.started += OnSprintStarted;
        sprintAction.canceled += OnSprintCanceled;
        formSwitchAction.performed += OnFormSwitchPerformed;
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
        // 实际逻辑：有冲突时暂不恢复动作图，下一次改键仍保留原来的开启意图。
        rebindMapWasEnabled |= input.currentActionMap != null && input.currentActionMap.enabled;
        InputAction action = player1[actionName];
        // 实际逻辑：等待改键时暂停玩家动作。停用会触发 canceled，先中止交互计时。
        AbortInteractHold();
        player1.Disable();
        InputSystem.onEvent += OnRebindInputEvent;
        rebindOperation = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            // 实际逻辑：玩家动作已暂停，保留设备状态更新，结束时才能读到当前按键。
            .WithMatchingEventsBeingSuppressed(false)
            .OnComplete(operation => FinishRebind(operation, onComplete))
            .OnCancel(operation => FinishRebind(operation, null))
            .Start();
    }

    void FinishRebind(InputActionRebindingExtensions.RebindingOperation operation, Action onComplete)
    {
        // 实际逻辑：改键的取消键已用于本次操作，这一帧不再拿 ESC 打开游戏菜单。
        rebindFinishedFrame = Time.frameCount;
        operation.Dispose();
        rebindOperation = null;
        // 实际逻辑：取消不写入新键，完成才保存覆盖。两种结束都恢复改键前的开关。
        if (onComplete != null)
            DataAndInitMgr.Instance.SaveBindingOverrides();
        // 实际逻辑：等本次按键状态写入后再恢复，避免用旧状态覆盖按住事实。
        Action restoreInput = null;
        restoreInput = () =>
        {
            InputSystem.onAfterUpdate -= restoreInput;
            InputSystem.onEvent -= OnRebindInputEvent;
            // 实际逻辑：重复键仍留在改键面板，也继续暂停玩家动作。改开后恢复原输入开关。
            if (rebindMapWasEnabled && !HasPlayerBindingConflict(out _))
            {
                rebindMapWasEnabled = false;
                InputOpenOrClose(true);
            }
            else
                SyncHeldInputs();
            onComplete?.Invoke();
        };
        InputSystem.onAfterUpdate += restoreInput;
    }

    /// <summary>玩家动作暂停期间，仍记录疾跑键的真实按住和松开，不把停用当作释放。</summary>
    void OnRebindInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        // 实际逻辑：只从设备状态事件读按键，忽略其它事件。
        if (eventPtr.type != StateEvent.Type && eventPtr.type != DeltaStateEvent.Type) return;
        foreach (InputControl control in sprintAction.controls)
        {
            if (control.device != device) continue;
            ButtonControl button = (ButtonControl)control;
            if (!button.ReadValueFromEvent(eventPtr, out float value)) continue;
            bool pressed = button.IsValueConsideredPressed(value);
            if (inputData.sprintHeld && !pressed)
                inputData.sprintReleased = true;
            inputData.sprintHeld = pressed;
            return;
        }
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
                // 回车固定结束探索；改成玩家动作会与长按抢键，必须改开才能退出设置。
                if (IsEscapeKey(path))
                {
                    message = label + " 使用了结束探索专用的回车，请改用其它键。";
                    return true;
                }
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
        if (verticalAction != null)
        {
            verticalAction.performed -= OnVerticalPerformed;
            verticalAction.canceled -= OnVerticalCanceled;
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
        if (sprintAction != null)
        {
            sprintAction.started -= OnSprintStarted;
            sprintAction.canceled -= OnSprintCanceled;
        }
        if (formSwitchAction != null)
            formSwitchAction.performed -= OnFormSwitchPerformed;
        if (inputData != null)
            inputData.sprintHeld = false;
        interactHolding = false;
        interactLongFired = false;
        gameplayActionsBound = false;
    }

    void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        inputData.moveInput = ctx.ReadValue<float>();
    }

    /// <summary>人工阅读入口：形态按键 → 当前精神体 → 原模式规则；与模式按钮共用费用、冷却和安全退出。</summary>
    void OnFormSwitchPerformed(InputAction.CallbackContext ctx)
    {
        // 安全：无人受控或正在控制实体时，不发精神体形态请求。
        if (player == null || player.Identity != PlayerIdentity.Spirit) return;
        // 实际逻辑：一次按下切换一次；改键结束或控制交接不补发此点按。
        player.RequestSpiritForm(player.ResolveFourWayAbility() != FourWayMode.Flying, PlayerFormChangeReason.Manual);
    }

    void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        inputData.moveInput = 0;
    }

    void OnVerticalPerformed(InputAction.CallbackContext ctx)
    {
        inputData.verticalInput = ctx.ReadValue<float>();
    }

    void OnVerticalCanceled(InputAction.CallbackContext ctx)
    {
        inputData.verticalInput = 0;
    }

    void OnSprintStarted(InputAction.CallbackContext ctx)
    {
        inputData.sprintHeld = true;
    }

    void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        // 实际逻辑：改键会停用动作并触发 canceled。键还按着就不是玩家松开，不写 sprintReleased，也不改 sprintHeld。
        if (ctx.control != null && ctx.control.IsPressed()) return;
        inputData.sprintHeld = false;
        // 实际逻辑：只有控件已经松开，才留下释放事实。同一段更新前再按下时，逻辑仍能解除耗尽禁跑。
        inputData.sprintReleased = true;
    }

    /// <summary>
    /// 动作重新启用后，按当前按键写回持续输入。不补发跳跃或交互这类点按。
    /// </summary>
    void SyncHeldInputs()
    {
        if (inputData == null) return;
        if (moveAction != null && moveAction.enabled)
            inputData.moveInput = moveAction.ReadValue<float>();
        if (verticalAction != null && verticalAction.enabled)
            inputData.verticalInput = verticalAction.ReadValue<float>();
        if (TryBoundButtonPressed(sprintAction, out bool sprintPressed))
            inputData.sprintHeld = sprintPressed;
    }

    /// <summary>
    /// 读绑定控件自己的按下。动作停用后相位会被清空，不能用动作的 IsPressed 当作恢复结果。
    /// </summary>
    static bool TryBoundButtonPressed(InputAction action, out bool pressed)
    {
        pressed = false;
        if (action == null || !action.enabled) return false;
        var controls = action.controls;
        if (controls.Count == 0) return false;
        for (int i = 0; i < controls.Count; i++)
        {
            if (controls[i].IsPressed())
            {
                pressed = true;
                return true;
            }
        }
        return true;
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
        // 演出在菜单内结束时只登记恢复意图，不能越过菜单直接开放操作。
        if (isOpen && menuInputPaused) { inputBeforeMenu = true; return; }
        if (isOpen)
        {
            input.currentActionMap.Enable();
            SyncHeldInputs();
            if (endingActor != null) ResetEndingJumpGate();
            return;
        }
        // 实际逻辑：Disable 会对仍按住的动作发 canceled。先中止交互，避免被当成点按。
        rebindMapWasEnabled = false;
        AbortInteractHold();
        input.currentActionMap.Disable();
        if (endingActor != null) { endingActor.ClearInput(); ClearEndingInputEdges(); }
    }

    /// <summary>菜单停用操作时仍观察疾跑键的真实释放，继续时只恢复持续输入，不补发点按。</summary>
    public void SetMenuInputPaused(bool paused)
    {
        if (menuInputPaused == paused) return;
        menuInputPaused = paused;
        if (paused)
        {
            inputBeforeMenu = InputEnabled;
            InputOpenOrClose(false);
            inputData.jumpPressed = inputData.jumpRelease = false;
            InputSystem.onEvent += OnRebindInputEvent;
        }
        else
        {
            InputSystem.onEvent -= OnRebindInputEvent;
            if (inputBeforeMenu && (player != null || SceneChangeMgr.Instance.IsEnding)) InputOpenOrClose(true);
        }
    }

    void OnDestroy()
    {
        InputSystem.onEvent -= OnRebindInputEvent;
        rebindOperation?.Dispose();
        UnbindGameplayActions();
    }
    /// <summary>
    /// 外部组装选择唯一控制目标；切换不转交旧角色的点按或交互计时。
    /// </summary>
    /// <param name="player"></param>
    public void BindPlayer(Player player)
    {
        // 安全：失效目标不替换现有控制，同目标重复绑定不清真实释放历史。
        if (player != null && !player.isActiveAndEnabled) return;
        if (endingActor != null) { endingActor.ClearInput(); endingActor = null; ClearEndingInputEdges(); }
        if (this.player == player) return;
        escapeHoldProgress = 0f;
        escapeHoldNeedsRelease = Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed);
        // 实际逻辑：先解除已经在控制的角色。传入的新角色还没接上，不能对它清输入。
        if (this.player != null && this.player != player)
            this.player.RemoveInput();
        AbortInteractHold();
        inputData.jumpPressed = inputData.jumpRelease = inputData.sprintReleased = false;
        inputData.moveInput = inputData.verticalInput = 0f;
        this.player = player;
        // 安全：空目标只表示当前无人受控。
        if (this.player == null) return;
        SyncHeldInputs();
        this.player.ChangeInputAsset(inputData, this);
    }

    public void UnbindPlayer(Player expected)
    {
        // 生命周期：只允许当前目标解绑自己，不创建新管理器。
        if (player == expected) BindPlayer(null);
    }

    public void ClearPlayerInputEdges(Player expected)
    {
        // 规则刷新只清当前目标的边沿，不影响另一角色，也不制造真实松键。
        if (player != expected) return;
        AbortInteractHold();
        inputData.jumpPressed = inputData.jumpRelease = inputData.sprintReleased = false;
    }

    /// <summary>
    /// 设置玩家模型指向，选择角色时使用
    /// </summary>
    /// <param name="name"></param>
    public void SetPlayerModle(string name)
    {
        playerModle = name;
    }
    Transform transitionFollow;
    public bool HasTransitionCamera => mainCamera != null && mainCamera.isActiveAndEnabled;
    public bool TransitionViewComplete => HasTransitionCamera && mainCamera.HasReachedPoint;

    /// <summary>
    /// 设置主摄像机
    /// </summary>
    /// <param name="camera"></param>
    public void SetMainCamera(MainCamera camera)
    {
        mainCamera = camera;
    }

    /// <summary>换关期间镜头改看实体，并把实体画在变暗遮罩前面。搬到新位置时才立刻对齐。</summary>
    public void BeginTransitionView(Transform target, bool snap)
    {
        transitionFollow = target;
        mainCamera?.SetTransitionPortrait(true, target);
        if (mainCamera != null && target != null) mainCamera.SetPoint(target.position);
        if (snap && mainCamera != null && target != null) mainCamera.SnapTo(target.position);
    }

    public void EndTransitionView(bool returningToMenu = false)
    {
        transitionFollow = null;
        mainCamera?.SetTransitionPortrait(false);
        // 实际逻辑：菜单保留镜头实例，但不让它与菜单镜头同时渲染或监听声音。
        if (returningToMenu) mainCamera?.SetGameViewEnabled(false);
    }
    void Update()
    {
        // 实际逻辑：ESC 固定为游戏菜单键，独立于可改键动作图；暂停输入后仍能关闭菜单。
        if ((player != null || SceneChangeMgr.Instance.IsEnding || SceneChangeMgr.Instance.EscapeSelectionPending || SceneChangeMgr.Instance.IsEscapeHandoff)
            && rebindOperation == null && rebindFinishedFrame != Time.frameCount
            && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SceneChangeMgr.Instance.ToggleGameMenu();
        //摄像机与玩家均不为空
        if (mainCamera != null)
        {
            Transform followed = transitionFollow != null ? transitionFollow : player != null ? player.transform : null;
            if (followed != null) mainCamera.SetPoint(followed.position);
        }
        UpdateInteractHold();
        UpdateEscapeHold(Time.unscaledDeltaTime);
        DeliverEndingInput();
    }

    static bool IsEscapeKey(string path) => string.Equals(path, "<Keyboard>/enter", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "<Keyboard>/numpadEnter", StringComparison.OrdinalIgnoreCase);

    bool EscapeKeyIsBound(out string actionName)
    {
        actionName = null;
        // 安全：输入资产还未组装时没有绑定可比较。
        if (player1 == null) return false;
        foreach (InputAction action in player1.actions)
            foreach (InputBinding binding in action.bindings)
                if (!binding.isComposite && IsEscapeKey(binding.effectivePath))
                { actionName = action.name; return true; }
        return false;
    }

    void UpdateEscapeHold(float elapsed)
    {
        Keyboard keyboard = Keyboard.current;
        bool held = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
        GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        bool typing = selected != null && (selected.GetComponent<UnityEngine.UI.InputField>() != null
            || selected.GetComponent<TMPro.TMP_InputField>() != null);
        // 暂停、改键、过场或文本编辑不积累；恢复后须松开再按，避免跨页面长按。
        if (!SceneChangeMgr.Instance.CanStartEscape || !InputEnabled || IsRebinding || typing || EscapeKeyConflict)
        {
            escapeHoldProgress = 0f;
            escapeHoldNeedsRelease = held;
            return;
        }
        if (!held)
        {
            escapeHoldNeedsRelease = false;
            escapeHoldProgress = Mathf.MoveTowards(escapeHoldProgress, 0f, elapsed / Mathf.Max(.01f, GameUIAssets.Shared.escapeRefillSeconds));
            return;
        }
        if (escapeHoldNeedsRelease) return;
        escapeHoldProgress = Mathf.MoveTowards(escapeHoldProgress, 1f, elapsed / Mathf.Max(.1f, GameUIAssets.Shared.escapeHoldSeconds));
        // 到零只请求原准备入口；不结算认知，也不改变主流程。
        if (escapeHoldProgress >= 1f)
        {
            SceneChangeMgr.Instance.StartEscape();
            escapeHoldProgress = 0f;
            escapeHoldNeedsRelease = true;
        }
    }

    /// <summary>换关揭开遮罩前调用：镜头直接到当前控制角色，不从上一关缓动。</summary>
    public void SnapCameraToPlayer()
    {
        if (mainCamera == null || player == null) return;
        mainCamera.SnapTo(player.transform.position);
    }
}
