namespace NodeEditor.Primitives;

/// <summary>
/// Abstract key codes used by <c>IInputSource</c>. Mapped from the host
/// input backend (raylib, SDL, win32, …) by an adapter.
/// </summary>
public enum EditorKey
{
    Unknown,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    Tab, Space, Enter, Escape, Backspace, Delete,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down, Insert, CapsLock,

    LeftBracket, RightBracket, Comma, Period, Slash,
    Minus, Equals, Apostrophe,

    // ⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — appended, so existing values do not move. A keymap chord names ANY user input, not only a
    // keyboard key: the keypad's + / −, a mouse button (Hold), and a mouse VALUE (Axis: the wheel, the movement).
    /// <summary>Keypad +.</summary>
    KpAdd,
    /// <summary>Keypad −.</summary>
    KpSubtract,
    /// <summary>The left mouse button (Press / Hold).</summary>
    MouseLeft,
    /// <summary>The right mouse button (Press / Hold).</summary>
    MouseRight,
    /// <summary>The middle mouse button (Press / Hold).</summary>
    MouseMiddle,
    /// <summary>The vertical wheel — an AXIS source (the notches this frame).</summary>
    MouseWheel,
    /// <summary>The horizontal wheel — an AXIS source.</summary>
    MouseWheelH,
    /// <summary>Mouse movement along X (px this frame) — an AXIS source.</summary>
    MouseDeltaX,
    /// <summary>Mouse movement along Y (px this frame) — an AXIS source.</summary>
    MouseDeltaY,
}
