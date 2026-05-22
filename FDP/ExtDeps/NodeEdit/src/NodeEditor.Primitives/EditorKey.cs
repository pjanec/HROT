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
}
