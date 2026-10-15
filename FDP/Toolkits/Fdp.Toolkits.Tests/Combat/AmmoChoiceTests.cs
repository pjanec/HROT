using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ M8 — the three rounds of a tank gun, for the loadout rails (<c>DESIGN_Missiles_And_Ammo.md</c> §2, §5): APFSDS (kinetic,
    /// 600 mm, 1100), HEAT-MP (shaped charge, 600 mm, 1200, a small area effect), HE-FRAG (kinetic 50 mm, 600, fragments + blast).
    /// </summary>
    internal static class M8Rounds
    {
        public const long Apfsds = 9001, Heat = 9002, He = 9003, TankGunType = 9100;

        public static void Register(TkbDatabase db)
        {
            foreach (var t in Templates()) db.Register(t);
        }

        public static TkbTemplate[] Templates() => new[]
        {
            Round("APFSDS", Apfsds, 600, 1100, null),
            Round("HEAT-MP", Heat, 600, 1200, new WarheadDto
                { Kind = WarheadKind.ShapedCharge, FragmentRadiusM = 5, FragmentPenetrationMm = 15, FragmentDamage = 40 }),
            Round("HE-FRAG", He, 50, 600, new WarheadDto
                { Kind = WarheadKind.HighExplosive, FragmentRadiusM = 15, FragmentPenetrationMm = 15, FragmentDamage = 200,
                  BlastLethalRadiusM = 3, BlastInjuryRadiusM = 8, BlastLethalDamage = 200 }),
        };

        private static TkbTemplate Round(string name, long type, float pen, float damage, WarheadDto? warhead)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = pen, Damage = damage });
            if (warhead != null) t.AddDescriptor(warhead);
            return t;
        }

        /// <summary>The tank gun: its own round APFSDS (as before M8), and a loadout of all three, APFSDS loaded.</summary>
        public static WeaponMountDto TankGun(bool withLoadout = true) => new()
        {
            AmmoGuid = unchecked((ulong)Apfsds), DamagePerHit = 1100, InitialAmmunition = 40,
            Loadout = withLoadout
                ? new() { new AmmoLoadDto { AmmoGuid = unchecked((ulong)Apfsds), Rounds = 20 },
                          new AmmoLoadDto { AmmoGuid = unchecked((ulong)Heat), Rounds = 10 },
                          new AmmoLoadDto { AmmoGuid = unchecked((ulong)He), Rounds = 10 } }
                : new(),
        };
    }

    /// <summary>
    /// ⭐⭐ M8 (D-I, W-6) — <see cref="AmmoChoice"/>: the weapon brain picks the round with the ONE armour model.
    /// 📄 <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §2, §7.
    /// </summary>
    public sealed class AmmoChoiceTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private const long Tank = 9201, Man = 9202, Technical = 9203;

        public AmmoChoiceTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<PhysicsCollider>();
            var db = new TkbDatabase();
            M8Rounds.Register(db);
            db.Register(Target("T-72", Tank, new CombatPlatformDefDto
                { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150, ArmorFrontCe = 800 }));
            db.Register(Target("Rifleman", Man, new CombatPlatformDefDto { MaxHealth = 100 }));
            db.Register(Target("Technical", Technical, new CombatPlatformDefDto
                { MaxHealth = 400, ArmorFront = 10, ArmorSide = 10, ArmorRear = 10 }));
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        private static TkbTemplate Target(string name, long type, CombatPlatformDefDto platform)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(platform);
            return t;
        }

        private Entity Spawn(long type, Vector3 at, float health, float hull)
        {
            var e = _w.CreateEntity();
            // facing the shooter at the origin (−X): a level shot meets the FRONT
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) });
            _w.AddComponent(e, new Health { Current = health, Max = health });
            _w.AddComponent(e, new TkbIdentity { TkbType = type });
            if (hull > 0f) _w.AddComponent(e, new PhysicsCollider { Radius = 2f, Height = hull });
            return e;
        }

        private string Pick(Entity target)
        {
            var shooter = _w.CreateEntity();
            _w.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            var load = AmmoLoadout.From(M8Rounds.TankGun().Loadout);
            int best = AmmoChoice.Best(_w, load, M8Rounds.TankGun(), shooter, target);
            return load.AmmoAt(best) switch { M8Rounds.Apfsds => "APFSDS", M8Rounds.Heat => "HEAT", M8Rounds.He => "HE", _ => "?" };
        }

        /// <summary>
        /// ⭐⭐ M8 (D-I) — the tank gun picks APFSDS for a tank (600 mm defeats the 500 mm front; the HEAT jet meets 800 mm of
        /// composite there — the …Ce column — and HE does nothing), HE for a man (every round kills him; the one that also sprays the
        /// room around him wins the tie) and HE for a technical (every round kills it; HE's fragments get through 10 mm, HEAT's
        /// fragments do less).
        /// </summary>
        [Fact]
        public void M8_TheTankGun_PicksApfsdsForATank_HeForMenInABuilding_HeatOrHeForATechnical()
        {
            Assert.Equal("APFSDS", Pick(Spawn(Tank, new Vector3(800, 0, 0), 2500, 2.4f)));
            Assert.Equal("HE", Pick(Spawn(Man, new Vector3(300, 0, 0), 100, 0f)));
            Assert.Equal("HE", Pick(Spawn(Technical, new Vector3(300, 0, 0), 400, 2f)));
        }

        // ── ⭐⭐ CE-2186 (W2, R-2020, DESIGN_Missiles_And_Ammo §2b) — enough, not most ──────────────────────────────────────────────

        private const long He25 = 9301, Apds25 = 9302, Atgm = 9303, Mbt = 9304;

        /// <summary>A 25 mm HE (cost 15), a 25 mm APDS (20) and an ATGM (300, a big blast — so by value alone it wins every tie) on one
        /// mount; <paramref name="costs"/> false ⇒ the same rounds with no cost data.</summary>
        private (AmmoLoadout Load, WeaponMountDto Mount) CostedMount(bool costs = true)
        {
            var db = (TkbDatabase)_w.GetSingletonManaged<ITkbDatabase>()!;
            if (!db.TryGetByType(He25, out _))
            {
                TkbTemplate Round(string name, long type, float pen, float dmg, float cost, WarheadDto? wh)
                {
                    var t = new TkbTemplate(name, type);
                    t.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = pen, Damage = dmg });
                    if (wh != null) t.AddDescriptor(wh);
                    if (costs) t.AddDescriptor(new MunitionCostDto { RelativeCost = cost });
                    return t;
                }
                db.Register(Round("25mm HE", He25, 20, 150, 15, new WarheadDto
                    { Kind = WarheadKind.HighExplosive, FragmentRadiusM = 5, FragmentPenetrationMm = 5, FragmentDamage = 40 }));
                db.Register(Round("25mm APDS", Apds25, 90, 200, 20, null));
                db.Register(Round("ATGM", Atgm, 800, 1500, 300, new WarheadDto
                    { Kind = WarheadKind.ShapedCharge, FragmentRadiusM = 20, FragmentPenetrationMm = 20, FragmentDamage = 300,
                      BlastLethalRadiusM = 6, BlastInjuryRadiusM = 15, BlastLethalDamage = 600 }));
                db.Register(Target("M1", Mbt, new CombatPlatformDefDto
                    { MaxHealth = 3000, ArmorFront = 600, ArmorSide = 350, ArmorRear = 200, ArmorFrontCe = 700 }));
            }
            var mount = new WeaponMountDto
            {
                AmmoGuid = unchecked((ulong)He25), DamagePerHit = 150, InitialAmmunition = 100,
                Loadout = new() { new AmmoLoadDto { AmmoGuid = unchecked((ulong)He25), Rounds = 50 },
                                  new AmmoLoadDto { AmmoGuid = unchecked((ulong)Apds25), Rounds = 50 },
                                  new AmmoLoadDto { AmmoGuid = unchecked((ulong)Atgm), Rounds = 4 } },
            };
            return (AmmoLoadout.From(mount.Loadout), mount);
        }

        private long PickFor(Entity target, int rounds, bool costs = true)
        {
            var (load, mount) = CostedMount(costs);
            var shooter = _w.CreateEntity();
            _w.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            return load.AmmoAt(AmmoChoice.Best(_w, load, mount, shooter, target, rounds));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2186</c> — the weapon brain picks the CHEAPEST round that is enough for the burst: a rifleman draws the 25 mm HE (every
        /// round kills him), an M1 the ATGM (nothing cheaper gets through its front), a technical the cheaper 25 mm that kills it in the
        /// burst — never the ATGM. 🔴 Red-proof: the pick by value only ⇒ the rifleman draws the ATGM (its blast wins the area tie-break).
        /// </summary>
        [Fact]
        public void CE2186_TheCheapestRoundThatIsEnough_HeForAMan_TheAtgmForATank_A25mmForATechnical()
        {
            Assert.Equal(He25, PickFor(Spawn(Man, new Vector3(300, 0, 0), 100, 0f), rounds: 3));
            Assert.Equal(Atgm, PickFor(Spawn(Mbt, new Vector3(800, 0, 0), 3000, 2.4f), rounds: 1));
            long technical = PickFor(Spawn(Technical, new Vector3(300, 0, 0), 400, 2f), rounds: 5);
            Assert.NotEqual(Atgm, technical);
            var shooter = _w.CreateEntity();
            _w.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            Assert.True(AmmoChoice.Sufficient(_w, CostedMount().Mount, technical, shooter, Spawn(Technical, new Vector3(300, 0, 0), 400, 2f), 5));
        }

        /// <summary>⭐ <c>CE-2186</c> — a loadout whose rounds have no cost data picks exactly as before (by value, then area): the rifleman
        /// draws the ATGM, as the value-only rule always gave him.</summary>
        [Fact]
        public void CE2186_RoundsWithNoCost_PickAsBefore()
        {
            Assert.Equal(Atgm, PickFor(Spawn(Man, new Vector3(300, 0, 0), 100, 0f), rounds: 3, costs: false));
        }

        /// <summary>⭐ M8 — the value of a round is the expected damage of one hit on the face the shot meets, ÷ the target's maximum
        /// health, capped at 1: the APFSDS on the tank's front does 1100 / 2500; the HEAT does nothing there.</summary>
        [Fact]
        public void M8_TheValue_IsOneRoundsExpectedDamage_OverTheMaximum()
        {
            var shooter = _w.CreateEntity();
            _w.AddComponent(shooter, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            var tank = Spawn(Tank, new Vector3(800, 0, 0), 2500, 2.4f);
            Assert.Equal(1100f / 2500f, AmmoChoice.ValueOf(_w, M8Rounds.Apfsds, M8Rounds.TankGun(), shooter, tank), 3);
            Assert.Equal(0f, AmmoChoice.ValueOf(_w, M8Rounds.Heat, M8Rounds.TankGun(), shooter, tank), 3);
        }

        /// <summary>
        /// ⭐ CE-1079 (docs/DESIGN_Entity_Control.md §8e D-2/D-3) — the operator's <c>F</c>: the breech moves to the next type that has
        /// rounds (an empty one is skipped, it wraps), at the cost of one reload, and forgets the AI's choice; a one-type mount or a
        /// mount with no loadout does not switch.
        /// </summary>
        [Fact]
        public void CE1079_Cycle_MovesToTheNextTypeWithRounds_SkipsAnEmptyOne_AndCostsOneReload()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<AmmoLoadout>();
            var gun = w.CreateEntity();
            var someone = w.CreateEntity();
            w.AddComponent(gun, new AmmoLoadout { Count = 3, Ammo0 = M8Rounds.Apfsds, Rounds0 = 5, Ammo1 = M8Rounds.Heat, Rounds1 = 0,
                                                  Ammo2 = M8Rounds.He, Rounds2 = 4, Loaded = 0, ChosenFor = someone });

            Assert.True(AmmoChoice.Cycle(w, gun, 1, reloadSeconds: 6f));
            var load = w.GetComponentRO<AmmoLoadout>(gun);
            Assert.Equal(2, load.Loaded);                 // HEAT has none: skipped to HE
            Assert.Equal(6f, load.SwitchRemaining, 3);    // one reload
            Assert.True(load.ChosenFor.IsNull);           // an AI that takes over chooses afresh
            Assert.True(AmmoChoice.Switching(w, gun));

            Assert.True(AmmoChoice.Cycle(w, gun, 1, reloadSeconds: 6f));
            Assert.Equal(0, w.GetComponentRO<AmmoLoadout>(gun).Loaded);   // wraps to APFSDS
            Assert.False(AmmoChoice.Cycle(w, gun, 2, reloadSeconds: 6f));  // two steps round a two-type ring: back where it was

            var rifle = w.CreateEntity();
            Assert.False(AmmoChoice.Cycle(w, rifle, 1, 2f));               // no loadout
            w.AddComponent(rifle, new AmmoLoadout { Count = 1, Ammo0 = M8Rounds.Apfsds, Rounds0 = 9 });
            Assert.False(AmmoChoice.Cycle(w, rifle, 1, 2f));               // one type
        }
    }
}
