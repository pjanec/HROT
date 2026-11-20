using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-3487</c> (flame FL-3; docs/designs/weapons/DESIGN_Flame_Weapons.md §7b N-1) — the TKB type of a ground-fire record entity (one ground cell's burning
    /// spots). Bare, like the ground damage's: the spots travel on <c>FireState</c> / <c>dtFireState</c>. Hidden from the Add Entity palette — only a napalm burst makes one.
    /// </summary>
    public static class GroundFireTkb
    {
        public const long Type = Fdp.Toolkit.Terrain.GroundFireRules.TkbType;   // the toolkit requests it (GroundFireSystem)
        public const string Name = "Ground fire";

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
