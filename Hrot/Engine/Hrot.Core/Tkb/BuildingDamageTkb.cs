using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-2, B-11) — the TKB type of a building-damage entity. Bare on purpose, like the cloud's:
    /// what it holds (the breaches) travels on <c>BuildingDamage</c> / <c>dtBuildingDamage</c>; its position is the building's. Hidden from the Add Entity
    /// palette — only a detonation makes one (through <c>BuildingDamageSystem</c>).
    /// </summary>
    public static class BuildingDamageTkb
    {
        public const long Type = Fdp.Toolkit.Terrain.BuildingDamages.TkbType;   // the toolkit requests it (BuildingDamageSystem)
        public const string Name = "Building damage";

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
