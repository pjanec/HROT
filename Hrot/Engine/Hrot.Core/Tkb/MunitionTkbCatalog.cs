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

        // ⭐⭐ LR-1 (docs/DESIGN_Visual_Effects.md §8.5, R-271: "Every ammo needs its tkb type, always") — a munition type for every
        //   built-in mount. ⚠ COMBAT-NEUTRAL BY CONSTRUCTION, pinned by EffectTests.LR1_*: every mount states its own penetration (it
        //   wins before the reference library's by-name pair), and no name below matches an EXPLOSIVE reference entry
        //   (ParameterResolver.Warhead falls back to the library BY NAME) — the kinetic ones carry their real names (no warhead there),
        //   the two shaped-charge ones (RPG, TOW) deliberately do NOT match "PG-7V HEAT" / "TOW-2 HEAT": giving them a real HEAT
        //   warhead is the combat lane's call, not a side effect of naming.
        //   ⭐ M3 (docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md §2, by decision): that call is made — the RPG and TOW TEMPLATES
        //   carry their OWN WarheadDto (the template-first rule of ParameterResolver.Warhead makes it win), copied from the library's
        //   "PG-7V HEAT" / "TOW-2 HEAT" so the numbers have one source; the names stay. A shaped charge reads the armour's …Ce column.
        /// <summary>5.56 mm ball (the rifles' round) — an ordinary bullet: no look, never replicated (R-271).</summary>
        public const int Tkb556Ball = 6003;
        /// <summary>5.56 mm tracer — mixed one in five into the rifles' magazines; drawn and replicated.</summary>
        public const int Tkb556Tracer = 6004;
        /// <summary>25 mm APDS-T (the Bradley's cannon) — every round traces.</summary>
        public const int Tkb25mmApdsT = 6005;
        /// <summary>120 mm APFSDS-T (the M1).</summary>
        public const int Tkb120mmApfsds = 6006;
        /// <summary>125 mm APFSDS-T (the T-72).</summary>
        public const int Tkb125mmApfsds = 6007;
        /// <summary>The TOW anti-tank missile (the Bradley).</summary>
        public const int TkbTowMissile = 6008;
        /// <summary>The RPG-7 rocket (the insurgents).</summary>
        public const int TkbRpg7Rocket = 6009;

        public const string Ball556Name = "5.56x45 ball", Tracer556Name = "5.56x45 tracer", Apds25Name = "25mm APDS-T",
                            Apfsds120Name = "120mm APFSDS", Apfsds125Name = "125mm APFSDS", TowName = "TOW missile", Rpg7Name = "RPG-7 rocket";

        /// <summary>Registers the munition types. ⚠ Once per database (<c>TkbDatabase.Register</c> throws on a duplicate).</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            Register(tkb, M67GrenadeName, TkbM67Grenade, new DISEntityType { Kind = 2, Domain = 8 });                   // anti-personnel
            Register(tkb, Mortar81mmHeName, Tkb81mmMortarHe, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });  // anti-personnel, ballistic
            // ⭐ LR-1 — DIS kind 2 (munition), domain 9 anti-armour / 8 anti-personnel, category 2 ballistic / 1 guided
            Register(tkb, Ball556Name, Tkb556Ball, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });
            Register(tkb, Tracer556Name, Tkb556Tracer, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });
            Register(tkb, Apds25Name, Tkb25mmApdsT, new DISEntityType { Kind = 2, Domain = 9, Category = 2 });
            Register(tkb, Apfsds120Name, Tkb120mmApfsds, new DISEntityType { Kind = 2, Domain = 9, Category = 2 });
            Register(tkb, Apfsds125Name, Tkb125mmApfsds, new DISEntityType { Kind = 2, Domain = 9, Category = 2 });
            Register(tkb, TowName, TkbTowMissile, new DISEntityType { Kind = 2, Domain = 9, Category = 1 }, ShapedChargeOf(TowLibraryName));
            Register(tkb, Rpg7Name, TkbRpg7Rocket, new DISEntityType { Kind = 2, Domain = 9, Category = 2 }, ShapedChargeOf(Rpg7LibraryName));
        }

        /// <summary>⭐ M3 — the reference-library entries the RPG and TOW warheads are copied from.</summary>
        public const string Rpg7LibraryName = "PG-7V HEAT", TowLibraryName = "TOW-2 HEAT";

        /// <summary>⭐ M3 — the library's generated warhead for <paramref name="libraryName"/>, as a template's own (a shaped charge).</summary>
        private static WarheadDto? ShapedChargeOf(string libraryName)
            => Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(libraryName, out var r) && r.Warhead != null
                ? r.Warhead.Dto with { Kind = WarheadKind.ShapedCharge }
                : null;

        private static void Register(ITkbDatabase tkb, string name, int type, DISEntityType dis, WarheadDto? warhead = null)
        {
            var t = new TkbTemplate(name, type) { DisType = dis };
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString(), HideFromPalette = true });
            if (BuiltInLooks.TryGetValue(type, out var look)) t.AddDescriptor(look);   // ⭐ P1 — how its round looks
            if (warhead != null) t.AddDescriptor(warhead);
            tkb.Register(t);
        }

        /// <summary>⭐ P1 (docs/DESIGN_Visual_Effects.md §8 PR-A) — how the built-in munitions' rounds look: the M67 a small olive
        /// grenade, the 81 mm a finned bomb.</summary>
        public static readonly System.Collections.Generic.IReadOnlyDictionary<long, MunitionVisualDto> BuiltInLooks =
            new System.Collections.Generic.Dictionary<long, MunitionVisualDto>
            {
                [TkbM67Grenade] = new() { Shape = MunitionShape.Grenade, Length = 0.11f, Diameter = 0.064f, ColorHex = "#4B5320" },
                [Tkb81mmMortarHe] = new() { Shape = MunitionShape.Bomb, Length = 0.5f, Diameter = 0.081f, ColorHex = "#5A5E3A" },
                // ⭐ LR-1 — the visible ones (R-271): tracers, the big gun rounds, the rocket and the missile. 5.56 ball has NO look.
                [Tkb556Tracer] = new() { Shape = MunitionShape.Bullet, Length = 0.023f, Diameter = 0.0057f, Tracer = true, TracerColorHex = "#FF6A30" },
                [Tkb25mmApdsT] = new() { Shape = MunitionShape.Shell, Length = 0.13f, Diameter = 0.025f, ColorHex = "#2F2F2F", Tracer = true, TracerColorHex = "#FF8A30" },
                [Tkb120mmApfsds] = new() { Shape = MunitionShape.Shell, Length = 0.68f, Diameter = 0.03f, ColorHex = "#3A3A3A", Tracer = true, TracerColorHex = "#FFB050" },
                [Tkb125mmApfsds] = new() { Shape = MunitionShape.Shell, Length = 0.57f, Diameter = 0.03f, ColorHex = "#3A3A3A", Tracer = true, TracerColorHex = "#FFB050" },
                [TkbTowMissile] = new() { Shape = MunitionShape.Missile, Length = 1.17f, Diameter = 0.152f, ColorHex = "#6B6E4A", SmokeTrail = true },
                [TkbRpg7Rocket] = new() { Shape = MunitionShape.Rocket, Length = 0.95f, Diameter = 0.085f, ColorHex = "#5C5A3E", SmokeTrail = true },
            };

        /// <summary>⭐ LR-3 — one of the built-in munition types above (6001–6009), whether or not it has a look: a round, never a body.</summary>
        public static bool IsBuiltIn(long type) => type >= TkbM67Grenade && type <= TkbRpg7Rocket;

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
