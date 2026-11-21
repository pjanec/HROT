using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ CE-1033 S5 (docs/DESIGN_Map_3D_Mode.md §3.12, R-260) — the TKB type of the map CAMERA entity: one per scenario, "the view
    /// the scenario opens in". An ordinary spatial entity (U7): its <c>SimTransform</c> holds the eye and the view direction, nothing
    /// else. Hidden from the palette, and never drawn or picked as a body.
    /// </summary>
    public static class MapCameraTkb
    {
        /// <summary>The camera entity's TKB type (next to the effect types, 7101–7131).</summary>
        public const long Type = 7201;

        public const string Name = "Map Camera";

        public static bool Is(long tkbType) => tkbType == Type;

        /// <summary>Registers the type. ⚠ Once per database (<c>TkbDatabase.Register</c> throws on a duplicate).</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            var t = new TkbTemplate(Name, Type);
            t.AddDescriptor(new TkbMasterDto { CustomName = Name, HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
