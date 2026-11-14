using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4591</c> (P-4; docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7) — the TKB type of a ground-damage entity (one ground cell's craters). Bare,
    /// like the building damage's: the craters travel on <c>GroundDamage</c> / <c>dtGroundDamage</c>. Hidden from the Add Entity palette — only a burst makes one.
    /// </summary>
    public static class GroundDamageTkb
    {
        public const long Type = Fdp.Toolkit.Terrain.GroundDamages.TkbType;   // the toolkit requests it (GroundDamageSystem)
        public const string Name = "Ground damage";

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
