using System;

namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope
    //   (ActionGroup.Edit vs EditingMode.Edit, EmbarkOffer.None vs ActionTarget.None collided).
    /// <summary>⭐ B-11 A1 — what a menu is opened on. A3 adds Ground, waypoint, spawn area, weather region …; ⭐ A6 (W-3) the timeline's
    /// <see cref="Event"/> / <see cref="Moment"/> / <see cref="Range"/>, the mode indicator's <see cref="Playhead"/>, a <see cref="Rule"/> row; ⭐ B-17 P2 (D11) the armed drawing tool's menu (<see cref="Drawing"/>, <see cref="ShapeEdit"/>).</summary>
    [Flags]
    public enum ActionTarget
    {
        None = 0, Unit = 1, Area = 2, Zone = 4, Route = 8, Graphic = Area | Zone | Route,
        Event = 16, Moment = 32, Range = 64, Playhead = 128, Rule = 256,
        Drawing = 512, ShapeEdit = 1024,   // ⭐ B-17 P2 — the armed drawing tool (Finish · Remove last point · Cancel) / the editor of a drawn shape or route (Finish · Cancel)
    }
}
