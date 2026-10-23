using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Input
{
    public class Vis2DInputMap
    {
        public MapMouseButton SelectButton { get; set; } = MapMouseButton.Left;
        public MapMouseButton PanButton { get; set; } = MapMouseButton.Right;

        /// <summary>
        /// ⭐ <c>CE-1287</c> — the SECOND pan button: the middle one drags the map as in the 3-D view (<c>MapCamera3D</c>: MMB pans),
        /// so the two views agree. 🔒 User, 2026-10-23: <i>"2d map need to support also middle button to drag map to be consistent
        /// with the 3d map"</i>. The right button still pans (and stays the context-menu click when it did not drag).
        /// </summary>
        public MapMouseButton SecondaryPanButton { get; set; } = MapMouseButton.Middle;
        
        // Modifiers
        public MapKeyboardKey MultiSelectMod { get; set; } = MapKeyboardKey.LeftShift;
        public MapKeyboardKey BoxSelectMod { get; set; } = MapKeyboardKey.LeftControl;
        
        public static Vis2DInputMap Default => new Vis2DInputMap();
    }
}
