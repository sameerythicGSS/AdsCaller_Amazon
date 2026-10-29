using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>Fullscreen shortcuts with optional Input System support and no package dependency.</summary>
public static class AdsUiInput
{
#if ENABLE_INPUT_SYSTEM
    private const BindingFlags StaticProperties = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly Type KeyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
    private static readonly Type GamepadType = Type.GetType("UnityEngine.InputSystem.Gamepad, Unity.InputSystem");
    private static readonly PropertyInfo KeyboardCurrent = KeyboardType?.GetProperty("current", StaticProperties);
    private static readonly PropertyInfo Gamepads = GamepadType?.GetProperty("all", StaticProperties);

    private static bool Pressed(object device, string name)
    {
        object control = device?.GetType().GetProperty(name)?.GetValue(device, null);
        return control?.GetType().GetProperty("wasPressedThisFrame")?.GetValue(control, null) is bool pressed && pressed;
    }
    private static bool NewInputPressed(bool confirm)
    {
        object keyboard = KeyboardCurrent?.GetValue(null, null);
        if (Pressed(keyboard, confirm ? "enterKey" : "escapeKey") || confirm && Pressed(keyboard, "numpadEnterKey")) return true;
        if (Gamepads?.GetValue(null, null) is IEnumerable pads)
            foreach (object pad in pads) if (Pressed(pad, confirm ? "buttonSouth" : "buttonEast")) return true;
        return false;
    }
#endif
    public static bool WasConfirmPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (NewInputPressed(true)) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetButtonDown("Submit");
#else
        return false;
#endif
    }
    public static bool WasCancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (NewInputPressed(false)) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetButtonDown("Cancel");
#else
        return false;
#endif
    }
}
