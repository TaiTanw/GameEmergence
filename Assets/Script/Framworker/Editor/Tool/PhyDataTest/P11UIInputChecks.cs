#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>真实关卡中验证长按、松手、暂停、改键与唯一准备入口。</summary>
public static class P11UIInputChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void EndExploration(GameplayPanel panel, List<string> passed)
    {
        InputControlMgr input = InputControlMgr.Instance;
        var map = (InputActionMap)typeof(InputControlMgr).GetField("player1", Hidden).GetValue(input);
        InputAction action = map["Interact"];
        InputBinding original = action.bindings[0];
        InputSettings.UpdateMode mode = InputSystem.settings.updateMode;
        object inputSystem = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo editUpdates = inputSystem.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool previousEditUpdates = (bool)editUpdates.GetValue(inputSystem);
        PlayerInput native = input.GetComponent<PlayerInput>();
        bool previousAutoSwitch = native.neverAutoSwitchControlSchemes;
        var previousDevices = native.actions.devices;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            editUpdates.SetValue(inputSystem, true);
            native.neverAutoSwitchControlSchemes = true;
            native.actions.devices = new InputDevice[] { keyboard };
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Send(keyboard); Tick(input, 0);
            Send(keyboard, Key.Enter); Tick(input, .5f); Refresh(panel);
            Check(input.EscapeHoldProgress > .2f && !SceneChangeMgr.Instance.EscapeSelectionPending
                && panel.FindUIObj<Image>("EndExplorationFill").rectTransform.rect.width < ((RectTransform)panel.transform.Find("CognitionArea")).rect.width * .8f,
                "真实回车短按缩短白条，不提前进入准备", passed);
            GameMenuInputChecks.Capture("p11-centered-hold.png");
            Send(keyboard); Tick(input, .2f);
            Check(input.EscapeHoldProgress == 0f, "未到零时松手，0.2 秒内回满白条", passed);
            Send(keyboard, Key.Enter); Tick(input, .7f);
            SceneChangeMgr.Instance.ToggleGameMenu(); Tick(input, 5f);
            Check(input.EscapeHoldProgress == 0f && !SceneChangeMgr.Instance.EscapeSelectionPending,
                "菜单打开立即取消长按，暂停停留不触发准备", passed);
            SceneChangeMgr.Instance.ToggleGameMenu(); Tick(input, 5f);
            Check(!SceneChangeMgr.Instance.EscapeSelectionPending, "关闭菜单后仍按回车须先松开再按", passed);
            Send(keyboard); Tick(input, 0);
            action.ApplyBindingOverride(0, "<Keyboard>/enter");
            Check(input.HasPlayerBindingConflict(out string message) && message.Contains("回车"),
                "回车绑定到交互时提示专用键冲突", passed);
            Send(keyboard, Key.Enter); Tick(input, 5f);
            Check(input.EscapeHoldProgress == 0f && !SceneChangeMgr.Instance.EscapeSelectionPending,
                "回车与交互重绑冲突时禁用结束长按", passed);
            Restore(action, original); Send(keyboard); Tick(input, 0);
            Check(!input.HasPlayerBindingConflict(out message), "冲突用例恢复原始玩家绑定", passed);
            input.StartRebind("Interact", 0, null);
            Tick(input, 5f);
            Check(input.IsRebinding && input.EscapeHoldProgress == 0f, "改键监听期间不积累结束长按", passed);
            GameMenuInputChecks.Send(keyboard, input, Key.Escape); Tick(input, 5f);
            Send(keyboard); Tick(input, 0);
            Check(!input.IsRebinding && !SceneChangeMgr.Instance.GameMenuOpen,
                "ESC 取消改键不误开菜单，也不触发结束探索", passed);
            Check(input.InputEnabled && !input.HasPlayerBindingConflict(out message),
                "取消改键后动作图恢复且无冲突: " + message, passed);
            Send(keyboard, Key.Enter); Tick(input, 1.9f);
            Check(!SceneChangeMgr.Instance.EscapeSelectionPending && input.EscapeHoldProgress > .9f,
                "长按不足 2 秒仍留在探索: progress=" + input.EscapeHoldProgress, passed);
            Tick(input, .11f);
            Check(SceneChangeMgr.Instance.EscapeSelectionPending && input.Player == null,
                "回车满 2 秒通过原入口进入准备并撤销控制: progress=" + input.EscapeHoldProgress
                    + ", canEnd=" + SceneChangeMgr.Instance.CanStartEscape + ", enabled=" + input.InputEnabled, passed);
            Tick(input, 5f);
            Check(input.EscapeHoldProgress == 0f, "进入准备后白条计时清零，不重复请求", passed);
        }
        finally
        {
            Restore(action, original);
            native.actions.devices = previousDevices;
            native.neverAutoSwitchControlSchemes = previousAutoSwitch;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = mode;
            editUpdates.SetValue(inputSystem, previousEditUpdates);
        }
    }
    static void Restore(InputAction action, InputBinding original)
    { if (original.overridePath == null) action.RemoveBindingOverride(0); else action.ApplyBindingOverride(0, original); }
    static void Send(Keyboard keyboard, params Key[] keys)
    { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); InputSystem.Update(); }
    static void Tick(InputControlMgr input, float elapsed)
    { typeof(InputControlMgr).GetMethod("UpdateEscapeHold", Hidden).Invoke(input, new object[] { elapsed }); }
    static void Refresh(GameplayPanel panel) => typeof(GameplayPanel).GetMethod("LateUpdate", Hidden).Invoke(panel, null);
    static void Check(bool condition, string message, List<string> passed)
    { if (!condition) throw new InvalidOperationException(message); passed.Add("PASS " + message); }
}
#endif
