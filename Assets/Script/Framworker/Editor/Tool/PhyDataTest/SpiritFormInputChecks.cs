#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>由真实关卡联测调用：键盘切换、改键面板与持久化；用户绑定按原始字节恢复。</summary>
public static class SpiritFormInputChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(List<string> passed, Player body, Player spirit, PlayerLifecycleRules rules)
    {
        InputControlMgr manager = InputControlMgr.Instance;
        DataAndInitMgr data = DataAndInitMgr.Instance;
        FieldInfo assetField = typeof(DataAndInitMgr).GetField("assetin", Hidden);
        InputActionAsset original = data.asset;
        InputActionAsset fresh = InputActionAsset.FromJson(Resources.Load<TextAsset>("player").text);
        InputSettings.UpdateMode updateMode = InputSystem.settings.updateMode;
        object inputSystem = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo editUpdates = inputSystem.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool previousEditUpdates = (bool)editUpdates.GetValue(inputSystem);
        PlayerInput native = manager.GetComponent<PlayerInput>();
        bool previousAutoSwitch = native.neverAutoSwitchControlSchemes;
        string bindingPath = Path.Combine(Application.persistentDataPath, "InputBindingOverrides.json");
        byte[] previousBinding = File.Exists(bindingPath) ? File.ReadAllBytes(bindingPath) : null;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        GameObject panelObject = null;
        try
        {
            manager.InputOpenOrClose(false);
            assetField.SetValue(data, fresh);
            editUpdates.SetValue(inputSystem, true);
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            manager.BindInputAsset(); manager.BindInputAsset(); manager.BindInputAsset();
            native.neverAutoSwitchControlSchemes = true;
            native.actions.devices = new InputDevice[] { keyboard };
            manager.InputOpenOrClose(true);
            InputAction form = native.actions.FindAction("FormSwitch");
            int presses = 0;
            form.performed += _ => presses++;
            float balance = rules.Cognition.Remaining;
            float cost = SceneLevelSetup.Current.FormChangeCost;
            Check(form.bindings[0].effectivePath == "<Keyboard>/q", "形态切换默认 Q", passed);
            Send(keyboard, Key.Q);
            Check(rules.IsSimulation && !spirit._ActionData.isFlying && presses == 1,
                "真实 Q 按下经当前角色提交模拟行走，重复绑定没有重复转换", passed);
            Check(Mathf.Approximately(rules.Cognition.Remaining, Mathf.Max(0f, balance - cost)),
                "形态按键成功沿用唯一认知余额扣费一次", passed);
            balance = rules.Cognition.Remaining;
            Check(spirit.FormCooldownRemaining > 0f && body.FormCooldownRemaining == 0f,
                "角色资源出口显示精神体真实 CD，实体没有转换 CD", passed);
            Send(keyboard, Key.Q); Send(keyboard);
            Check(presses == 1 && rules.IsSimulation && rules.Cognition.Remaining == balance,
                "持续按住和释放不会连续转换或扣费", passed);
            Send(keyboard, Key.Q);
            Check(rules.IsSimulation && rules.Cognition.Remaining == balance,
                "冷却内再次按下仍被原规则拒绝且不扣费", passed);
            manager.BindPlayer(body);
            Send(keyboard); Send(keyboard, Key.Q);
            Check(rules.IsSimulation && rules.Cognition.Remaining == balance,
                "控制实体时形态按键无效", passed);
            int beforeResume = presses;
            manager.BindPlayer(spirit);
            manager.InputOpenOrClose(false); manager.InputOpenOrClose(true);
            InputSystem.Update(); InputSystem.Update();
            Check(presses == beforeResume, "控制交接或输入恢复不补发仍按住的形态键", passed);
            Send(keyboard);

            panelObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyObjAssets/UIPanel/InputChangePanel.prefab"));
            InputChangePanel panel = panelObject.GetComponent<InputChangePanel>();
            typeof(InputChangePanel).GetMethod("Start", Hidden).Invoke(panel, null);
            Button button = panel.FindUIObj<Button>("formSwitch");
            Text label = panel.FindUIObj<Text>("formSwitchT");
            Check(button != null && label != null && label.text == "<Keyboard>/q", "真实改键预设显示形态切换行和默认键", passed);
            Transform content = panel.transform.Find("rebindScroll/viewport/rebindContent");
            Check(button.transform.parent.parent == content && content.childCount == 8
                && button.transform.parent.GetSiblingIndex() == 7, "形态行排在滚动列表末尾，原七行完整保留", passed);
            button.onClick.Invoke();
            CompleteRebind(manager, keyboard, Key.E);
            Check(manager.HasPlayerBindingConflict(out string conflict) && conflict.Contains("FormSwitch")
                && !native.currentActionMap.enabled && label.text == "<Keyboard>/e",
                "真实形态改键与交互键重复时提示冲突并暂停动作", passed);
            Send(keyboard);
            button.onClick.Invoke();
            beforeResume = presses;
            CompleteRebind(manager, keyboard, Key.R);
            Check(!manager.HasPlayerBindingConflict(out _) && native.currentActionMap.enabled
                && label.text == "<Keyboard>/r", "改为 R 后真实面板更新显示、解除冲突并恢复输入", passed);
            Check(presses == beforeResume && rules.Cognition.Remaining == balance,
                "改键完成时按住新键不会触发转换或扣费", passed);
            var reload = InputActionAsset.FromJson(Resources.Load<TextAsset>("player").text);
            try
            {
                reload.LoadBindingOverridesFromJson(File.ReadAllText(bindingPath));
                Check(reload.FindAction("FormSwitch").bindings[0].effectivePath == "<Keyboard>/r"
                    && reload.FindAction("Interact").bindings[0].effectivePath == "<Keyboard>/e",
                    "保存的覆盖可重载，新形态键不改交互键", passed);
            }
            finally { Object.DestroyImmediate(reload); }
            Send(keyboard); Send(keyboard, Key.Q);
            Check(presses == beforeResume, "改键后旧 Q 不再产生形态请求", passed);
            Send(keyboard); Send(keyboard, Key.R);
            Check(presses == beforeResume + 1, "改键后新 R 产生一次形态请求", passed);
        }
        finally
        {
            var operation = (InputActionRebindingExtensions.RebindingOperation)typeof(InputControlMgr).GetField("rebindOperation", Hidden).GetValue(manager);
            if (operation != null) { operation.Cancel(); InputSystem.Update(); }
            manager.InputOpenOrClose(false);
            assetField.SetValue(data, original);
            manager.BindInputAsset();
            native.neverAutoSwitchControlSchemes = previousAutoSwitch;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = updateMode;
            editUpdates.SetValue(inputSystem, previousEditUpdates);
            manager.BindPlayer(spirit);
            manager.InputOpenOrClose(true);
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            Object.DestroyImmediate(fresh);
            if (previousBinding != null) File.WriteAllBytes(bindingPath, previousBinding);
            else if (File.Exists(bindingPath)) File.Delete(bindingPath);
            Check(previousBinding == null ? !File.Exists(bindingPath)
                : Convert.ToBase64String(File.ReadAllBytes(bindingPath)) == Convert.ToBase64String(previousBinding),
                "用户改键文件按原始字节或原本不存在的状态恢复", passed);
        }
    }

    private static void Send(Keyboard keyboard, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update(); InputSystem.Update();
    }

    private static void CompleteRebind(InputControlMgr manager, Keyboard keyboard, Key key)
    {
        Send(keyboard, key);
        var operation = (InputActionRebindingExtensions.RebindingOperation)typeof(InputControlMgr).GetField("rebindOperation", Hidden).GetValue(manager);
        if (operation == null || operation.candidates.Count == 0) throw new InvalidOperationException("真实键盘事件没有进入改键候选。");
        // 只跳过候选比较的 0.05 秒等待；按键采集、绑定应用、保存和面板回调仍走真实入口。
        operation.Complete();
        InputSystem.Update(); InputSystem.Update();
    }

    private static void Check(bool condition, string message, List<string> passed)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed.Add("PASS " + message);
    }
}
#endif
