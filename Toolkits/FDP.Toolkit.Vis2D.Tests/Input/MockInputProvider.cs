using System.Numerics;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Input
{
    public class MockInputProvider : IInputProvider
    {
        public Vector2 MousePosition { get; set; } = Vector2.Zero;
        public Vector2 MouseDelta { get; set; } = Vector2.Zero;
        public float MouseWheelMove { get; set; } = 0f;
        
        public bool IsLeftPressed { get; set; }
        public bool IsRightPressed { get; set; }
        public bool IsLeftDown { get; set; }
        public bool IsRightDown { get; set; }
        public bool IsLeftReleased { get; set; }
        public bool IsRightReleased { get; set; }
        
        // Key States
        public bool IsCtrlDown { get; set; }
        public bool IsShiftDown { get; set; }

        public bool IsMouseCaptured { get; set; }
        public bool IsKeyboardCaptured { get; set; }

        public bool IsMouseButtonPressed(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftPressed;
            if (button == MouseButton.Right) return IsRightPressed;
            return false;
        }

        public bool IsMouseButtonDown(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftDown;
            if (button == MouseButton.Right) return IsRightDown;
            return false;
        }

        public bool IsMouseButtonReleased(MouseButton button)
        {
            if (button == MouseButton.Left) return IsLeftReleased;
            if (button == MouseButton.Right) return IsRightReleased;
            return false;
        }

        public bool IsKeyPressed(KeyboardKey key)
        {
            return false;
        }

        public bool IsKeyDown(KeyboardKey key)
        {
             if (key == KeyboardKey.LeftControl || key == KeyboardKey.RightControl) return IsCtrlDown;
             if (key == KeyboardKey.LeftShift || key == KeyboardKey.RightShift) return IsShiftDown;
             return false;
        }

        public bool IsKeyReleased(KeyboardKey key)
        {
            return false;
        }
    }
}
