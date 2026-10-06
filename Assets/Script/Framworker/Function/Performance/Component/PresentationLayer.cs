using PhyData;
using UnityEngine;

/// <summary>
/// 角色表现层。
/// 数据只从逻辑层和物理层流入：逻辑层决定行为状态与移动意图，物理层提供已经结算的几何和速度快照；
/// 本组件只选择 Animator 状态和精灵朝向，不向逻辑层或物理层回写任何数据。
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
public class PresentationLayer : MonoBehaviour
{
    private const int BaseLayerIndex = 0;
    private const float MoveInputDeadZone = 0.01f;
    private const float VerticalSpeedDeadZone = 0.01f;
    private const float JumpImpulseEpsilon = 0.01f;

    // Animator Controller 当前没有连线，所有状态都由这里使用完整路径直接播放。
    // “On” 是现有 Controller 中 idle 动画对应的状态名；“OnAir” 对应上升循环动画。
    private static readonly int IdleStateHash = Animator.StringToHash("Base Layer.On");
    private static readonly int MoveStateHash = Animator.StringToHash("Base Layer.move");
    private static readonly int JumpTriggerStateHash = Animator.StringToHash("Base Layer.Jump_Up");
    private static readonly int AirUpStateHash = Animator.StringToHash("Base Layer.OnAir");
    private static readonly int LandingTriggerStateHash = Animator.StringToHash("Base Layer.Jump_Down");
    private static readonly int AirDownStateHash = Animator.StringToHash("Base Layer.OnAir_Down");
    private static readonly int WallStateHash = Animator.StringToHash("Base Layer.Wall");
    private static readonly int SwimStateHash = Animator.StringToHash("Base Layer.Swim");
    private static readonly int FlightStateHash = Animator.StringToHash("Base Layer.Flight");
    private static readonly int SurfaceSwimStateHash = Animator.StringToHash("Base Layer.SurfaceSwim");

    private enum MotionMode
    {
        Ground,
        Air,
        Wall,
        Swim,
        Flight,
        SurfaceSwim
    }

    private enum AirPhase
    {
        None,
        Rising,
        Falling
    }

    private enum AnimationState
    {
        None,
        Idle,
        Move,
        JumpTrigger,
        AirUp,
        LandingTrigger,
        AirDown,
        Wall,
        Swim,
        Flight,
        SurfaceSwim
    }

    /// <summary>
    /// 触发动作只在事件边沿开始，并在有限时间后退出；它们不能被当成持续运动状态反复维持。
    /// </summary>
    private enum TriggerAction
    {
        None,
        JumpStart,
        Landing
    }

    /// <summary>
    /// Animator 只负责采样已经关联好的切片，不保存玩法状态。
    /// </summary>
    private Animator animator;

    /// <summary>
    /// SpriteRenderer 只消费逻辑层的水平移动意图来保持原有左右翻转行为。
    /// </summary>
    private SpriteRenderer spriteRenderer;

    /// <summary>
    /// 物理层速度只读快照。主动竖直速度用于识别新的跳跃冲量，主动与被动速度之和用于判断升降阶段。
    /// </summary>
    private ReadOnly_PlayerPhysicsData playerPhysicsData;

    /// <summary>
    /// 物理层几何只读快照。着地事实优先于可能晚一帧切换的逻辑状态，用于及时收束到地面动画。
    /// </summary>
    private ReadOnly_GeometryPhysicsData geometryPhysicsData;

    /// <summary>
    /// 逻辑层只读输出。提供移动意图与 FSM 行为状态，不允许表现层修改。
    /// </summary>
    private ReadOnly_ActionData actionData;

    private AnimationState currentAnimation = AnimationState.None;
    private TriggerAction activeTrigger = TriggerAction.None;
    private MotionMode previousMotionMode = MotionMode.Ground;
    private AirPhase airPhase = AirPhase.None;
    private float previousActiveVerticalSpeed;
    private bool hasVerticalSpeedSample;

    // 墙跳事件先由 Update 写入逻辑层、再由下一个 FixedUpdate 写入物理速度。
    // 离墙后的短暂等待避免在墙跳速度尚未结算时错误闪过一次下落起手动画。
    private bool waitingForPostWallPhysics;
    private float postWallActiveVerticalSpeed;

