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

        /// <summary>
        /// Returns the next key pressed in the input queue (Raylib-style polling).
        /// Cast the return value to <see cref="KeyboardKey"/>.
        /// Returns <c>0</c> when no more keys are queued.
        /// </summary>
        int GetKeyPressed();

        bool IsMouseCaptured { get; }
        bool IsKeyboardCaptured { get; }
    }
}
