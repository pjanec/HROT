using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Input
{
    public class Vis2DInputMap
    {
        public MapMouseButton SelectButton { get; set; } = MapMouseButton.Left;
        public MapMouseButton PanButton { get; set; } = MapMouseButton.Right;
        
        // Modifiers
        public MapKeyboardKey MultiSelectMod { get; set; } = MapKeyboardKey.LeftShift;
        public MapKeyboardKey BoxSelectMod { get; set; } = MapKeyboardKey.LeftControl;
        
        public static Vis2DInputMap Default => new Vis2DInputMap();
    }
}
