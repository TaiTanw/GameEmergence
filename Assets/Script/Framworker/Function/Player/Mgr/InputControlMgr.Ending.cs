using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public partial class InputControlMgr
{
    EndingActor endingActor;
    bool endingJumpNeedsRelease;
    internal void BindEndingActor(EndingActor actor)
    {
        if (actor != null && !actor.isActiveAndEnabled) return;
        if (endingActor == actor && player == null) return;
        BindPlayer(null);
        endingActor = actor;
        ClearEndingInputEdges();
        SyncHeldInputs();
        ResetEndingJumpGate();
    }
    void ClearEndingInputEdges()
    {
        AbortInteractHold();
        if (inputData == null) return;
        inputData.jumpPressed = inputData.jumpRelease = inputData.sprintReleased = false;
        inputData.interactTapped = inputData.interactLongPressed = false;
    }
    void DeliverEndingInput()
    {
        if (!SceneChangeMgr.Instance.IsEnding || inputData == null) return;
        bool held = EndingJumpHeld();
        if (!held) endingJumpNeedsRelease = false;
        if (endingActor != null && !endingActor.ControlEnabled) endingJumpNeedsRelease = held;
        if (endingActor != null)
            endingActor.ReceiveInput(InputEnabled ? new EndingInputFrame(inputData.moveInput, inputData.jumpPressed && !endingJumpNeedsRelease) : default);
        // 无受控角色也消费边沿，之后生成／切换角色不能补发之前的点按。
        ClearEndingInputEdges();
    }
    internal void FollowEndingCamera(Transform target) { EnsureEndingCamera(); mainCamera.SetEndingTarget(target); }
    internal void PositionEndingCamera(Vector3 position) { EnsureEndingCamera(); mainCamera.SetEndingPosition(position); }
    internal void EndEndingView() { if (mainCamera != null) mainCamera.EndEndingView(); }
    internal void ReleaseMainCamera(MainCamera camera) { if (mainCamera == camera) mainCamera = null; }
    void ResetEndingJumpGate() { endingJumpNeedsRelease = EndingJumpHeld(); ClearEndingInputEdges(); }
    bool EndingJumpHeld()
    {
        if (jumpAction == null) return false;
        foreach (var control in jumpAction.controls) if (control.IsPressed()) return true;
        return false;
    }
    void EnsureEndingCamera()
    {
        if (mainCamera != null) return;
        // 冷启动直接重播也有游戏镜头；优先采用本场景镜头，不要求制作脚本理解常驻服务。
        foreach (Camera camera in Camera.allCameras)
            if (camera.gameObject.scene == SceneManager.GetActiveScene() && camera.gameObject.layer != 5 && camera.targetTexture == null)
            { mainCamera = camera.gameObject.AddComponent<MainCamera>(); break; }
        if (mainCamera != null) return;
        var item = new GameObject("EndingGameCamera");
        Camera created = item.AddComponent<Camera>();
        created.orthographic = true; created.orthographicSize = 5f;
        created.backgroundColor = Color.black; created.clearFlags = CameraClearFlags.SolidColor;
        item.AddComponent<AudioListener>();
        mainCamera = item.AddComponent<MainCamera>();
    }
}
