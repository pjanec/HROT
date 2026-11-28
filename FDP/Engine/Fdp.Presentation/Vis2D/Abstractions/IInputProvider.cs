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

        // ── ⭐ SD-S2a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-102) — the gamepad (pad 0). Default bodies: no pad, so a headless or test
        //    provider keeps compiling and reads nothing. ⛔ No deadzone here — the keymap reader applies ONE (KeymapReader.PadDeadzone) to every pad axis.

        /// <summary>Whether a gamepad is connected (pad 0). Default: no pad.</summary>
        bool GamepadAvailable => false;

        /// <summary>The pad's <paramref name="axis"/> this frame, raw (−1 … +1). Default: 0.</summary>
        float GamepadAxis(MapGamepadAxis axis) => 0f;

        /// <summary>Whether the pad's <paramref name="button"/> is held. Default: false.</summary>
        bool GamepadButtonDown(MapGamepadButton button) => false;

        /// <summary>Whether the pad's <paramref name="button"/> went down this frame. Default: false.</summary>
        bool GamepadButtonPressed(MapGamepadButton button) => false;
    }
}
