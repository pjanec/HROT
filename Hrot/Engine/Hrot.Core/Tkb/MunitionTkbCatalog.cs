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
        /// <summary>⭐ Air Ops slice 4 (DESIGN_Air_Operations §8) — a Mk-82-class 500 lb free-fall bomb: an indirect area warhead (the
        /// library's own <c>"Mk-82 bomb"</c> entry, CE-3201: 94 kg TNT-equivalent in a 227 kg case), dropped by the Release profile.</summary>
        public const int TkbMk82Bomb = 6022;
        /// <summary>⭐ Air Ops slice 6a (AW-2, DESIGN_Air_Operations §7a) — the A-10's GAU-8 30 mm HEI: the library's <c>"30mm HE"</c> warhead
        /// (an area burst against men and soft vehicles).</summary>
        public const int TkbGau8Hei = 6023;
        /// <summary>⭐ Air Ops 6a (AW-2) — the GAU-8's armour-piercing round: the library's nearest penetrator, <c>"30mm APFSDS"</c>.</summary>
        public const int TkbGau8Api = 6024;
        /// <summary>⭐ Air Ops 6a (AW-4) — a Hydra-70-class UNGUIDED 70 mm rocket with an HE warhead (the library's <c>"70mm rocket HE"</c>,
        /// public M151 figures): fast and flat, scattered by its pod's dispersion.</summary>
        public const int TkbHydra70Rocket = 6025;
        /// <summary>⭐ Indirect fire IF-1 (I7, IF-A; DESIGN_Indirect_Fire §4) — the 60 mm mortar's HE bomb (the library's <c>"60mm mortar HE"</c>, BY
        /// NAME like the 81 mm), with its charges.</summary>
        public const int Tkb60mmMortarHe = 6026;
        /// <summary>⭐ IF-1 (I7) — the 120 mm mortar's HE bomb (library <c>"120mm mortar HE"</c>, by name), with its charges.</summary>
        public const int Tkb120mmMortarHe = 6027;
        /// <summary>⭐ IF-1 (I7) — the 105 mm howitzer's HE shell (library <c>"105mm howitzer HE"</c>, by name), with its charges.</summary>
        public const int Tkb105mmHowitzerHe = 6028;
        /// <summary>⭐ IF-1 (I7) — the 155 mm howitzer's HE shell (library <c>"155mm howitzer HE"</c>, by name), with its charges.</summary>
        public const int Tkb155mmHowitzerHe = 6029;
        /// <summary>⭐ <c>CE-2187</c> (DESIGN_Missiles_And_Ammo §3c, Weapon Systems §5c) — a Stinger-class MANPADS missile: an IR seeker
        /// (cone, flare rejection), air targets only, a proximity-fused fragmentation warhead (the library's <c>"Stinger warhead"</c>).</summary>
        public const int TkbStingerMissile = 6030;
        /// <summary>⭐ <c>CE-2187</c> — an Igla-class MANPADS missile: slower, a smaller warhead, an older seeker (more easily decoyed).</summary>
        public const int TkbIglaMissile = 6031;
        /// <summary>⭐ <c>CE-2190</c> (Weapon Systems §5c) — the ZU-23-2's 23 mm HE-I (the library's <c>"23mm HEI"</c>): no proximity fuze.</summary>
        public const int Tkb23mmHei = 6032;
        /// <summary>⭐ <c>CE-2190</c> — the ZU-23-2's 23 mm API (the library's <c>"23mm API"</c>).</summary>
        public const int Tkb23mmApi = 6033;
        /// <summary>⭐ <c>CE-2188</c> / <c>CE-1111</c> (BS-1-DESIGN §5.4c) — an aircraft's IR decoy FLARE: an expendable (DIS 8.2.2), one
        /// round of the dispenser mount's loadout; it fools an <c>InfraRed</c> seeker.</summary>
        public const int TkbFlare = 6034;
        /// <summary>⭐ <c>CE-2188</c> / <c>CE-1111</c> — a CHAFF bundle: an expendable (DIS 8.2.1); it fools a <c>Radar</c> seeker.</summary>
        public const int TkbChaff = 6035;

        public const string Mortar60mmHeName = "60mm mortar HE", Mortar120mmHeName = "120mm mortar HE",
                            Howitzer105HeName = "105mm howitzer HE", Howitzer155HeName = "155mm howitzer HE";

        /// <summary>
        /// ⭐ IF-1 (I7, IF-B) — the CHARGES, vacuum-equivalent: <c>v = √(g · R_max)</c> per charge (m/s), from public maximum ranges per charge
        /// (rounded): the M224 60 mm (≈ 0.42 / 1.0 / 1.75 / 2.6 / 3.49 km), the M252 81 mm (1.0 / 2.2 / 3.4 / 4.5 / 5.6 km), the M120 120 mm
        /// (1.5 / 3.0 / 4.5 / 5.9 / 7.2 km), the M119 105 mm (3.5 / 4.5 / 5.5 / 6.8 / 8.0 / 9.8 / 11.5 km) and the M109 155 mm
        /// (4.0 / 5.5 / 7.0 / 9.0 / 11.0 / 13.0 / 15.5 / 18.1 km). ⚠ Range per charge is approximate — tuning starts here.
        /// </summary>
        public static System.Collections.Generic.List<float> ChargesFor(params float[] maxRangesKm)
        {
            var speeds = new System.Collections.Generic.List<float>(maxRangesKm.Length);
            foreach (float km in maxRangesKm) speeds.Add(MathF.Round(MathF.Sqrt(9.81f * km * 1000f), 1));
            return speeds;
        }

        public static readonly float[] Mortar60RangesKm = { 0.42f, 1.0f, 1.75f, 2.6f, 3.49f };
        public static readonly float[] Mortar81RangesKm = { 1.0f, 2.2f, 3.4f, 4.5f, 5.6f };
        public static readonly float[] Mortar120RangesKm = { 1.5f, 3.0f, 4.5f, 5.9f, 7.2f };
        public static readonly float[] Howitzer105RangesKm = { 3.5f, 4.5f, 5.5f, 6.8f, 8.0f, 9.8f, 11.5f };
        public static readonly float[] Howitzer155RangesKm = { 4.0f, 5.5f, 7.0f, 9.0f, 11.0f, 13.0f, 15.5f, 18.1f };

        public const string Pg7vlName = "PG-7VL rocket", Pg7vrName = "PG-7VR rocket", LawName = "M72 LAW rocket", NlawName = "NLAW missile",
                            JavelinName = "Javelin missile", KornetName = "Kornet missile", FpvWarheadName = "FPV warhead",
                            TankHeatMpName = "120mm HEAT-MP round", TankHeFragName = "tank HE-FRAG round", HmgApName = "12.7mm AP round",
                            Ball762Name = "7.62x51 ball", Tracer762Name = "7.62x51 tracer", Mk82Name = "Mk-82 bomb",
                            Gau8HeiName = "GAU-8 30mm HEI", Gau8ApiName = "GAU-8 30mm API", Hydra70Name = "Hydra-70 HE rocket",
                            StingerName = "Stinger missile", IglaName = "Igla missile", Zu23HeiName = "ZU-23 23mm HE-I", Zu23ApiName = "ZU-23 23mm API",
                            FlareName = "IR decoy flare", ChaffName = "chaff bundle";

        /// <summary>⭐ <c>CE-2187</c> / <c>CE-2190</c> — the library entries the air-defence rounds take their numbers from.</summary>
        public const string StingerLibraryName = "Stinger warhead", IglaLibraryName = "Igla warhead",
                            Zu23HeiLibraryName = "23mm HEI", Zu23ApiLibraryName = "23mm API";

        /// <summary>⭐ <c>CE-2187</c> (§3c, AD2) — the SAMs' proximity fuze reach (m).</summary>
        public const float SamProximityFuzeM = 4f;

        /// <summary>⭐ Air Ops 6a — the reference-library entries the air weapons' rounds take their numbers from (the template's own names
        /// match no entry, so nothing is picked up by name — LR-1).</summary>
        public const string Gau8HeiLibraryName = "30mm HE", Gau8ApiLibraryName = "30mm APFSDS", Hydra70LibraryName = "70mm rocket HE";

        /// <summary>⭐ M5 — the reference-library entries the new warheads are copied from (the nearest real one; the template's numbers win).</summary>
        public const string TankHeatLibraryName = "120mm HEAT", TankHeLibraryName = "125mm HE-frag";

        /// <summary>Registers the munition types. ⚠ Once per database (<c>TkbDatabase.Register</c> throws on a duplicate).</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            // ⭐⭐ CE-3213 (R-3011) — every round carries its FULL SISO DIS munition type, cited beside it from SISO-REF-010 v37 (2026-05-25,
            //   the XML the opendis7 generator ships): kind.domain.country.category.subcategory.specific. A round the catalog NAMES
            //   exactly gets its specific; a generic one stops at its calibre; a field the enumeration does not give stays 0 (a
            //   wildcard) — ⛔ never invented. ⚠ The coarse domains of 2026-10-08 were WRONG (anti-armour coded 9, the SAMs 2): SISO's
            //   munition domain is 1 anti-air · 2 anti-armour · 8 anti-personnel · 9 battlefield support.
            Register(tkb, M67GrenadeName, TkbM67Grenade, Dis(9, Usa, 2, 85, 1));                 // "Grenade, Hand / Fragmentation M67"
            Register(tkb, Mortar81mmHeName, Tkb81mmMortarHe, Dis(9, Usa, 2, 8),                   // "81 mm" (battlefield support)
                     charges: ChargesFor(Mortar81RangesKm));   // ⭐ IF-1 (I7) — charges only (no pen/damage: the MortarTeam 2005 reads nothing new)
            // ⭐ LR-1 — DIS kind 2 (munition), domain 9 anti-armour / 8 anti-personnel, category 2 ballistic / 1 guided
            Register(tkb, Ball556Name, Tkb556Ball, Dis(8, Usa, 2, 1, 4));                         // "5.56 mm / 5.56 x 45"
            Register(tkb, Tracer556Name, Tkb556Tracer, Dis(8, Usa, 2, 1));                        // "5.56 mm"
            Register(tkb, Apds25Name, Tkb25mmApdsT, Dis(2, Usa, 2, 3, 2));                        // "25 mm / M791 APDS-T" (the Bradley's)
            Register(tkb, Apfsds120Name, Tkb120mmApfsds, Dis(2, Usa, 2, 13));                     // "120 mm"
            Register(tkb, Apfsds125Name, Tkb125mmApfsds, Dis(2, Russia, 2, 11));                  // "125 mm"
            // ⭐ M5 — the TOW and the RPG gain their flight (§5): the TOW a command-guided 280 m/s for 15 s (it flew at the 1875 m/s the
            //   NED builder derives from its range), the PG-7V an unguided 300 m/s (its mount's own muzzle speed) for 4.5 s (self-destruct).
            //   Their penetration and damage stay on the mounts (LR-1: every mount states its own).
            Register(tkb, TowName, TkbTowMissile, Dis(2, Usa, 1, 1, 3), ShapedChargeOf(TowLibraryName),   // "BGM-71 TOW / BGM-71D TOW 2"
                     missile: new MissileDto { Guidance = GuidanceKind.Command, SpeedMps = 280f, MaxFlightSeconds = 15f, MinRangeM = 65f,
                                               TargetDomain = TargetDomain.Ground });   // ⭐ CE-2187 (AD1) — the ATGMs: ground targets only
            Register(tkb, Rpg7Name, TkbRpg7Rocket, Dis(2, Russia, 2, 8), ShapedChargeOf(Rpg7LibraryName),   // "85 mm" (the PG-7V's warhead)
                     missile: Rocket(300f, 4.5f));

            // ⭐ M5 — the §5 table: name, id, DIS (domain 9 anti-armour / 8 anti-personnel, category 1 guided / 2 ballistic), warhead,
            //   penetration (mm RHA), damage, flight.
            Register(tkb, Pg7vlName, TkbPg7vlRocket, Dis(2, Russia, 2, 27, 1), ShapedChargeOf(Rpg7LibraryName), 500f, 450f, Rocket(300f, 4.5f));   // "93 mm / PG-7VL HEAT"
            Register(tkb, Pg7vrName, TkbPg7vrRocket, Dis(2, Russia, 2, 23, 5), ShapedChargeOf(Rpg7LibraryName, tandem: true), 650f, 500f, Rocket(280f, 4.5f));   // "105 mm / PG-7VR HEAT Tandem"
            Register(tkb, LawName, TkbLawRocket, Dis(2, Usa, 2, 5), ShapedChargeOf(Rpg7LibraryName), 300f, 350f, Rocket(145f, 3f));   // "66 mm 72 LAW"
            Register(tkb, NlawName, TkbNlawMissile, Dis(2, Sweden, 1, 2), ShapedChargeOf(Rpg7LibraryName), 500f, 900f,   // "NLAW/MBT-LAW"
                     new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 200f, MaxFlightSeconds = 5f, MinRangeM = 20f, TargetDomain = TargetDomain.Ground,
                                      Overfly = true, OverflyHeightM = 1f });   // ⭐ CE-3198 — the overfly top attack (§3b)
            Register(tkb, JavelinName, TkbJavelinMissile, Dis(2, Usa, 1, 8), ShapedChargeOf(TowLibraryName, tandem: true), 760f, 1500f,   // "Javelin"
                     new MissileDto { Guidance = GuidanceKind.FireAndForget, SpeedMps = 140f, MaxFlightSeconds = 20f, MinRangeM = 65f, TargetDomain = TargetDomain.Ground,
                                      TopAttack = true, TopAttackMinRangeM = 150f, CruiseAltitudeM = 150f, DiveAngleDeg = 60f,
                                      // ⭐ CE-2222 — a missile, not a 30 °/s airframe: at 120 °/s and a 45° climb the profile FITS from its own
                                      //   150 m (apex ~48 m at 200 m); at the engine's 30 °/s it fitted only beyond ~610 m, so every closer shot flew direct
                                      TurnRateDegPerSec = 120f, ClimbAngleDeg = 45f });
            Register(tkb, KornetName, TkbKornetMissile, Dis(2, Russia, 1, 15), ShapedChargeOf(TowLibraryName, tandem: true), 1100f, 2000f,   // "AT-14"
                     new MissileDto { Guidance = GuidanceKind.Command, SpeedMps = 300f, MaxFlightSeconds = 20f, MinRangeM = 100f, TargetDomain = TargetDomain.Ground });
            Register(tkb, FpvWarheadName, TkbFpvWarhead, Dis(2, 0, 1), ShapedChargeOf(Rpg7LibraryName), 400f, 450f);   // no entry: anti-armour, guided
            Register(tkb, TankHeatMpName, TkbTankHeatMp, Dis(2, Usa, 2, 13, 6), ShapedChargeOf(TankHeatLibraryName), 600f, 1200f);   // "120 mm / M830A1 HEAT-MP-T"
            Register(tkb, TankHeFragName, TkbTankHeFrag, Dis(9, 0, 2), LibraryWarhead(TankHeLibraryName), 50f, 600f);   // no calibre named: battlefield support, ballistic
            Register(tkb, HmgApName, TkbHmgAp, Dis(8, Usa, 2, 5), null, 25f, 60f);               // "12.7 mm"
            Register(tkb, Ball762Name, Tkb762Ball, Dis(8, Usa, 2, 2));                            // "7.62 mm"
            Register(tkb, Tracer762Name, Tkb762Tracer, Dis(8, Usa, 2, 2));                        // "7.62 mm"
            // ⭐ Air Ops slice 4 — a free-fall bomb: indirect + area from the library (falls under gravity, bursts where it lands)
            Register(tkb, Mk82Name, TkbMk82Bomb, Dis(9, Usa, 2, 73),                                // "Mk-82" (ballistic)
                     LibraryWarhead(Mk82Name), 50f, 1200f);   // CE-3201: the library's own bomb entry (indirect, 94 kg)
            // ⭐ Air Ops 6a (AW-2, AW-4) — the air weapons' rounds: every number from the library entry (its generic pair: penetration
            //   and damage; its generated warhead); the rocket's flight is its own (700 m/s, self-destruct after 6 s, ~4 km).
            Register(tkb, Gau8HeiName, TkbGau8Hei, Dis(2, Usa, 2, 4),     // "30 mm" (SISO lists only the PGU-14 / PGU-13 belts, not each round)
                     LibraryWarhead(Gau8HeiLibraryName), LibraryPenetration(Gau8HeiLibraryName), LibraryDamage(Gau8HeiLibraryName));
            Register(tkb, Gau8ApiName, TkbGau8Api, Dis(2, Usa, 2, 4),     // "30 mm"
                     null, LibraryPenetration(Gau8ApiLibraryName), LibraryDamage(Gau8ApiLibraryName));
            Register(tkb, Hydra70Name, TkbHydra70Rocket, Dis(9, Usa, 2, 21),   // "Hydra 70 Rocket System (70 mm, 2.75 in)"
                     LibraryWarhead(Hydra70LibraryName), LibraryPenetration(Hydra70LibraryName), LibraryDamage(Hydra70LibraryName),
                     Rocket(700f, 6f));
            // ⭐ IF-1 (I7, IF-A) — the indirect rounds: warheads BY NAME from the library (like the 81 mm), their charges on the generic profile
            Register(tkb, Mortar60mmHeName, Tkb60mmMortarHe, Dis(9, Usa, 2, 5), charges: ChargesFor(Mortar60RangesKm));            // "60 mm"
            Register(tkb, Mortar120mmHeName, Tkb120mmMortarHe, Dis(9, Usa, 2, 11), charges: ChargesFor(Mortar120RangesKm));       // "120 mm"
            Register(tkb, Howitzer105HeName, Tkb105mmHowitzerHe, Dis(9, Usa, 2, 10), charges: ChargesFor(Howitzer105RangesKm));   // "105 mm"
            Register(tkb, Howitzer155HeName, Tkb155mmHowitzerHe, Dis(9, Usa, 2, 14), charges: ChargesFor(Howitzer155RangesKm));   // "155 mm"

            // ⭐⭐ CE-2187 (DESIGN_Missiles_And_Ammo §3c, AD1/AD2) — the MANPADS: an IR seeker, AIR targets only, a proximity-fused
            //   fragmentation warhead (the library's, its fuze switched to proximity). Public figures, rounded: Stinger Mach ~2.2, 8 km
            //   flight, 200 m minimum; Igla ~570 m/s, 500 m minimum, an older seeker (more easily decoyed). The cone and rejections are
            //   starter values — tuning starts here.
            Register(tkb, StingerName, TkbStingerMissile, Dis(1, Usa, 1, 15), ProximityFused(StingerLibraryName), 0f, LibraryDamage(StingerLibraryName),   // "Stinger" (anti-air)
                     new MissileDto { Guidance = GuidanceKind.InfraRed, SpeedMps = 700f, MaxFlightSeconds = 12f, MinRangeM = 200f,
                                      TargetDomain = TargetDomain.Air, SeekerConeDeg = 20f, FlareRejection = 0.6f });
            Register(tkb, IglaName, TkbIglaMissile, Dis(1, Russia, 1, 30), ProximityFused(IglaLibraryName), 0f, LibraryDamage(IglaLibraryName),   // "SA-18 Grouse (9M39)"
                     new MissileDto { Guidance = GuidanceKind.InfraRed, SpeedMps = 570f, MaxFlightSeconds = 14f, MinRangeM = 500f,
                                      TargetDomain = TargetDomain.Air, SeekerConeDeg = 20f, FlareRejection = 0.4f });
            // ⭐ CE-2190 (Weapon Systems §5c, AD5) — the ZU-23-2's rounds: plain (no proximity fuze), AT2's lead aims them
            Register(tkb, Zu23HeiName, Tkb23mmHei, Dis(1, Russia, 2, 3, 5),   // "23 mm / 23 mm ZSU AAA HEI OFZ" (the ZU-23's 23×152 round)
                     LibraryWarhead(Zu23HeiLibraryName), LibraryPenetration(Zu23HeiLibraryName), LibraryDamage(Zu23HeiLibraryName));
            Register(tkb, Zu23ApiName, Tkb23mmApi, Dis(1, Russia, 2, 3),      // "23 mm"
                     null, LibraryPenetration(Zu23ApiLibraryName), LibraryDamage(Zu23ApiLibraryName));

            // ⭐⭐ CE-2188 / CE-1111 (BS-1-DESIGN §5.4c) — the EXPENDABLES a dispenser mount carries: DIS kind 8 (expendable), domain 2
            //   (air), category 2 flare / 1 chaff — the kind decides which seeker each fools (Munitions.DecoyOf). A flare burns ~5 s and
            //   falls; chaff blooms and hangs longer, sinking slowly. Starter values — tuning starts here.
            // ⭐ CE-3213 — SISO-REF-010 v37: kind 8 (Expendable), domain 2 (Air), category 2 "Flare" / 1 "Chaff" (enumeration 25); a generic
            //   decoy names no country or type ⇒ 0.
            Register(tkb, FlareName, TkbFlare, new DISEntityType { Kind = 8, Domain = 2, Category = 2 },
                     expendable: new ExpendableDto { LifeSeconds = 5f, SinkMps = 8f });
            Register(tkb, ChaffName, TkbChaff, new DISEntityType { Kind = 8, Domain = 2, Category = 1 },
                     expendable: new ExpendableDto { LifeSeconds = 8f, SinkMps = 2f });
        }

        // ⭐ CE-3213 — the SISO-REF-010 countries the munition codes use (enumeration 29, Country).
        private const ushort Usa = 225, Russia = 222, Sweden = 205;

        /// <summary>⭐ <c>CE-3213</c> — a munition's DIS type (kind 2): domain (1 anti-air · 2 anti-armour · 8 anti-personnel · 9 battlefield
        /// support), country, category (1 guided · 2 ballistic), subcategory and specific, as SISO-REF-010 v37 enumerates them.</summary>
        private static DISEntityType Dis(byte domain, ushort country, byte category, byte subcategory = 0, byte specific = 0)
            => new() { Kind = 2, Domain = domain, Country = country, Category = category, Subcategory = subcategory, Specific = specific };

        /// <summary>⭐ <c>CE-2187</c> — the library's warhead for <paramref name="libraryName"/>, its fuze a proximity fuze of
        /// <see cref="SamProximityFuzeM"/>.</summary>
        private static WarheadDto? ProximityFused(string libraryName)
            => LibraryWarhead(libraryName) is { } w ? w with { Fuze = FuzeKind.Proximity, ProximityFuzeM = SamProximityFuzeM } : null;

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

        /// <summary>⭐ Air Ops 6a — the library entry's generic penetration (mm RHA) and damage, so a round's numbers have one source.</summary>
        public static float LibraryPenetration(string libraryName)
            => Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(libraryName, out var r) ? r.Generic.PenetrationMm : 0f;

        /// <inheritdoc cref="LibraryPenetration"/>
        public static float LibraryDamage(string libraryName)
            => Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(libraryName, out var r) ? r.Generic.Damage : 0f;

        private static void Register(ITkbDatabase tkb, string name, int type, DISEntityType dis, WarheadDto? warhead = null,
                                     float penetrationMm = 0f, float damage = 0f, MissileDto? missile = null,
                                     System.Collections.Generic.List<float>? charges = null, ExpendableDto? expendable = null)
        {
            var t = new TkbTemplate(name, type) { DisType = dis };
            t.AddDescriptor(new TkbMasterDto { CustomName = name, DisType = dis.ToString(), HideFromPalette = true });
            if (BuiltInLooks.TryGetValue(type, out var look)) t.AddDescriptor(look);   // ⭐ P1 — how its round looks
            if (warhead != null) t.AddDescriptor(warhead);
            // ⭐ M5 — the round's own numbers (generic: from any weapon), what CombatTkb.RoundOf reads for a loadout's other rounds
            if (penetrationMm > 0f || damage > 0f || charges != null)
                t.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = penetrationMm, Damage = damage,
                                                              ChargeSpeedsMps = charges ?? new() });   // ⭐ IF-1 (I7) — the charges
            if (missile != null) t.AddDescriptor(missile);
            if (expendable != null) t.AddDescriptor(expendable);   // ⭐ CE-2188 (§5.4c) — a flare / chaff: its decoy's life and sink
            // ⭐ CE-2186 (W2, R-2020) — what one round costs, relative: the weapon brain picks the CHEAPEST round that is enough
            if (Costs.TryGetValue(type, out float cost)) t.AddDescriptor(new MunitionCostDto { RelativeCost = cost });
            tkb.Register(t);
        }

        /// <summary>
        /// ⭐ <c>CE-2186</c> (W2, R-2020; docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §2c) — each built-in munition's RELATIVE
        /// cost: tiers by class, ordered by public unit prices and compressed (a rifle round 1, a guided missile or a bomb in the hundreds)
        /// — only the ORDER matters to <c>AmmoChoice.Best</c>, which picks the cheapest round that is enough.
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyDictionary<long, float> Costs =
            new System.Collections.Generic.Dictionary<long, float>
            {
                [Tkb556Ball] = 1, [Tkb556Tracer] = 1, [Tkb762Ball] = 1, [Tkb762Tracer] = 1,     // small arms
                [TkbHmgAp] = 3,                                                                  // heavy MG
                [TkbM67Grenade] = 5,                                                             // hand grenade
                [TkbGau8Hei] = 10, [TkbGau8Api] = 10, [Tkb25mmApdsT] = 20,                        // autocannon
                [TkbRpg7Rocket] = 30, [TkbLawRocket] = 30, [TkbPg7vlRocket] = 40, [TkbPg7vrRocket] = 60,   // unguided rockets
                [Tkb60mmMortarHe] = 30, [Tkb81mmMortarHe] = 50, [TkbTankHeFrag] = 60, [Tkb120mmMortarHe] = 80,
                [Tkb105mmHowitzerHe] = 80, [TkbTankHeatMp] = 80, [Tkb120mmApfsds] = 100, [Tkb125mmApfsds] = 100,
                [Tkb155mmHowitzerHe] = 100,                                                      // shells
                [TkbFpvWarhead] = 50, [TkbHydra70Rocket] = 100, [TkbTowMissile] = 300, [TkbNlawMissile] = 300,
                [TkbKornetMissile] = 300, [TkbMk82Bomb] = 300, [TkbJavelinMissile] = 500,         // guided / air
                [Tkb23mmHei] = 15, [Tkb23mmApi] = 15,                                            // ⭐ CE-2190 — the ZU-23's rounds
                [TkbIglaMissile] = 400, [TkbStingerMissile] = 600,                               // ⭐ CE-2187 — the MANPADS
                [TkbFlare] = 2, [TkbChaff] = 2,                                                  // ⭐ CE-2188 — expendables (never chosen to hurt)
            };

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
                [TkbMk82Bomb] = new() { Shape = MunitionShape.Bomb, Length = 2.2f, Diameter = 0.27f, ColorHex = "#4B5320" },
                // ⭐ Air Ops 6a — the GAU-8's rounds trace (a gun run is seen); the Hydra is a rocket with a smoke trail
                [TkbGau8Hei] = new() { Shape = MunitionShape.Shell, Length = 0.29f, Diameter = 0.03f, ColorHex = "#5A5E3A", Tracer = true, TracerColorHex = "#FF8A30" },
                [TkbGau8Api] = new() { Shape = MunitionShape.Shell, Length = 0.29f, Diameter = 0.03f, ColorHex = "#2F2F2F", Tracer = true, TracerColorHex = "#FF8A30" },
                [TkbHydra70Rocket] = new() { Shape = MunitionShape.Rocket, Length = 1.4f, Diameter = 0.07f, ColorHex = "#6B6E4A", SmokeTrail = true },
                // ⭐ IF-1 (I7) — mortar bombs and shells in flight
                [Tkb60mmMortarHe] = new() { Shape = MunitionShape.Bomb, Length = 0.35f, Diameter = 0.06f, ColorHex = "#5A5E3A" },
                [Tkb120mmMortarHe] = new() { Shape = MunitionShape.Bomb, Length = 0.7f, Diameter = 0.12f, ColorHex = "#5A5E3A" },
                [Tkb105mmHowitzerHe] = new() { Shape = MunitionShape.Shell, Length = 0.5f, Diameter = 0.105f, ColorHex = "#5A5E3A" },
                [Tkb155mmHowitzerHe] = new() { Shape = MunitionShape.Shell, Length = 0.7f, Diameter = 0.155f, ColorHex = "#5A5E3A" },
                // ⭐ CE-2187 / CE-2190 — the MANPADS missiles trail smoke; the 23 mm traces
                [TkbStingerMissile] = new() { Shape = MunitionShape.Missile, Length = 1.52f, Diameter = 0.07f, ColorHex = "#6B6E4A", SmokeTrail = true },
                [TkbIglaMissile] = new() { Shape = MunitionShape.Missile, Length = 1.57f, Diameter = 0.072f, ColorHex = "#5A5E3A", SmokeTrail = true },
                [Tkb23mmHei] = new() { Shape = MunitionShape.Shell, Length = 0.2f, Diameter = 0.023f, ColorHex = "#5A5E3A", Tracer = true, TracerColorHex = "#FF8A30" },
                [Tkb23mmApi] = new() { Shape = MunitionShape.Shell, Length = 0.2f, Diameter = 0.023f, ColorHex = "#2F2F2F", Tracer = true, TracerColorHex = "#FF8A30" },
            };

        /// <summary>⭐ LR-3 — one of the built-in munition types above (6001–6035), whether or not it has a look: a round, never a body.</summary>
        public static bool IsBuiltIn(long type) => type >= TkbM67Grenade && type <= TkbChaff;

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
