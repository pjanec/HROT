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
    }
}
