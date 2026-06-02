using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// <see cref="IInputSource"/> that reads from ImGuiNET each frame.
/// Frame-snapshot members (<see cref="MousePosition"/>, <see cref="MouseDelta"/>,
/// <see cref="WheelDelta"/>, <see cref="Modifiers"/>, <see cref="TextThisFrame"/>)
/// are guarded against a missing ImGui context and return zero/empty in that case.
/// <para>
/// The enum-mapping helpers (<see cref="MapMouseButton"/>,
/// <see cref="MapEditorKey"/>, <see cref="MapModifiers"/>) are
/// pure static functions — unit-testable without an ImGui context.
/// </para>
/// </summary>
public sealed class ImGuiInputSource : IInputSource
{
    // ── Frame-snapshot properties ─────────────────────────────────────────────

    // ── Context guard helper ──────────────────────────────────────────────────
    // AccessViolationException is a corrupted-state exception that managed
    // try/catch cannot handle.  We must check the context pointer BEFORE any
    // ImGui native call to prevent the AV entirely.
    private static bool HasContext => ImGui.GetCurrentContext() != IntPtr.Zero;

    // ── Frame-snapshot properties ─────────────────────────────────────────────

    /// <inheritdoc/>
    public Vector2 MousePosition
    {
        get
        {
            if (!HasContext) return Vector2.Zero;
            try { return ImGui.GetMousePos(); }
            catch { return Vector2.Zero; }
        }
    }

    /// <inheritdoc/>
    public Vector2 MouseDelta
    {
        get
        {
            if (!HasContext) return Vector2.Zero;
            try { return ImGui.GetIO().MouseDelta; }
            catch { return Vector2.Zero; }
        }
    }

    /// <inheritdoc/>
    public float WheelDelta
    {
        get
        {
            if (!HasContext) return 0f;
            try { return ImGui.GetIO().MouseWheel; }
            catch { return 0f; }
        }
    }

    /// <inheritdoc/>
    public KeyModifiers Modifiers
    {
        get
        {
            if (!HasContext) return KeyModifiers.None;
            try
            {
                var io = ImGui.GetIO();
                return MapModifiers(io.KeyCtrl, io.KeyShift, io.KeyAlt, io.KeySuper);
            }
            catch { return KeyModifiers.None; }
        }
    }

    /// <inheritdoc/>
    public ReadOnlySpan<char> TextThisFrame
    {
        get
        {
            if (!HasContext) return ReadOnlySpan<char>.Empty;
            try
            {
                // ImGuiNET exposes typed text via AddInputCharacter / the InputCharacters list.
                // The underlying ImGuiIO::InputQueueCharacters buffer is accessible through
                // unsafe pointer in some versions; for compatibility we use the simple
                // GetInputQueueCharacter iteration available in ImGui.NET 1.91+.
                unsafe
                {
                    var io    = ImGui.GetIO();
                    var chars = new List<char>(4);
                    ushort c;
                    // Iterate through the ring-buffer by index until we hit '\0'.
                    for (int i = 0; i < io.InputQueueCharacters.Size; i++)
                    {
                        c = io.InputQueueCharacters[i];
                        if (c == 0) break;
                        chars.Add((char)c);
                    }
                    return chars.Count > 0
                        ? new ReadOnlySpan<char>(chars.ToArray())
                        : ReadOnlySpan<char>.Empty;
                }
            }
            catch { return ReadOnlySpan<char>.Empty; }
        }
    }

