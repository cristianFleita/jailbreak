using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class InputSystemKey
{
    public static bool WasPressedThisFrame(KeyCode code)
    {
        if (TryMouseButton(code, out var mouseButton))
            return mouseButton?.wasPressedThisFrame == true;

        var key = ToKey(code);
        var keyboard = Keyboard.current;
        return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    public static bool IsPressed(KeyCode code)
    {
        if (TryMouseButton(code, out var mouseButton))
            return mouseButton?.isPressed == true;

        var key = ToKey(code);
        var keyboard = Keyboard.current;
        return keyboard != null && key != Key.None && keyboard[key].isPressed;
    }

    private static bool TryMouseButton(KeyCode code, out ButtonControl button)
    {
        button = null;
        var mouse = Mouse.current;

        switch (code)
        {
            case KeyCode.Mouse0: button = mouse?.leftButton; return true;
            case KeyCode.Mouse1: button = mouse?.rightButton; return true;
            case KeyCode.Mouse2: button = mouse?.middleButton; return true;
            case KeyCode.Mouse3: button = mouse?.backButton; return true;
            case KeyCode.Mouse4: button = mouse?.forwardButton; return true;
            default: return false;
        }
    }

    private static Key ToKey(KeyCode code)
    {
        switch (code)
        {
            case KeyCode.None: return Key.None;
            case KeyCode.Backspace: return Key.Backspace;
            case KeyCode.Tab: return Key.Tab;
            case KeyCode.Return: return Key.Enter;
            case KeyCode.Escape: return Key.Escape;
            case KeyCode.Space: return Key.Space;
            case KeyCode.LeftArrow: return Key.LeftArrow;
            case KeyCode.RightArrow: return Key.RightArrow;
            case KeyCode.UpArrow: return Key.UpArrow;
            case KeyCode.DownArrow: return Key.DownArrow;
            case KeyCode.LeftShift: return Key.LeftShift;
            case KeyCode.RightShift: return Key.RightShift;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftAlt: return Key.LeftAlt;
            case KeyCode.RightAlt: return Key.RightAlt;
        }

        return Enum.TryParse(code.ToString(), out Key parsed) ? parsed : Key.None;
    }
}
