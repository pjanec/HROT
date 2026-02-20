using System.Numerics;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Abstractions
{
    public interface IInputProvider
    {
        Vector2 MousePosition { get; }
        Vector2 MouseDelta { get; }
        float MouseWheelMove { get; }

        bool IsMouseButtonPressed(MouseButton button);
        bool IsMouseButtonDown(MouseButton button);
        bool IsMouseButtonReleased(MouseButton button);
        
        bool IsKeyPressed(KeyboardKey key);
        bool IsKeyDown(KeyboardKey key);
        bool IsKeyReleased(KeyboardKey key);

        bool IsMouseCaptured { get; }
        bool IsKeyboardCaptured { get; }
    }
}