    private bool isInitialized;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 由 Player 在 Start 中注入三份只读数据。此后数据始终按
    /// FSM/Physics 写入 → 只读包装公开 → PresentationLayer 消费 的单向路径流动。
    /// </summary>
    public void Init(
        ReadOnly_PlayerPhysicsData physicsData,
        ReadOnly_GeometryPhysicsData geometryData,
        ReadOnly_ActionData readOnlyActionData)
    {
        playerPhysicsData = physicsData;
        geometryPhysicsData = geometryData;
        actionData = readOnlyActionData;

        if (playerPhysicsData == null || geometryPhysicsData == null || actionData == null)
        {
            Debug.LogError("PresentationLayer 初始化失败：逻辑层或物理层只读数据未注入。", this);
            enabled = false;
            return;
        }

        if (!HasAllAnimationStates())
        {
            enabled = false;
            return;
        }

        previousActiveVerticalSpeed = playerPhysicsData.verticalSpeed;
        hasVerticalSpeedSample = true;
        previousMotionMode = ResolveMotionMode();
        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateFacing();

        MotionMode behavior = ResolveMotionMode();
        // 行为事实优先：上浮不是跳跃，水底几何着地也不能选择行走动画。
        if (behavior == MotionMode.Swim || behavior == MotionMode.Flight || behavior == MotionMode.SurfaceSwim)
        {
            ResetMotionHistory();
            Play(behavior == MotionMode.Flight ? AnimationState.Flight
                : behavior == MotionMode.SurfaceSwim ? AnimationState.SurfaceSwim : AnimationState.Swim);
            return;
        }

        // 物理层将环境竖直影响单独保存在 phyVSpeed；升降表现必须使用两者之和，
        // 否则移动平台或力场造成的实际升降会与画面方向不一致。
        float activeVerticalSpeed = playerPhysicsData.verticalSpeed;
        float totalVerticalSpeed = activeVerticalSpeed + playerPhysicsData.phyVSpeed;
        // 着地会把负的主动竖直速度归零，同样表现为数值上升，但它不是跳跃。
        // 因此只有“主动速度离散增大且结果为正”才是 CharacterPhysics 消费 jump/wallJump 后的触发边沿。
        bool receivedJumpImpulse = hasVerticalSpeedSample
            && activeVerticalSpeed > VerticalSpeedDeadZone
            && activeVerticalSpeed > previousActiveVerticalSpeed + JumpImpulseEpsilon;

        MotionMode motionMode = ResolveMotionMode();
        bool justLanded = motionMode == MotionMode.Ground
            && (previousMotionMode == MotionMode.Air || previousMotionMode == MotionMode.Wall);

        // 同一帧既着地又因跳跃缓冲重新起跳时，新的跳跃触发优先于落地触发。
        if (receivedJumpImpulse)
        {
            BeginTriggerAction(TriggerAction.JumpStart);
        }
        else if (justLanded)
        {
            BeginTriggerAction(TriggerAction.Landing);
        }

        // 触发动作拥有当前帧时，不再运行持续状态选择；触发完成或被物理状态打断后，立即回到持续状态。
        if (!UpdateTriggerAction(motionMode, totalVerticalSpeed))
        {
            switch (motionMode)
            {
                case MotionMode.Ground:
                    UpdateGroundAnimation();
                    break;
                case MotionMode.Wall:
                    UpdateWallAnimation();
                    break;
                case MotionMode.Air:
                    UpdateSustainedAirAnimation(totalVerticalSpeed, activeVerticalSpeed);
                    break;
            }
        }

        previousMotionMode = motionMode;
        previousActiveVerticalSpeed = activeVerticalSpeed;
        hasVerticalSpeedSample = true;
    }

    /// <summary>
    /// 保留既有翻转规则：角色朝向只跟随逻辑层已经过滤后的移动意图，零输入时保持最后朝向。
    /// </summary>
    private void UpdateFacing()
    {
        if (actionData.onMove > MoveInputDeadZone)
        {
            spriteRenderer.flipX = false;
        }
        else if (actionData.onMove < -MoveInputDeadZone)
        {
            spriteRenderer.flipX = true;
        }
    }

