using System;

namespace Hrot.Common.Actions
{
    // ⚠ One enum per file: the CycloneDDS code generator writes one IDL per source file, and IDL enumerators share one scope
    //   (ActionGroup.Edit vs EditingMode.Edit, EmbarkOffer.None vs ActionTarget.None collided).
    /// <summary>⭐ B-11 A1 — what a menu is opened on. A3 adds Ground, waypoint, spawn area, weather region, event …</summary>
    [Flags]
    public enum ActionTarget { None = 0, Unit = 1, Area = 2, Zone = 4, Route = 8, Graphic = Area | Zone | Route }
}
