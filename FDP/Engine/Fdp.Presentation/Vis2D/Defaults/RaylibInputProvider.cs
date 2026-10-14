using System.Numerics;
using Raylib_cs;
using ImGuiNET;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Defaults
{
    public class RaylibInputProvider : IInputProvider
    {
        public Vector2 MousePosition => Raylib.GetMousePosition();
        public Vector2 MouseDelta => Raylib.GetMouseDelta();
        public float MouseWheelMove => Raylib.GetMouseWheelMove();

        /// <remarks>⭐ CE-1072 — never while the cursor is LOCKED: the hidden cursor's position still wanders, and over a docked panel
        /// ImGui would claim the mouse and the first-person view would stop receiving its input until it wandered back.</remarks>
        public bool IsMouseCaptured => !CursorLocked && ImGuiNET.ImGui.GetIO().WantCaptureMouse;

        /// <summary>⭐ CE-1072 — true while <see cref="SetCursorLocked"/> holds the cursor (the map owns the mouse).</summary>
        public bool CursorLocked { get; private set; }
        public bool IsKeyboardCaptured => ImGuiNET.ImGui.GetIO().WantCaptureKeyboard;

        public bool IsMouseButtonPressed(MapMouseButton button) =>
            Raylib.IsMouseButtonPressed((Raylib_cs.MouseButton)(int)button);
        public bool IsMouseButtonDown(MapMouseButton button) =>
            Raylib.IsMouseButtonDown((Raylib_cs.MouseButton)(int)button);
        public bool IsMouseButtonReleased(MapMouseButton button) =>
            Raylib.IsMouseButtonReleased((Raylib_cs.MouseButton)(int)button);

        public bool IsKeyPressed(MapKeyboardKey key) =>
            Raylib.IsKeyPressed((Raylib_cs.KeyboardKey)(int)key);
        public bool IsKeyDown(MapKeyboardKey key) =>
            Raylib.IsKeyDown((Raylib_cs.KeyboardKey)(int)key);
        public bool IsKeyReleased(MapKeyboardKey key) =>
            Raylib.IsKeyReleased((Raylib_cs.KeyboardKey)(int)key);
        public int GetKeyPressed() => Raylib.GetKeyPressed();

        /// <summary>⭐ CE-3174 — the first-person view's cursor lock (Raylib keeps reporting <see cref="MouseDelta"/>).</summary>
        public void SetCursorLocked(bool locked)
        {
            if (locked == CursorLocked) return;
            CursorLocked = locked;
            if (locked) Raylib.DisableCursor();
            else Raylib.EnableCursor();
            // ⭐ CE-1072 — and ImGui ignores the mouse meanwhile, so no panel hovers, clicks or captures under the hidden cursor
            var io = ImGuiNET.ImGui.GetIO();
            if (locked) io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
            else io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
        }
    }
}
