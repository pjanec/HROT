namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Keyboard key identifiers for <see cref="IMapLayer.HandleKeyInput"/>.
/// Values match the Raylib-cs / GLFW3 keyboard scan codes so that a direct
/// cast from the raw int returned by <c>IInputProvider.GetKeyPressed()</c> is safe.
/// Only the subset used by tools is enumerated; all other keys arrive as
/// unnamed (but valid) enum values.
/// </summary>
public enum MapKeyboardKey
{
    Unknown     = 0,
    Enter       = 257,
    Escape      = 256,
    Delete      = 261,
    LeftShift   = 340,
    LeftControl = 341,
    RightShift  = 344,
    RightControl = 345,
}
