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

        public bool IsMouseCaptured => ImGuiNET.ImGui.GetIO().WantCaptureMouse;
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
    }
}
