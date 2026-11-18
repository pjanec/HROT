using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4664</c> (thermal drivers TD-14; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4b) — the TKB type of a ground-heat-marks entity (one ground cell's warm
    /// vehicle stands). Bare, like the ground damage's: the marks travel on <c>GroundHeatMarks</c> / <c>dtGroundHeatMarks</c>. Hidden from the Add Entity palette and never
    /// drawn in the normal views — only a vehicle leaving a warm stand makes one.
    /// </summary>
    public static class GroundHeatMarksTkb
    {
        public const long Type = Fdp.Toolkit.Terrain.GroundHeatMarkRules.TkbType;   // the toolkit requests it (GroundHeatMarkSystem)
        public const string Name = "Ground heat marks";

        public static bool Is(long tkbType) => tkbType == Type;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            var t = new TkbTemplate(Name, Type);
            t.AddDescriptor(new TkbMasterDto { CustomName = Name, HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
