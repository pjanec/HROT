using System.Numerics;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tests.Input
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

        public bool IsMouseButtonPressed(MapMouseButton button)
        {
            if (button == MapMouseButton.Left) return IsLeftPressed;
            if (button == MapMouseButton.Right) return IsRightPressed;
            return false;
        }

        public bool IsMouseButtonDown(MapMouseButton button)
        {
            if (button == MapMouseButton.Left) return IsLeftDown;
            if (button == MapMouseButton.Right) return IsRightDown;
            return false;
        }

        public bool IsMouseButtonReleased(MapMouseButton button)
        {
            if (button == MapMouseButton.Left) return IsLeftReleased;
            if (button == MapMouseButton.Right) return IsRightReleased;
            return false;
        }

        public bool IsKeyPressed(MapKeyboardKey key)
        {
            return false;
        }

        public bool IsKeyDown(MapKeyboardKey key)
        {
             if (key == MapKeyboardKey.LeftControl || key == MapKeyboardKey.RightControl) return IsCtrlDown;
             if (key == MapKeyboardKey.LeftShift || key == MapKeyboardKey.RightShift) return IsShiftDown;
             return false;
        }

        public bool IsKeyReleased(MapKeyboardKey key)
        {
            return false;
        }

        /// <summary>Queued key presses returned by <see cref="GetKeyPressed"/> (FIFO).</summary>
        public Queue<MapKeyboardKey> KeyPressQueue { get; } = new();

        public int GetKeyPressed()
            => KeyPressQueue.Count > 0 ? (int)KeyPressQueue.Dequeue() : 0;
    }
}
