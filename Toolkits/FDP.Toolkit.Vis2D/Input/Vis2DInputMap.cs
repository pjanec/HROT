using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Input
{
    public class Vis2DInputMap
    {
        public MouseButton SelectButton { get; set; } = MouseButton.Left;
        public MouseButton PanButton { get; set; } = MouseButton.Right;
        
        // Modifiers
        public KeyboardKey MultiSelectMod { get; set; } = KeyboardKey.LeftShift;
        public KeyboardKey BoxSelectMod { get; set; } = KeyboardKey.LeftControl;
        
        public static Vis2DInputMap Default => new Vis2DInputMap();
    }
}
