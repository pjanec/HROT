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
            if (BuiltInLooks.TryGetValue(type, out var look)) t.AddDescriptor(look);   // ⭐ P1 — how its round looks
            tkb.Register(t);
        }

        /// <summary>⭐ P1 (docs/DESIGN_Visual_Effects.md §8 PR-A) — how the built-in munitions' rounds look: the M67 a small olive
        /// grenade, the 81 mm a finned bomb.</summary>
        public static readonly System.Collections.Generic.IReadOnlyDictionary<long, MunitionVisualDto> BuiltInLooks =
            new System.Collections.Generic.Dictionary<long, MunitionVisualDto>
            {
                [TkbM67Grenade] = new() { Shape = MunitionShape.Grenade, Length = 0.11f, Diameter = 0.064f, ColorHex = "#4B5320" },
                [Tkb81mmMortarHe] = new() { Shape = MunitionShape.Bomb, Length = 0.5f, Diameter = 0.081f, ColorHex = "#5A5E3A" },
            };

        /// <summary>⭐ P1 (§8 PR-A) — the look of a round of munition type <paramref name="ammo"/>: its TKB <see cref="MunitionVisualDto"/>,
        /// else the built-in one (a host with no TKB — the replay browser — still draws the built-ins), else null (no body: a bullet).</summary>
        public static MunitionVisualDto? VisualOf(ITkbDatabase? tkb, long ammo)
        {
            if (ammo == 0) return null;
            if (tkb != null && tkb.TryGetByType(ammo, out var t) && t?.GetDescriptor<MunitionVisualDto>() is { } v) return v;
            return BuiltInLooks.TryGetValue(ammo, out var b) ? b : null;
        }
    }
}
