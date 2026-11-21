using System;
using System.Numerics;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Vis2D.Abstractions;
using ImGuiNET;
using NodeEditor.Primitives;
using Raylib_cs;

namespace Fdp.Presentation.Input;

/// <summary>
/// ⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — an <see cref="EditorKey"/> as the Raylib / GLFW key code the map's <c>IInputProvider</c> speaks
/// (<c>MapKeyboardKey</c> carries the same ints). 0 for a chord source that is not a keyboard key (a mouse button or value).
/// </summary>
public static class RaylibKeyCodes
{
    /// <summary>The Raylib key code of <paramref name="key"/>; 0 when it is not a keyboard key.</summary>
    public static int Of(EditorKey key)
    {
        if (key >= EditorKey.A && key <= EditorKey.Z) return (int)KeyboardKey.A + (key - EditorKey.A);
        if (key >= EditorKey.D0 && key <= EditorKey.D9) return (int)KeyboardKey.Zero + (key - EditorKey.D0);
        if (key >= EditorKey.F1 && key <= EditorKey.F12) return (int)KeyboardKey.F1 + (key - EditorKey.F1);
        return key switch
        {
            EditorKey.Tab => (int)KeyboardKey.Tab, EditorKey.Space => (int)KeyboardKey.Space, EditorKey.Enter => (int)KeyboardKey.Enter,
            EditorKey.Escape => (int)KeyboardKey.Escape, EditorKey.Backspace => (int)KeyboardKey.Backspace, EditorKey.Delete => (int)KeyboardKey.Delete,
            EditorKey.Home => (int)KeyboardKey.Home, EditorKey.End => (int)KeyboardKey.End, EditorKey.PageUp => (int)KeyboardKey.PageUp,
            EditorKey.PageDown => (int)KeyboardKey.PageDown, EditorKey.Left => (int)KeyboardKey.Left, EditorKey.Right => (int)KeyboardKey.Right,
            EditorKey.Up => (int)KeyboardKey.Up, EditorKey.Down => (int)KeyboardKey.Down, EditorKey.Insert => (int)KeyboardKey.Insert,
            EditorKey.CapsLock => (int)KeyboardKey.CapsLock, EditorKey.LeftBracket => (int)KeyboardKey.LeftBracket,
            EditorKey.RightBracket => (int)KeyboardKey.RightBracket, EditorKey.Comma => (int)KeyboardKey.Comma, EditorKey.Period => (int)KeyboardKey.Period,
            EditorKey.Slash => (int)KeyboardKey.Slash, EditorKey.Minus => (int)KeyboardKey.Minus, EditorKey.Equals => (int)KeyboardKey.Equal,
            EditorKey.Apostrophe => (int)KeyboardKey.Apostrophe, EditorKey.KpAdd => (int)KeyboardKey.KpAdd, EditorKey.KpSubtract => (int)KeyboardKey.KpSubtract,
            _ => 0,
        };
    }
}

/// <summary>
/// ⭐ B-10 K4 §6g — the map's device (<see cref="IInputProvider"/>: Raylib in production, a hand-made one in a rail) as <see cref="IChordInput"/>, so the FPS
/// station, the Player's map keys and the camera read their rows through the keymap and not the provider's keys. ⛔ No allocation per call.
/// </summary>
public sealed class ProviderChordInput : IChordInput
{
    private readonly IInputProvider _input;

    /// <param name="input">The provider to read.</param>
    public ProviderChordInput(IInputProvider input) => _input = input ?? throw new ArgumentNullException(nameof(input));

    /// <inheritdoc/>
    public KeyModifiers Modifiers
    {
        get
        {
            var m = KeyModifiers.None;
            if (Down(KeyboardKey.LeftControl) || Down(KeyboardKey.RightControl)) m |= KeyModifiers.Ctrl;
            if (Down(KeyboardKey.LeftShift) || Down(KeyboardKey.RightShift)) m |= KeyModifiers.Shift;
            if (Down(KeyboardKey.LeftAlt) || Down(KeyboardKey.RightAlt)) m |= KeyModifiers.Alt;
            if (Down(KeyboardKey.LeftSuper) || Down(KeyboardKey.RightSuper)) m |= KeyModifiers.Super;
            return m;
        }
    }

    private bool Down(KeyboardKey k) => _input.IsKeyDown((MapKeyboardKey)(int)k);

