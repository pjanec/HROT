using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Raylib_cs;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>
/// Raylib-backed input source. Reads mouse + keyboard state each frame via Raylib.
/// </summary>
public sealed class FakeInputSource : IInputSource
{
    // Map EditorKey → Raylib KeyboardKey
    private static readonly Dictionary<EditorKey, KeyboardKey> _keyMap = BuildKeyMap();

    private char[]  _textBuffer = new char[64];
    private int     _textLen;

    /// <summary>Called once per frame to capture text input from Raylib.</summary>
    public void BeginFrame()
    {
        _textLen = 0;
        int ch;
        while ((ch = Raylib.GetCharPressed()) != 0 && _textLen < _textBuffer.Length)
            _textBuffer[_textLen++] = (char)ch;
    }

    public Vector2     MousePosition => Raylib.GetMousePosition();
    public Vector2     MouseDelta    => Raylib.GetMouseDelta();
    public float       WheelDelta    => Raylib.GetMouseWheelMove();
    public ReadOnlySpan<char> TextThisFrame => _textBuffer.AsSpan(0, _textLen);

    public KeyModifiers Modifiers
    {
        get
        {
            var m = KeyModifiers.None;
            if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl)) m |= KeyModifiers.Ctrl;
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)   || Raylib.IsKeyDown(KeyboardKey.RightShift))   m |= KeyModifiers.Shift;
            if (Raylib.IsKeyDown(KeyboardKey.LeftAlt)     || Raylib.IsKeyDown(KeyboardKey.RightAlt))     m |= KeyModifiers.Alt;
            return m;
        }
    }

    public bool IsMouseDown(NodeEditor.Primitives.MouseButton btn)          => Raylib.IsMouseButtonDown(ToRaylib(btn));
    public bool IsMousePressed(NodeEditor.Primitives.MouseButton btn)       => Raylib.IsMouseButtonPressed(ToRaylib(btn));
    public bool IsMouseReleased(NodeEditor.Primitives.MouseButton btn)      => Raylib.IsMouseButtonReleased(ToRaylib(btn));
    public bool IsMouseDoubleClicked(NodeEditor.Primitives.MouseButton btn) => false; // Raylib has no built-in double-click

    public bool IsKeyDown(EditorKey k)                => _keyMap.TryGetValue(k, out var rk) && Raylib.IsKeyDown(rk);
    public bool IsKeyPressed(EditorKey k, bool allowRepeat = false)
        => _keyMap.TryGetValue(k, out var rk) &&
           (Raylib.IsKeyPressed(rk) || (allowRepeat && Raylib.IsKeyPressedRepeat(rk)));
    public bool IsKeyReleased(EditorKey k)            => _keyMap.TryGetValue(k, out var rk) && Raylib.IsKeyReleased(rk);

    private static Raylib_cs.MouseButton ToRaylib(NodeEditor.Primitives.MouseButton btn) => btn switch
    {
        NodeEditor.Primitives.MouseButton.Left   => Raylib_cs.MouseButton.Left,
        NodeEditor.Primitives.MouseButton.Right  => Raylib_cs.MouseButton.Right,
        NodeEditor.Primitives.MouseButton.Middle => Raylib_cs.MouseButton.Middle,
        _                                        => Raylib_cs.MouseButton.Left,
    };

    private static Dictionary<EditorKey, KeyboardKey> BuildKeyMap() => new()
    {
        [EditorKey.A]            = KeyboardKey.A,
        [EditorKey.B]            = KeyboardKey.B,
        [EditorKey.C]            = KeyboardKey.C,
        [EditorKey.D]            = KeyboardKey.D,
        [EditorKey.E]            = KeyboardKey.E,
        [EditorKey.F]            = KeyboardKey.F,
        [EditorKey.G]            = KeyboardKey.G,
        [EditorKey.H]            = KeyboardKey.H,
        [EditorKey.I]            = KeyboardKey.I,
        [EditorKey.J]            = KeyboardKey.J,
        [EditorKey.K]            = KeyboardKey.K,
        [EditorKey.L]            = KeyboardKey.L,
        [EditorKey.M]            = KeyboardKey.M,
        [EditorKey.N]            = KeyboardKey.N,
        [EditorKey.O]            = KeyboardKey.O,
        [EditorKey.P]            = KeyboardKey.P,
        [EditorKey.Q]            = KeyboardKey.Q,
        [EditorKey.R]            = KeyboardKey.R,
        [EditorKey.S]            = KeyboardKey.S,
        [EditorKey.T]            = KeyboardKey.T,
        [EditorKey.U]            = KeyboardKey.U,
        [EditorKey.V]            = KeyboardKey.V,
        [EditorKey.W]            = KeyboardKey.W,
        [EditorKey.X]            = KeyboardKey.X,
        [EditorKey.Y]            = KeyboardKey.Y,
        [EditorKey.Z]            = KeyboardKey.Z,
        [EditorKey.D0]           = KeyboardKey.Zero,
        [EditorKey.D1]           = KeyboardKey.One,
        [EditorKey.D2]           = KeyboardKey.Two,
        [EditorKey.D3]           = KeyboardKey.Three,
        [EditorKey.D4]           = KeyboardKey.Four,
        [EditorKey.D5]           = KeyboardKey.Five,
        [EditorKey.D6]           = KeyboardKey.Six,
        [EditorKey.D7]           = KeyboardKey.Seven,
        [EditorKey.D8]           = KeyboardKey.Eight,
        [EditorKey.D9]           = KeyboardKey.Nine,
        [EditorKey.F1]           = KeyboardKey.F1,
        [EditorKey.F2]           = KeyboardKey.F2,
        [EditorKey.F3]           = KeyboardKey.F3,
        [EditorKey.F4]           = KeyboardKey.F4,
        [EditorKey.F5]           = KeyboardKey.F5,
        [EditorKey.F6]           = KeyboardKey.F6,
        [EditorKey.F7]           = KeyboardKey.F7,
        [EditorKey.F8]           = KeyboardKey.F8,
        [EditorKey.F9]           = KeyboardKey.F9,
        [EditorKey.F10]          = KeyboardKey.F10,
        [EditorKey.F11]          = KeyboardKey.F11,
        [EditorKey.F12]          = KeyboardKey.F12,
        [EditorKey.Tab]          = KeyboardKey.Tab,
        [EditorKey.Space]        = KeyboardKey.Space,
        [EditorKey.Enter]        = KeyboardKey.Enter,
        [EditorKey.Escape]       = KeyboardKey.Escape,
        [EditorKey.Backspace]    = KeyboardKey.Backspace,
        [EditorKey.Delete]       = KeyboardKey.Delete,
        [EditorKey.Home]         = KeyboardKey.Home,
        [EditorKey.End]          = KeyboardKey.End,
        [EditorKey.PageUp]       = KeyboardKey.PageUp,
        [EditorKey.PageDown]     = KeyboardKey.PageDown,
        [EditorKey.Left]         = KeyboardKey.Left,
        [EditorKey.Right]        = KeyboardKey.Right,
        [EditorKey.Up]           = KeyboardKey.Up,
        [EditorKey.Down]         = KeyboardKey.Down,
        [EditorKey.Insert]       = KeyboardKey.Insert,
        [EditorKey.CapsLock]     = KeyboardKey.CapsLock,
        [EditorKey.LeftBracket]  = KeyboardKey.LeftBracket,
        [EditorKey.RightBracket] = KeyboardKey.RightBracket,
        [EditorKey.Comma]        = KeyboardKey.Comma,
        [EditorKey.Period]       = KeyboardKey.Period,
        [EditorKey.Slash]        = KeyboardKey.Slash,
        [EditorKey.Minus]        = KeyboardKey.Minus,
        [EditorKey.Equals]       = KeyboardKey.Equal,
        [EditorKey.Apostrophe]   = KeyboardKey.Apostrophe,
    };
}
