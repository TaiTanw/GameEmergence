using UnityEngine;

/// <summary>独立演出角色，只响应水平／跳跃；不挂普通 Player、资源、死亡或环境玩法组件。</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class EndingActor : MonoBehaviour
{
    [SerializeField, Min(0f)] float moveSpeed = 5f;
    [SerializeField, Min(0f)] float jumpSpeed = 10f;
    [SerializeField] LayerMask groundLayers;
    [SerializeField, Min(.001f)] float groundProbeDistance = .06f;
    [SerializeField] Animator animator;
    [SerializeField] SpriteRenderer sprite;
    [SerializeField] bool controlEnabled = true;
    [SerializeField] bool automaticMotion = true;
    [SerializeField] bool automaticAnimation = true;
    Rigidbody2D body;
    BoxCollider2D shape;
    MonoPublicMgr clock;
    EndingSceneContext context;
    float horizontal;
    bool pendingJump, grounded;
    int animationHash;
    readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
    public bool ControlEnabled => controlEnabled;
    public bool IsGrounded => grounded;
    public Animator Animator => animator;
    public bool AutomaticMotion => automaticMotion;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>(); shape = GetComponent<BoxCollider2D>();
        if (body.simulated) body.simulated = false;
    }
    void OnEnable()
    {
        clock = MonoPublicMgr.Instance;
        // 只在已有相位写速度，共用相位 4 的唯一物理模拟。
        clock.AddPhysicalTimingUpdate(AdvanceMotion, 3);
        RefreshActivity();
    }
    void OnDisable()
    {
        if (clock != null && !MonoPublicMgr.IsQuitting) clock.RemovePhysicalTimingUpdate(AdvanceMotion, 3);
        ClearInput();
        if (body != null && body.simulated) body.simulated = false;
    }
    internal void Attach(EndingSceneContext value) { context = value; RefreshActivity(); }
    internal void RefreshActivity()
    {
        ClearInput();
        if (body == null) return;
        bool participating = isActiveAndEnabled && automaticMotion
            && context != null && context.IsActive && context.IsRevealed && !context.IsPaused;
        // 同值写入会干扰原生插值；角色登记、操作开关与物理参与分别处理。
        if (body.simulated != participating) body.simulated = participating;
    }
    public void SetControlEnabled(bool enabled)
    { controlEnabled = enabled; ClearInput(); context?.ResetActorInput(this); }
    public void SetAutomaticMotion(bool enabled) { automaticMotion = enabled; RefreshActivity(); }
    public void SetAutomaticAnimation(bool enabled) { automaticAnimation = enabled; animationHash = 0; }
    public void ClearInput() { horizontal = 0f; pendingJump = false; }
    public void ReceiveInput(EndingInputFrame frame)
    {
        if (!controlEnabled || context == null || !context.IsActive || !context.IsRevealed || context.IsPaused)
        { ClearInput(); return; }
        horizontal = Mathf.Clamp(frame.Horizontal, -1f, 1f);
        pendingJump |= frame.JumpPressed;
    }
    void AdvanceMotion()
    {
        if (body == null || !body.simulated || !automaticMotion) return;
        ContactFilter2D filter = new ContactFilter2D(); filter.SetLayerMask(groundLayers); filter.useTriggers = false;
        int count = shape.Cast(Vector2.down, filter, groundHits, groundProbeDistance);
        grounded = false;
        for (int i = 0; i < count; i++)
            if (groundHits[i].collider != shape && groundHits[i].normal.y > .5f) { grounded = true; break; }
        Vector2 velocity = body.velocity;
        velocity.x = controlEnabled ? horizontal * moveSpeed : 0f;
        if (controlEnabled && pendingJump && grounded) velocity.y = jumpSpeed;
        pendingJump = false;
        body.velocity = velocity;
    }
    void LateUpdate()
    {
        if (!automaticAnimation || animator == null || body == null || !body.simulated) return;
        if (sprite != null && Mathf.Abs(horizontal) > .01f) sprite.flipX = horizontal < 0f;
        string state = !grounded ? body.velocity.y > .05f ? "JumpUp" : "JumpDown"
            : Mathf.Abs(body.velocity.x) > .05f ? "Walk" : "Idle";
        int hash = Animator.StringToHash("Base Layer." + state);
        if (animationHash == hash) return;
        animator.Play(hash, 0, 0f); animationHash = hash;
    }
    internal bool ValidateConfiguration(out string error)
    {
        // 场景内可预摆未启用角色，它的 Awake 尚未执行；检查组件而非要求它先启用。
        if (body == null) body = GetComponent<Rigidbody2D>();
        if (shape == null) shape = GetComponent<BoxCollider2D>();
        error = null;
        if (!EndingAxisRange.Finite(moveSpeed) || moveSpeed < 0f || !EndingAxisRange.Finite(jumpSpeed) || jumpSpeed < 0f
            || !EndingAxisRange.Finite(groundProbeDistance) || groundProbeDistance <= 0f || groundLayers.value == 0
            || body == null || body.bodyType != RigidbodyType2D.Dynamic || shape == null || shape.isTrigger)
        { error = name + "：演出角色需要有效运动参数、地面层、Dynamic 刚体及实体碰撞框。"; return false; }
        if (!automaticAnimation) return true;
        if (animator == null || animator.runtimeAnimatorController == null)
        { error = name + "：自动动画需要专属 Animator Controller。"; return false; }
        if (!animator.isActiveAndEnabled) return true;
        foreach (string state in new[] { "Idle", "Walk", "JumpUp", "JumpDown" })
            if (!animator.HasState(0, Animator.StringToHash("Base Layer." + state)))
            { error = name + "：专属动画缺少状态 " + state; return false; }
        return true;
    }
}
