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
            CheckCognitionDurations(panel, input, keyboard, passed);
            Send(keyboard, Key.Enter); Tick(input, .5f); Refresh(panel);
            Check(input.EscapeHoldProgress > .2f && !SceneChangeMgr.Instance.EscapeSelectionPending
                && panel.GetComponentInChildren<CognitionBarGraphic>().ConfirmationFraction < .8f,
                "真实回车短按缩短黑色刻度，不提前进入准备", passed);
            GameMenuInputChecks.Capture("p27-integrated-hold.png");
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
            Refresh(panel);
            Check(input.EscapeHoldProgress == 0f && !SceneChangeMgr.Instance.EscapeSelectionPending,
                "回车与交互重绑冲突时禁用结束长按", passed);
            Check(panel.FindUIObj<Text>("RefreshMessageText").text.Contains("主菜单")
                && panel.GetComponentInChildren<CognitionBarGraphic>().ConfirmationFraction == 0f,
                "移除便签后键位冲突仍显示改键指引，并隐藏可确认刻度", passed);
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
            GameObject typing = new GameObject("P27TypingFixture", typeof(RectTransform), typeof(InputField));
            var events = UnityEngine.EventSystems.EventSystem.current;
            GameObject previousSelection = events.currentSelectedGameObject;
            try
            {
                events.SetSelectedGameObject(typing);
                Send(keyboard, Key.Enter); Tick(input, 5f);
                Check(input.EscapeHoldProgress == 0f && !SceneChangeMgr.Instance.EscapeSelectionPending,
                    "文本输入期间回车不贡献结束确认", passed);
                events.SetSelectedGameObject(previousSelection); Tick(input, 5f);
                Check(input.EscapeHoldProgress == 0f && !SceneChangeMgr.Instance.EscapeSelectionPending,
                    "离开文本输入后仍按回车须先松手", passed);
            }
            finally { events.SetSelectedGameObject(previousSelection); UnityEngine.Object.DestroyImmediate(typing); }
            Send(keyboard); Tick(input, 0);
            float balance = LevelStageMgr.Instance.CognitionRemaining;
            float seconds = Mathf.Max(.2f, 2f * balance / SceneLevelSetup.Current.InitialCognition);
            Send(keyboard, Key.NumpadEnter); Tick(input, seconds - .01f);
            Check(!SceneChangeMgr.Instance.EscapeSelectionPending && input.EscapeHoldProgress > .9f,
                "数字回车不足当前余额目标时长仍留在探索: seconds=" + seconds, passed);
            Tick(input, .02f);
            Check(SceneChangeMgr.Instance.EscapeSelectionPending && input.Player == null,
                "数字回车达到余额目标时长通过原入口进入准备并撤销控制: progress=" + input.EscapeHoldProgress
                    + ", canEnd=" + SceneChangeMgr.Instance.CanStartEscape + ", enabled=" + input.InputEnabled, passed);
            Check(LevelStageMgr.Instance.CognitionRemaining == balance, "完成长按不扣实际认知", passed);
            int version = SceneChangeMgr.Instance.EscapePreparationVersion;
            Tick(input, 5f);
            Check(input.EscapeHoldProgress == 0f && SceneChangeMgr.Instance.EscapePreparationVersion == version,
                "进入准备后白条计时清零，持续按住不重复请求", passed);
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
    static void CheckCognitionDurations(GameplayPanel panel, InputControlMgr input, Keyboard keyboard, List<string> passed)
    {
        // 边界夹具仅在隔离测试场景暂设余额，退出后恢复同一记录；不增加正式写入口。
        var rules = (PlayerLifecycleRules)typeof(SceneChangeMgr).GetField("levelRules", Hidden).GetValue(SceneChangeMgr.Instance);
        LevelCognition cognition = rules.Cognition;
        PropertyInfo remaining = typeof(LevelCognition).GetProperty("Remaining");
        float saved = cognition.Remaining, capacity = SceneLevelSetup.Current.InitialCognition;
        try
        {
            float[] fractions = { 1f, .5f, .1f, .01f };
            float[] seconds = { 2f, 1f, .2f, .2f };
            for (int i = 0; i < fractions.Length; i++)
            {
                float balance = capacity * fractions[i];
                remaining.SetValue(cognition, balance);
                Send(keyboard, Key.Enter); Tick(input, seconds[i] - .01f); Refresh(panel);
                Check(!SceneChangeMgr.Instance.EscapeSelectionPending && Mathf.Abs(input.EscapeHoldProgress - (1f - .01f / seconds[i])) < .001f,
                    "余额比例 " + fractions[i] + " 的目标确认时长为 " + seconds[i] + " 秒", passed);
                CognitionBarGraphic bar = panel.GetComponentInChildren<CognitionBarGraphic>();
                Check(Mathf.Abs(bar.CognitionFraction - fractions[i]) < .001f
                    && bar.ConfirmationFraction <= bar.CognitionFraction && !bar.raycastTarget,
                    "余额比例 " + fractions[i] + " 的刻度位于真实认知条内部且不抢占鼠标", passed);
                Check(cognition.Remaining == balance, "长按中余额不变：" + fractions[i], passed);
                Send(keyboard); Tick(input, .2f);
                Check(input.EscapeHoldProgress == 0f && cognition.Remaining == balance,
                    "松手回填不返还或扣除认知：" + fractions[i], passed);
            }
            remaining.SetValue(cognition, capacity);
            Send(keyboard, Key.Enter); Tick(input, .5f);
            remaining.SetValue(cognition, capacity * .5f);
            Tick(input, .5f); Refresh(panel);
            Check(Mathf.Abs(input.EscapeHoldProgress - .5f) < .001f,
                "同次长按中认知降低，仍沿按下时锁定的两秒目标推进", passed);
            CognitionBarGraphic current = panel.GetComponentInChildren<CognitionBarGraphic>();
            Check(Mathf.Abs(current.CognitionFraction - .5f) < .001f && Mathf.Abs(current.ConfirmationFraction - .25f) < .001f,
                "锁定确认时长时黑色刻度仍随实时认知缩短", passed);
            GameMenuInputChecks.Capture("p27-half-cognition-hold.png");
            Send(keyboard); Tick(input, .2f);
            Send(keyboard, Key.Enter); Tick(input, .5f);
            Check(Mathf.Abs(input.EscapeHoldProgress - .5f) < .001f,
                "重新按下按新的半条余额计算一秒目标", passed);
            Send(keyboard); Tick(input, .2f);
        }
        finally { remaining.SetValue(cognition, saved); }
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
