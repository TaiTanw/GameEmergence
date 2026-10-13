#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>仅编辑器批量检查使用：按连续输入记录真实固定步与渲染帧，不另开物理模拟。</summary>
[DefaultExecutionOrder(32000)]
public sealed class EndingMotionProbe : MonoBehaviour
{
    public EndingActor Actor;
    public string Output;
    public bool Complete;
    public readonly int[] Steps = new int[4], BadSteps = new int[4], Frames = new int[4], BackwardFrames = new int[4];
    public readonly float[] MaxFrame = new float[4], MaxCameraError = new float[4];
    public readonly float[] MaxSpeedError = new float[4];
    readonly List<string> rows = new List<string>();
    Rigidbody2D body;
    RenderTexture render;
    Keyboard keyboard;
    PlayerInput nativeInput;
    Camera gameCamera;
    InputSettings.UpdateMode previousMode;
    UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>? previousDevices;
    bool previousAutoSwitch, previousEditUpdates;
    object inputSystem;
    PropertyInfo editUpdates;
    int phase;
    float elapsed;
    Vector2 previousPhysics;
    float previousDisplay;
    bool started, hooked;

    void Start()
    {
        body = Actor.GetComponent<Rigidbody2D>();
        render = new RenderTexture(320, 180, 24);
        keyboard = InputSystem.AddDevice<Keyboard>();
        nativeInput = InputControlMgr.Instance.GetComponent<PlayerInput>();
        previousDevices = nativeInput.actions.devices; previousAutoSwitch = nativeInput.neverAutoSwitchControlSchemes;
        nativeInput.neverAutoSwitchControlSchemes = true; nativeInput.actions.devices = new InputDevice[] { keyboard };
        previousMode = InputSystem.settings.updateMode; InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        inputSystem = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        editUpdates = inputSystem.GetType().GetProperty("runPlayerUpdatesInEditMode");
        previousEditUpdates = (bool)editUpdates.GetValue(inputSystem); editUpdates.SetValue(inputSystem, true);
        gameCamera = InputControlMgr.Instance.GameCamera;
        MonoPublicMgr.Instance.AddPhysicalTimingUpdate(AfterPhysics, 5);
        Camera.onPreCull += BeforeRender; hooked = true;
        rows.Add("kind,phase,frame,time,dt,input,physics_x,display_x,velocity_x,grounded,camera_x");
    }
    void LateUpdate()
    {
        // batch editor 不自动绘制 Game View；显式渲染同一只游戏镜头，仍由原 PlayerLoop 插值。
        if (!started || Complete) return;
        if (gameCamera == null) return;
        var previous = gameCamera.targetTexture;
        try { gameCamera.targetTexture = render; gameCamera.Render(); }
        finally { gameCamera.targetTexture = previous; }
    }
    void Update()
    {
        if (Complete || !Actor.ControlEnabled) return;
        if (!started) { started = true; phase = 1; elapsed = 0; previousPhysics = body.position; previousDisplay = Actor.transform.position.x; SendDirection(); }
        elapsed += Time.unscaledDeltaTime;
        if ((phase == 1 || phase == 3) && elapsed >= 1.5f || phase == 2 && elapsed >= 6f)
        {
            ++phase; elapsed = 0; previousDisplay = Actor.transform.position.x;
            SendDirection();
            if (phase == 4) { Complete = true; Flush(); }
        }
    }
    void SendDirection()
    {
        // 只发送一次按下／释放，经过正式动作图；不每帧覆写 inputData 来掩盖中途丢输入。
        InputSystem.QueueStateEvent(keyboard, phase == 1 ? new KeyboardState(Key.D)
            : phase == 3 ? new KeyboardState(Key.A) : new KeyboardState());
        InputSystem.Update(); InputSystem.Update();
    }
    void AfterPhysics()
    {
        if (!started || Complete) return;
        float dx = body.position.x - previousPhysics.x; previousPhysics = body.position;
        Record("fixed", Time.fixedDeltaTime, 0);
        if ((phase != 1 && phase != 3) || elapsed < .1f) return;
        ++Steps[phase];
        float expected = (phase == 1 ? 1 : -1) * 5f * Time.fixedDeltaTime;
        if (Mathf.Abs(dx - expected) > .002f) ++BadSteps[phase];
    }
    void BeforeRender(Camera camera)
    {
        if (!started || Complete || !camera.enabled || camera.GetComponent<MainCamera>() == null) return;
        float x = Actor.transform.position.x, direction = phase == 3 ? -1 : 1;
        Record("render", Time.unscaledDeltaTime, camera.transform.position.x);
        if ((phase == 1 || phase == 3) && elapsed >= .1f)
        {
            ++Frames[phase];
            if ((x - previousDisplay) * direction < -.001f) ++BackwardFrames[phase];
            MaxFrame[phase] = Mathf.Max(MaxFrame[phase], Time.unscaledDeltaTime);
            MaxCameraError[phase] = Mathf.Max(MaxCameraError[phase], Mathf.Abs(x - camera.transform.position.x));
            MaxSpeedError[phase] = Mathf.Max(MaxSpeedError[phase], Mathf.Abs((x - previousDisplay) * direction - 5f * Time.unscaledDeltaTime));
        }
        previousDisplay = x;
    }
    void Record(string kind, float dt, float cameraX)
    {
        rows.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F6},{4:F6},{5:F3},{6:F6},{7:F6},{8:F6},{9},{10:F6}",
            kind, phase, Time.frameCount, Time.unscaledTime, dt, InputControlMgr.Instance.PlayerInputData.moveInput,
            body.position.x, Actor.transform.position.x, body.velocity.x, Actor.IsGrounded, cameraX));
    }
    public void Flush() { if (!string.IsNullOrEmpty(Output)) File.WriteAllLines(Output, rows); }
    void OnDestroy()
    {
        if (hooked) { if (!MonoPublicMgr.IsQuitting) MonoPublicMgr.Instance.RemovePhysicalTimingUpdate(AfterPhysics, 5); Camera.onPreCull -= BeforeRender; }
        Flush();
        if (render != null) { render.Release(); Destroy(render); }
        if (nativeInput != null) { nativeInput.actions.devices = previousDevices; nativeInput.neverAutoSwitchControlSchemes = previousAutoSwitch; }
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (hooked) { InputSystem.settings.updateMode = previousMode; editUpdates.SetValue(inputSystem, previousEditUpdates); }
    }
}
#endif
