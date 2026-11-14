using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4603</c> (B-6; docs/DESIGN_Vehicle_Breaching.md §7.7) — the TKB type of a ground-damage entity (one cell's felled lone trunks). Bare,
    /// like the building damage's: the trunks travel on <c>TreeDamage</c> / <c>dtTreeDamage</c>. Hidden from the Add Entity palette — only a blast or a player's hull makes one.
    /// </summary>
    public static class TreeDamageTkb
    {
        public const long Type = Fdp.Toolkit.Terrain.TreeDamages.TkbType;   // the toolkit requests it (TreeDamageSystem)
        public const string Name = "Tree damage";

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
