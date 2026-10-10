using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-1) — the development MUNITION types: the TKB types a weapon mount loads
    /// (<see cref="WeaponMountDto.AmmoGuid"/>) and a shot and a detonation carry (W-2). Their warheads come from the reference library
    /// BY NAME (<c>ParameterResolver.Warhead</c>), so each type is named exactly like its <c>ammo.json</c> entry and states no numbers;
    /// a type that needs other numbers adds a <see cref="WarheadDto"/>.
    /// <para>DIS kind 2 (munition), hidden from the Add Entity palette — a munition is not a unit anybody places. ⚠ Development
    /// default like <see cref="UrbanCombatTkbCatalog"/>: the real system loads TKB from files synced to all nodes.</para>
    /// <para>📄 docs/DESIGN_Building_Interiors.md §3k.</para>
    /// </summary>
    public static class MunitionTkbCatalog
    {
        /// <summary>The M67 hand grenade (fragmentation, time fuze).</summary>
        public const int TkbM67Grenade = 6001;
        /// <summary>An 81 mm mortar HE bomb (impact fuze, indirect).</summary>
        public const int Tkb81mmMortarHe = 6002;

        public const string M67GrenadeName = "M67 grenade";
        public const string Mortar81mmHeName = "81mm mortar HE";

        /// <summary>Registers the munition types. ⚠ Once per database (<c>TkbDatabase.Register</c> throws on a duplicate).</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            Register(tkb, M67GrenadeName, TkbM67Grenade, new DISEntityType { Kind = 2, Domain = 8 });                   // anti-personnel
            Register(tkb, Mortar81mmHeName, Tkb81mmMortarHe, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });  // anti-personnel, ballistic
        }

        private static void Register(ITkbDatabase tkb, string name, int type, DISEntityType dis)
        {
            var t = new TkbTemplate(name, type) { DisType = dis };
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString(), HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