    /// <summary>
    /// 几何着地是物理事实，优先用于结束空中动画；逻辑地面状态同时覆盖首个物理快照尚未生成的初始化时段。
    /// 贴墙由逻辑 FSM 判断，因为它还包含“持续朝墙输入”的行为条件；其余情况统一视为空中。
    /// </summary>
    private MotionMode ResolveMotionMode()
    {
        if (actionData.isFlying) return MotionMode.Flight;
        if (actionData.isSurfaceSwimming) return MotionMode.SurfaceSwim;
        if (actionData.isSwimming) return MotionMode.Swim;
        // 水面跃出的首帧可能仍报告浅水池底，表现先按逻辑进入空中。
        if (actionData.surfaceJumpInProgress) return MotionMode.Air;
        if (geometryPhysicsData.isGrounded
            || actionData.NowState == PlayerStateMachine.E_playerState.isOnGround)
        {
            return MotionMode.Ground;
        }

        if (actionData.NowState == PlayerStateMachine.E_playerState.onWallSliding)
        {
            return MotionMode.Wall;
        }

        return MotionMode.Air;
    }

    public void ResetMotionHistory()
    {
        activeTrigger = TriggerAction.None;
        ResetAirState();
        hasVerticalSpeedSample = false;
        previousMotionMode = ResolveMotionMode();
        previousActiveVerticalSpeed = playerPhysicsData.verticalSpeed;
    }

    private void UpdateGroundAnimation()
    {
        ResetAirState();

        // 地面行走表达的是玩家行为意图；环境带动但没有输入时仍保持待机，避免把平台位移误画成主动行走。
        AnimationState desiredState = Mathf.Abs(actionData.onMove) > MoveInputDeadZone
            ? AnimationState.Move
            : AnimationState.Idle;
        Play(desiredState);
    }

    private void UpdateWallAnimation()
    {
        ResetAirState();
        Play(AnimationState.Wall);
    }

    /// <summary>
    /// 启动一次瞬时表现动作。Jump_Up 表达跳跃触发，Jump_Down 表达落地触发；二者都不是空中持续状态。
    /// </summary>
    private void BeginTriggerAction(TriggerAction trigger)
    {
        activeTrigger = trigger;
        waitingForPostWallPhysics = false;

        switch (trigger)
        {
            case TriggerAction.JumpStart:
                airPhase = AirPhase.Rising;
                Play(AnimationState.JumpTrigger, true);
                break;
            case TriggerAction.Landing:
                ResetAirState();
                Play(AnimationState.LandingTrigger, true);
                break;
        }
    }

