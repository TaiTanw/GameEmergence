#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>关卡流程中的真实 ESC 输入和暂停恢复检查；复用形态检查的隔离键盘。</summary>
public static class GameMenuInputChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(List<string> passed, Keyboard keyboard, InputControlMgr input, Player spirit, PlayerLifecycleRules rules)
    {
        float timeScale = Time.timeScale;
        float cognition = rules.Cognition.Remaining;
        bool simulation = rules.IsSimulation;
        Time.timeScale = .6f;
        try
        {
            Send(keyboard, input, Key.LeftShift);
            Check(input.PlayerInputData.sprintHeld, "菜单前真实疾跑键处于按住状态", passed);
            Send(keyboard, input, Key.Escape, Key.LeftShift);
            Check(SceneChangeMgr.Instance.GameMenuOpen && !input.InputEnabled && Time.timeScale == 0f,
                "固定 ESC 打开菜单并暂停游戏时间与动作图", passed);
            Check(WorldPaused(typeof(BasicEntity)) && WorldPaused(typeof(Taijie)), "菜单暂停实体和平台既有世界入口", passed);
            Check(!input.PlayerInputData.sprintReleased, "暂停动作图不伪造疾跑松键", passed);
            Send(keyboard, input, Key.Q);
            Check(!input.PlayerInputData.sprintHeld && input.PlayerInputData.sprintReleased,
                "菜单中真实松开疾跑仍保留松键事实", passed);
            Check(rules.IsSimulation == simulation && rules.Cognition.Remaining == cognition && input.Player == spirit,
                "菜单中形态键无效，不改变认知、模式和控制对象", passed);
            Send(keyboard, input, Key.Escape);
            Check(!SceneChangeMgr.Instance.GameMenuOpen && input.InputEnabled && Time.timeScale == .6f,
                "动作图关闭时 ESC 仍可恢复原时间与输入", passed);
            Check(WorldPaused(typeof(BasicEntity)) == !simulation && WorldPaused(typeof(Taijie)) == !simulation,
                "关闭菜单按原漫游或模拟模式恢复世界许可", passed);
            Check(!input.PlayerInputData.jumpPressed && !input.PlayerInputData.jumpRelease,
                "暂停恢复没有补发跳跃边沿", passed);
            Send(keyboard, input);
        }
        finally
        {
            if (SceneChangeMgr.Instance.GameMenuOpen) SceneChangeMgr.Instance.ToggleGameMenu();
            Time.timeScale = timeScale;
        }
    }

    public static void Send(Keyboard keyboard, InputControlMgr input, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        typeof(InputControlMgr).GetMethod("Update", Hidden).Invoke(input, null);
        InputSystem.Update();
    }

    static bool WorldPaused(Type type) => (bool)type.GetField("worldPaused", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    static void Check(bool condition, string message, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed.Add("PASS " + message);
    }

    public static void Capture(string name)
    {
        Camera camera = (Camera)typeof(UIMgr).GetField("uiCamera", Hidden).GetValue(UIMgr.Instance);
        RenderTexture previous = camera.targetTexture;
        RenderTexture active = RenderTexture.active;
        RenderTexture render = new RenderTexture(1920, 1080, 24);
        Texture2D texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        Camera game = InputControlMgr.Instance.GameCamera;
        RenderTexture gameTarget = game != null ? game.targetTexture : null;
        try
        {
            camera.targetTexture = render;
            if (game != null && game.isActiveAndEnabled) { game.targetTexture = render; game.Render(); }
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            Directory.CreateDirectory(".utmp/GameUIValidation");
            File.WriteAllBytes(".utmp/GameUIValidation/" + name, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous; RenderTexture.active = active;
            if (game != null) game.targetTexture = gameTarget;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(render);
        }
    }
}
#endif
