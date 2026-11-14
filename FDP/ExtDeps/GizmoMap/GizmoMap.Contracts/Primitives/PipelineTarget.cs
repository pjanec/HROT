using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [Flags]
    public enum PipelineTarget : byte
    {
        None       = 0,
        Map2D      = 1,
        Viewport3D = 2,
        NodeGraph  = 4,
        All        = 7,

        /// <summary>
        /// ⭐ AQ93 §12.4 (🔒 user, 2026-10-17: <i>"for sure no 2d road net gizmo should be shown in 3d map where the triangle road net
        /// is expected"</i>) — a 2-D SYMBOL of something the 3-D map draws as a model of its own: a 3-D pass never draws it, whatever
        /// else is set. ⭐ Combined with <see cref="Map2D"/>. ⛔ Not part of <see cref="All"/>, and <see cref="Map2D"/> keeps meaning
        /// "the map, both modes" (DESIGN_Map_3D_Mode.md §7) — so every existing gizmo is unchanged.
        /// </summary>
        FlatOnly   = 8,
    }
}
