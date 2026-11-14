using System.Numerics;

namespace Fdp.Toolkit.Vis2D.Abstractions
{
    public interface IInputProvider
    {
        Vector2 MousePosition { get; }
        Vector2 MouseDelta { get; }
        float MouseWheelMove { get; }

        bool IsMouseButtonPressed(MapMouseButton button);
        bool IsMouseButtonDown(MapMouseButton button);
        bool IsMouseButtonReleased(MapMouseButton button);

        bool IsKeyPressed(MapKeyboardKey key);
        bool IsKeyDown(MapKeyboardKey key);
        bool IsKeyReleased(MapKeyboardKey key);

        /// <summary>
        /// Returns the next key pressed in the input queue (Raylib-style polling).
        /// Cast the return value to <see cref="MapKeyboardKey"/>.
        /// Returns <c>0</c> when no more keys are queued.
        /// </summary>
        int GetKeyPressed();

        bool IsMouseCaptured { get; }
        bool IsKeyboardCaptured { get; }

        /// <summary>
        /// ⭐ <c>CE-3174</c> (M-9) — hide the cursor and keep it in the window, so mouse movement is pure look (a first-person view);
        /// false gives it back. Default: nothing (a headless or test provider has no cursor).
        /// </summary>
        void SetCursorLocked(bool locked) { }
    }
}