    // ── Per-frame query methods ───────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsMouseDown(MouseButton btn)
    {
        if (!HasContext) return false;
        try { return ImGui.IsMouseDown(MapMouseButton(btn)); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsMousePressed(MouseButton btn)
    {
        if (!HasContext) return false;
        try { return ImGui.IsMouseClicked(MapMouseButton(btn)); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsMouseReleased(MouseButton btn)
    {
        if (!HasContext) return false;
        try { return ImGui.IsMouseReleased(MapMouseButton(btn)); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsMouseDoubleClicked(MouseButton btn)
    {
        if (!HasContext) return false;
        try { return ImGui.IsMouseDoubleClicked(MapMouseButton(btn)); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsKeyDown(EditorKey k)
    {
        var ik = MapEditorKey(k);
        if (ik == ImGuiKey.None) return false;
        if (!HasContext) return false;
        try { return ImGui.IsKeyDown(ik); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsKeyPressed(EditorKey k, bool allowRepeat = false)
    {
        var ik = MapEditorKey(k);
        if (ik == ImGuiKey.None) return false;
        if (!HasContext) return false;
        try { return ImGui.IsKeyPressed(ik, allowRepeat); }
        catch { return false; }
    }

    /// <inheritdoc/>
    public bool IsKeyReleased(EditorKey k)
    {
        var ik = MapEditorKey(k);
        if (ik == ImGuiKey.None) return false;
        if (!HasContext) return false;
        try { return ImGui.IsKeyReleased(ik); }
        catch { return false; }
    }

    // ── Pure static mapping helpers (unit-testable without ImGui context) ─────

    /// <summary>Maps a NodeEdit <see cref="MouseButton"/> to the ImGui equivalent.</summary>
    public static ImGuiMouseButton MapMouseButton(MouseButton btn) => btn switch
    {
        MouseButton.Left   => ImGuiMouseButton.Left,
        MouseButton.Right  => ImGuiMouseButton.Right,
        MouseButton.Middle => ImGuiMouseButton.Middle,
        // X1/X2 do not have named ImGuiMouseButton enum values but ImGui encodes
        // them as integer indices 3/4.
        MouseButton.X1     => (ImGuiMouseButton)3,
        MouseButton.X2     => (ImGuiMouseButton)4,
        _                  => ImGuiMouseButton.Left,
    };

    /// <summary>Maps a NodeEdit <see cref="EditorKey"/> to an <see cref="ImGuiKey"/>.</summary>
    public static ImGuiKey MapEditorKey(EditorKey k) => k switch
    {
        // Letters
        EditorKey.A => ImGuiKey.A, EditorKey.B => ImGuiKey.B,
        EditorKey.C => ImGuiKey.C, EditorKey.D => ImGuiKey.D,
        EditorKey.E => ImGuiKey.E, EditorKey.F => ImGuiKey.F,
        EditorKey.G => ImGuiKey.G, EditorKey.H => ImGuiKey.H,
        EditorKey.I => ImGuiKey.I, EditorKey.J => ImGuiKey.J,
        EditorKey.K => ImGuiKey.K, EditorKey.L => ImGuiKey.L,
        EditorKey.M => ImGuiKey.M, EditorKey.N => ImGuiKey.N,
        EditorKey.O => ImGuiKey.O, EditorKey.P => ImGuiKey.P,
        EditorKey.Q => ImGuiKey.Q, EditorKey.R => ImGuiKey.R,
        EditorKey.S => ImGuiKey.S, EditorKey.T => ImGuiKey.T,
        EditorKey.U => ImGuiKey.U, EditorKey.V => ImGuiKey.V,
        EditorKey.W => ImGuiKey.W, EditorKey.X => ImGuiKey.X,
        EditorKey.Y => ImGuiKey.Y, EditorKey.Z => ImGuiKey.Z,

        // Digits
        EditorKey.D0 => ImGuiKey._0, EditorKey.D1 => ImGuiKey._1,
        EditorKey.D2 => ImGuiKey._2, EditorKey.D3 => ImGuiKey._3,
        EditorKey.D4 => ImGuiKey._4, EditorKey.D5 => ImGuiKey._5,
        EditorKey.D6 => ImGuiKey._6, EditorKey.D7 => ImGuiKey._7,
        EditorKey.D8 => ImGuiKey._8, EditorKey.D9 => ImGuiKey._9,

        // Function keys
        EditorKey.F1  => ImGuiKey.F1,  EditorKey.F2  => ImGuiKey.F2,
        EditorKey.F3  => ImGuiKey.F3,  EditorKey.F4  => ImGuiKey.F4,
        EditorKey.F5  => ImGuiKey.F5,  EditorKey.F6  => ImGuiKey.F6,
        EditorKey.F7  => ImGuiKey.F7,  EditorKey.F8  => ImGuiKey.F8,
        EditorKey.F9  => ImGuiKey.F9,  EditorKey.F10 => ImGuiKey.F10,
        EditorKey.F11 => ImGuiKey.F11, EditorKey.F12 => ImGuiKey.F12,

        // Navigation / editing
        EditorKey.Tab       => ImGuiKey.Tab,
        EditorKey.Space     => ImGuiKey.Space,
        EditorKey.Enter     => ImGuiKey.Enter,
        EditorKey.Escape    => ImGuiKey.Escape,
        EditorKey.Backspace => ImGuiKey.Backspace,
        EditorKey.Delete    => ImGuiKey.Delete,
        EditorKey.Home      => ImGuiKey.Home,
        EditorKey.End       => ImGuiKey.End,
        EditorKey.PageUp    => ImGuiKey.PageUp,
        EditorKey.PageDown  => ImGuiKey.PageDown,
        EditorKey.Left      => ImGuiKey.LeftArrow,
        EditorKey.Right     => ImGuiKey.RightArrow,
        EditorKey.Up        => ImGuiKey.UpArrow,
        EditorKey.Down      => ImGuiKey.DownArrow,
        EditorKey.Insert    => ImGuiKey.Insert,
        EditorKey.CapsLock  => ImGuiKey.CapsLock,

        // Punctuation
        EditorKey.LeftBracket  => ImGuiKey.LeftBracket,
        EditorKey.RightBracket => ImGuiKey.RightBracket,
        EditorKey.Comma        => ImGuiKey.Comma,
        EditorKey.Period       => ImGuiKey.Period,
        EditorKey.Slash        => ImGuiKey.Slash,
        EditorKey.Minus        => ImGuiKey.Minus,
        EditorKey.Equals       => ImGuiKey.Equal,
        EditorKey.Apostrophe   => ImGuiKey.Apostrophe,

        _ => ImGuiKey.None,
    };

    /// <summary>
    /// Assembles <see cref="KeyModifiers"/> from individual ImGui modifier bool flags.
    /// Pure function — no ImGui context needed.
    /// </summary>
    public static KeyModifiers MapModifiers(bool ctrl, bool shift, bool alt, bool super)
    {
        var mods = KeyModifiers.None;
        if (ctrl)  mods |= KeyModifiers.Ctrl;
        if (shift) mods |= KeyModifiers.Shift;
        if (alt)   mods |= KeyModifiers.Alt;
        if (super) mods |= KeyModifiers.Super;
        return mods;
    }
}
