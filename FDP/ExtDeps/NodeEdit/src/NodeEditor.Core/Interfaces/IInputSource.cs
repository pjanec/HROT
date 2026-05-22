using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Per-frame input snapshot for the canvas. The host provides an
/// implementation matching its windowing backend (raylib, SDL, …).
/// </summary>
public interface IInputSource
{
    /// <summary>Mouse position in screen coordinates.</summary>
    Vector2 MousePosition { get; }

    /// <summary>Mouse movement this frame.</summary>
    Vector2 MouseDelta { get; }

    /// <summary>Mouse wheel delta this frame (positive = scroll up).</summary>
    float WheelDelta { get; }

    bool IsMouseDown(MouseButton btn);
    bool IsMousePressed(MouseButton btn);
    bool IsMouseReleased(MouseButton btn);
    bool IsMouseDoubleClicked(MouseButton btn);

    bool IsKeyDown(EditorKey k);
    bool IsKeyPressed(EditorKey k, bool allowRepeat = false);
    bool IsKeyReleased(EditorKey k);

    KeyModifiers Modifiers { get; }

    /// <summary>Text input received this frame (for text editing widgets).</summary>
    ReadOnlySpan<char> TextThisFrame { get; }
}