    /// <inheritdoc/>
    public bool IsDown(EditorKey source) => source switch
    {
        EditorKey.MouseLeft => _input.IsMouseButtonDown(MapMouseButton.Left),
        EditorKey.MouseRight => _input.IsMouseButtonDown(MapMouseButton.Right),
        EditorKey.MouseMiddle => _input.IsMouseButtonDown(MapMouseButton.Middle),
        _ => RaylibKeyCodes.Of(source) is var code && code != 0 && _input.IsKeyDown((MapKeyboardKey)code),
    };

    /// <inheritdoc/>
    public bool IsPressed(EditorKey source, bool allowRepeat = false) => source switch
    {
        EditorKey.MouseLeft => _input.IsMouseButtonPressed(MapMouseButton.Left),
        EditorKey.MouseRight => _input.IsMouseButtonPressed(MapMouseButton.Right),
        EditorKey.MouseMiddle => _input.IsMouseButtonPressed(MapMouseButton.Middle),
        _ => RaylibKeyCodes.Of(source) is var code && code != 0 && _input.IsKeyPressed((MapKeyboardKey)code),
    };

    /// <inheritdoc/>
    public float Value(EditorKey source) => source switch
    {
        EditorKey.MouseWheel => _input.MouseWheelMove,
        EditorKey.MouseDeltaX => _input.MouseDelta.X,
        EditorKey.MouseDeltaY => _input.MouseDelta.Y,
        _ => 0f,
    };
}

/// <summary>
/// ⭐ B-10 K4 §6g — ImGui's own device as <see cref="IChordInput"/> (a panel's keys: the timeline). Guarded against a missing ImGui context, as
/// <see cref="ImGuiInputSource"/> is. ⛔ No allocation per call.
/// </summary>
public sealed class ImGuiChordInput : IChordInput
{
    private static bool HasContext => ImGui.GetCurrentContext() != IntPtr.Zero;

    /// <inheritdoc/>
    public KeyModifiers Modifiers
    {
        get
        {
            if (!HasContext) return KeyModifiers.None;
            var io = ImGui.GetIO();
            return ImGuiInputSource.MapModifiers(io.KeyCtrl, io.KeyShift, io.KeyAlt, io.KeySuper);
        }
    }

    /// <summary>Whether a keypad key reads as ImGui's keypad key too (the timeline zooms on either).</summary>
    private static ImGuiKey KeyOf(EditorKey k) => k switch
    {
        EditorKey.KpAdd => ImGuiKey.KeypadAdd,
        EditorKey.KpSubtract => ImGuiKey.KeypadSubtract,
        _ => ImGuiInputSource.MapEditorKey(k),
    };

    /// <inheritdoc/>
    public bool IsDown(EditorKey source)
    {
        if (!HasContext) return false;
        return source switch
        {
            EditorKey.MouseLeft => ImGui.IsMouseDown(ImGuiMouseButton.Left),
            EditorKey.MouseRight => ImGui.IsMouseDown(ImGuiMouseButton.Right),
            EditorKey.MouseMiddle => ImGui.IsMouseDown(ImGuiMouseButton.Middle),
            _ => KeyOf(source) is var k && k != ImGuiKey.None && ImGui.IsKeyDown(k),
        };
    }

    /// <inheritdoc/>
    public bool IsPressed(EditorKey source, bool allowRepeat = false)
    {
        if (!HasContext) return false;
        return source switch
        {
            EditorKey.MouseLeft => ImGui.IsMouseClicked(ImGuiMouseButton.Left),
            EditorKey.MouseRight => ImGui.IsMouseClicked(ImGuiMouseButton.Right),
            EditorKey.MouseMiddle => ImGui.IsMouseClicked(ImGuiMouseButton.Middle),
            _ => KeyOf(source) is var k && k != ImGuiKey.None && ImGui.IsKeyPressed(k, allowRepeat),
        };
    }

    /// <inheritdoc/>
    public float Value(EditorKey source)
    {
        if (!HasContext) return 0f;
        var io = ImGui.GetIO();
        return source switch
        {
            EditorKey.MouseWheel => io.MouseWheel,
            EditorKey.MouseWheelH => io.MouseWheelH,
            EditorKey.MouseDeltaX => io.MouseDelta.X,
            EditorKey.MouseDeltaY => io.MouseDelta.Y,
            _ => 0f,
        };
    }
}
