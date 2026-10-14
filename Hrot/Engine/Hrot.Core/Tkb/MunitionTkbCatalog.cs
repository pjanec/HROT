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

        // ⭐⭐ M5 (docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §5) — the anti-armour family and the tank rounds. Unlike the
        //   LR-1 types above, each states its OWN numbers on the template: a generic AmmoWeaponBallisticsDto (penetration + damage — what
        //   another round of a loadout reads, CombatTkb.RoundOf), a WarheadDto copied from the nearest reference-library entry (one source
        //   for the fragment and blast numbers) and, for a rocket or missile, a MissileDto (speed, flight time, guidance, minimum range).
        //   The names deliberately match NO library entry, so nothing is picked up by name. Approximate public figures: tuning starts here.
        /// <summary>PG-7VL — the RPG-7's bigger single shaped charge.</summary>
        public const int TkbPg7vlRocket = 6010;
        /// <summary>PG-7VR — the RPG-7's tandem round (defeats ERA).</summary>
        public const int TkbPg7vrRocket = 6011;
        /// <summary>An M72-class disposable light anti-tank weapon.</summary>
        public const int TkbLawRocket = 6012;
        /// <summary>NLAW — fire-and-forget; ⚠ flies DIRECT here (its overfly top attack needs a downward fuze the model lacks — CE-3198).</summary>
        public const int TkbNlawMissile = 6013;
        /// <summary>Javelin — fire-and-forget, tandem, top attack beyond 150 m.</summary>
        public const int TkbJavelinMissile = 6014;
        /// <summary>A Kornet-class command-guided tandem ATGM (the technical's, M9).</summary>
        public const int TkbKornetMissile = 6015;
        /// <summary>An FPV drone's shaped-charge warhead (the drone flies it, M7) — no flight of its own.</summary>
        public const int TkbFpvWarhead = 6016;
        /// <summary>120 mm HEAT-MP — the tank guns' multi-purpose shaped charge (the M1's and, approximated, the T-72's).</summary>
        public const int TkbTankHeatMp = 6017;
        /// <summary>120 / 125 mm HE-FRAG — the tank guns' high-explosive round: against men and soft targets.</summary>
        public const int TkbTankHeFrag = 6018;
        /// <summary>12.7 / 14.5 mm AP — the heavy machine gun's round (the technicals', M9).</summary>
        public const int TkbHmgAp = 6019;
        /// <summary>⭐ M5 — 7.62 mm ball: the tanks' coaxial machine gun.</summary>
        public const int Tkb762Ball = 6020;
        /// <summary>⭐ M5 — 7.62 mm tracer, one in five in the coax belt.</summary>
        public const int Tkb762Tracer = 6021;

        public const string Pg7vlName = "PG-7VL rocket", Pg7vrName = "PG-7VR rocket", LawName = "M72 LAW rocket", NlawName = "NLAW missile",
                            JavelinName = "Javelin missile", KornetName = "Kornet missile", FpvWarheadName = "FPV warhead",
                            TankHeatMpName = "120mm HEAT-MP round", TankHeFragName = "tank HE-FRAG round", HmgApName = "12.7mm AP round",
                            Ball762Name = "7.62x51 ball", Tracer762Name = "7.62x51 tracer";

        /// <summary>⭐ M5 — the reference-library entries the new warheads are copied from (the nearest real one; the template's numbers win).</summary>
        public const string TankHeatLibraryName = "120mm HEAT", TankHeLibraryName = "125mm HE-frag";

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
            // ⭐ M5 — the TOW and the RPG gain their flight (§5): the TOW a command-guided 280 m/s for 15 s (it flew at the 1875 m/s the
            //   NED builder derives from its range), the PG-7V an unguided 300 m/s (its mount's own muzzle speed) for 4.5 s (self-destruct).
            //   Their penetration and damage stay on the mounts (LR-1: every mount states its own).
            Register(tkb, TowName, TkbTowMissile, new DISEntityType { Kind = 2, Domain = 9, Category = 1 }, ShapedChargeOf(TowLibraryName),
                     missile: new MissileDto { Guidance = GuidanceKind.Command, SpeedMps = 280f, MaxFlightSeconds = 15f, MinRangeM = 65f });
            Register(tkb, Rpg7Name, TkbRpg7Rocket, new DISEntityType { Kind = 2, Domain = 9, Category = 2 }, ShapedChargeOf(Rpg7LibraryName),
                     missile: Rocket(300f, 4.5f));

            // ⭐ M5 — the §5 table: name, id, DIS (domain 9 anti-armour / 8 anti-personnel, category 1 guided / 2 ballistic), warhead,
            //   penetration (mm RHA), damage, flight.
            var antiArmourRocket = new DISEntityType { Kind = 2, Domain = 9, Category = 2 };
            var antiArmourGuided = new DISEntityType { Kind = 2, Domain = 9, Category = 1 };
            Register(tkb, Pg7vlName, TkbPg7vlRocket, antiArmourRocket, ShapedChargeOf(Rpg7LibraryName), 500f, 450f, Rocket(300f, 4.5f));
            Register(tkb, Pg7vrName, TkbPg7vrRocket, antiArmourRocket, ShapedChargeOf(Rpg7LibraryName, tandem: true), 650f, 500f, Rocket(280f, 4.5f));
            Register(tkb, LawName, TkbLawRocket, antiArmourRocket, ShapedChargeOf(Rpg7LibraryName), 300f, 350f, Rocket(145f, 3f));
            Register(tkb, NlawName, TkbNlawMissile, antiArmourGuided, ShapedChargeOf(Rpg7LibraryName), 500f, 900f,
                     new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 200f, MaxFlightSeconds = 5f, MinRangeM = 20f });
            Register(tkb, JavelinName, TkbJavelinMissile, antiArmourGuided, ShapedChargeOf(TowLibraryName, tandem: true), 760f, 1500f,
                     new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 140f, MaxFlightSeconds = 20f, MinRangeM = 65f,
                                      TopAttack = true, TopAttackMinRangeM = 150f, CruiseAltitudeM = 150f, DiveAngleDeg = 60f });
            Register(tkb, KornetName, TkbKornetMissile, antiArmourGuided, ShapedChargeOf(TowLibraryName, tandem: true), 1100f, 2000f,
                     new MissileDto { Guidance = GuidanceKind.Command, SpeedMps = 300f, MaxFlightSeconds = 20f, MinRangeM = 100f });
            Register(tkb, FpvWarheadName, TkbFpvWarhead, antiArmourGuided, ShapedChargeOf(Rpg7LibraryName), 400f, 450f);
            Register(tkb, TankHeatMpName, TkbTankHeatMp, new DISEntityType { Kind = 2, Domain = 9, Category = 2 }, ShapedChargeOf(TankHeatLibraryName), 600f, 1200f);
            Register(tkb, TankHeFragName, TkbTankHeFrag, new DISEntityType { Kind = 2, Domain = 8, Category = 2 }, LibraryWarhead(TankHeLibraryName), 50f, 600f);
            Register(tkb, HmgApName, TkbHmgAp, new DISEntityType { Kind = 2, Domain = 9, Category = 2 }, null, 25f, 60f);
            Register(tkb, Ball762Name, Tkb762Ball, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });
            Register(tkb, Tracer762Name, Tkb762Tracer, new DISEntityType { Kind = 2, Domain = 8, Category = 2 });
        }

        /// <summary>⭐ M5 — an unguided rocket's flight: its speed and how long until it self-destructs.</summary>
        private static MissileDto Rocket(float speed, float seconds)
            => new() { Guidance = GuidanceKind.Unguided, SpeedMps = speed, MaxFlightSeconds = seconds };

        /// <summary>⭐ M3 — the reference-library entries the RPG and TOW warheads are copied from.</summary>
        public const string Rpg7LibraryName = "PG-7V HEAT", TowLibraryName = "TOW-2 HEAT";

        /// <summary>⭐ M3 — the library's generated warhead for <paramref name="libraryName"/>, as a template's own (a shaped charge).</summary>
        private static WarheadDto? ShapedChargeOf(string libraryName, bool tandem = false)
            => LibraryWarhead(libraryName) is { } w ? w with { Kind = WarheadKind.ShapedCharge, Tandem = tandem } : null;

        /// <summary>⭐ M5 — the library's generated warhead for <paramref name="libraryName"/>, as is (an HE round keeps its kind).</summary>
        private static WarheadDto? LibraryWarhead(string libraryName)
            => Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(libraryName, out var r) && r.Warhead != null ? r.Warhead.Dto : null;

        private static void Register(ITkbDatabase tkb, string name, int type, DISEntityType dis, WarheadDto? warhead = null,
                                     float penetrationMm = 0f, float damage = 0f, MissileDto? missile = null)
        {
            var t = new TkbTemplate(name, type) { DisType = dis };
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString(), HideFromPalette = true });
            if (BuiltInLooks.TryGetValue(type, out var look)) t.AddDescriptor(look);   // ⭐ P1 — how its round looks
            if (warhead != null) t.AddDescriptor(warhead);
            // ⭐ M5 — the round's own numbers (generic: from any weapon), what CombatTkb.RoundOf reads for a loadout's other rounds
            if (penetrationMm > 0f || damage > 0f)
                t.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = penetrationMm, Damage = damage });
            if (missile != null) t.AddDescriptor(missile);
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
                // ⭐ M5 — the rockets, the missiles and the tank rounds are seen; the HMG's AP and the 7.62 ball are bullets (no look)
                [TkbPg7vlRocket] = new() { Shape = MunitionShape.Rocket, Length = 1.0f, Diameter = 0.093f, ColorHex = "#5C5A3E", SmokeTrail = true },
                [TkbPg7vrRocket] = new() { Shape = MunitionShape.Rocket, Length = 1.13f, Diameter = 0.105f, ColorHex = "#5C5A3E", SmokeTrail = true },
                [TkbLawRocket] = new() { Shape = MunitionShape.Rocket, Length = 0.5f, Diameter = 0.066f, ColorHex = "#4B5320", SmokeTrail = true },
                [TkbNlawMissile] = new() { Shape = MunitionShape.Missile, Length = 1.0f, Diameter = 0.15f, ColorHex = "#4B5320", SmokeTrail = true },
                [TkbJavelinMissile] = new() { Shape = MunitionShape.Missile, Length = 1.1f, Diameter = 0.127f, ColorHex = "#6B6E4A", SmokeTrail = true },
                [TkbKornetMissile] = new() { Shape = MunitionShape.Missile, Length = 1.2f, Diameter = 0.152f, ColorHex = "#5A5E3A", SmokeTrail = true },
                [TkbTankHeatMp] = new() { Shape = MunitionShape.Shell, Length = 0.98f, Diameter = 0.12f, ColorHex = "#3A3A3A", Tracer = true, TracerColorHex = "#FFB050" },
                [TkbTankHeFrag] = new() { Shape = MunitionShape.Shell, Length = 0.98f, Diameter = 0.12f, ColorHex = "#5A5E3A", Tracer = true, TracerColorHex = "#FFB050" },
                [Tkb762Tracer] = new() { Shape = MunitionShape.Bullet, Length = 0.033f, Diameter = 0.0078f, Tracer = true, TracerColorHex = "#FF6A30" },
            };

        /// <summary>⭐ LR-3 — one of the built-in munition types above (6001–6021), whether or not it has a look: a round, never a body.</summary>
        public static bool IsBuiltIn(long type) => type >= TkbM67Grenade && type <= Tkb762Tracer;

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