    /// <summary>
    /// 返回 true 表示本帧仍由触发动作控制 Animator；返回 false 后由地面/空中/贴墙持续状态接管。
    /// </summary>
    private bool UpdateTriggerAction(MotionMode motionMode, float totalVerticalSpeed)
    {
        switch (activeTrigger)
        {
            case TriggerAction.JumpStart:
                // 起跳动作可以覆盖物理仍报告着地的起跳首帧；开始下降或进入贴墙姿态后，
                // 立即让位给对应持续状态，避免较长的起跳切片遮住持续下落/贴墙表现。
                if (motionMode == MotionMode.Wall
                    || (motionMode == MotionMode.Air && totalVerticalSpeed < -VerticalSpeedDeadZone))
                {
                    activeTrigger = TriggerAction.None;
                    return false;
                }

                if (!IsCurrentAnimationFinished())
                {
                    return true;
                }

                activeTrigger = TriggerAction.None;
                return false;

            case TriggerAction.Landing:
                // 落地收势只在“着地且没有主动移动”时成立。离地、贴墙或出现移动意图后，
                // 立即交回对应持续状态，避免角色已经移动但画面仍保持落地姿势。
                if (motionMode != MotionMode.Ground
                    || Mathf.Abs(actionData.onMove) > MoveInputDeadZone)
                {
                    activeTrigger = TriggerAction.None;
                    return false;
                }

                if (!IsCurrentAnimationFinished())
                {
                    return true;
                }

                activeTrigger = TriggerAction.None;
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// 空中持续状态只由实际总竖直速度决定。上升使用 OnAir，下降使用 OnAir_Down；
    /// 这里不再播放 Jump_Down，避免落地触发动画占据整个下降阶段。
    /// </summary>
    private void UpdateSustainedAirAnimation(float totalVerticalSpeed, float activeVerticalSpeed)
    {
        bool justLeftWall = previousMotionMode == MotionMode.Wall;
        if (justLeftWall)
        {
            waitingForPostWallPhysics = true;
            postWallActiveVerticalSpeed = previousActiveVerticalSpeed;
            airPhase = AirPhase.None;
        }

        if (waitingForPostWallPhysics)
        {
            bool physicsSpeedChanged = Mathf.Abs(activeVerticalSpeed - postWallActiveVerticalSpeed)
                > JumpImpulseEpsilon;

            if (!physicsSpeedChanged)
            {
                // 墙跳/离墙的逻辑已经发生，但物理帧还未结算；继续显示墙面切片，不猜测下一动画。
                return;
            }

            waitingForPostWallPhysics = false;
        }

        AirPhase observedPhase = ResolveAirPhase(totalVerticalSpeed);
        airPhase = observedPhase;
        Play(airPhase == AirPhase.Rising ? AnimationState.AirUp : AnimationState.AirDown);
    }

    private AirPhase ResolveAirPhase(float totalVerticalSpeed)
    {
        if (totalVerticalSpeed > VerticalSpeedDeadZone)
        {
            return AirPhase.Rising;
        }

        if (totalVerticalSpeed < -VerticalSpeedDeadZone)
        {
            return AirPhase.Falling;
        }

        // 顶点附近保留上一阶段，防止速度在零点附近轻微波动时反复重播上下落起手。
        return airPhase == AirPhase.None ? AirPhase.Falling : airPhase;
    }

    private void ResetAirState()
    {
        airPhase = AirPhase.None;
        waitingForPostWallPhysics = false;
    }

    private void Play(AnimationState state, bool restart = false)
    {
        if (!restart && currentAnimation == state)
        {
            return;
        }

        animator.Play(GetStateHash(state), BaseLayerIndex, 0f);
        currentAnimation = state;
    }

    private bool IsCurrentAnimationFinished()
    {
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(BaseLayerIndex);
        return stateInfo.fullPathHash == GetStateHash(currentAnimation) && stateInfo.normalizedTime >= 1f;
    }

    private static int GetStateHash(AnimationState state)
    {
        switch (state)
        {
            case AnimationState.Idle:
                return IdleStateHash;
            case AnimationState.Move:
                return MoveStateHash;
            case AnimationState.JumpTrigger:
                return JumpTriggerStateHash;
            case AnimationState.AirUp:
                return AirUpStateHash;
            case AnimationState.LandingTrigger:
                return LandingTriggerStateHash;
            case AnimationState.AirDown:
                return AirDownStateHash;
            case AnimationState.Wall:
                return WallStateHash;
            case AnimationState.Swim:
                return SwimStateHash;
            case AnimationState.Flight:
                return FlightStateHash;
            case AnimationState.SurfaceSwim:
                return SurfaceSwimStateHash;
            default:
                return IdleStateHash;
        }
    }

    private bool HasAllAnimationStates()
    {
        int[] requiredStates =
        {
            IdleStateHash,
            MoveStateHash,
            JumpTriggerStateHash,
            AirUpStateHash,
            LandingTriggerStateHash,
            AirDownStateHash,
            WallStateHash,
            SwimStateHash,
            FlightStateHash,
            SurfaceSwimStateHash
        };

        for (int i = 0; i < requiredStates.Length; i++)
        {
            if (!animator.HasState(BaseLayerIndex, requiredStates[i]))
            {
                Debug.LogError(
                    "PresentationLayer 初始化失败：Animator 的 Base Layer 缺少表现层需要的动画状态。",
                    this);
                return false;
            }
        }

        return true;
    }
}
