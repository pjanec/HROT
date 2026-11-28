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

    // ⭐ SD-S2a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-102) — the GAMEPAD (pad 0) as keymap sources, appended. Sticks and triggers are
    // AXIS (value) sources — the keymap reader applies one deadzone to them; a trigger also reads as held past half way (Hold / Press). The buttons are keys.
    /// <summary>The pad's left stick, −1 left … +1 right — an AXIS source.</summary>
    PadLeftX,
    /// <summary>The pad's left stick, −1 up … +1 down — an AXIS source.</summary>
    PadLeftY,
    /// <summary>The pad's right stick, −1 left … +1 right — an AXIS source.</summary>
    PadRightX,
    /// <summary>The pad's right stick, −1 up … +1 down — an AXIS source.</summary>
    PadRightY,
    /// <summary>The pad's left trigger, 0 … 1 — an AXIS source; held as a button (Hold / Press).</summary>
    PadTriggerL,
    /// <summary>The pad's right trigger, 0 … 1 — an AXIS source; held as a button (Hold / Press).</summary>
    PadTriggerR,
    /// <summary>The pad's bottom face button (A on an Xbox pad).</summary>
    PadA,
    /// <summary>The pad's right face button (B).</summary>
    PadB,
    /// <summary>The pad's left face button (X).</summary>
    PadX,
    /// <summary>The pad's top face button (Y).</summary>
    PadY,
    /// <summary>The pad's left bumper.</summary>
    PadLB,
    /// <summary>The pad's right bumper.</summary>
    PadRB,
    /// <summary>The pad's Back / View button.</summary>
    PadBack,
    /// <summary>The pad's Start / Menu button.</summary>
    PadStart,
    /// <summary>The pad's left stick pressed.</summary>
    PadLeftStick,
    /// <summary>The pad's right stick pressed.</summary>
    PadRightStick,
    /// <summary>The pad's d-pad up.</summary>
    PadDpadUp,
    /// <summary>The pad's d-pad down.</summary>
    PadDpadDown,
    /// <summary>The pad's d-pad left.</summary>
    PadDpadLeft,
    /// <summary>The pad's d-pad right.</summary>
    PadDpadRight,
}
