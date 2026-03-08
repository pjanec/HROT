using System.Numerics;
using Raylib_cs;
using ImGuiNET;
using FDP.Toolkit.Vis2D.Abstractions;

namespace FDP.Toolkit.Vis2D.Defaults
{
    public class RaylibInputProvider : IInputProvider
    {
        public Vector2 MousePosition => Raylib.GetMousePosition();
        public Vector2 MouseDelta => Raylib.GetMouseDelta();
        public float MouseWheelMove => Raylib.GetMouseWheelMove();

        public bool IsMouseCaptured => ImGui.GetIO().WantCaptureMouse;
        public bool IsKeyboardCaptured => ImGui.GetIO().WantCaptureKeyboard;

        public bool IsMouseButtonPressed(MouseButton button) => Raylib.IsMouseButtonPressed(button);
        public bool IsMouseButtonDown(MouseButton button) => Raylib.IsMouseButtonDown(button);
        public bool IsMouseButtonReleased(MouseButton button) => Raylib.IsMouseButtonReleased(button);
        
        public bool IsKeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key);
        public bool IsKeyDown(KeyboardKey key) => Raylib.IsKeyDown(key);
        public bool IsKeyReleased(KeyboardKey key) => Raylib.IsKeyReleased(key);
        public int GetKeyPressed() => Raylib.GetKeyPressed();
    }
}
