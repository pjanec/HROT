using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tests.Utility;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Perception
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V1 — the danger of a contact TO A VEHICLE is what its weapons can do to that vehicle's armour
    /// (DESIGN_Vehicle_Exposure_Cycle §2, Sensors §11.2 G1): the chance his round penetrates the face of mine it faces, through the SAME
    /// <c>ArmorModel</c> the shot uses (R-212 A1) — the person scale's "he can hurt me = 1". A person keeps armed 1 / unarmed 0.3.
    /// Numbers: a T-72 (500 / 250 / 150 mm, 2500 HP) facing +X at the origin.
    /// </summary>
    public sealed class ThreatDangerTests : IDisposable
    {
        private const long RpgManType = 9101, RiflemanType = 9102, TowTeamType = 9103, LightRocketType = 9104, UnknownRoundType = 9105;

        private readonly UtilityTestWorld _world = new();

        public ThreatDangerTests()
        {
            _world.Repo.RegisterComponent<VehicleParams>();
            _world.UseTkb(
                UtilityTestWorld.TankTemplate(),
                UtilityTestWorld.InfantryTemplate(),
                Armed("RPG man", RpgManType, new WeaponMountDto { Range = 300, Penetration = 300, DamagePerHit = 400 }),
                Armed("Rifleman", RiflemanType, new WeaponMountDto { Range = 500, Penetration = 5, DamagePerHit = 10 }),
                Armed("TOW team", TowTeamType, new WeaponMountDto { Range = 3750, Penetration = 800, DamagePerHit = 2000 }),
                Armed("Light rocket", LightRocketType, new WeaponMountDto { Range = 200, Penetration = 275, DamagePerHit = 300 }),
                Armed("Unknown round", UnknownRoundType, new WeaponMountDto { Range = 500, Penetration = 0, DamagePerHit = 10 }));
        }

        public void Dispose() => _world.Dispose();

        private static Fdp.Interfaces.TkbTemplate Armed(string name, long type, WeaponMountDto mount)
        {
            var t = new Fdp.Interfaces.TkbTemplate(name, type);
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100, ArmorFront = 5 });
            t.AddDescriptor(new WeaponSuiteDto { Mounts = { mount } });
            return t;
        }

        /// <summary>A T-72 at the origin facing +X (a vehicle: <c>VehicleParams.Class = Tank</c>).</summary>
        private Entity Tank()
        {
            var e = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(e, new Health { Current = 2500, Max = 2500 });
            _world.Repo.AddComponent(e, new VehicleParams { Class = VehicleClass.Tank });
            _world.SetType(e, UtilityTestWorld.TankType);
            return e;
        }

        /// <summary>A soldier at the origin (a person: <c>VehicleParams.Class = Pedestrian</c>).</summary>
        private Entity Soldier()
        {
            var e = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(e, new Health { Current = 100, Max = 100 });
            _world.Repo.AddComponent(e, new VehicleParams { Class = VehicleClass.Pedestrian });
            _world.SetType(e, UtilityTestWorld.InfantryType);
            return e;
        }

        private Entity Shooter(long type, Vector3 at)
        {
            var e = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.Repo.AddComponent(e, new Health { Current = 100, Max = 100 });
            _world.Repo.AddComponent(e, new WeaponState { Ammo = 4, MaxAmmo = 4 });
            _world.SetType(e, type);
            return e;
        }

        /// <summary>
        /// ⭐ To a tank the RPG man is fully dangerous on its SIDE (300 mm vs 250 ⇒ P 1) and harmless on its FRONT (300 vs 500 ⇒ P 0 ⇒
        /// the floor 0.1); the rifleman is harmless anywhere (0.1); a TOW team penetrates the front (1). A light rocket (275 mm) half-
        /// penetrates the side (P 0.75) and fully the rear. An UNKNOWN round (no penetration) ignores armour, as the shot does (1).
        /// To a SOLDIER both men are simply armed (1) — a 5 mm vest is not a vehicle's armour.
        /// </summary>
        [Fact]
        public void V1_ToATank_TheAtGunnerIsTheThreat_TheRiflemanIsNearlyHarmless_AndToASoldier_BothAreArmed()
        {
            var tank = Tank();
            var rpgOnTheSide  = Shooter(RpgManType, new Vector3(0, 100, 0));
            var rpgInFront    = Shooter(RpgManType, new Vector3(100, 0, 0));
            var rifleOnTheSide = Shooter(RiflemanType, new Vector3(0, -100, 0));
            var towInFront    = Shooter(TowTeamType, new Vector3(1000, 0, 0));

            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, rpgOnTheSide), precision: 4);
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, rpgInFront), precision: 4);
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, rifleOnTheSide), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, towInFront), precision: 4);
            Assert.Equal(0.75f, ThreatDanger.Of(_world.Repo, tank, Shooter(LightRocketType, new Vector3(0, 100, 0))), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, Shooter(LightRocketType, new Vector3(-100, 0, 0))), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, Shooter(UnknownRoundType, new Vector3(100, 0, 0))), precision: 4);

            var soldier = Soldier();
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, soldier, rpgOnTheSide));
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, soldier, rifleOnTheSide));
        }

        /// <summary>
        /// ⭐ Beyond his weapon's range his danger fades to the floor: the TOW (3750 m) in front at 4000 m is 2/3
        /// (range fade 3750 → 4500 m), at 4600 m only the floor; the RPG on the side at 400 m (beyond its 360 m fade) is the
        /// floor too; an unarmed or killed contact is unchanged (0.3 / 0).
        /// </summary>
        [Fact]
        public void V1_BeyondHisRange_TheDangerFades_AndUnarmedOrKilledAreUnchanged()
        {
            var tank = Tank();
            Assert.Equal(2f / 3f, ThreatDanger.Of(_world.Repo, tank, Shooter(TowTeamType, new Vector3(4000, 0, 0))), precision: 4);
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, Shooter(TowTeamType, new Vector3(4600, 0, 0))), precision: 4);
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, Shooter(RpgManType, new Vector3(0, 400, 0))), precision: 4);

            var unarmed = _world.SpawnTypedTarget(UtilityTestWorld.InfantryType, 50f, maxHealth: 100f);
            Assert.Equal(ThreatDanger.Unarmed, ThreatDanger.Of(_world.Repo, tank, unarmed));

            var killed = Shooter(RpgManType, new Vector3(0, 50, 0));
            _world.Repo.GetComponentRW<Health>(killed).Current = 0;
            Assert.Equal(0f, ThreatDanger.Of(_world.Repo, tank, killed));
        }

        // ── V1 on the as-built hit model (backend B3–B7): rounds, penetrators, the 3-D face, top attack ────────────────

        private const long HeOnlyType = 9110, TankGunType = 9111, HeatOnlyType = 9112, ApfsdsOnlyType = 9113, JavelinTeamType = 9114,
                           CeTankType = 9115, JavelinRound = 9010;

        /// <summary>The TKB of these rails: the base set + the M8 rounds, gun types by round, a Javelin team, a T-72 with 800 mm CE front.</summary>
        private void UseHitModelTkb()
        {
            static Fdp.Interfaces.TkbTemplate Gun(string name, long type, WeaponMountDto mount)
            {
                var t = new Fdp.Interfaces.TkbTemplate(name, type);
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100 });
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { mount } });
                return t;
            }
            var ceTank = new Fdp.Interfaces.TkbTemplate("T-72 (ERA)", CeTankType);
            ceTank.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150, ArmorFrontCe = 800 });
            var javelin = new Fdp.Interfaces.TkbTemplate("Javelin", JavelinRound);
            javelin.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = 400, Damage = 2000 });
            javelin.AddDescriptor(new MissileDto { Guidance = GuidanceKind.FireAndForget, TopAttack = true, TopAttackMinRangeM = 150, MinRangeM = 65 });
            var all = new System.Collections.Generic.List<Fdp.Interfaces.TkbTemplate>
            {
                UtilityTestWorld.TankTemplate(), UtilityTestWorld.InfantryTemplate(), ceTank, javelin,
                Armed("RPG man", RpgManType, new WeaponMountDto { Range = 300, Penetration = 300, DamagePerHit = 400 }),
                Gun("HE gun", HeOnlyType, new WeaponMountDto { Range = 2000, AmmoGuid = unchecked((ulong)Fdp.Toolkit.Combat.Tests.M8Rounds.He), DamagePerHit = 600 }),
                Gun("Tank gun", TankGunType, Fdp.Toolkit.Combat.Tests.M8Rounds.TankGun() with { Range = 2000, AmmoGuid = unchecked((ulong)Fdp.Toolkit.Combat.Tests.M8Rounds.He) }),   // HE loaded, APFSDS in the loadout
                Gun("HEAT gun", HeatOnlyType, new WeaponMountDto { Range = 2000, AmmoGuid = unchecked((ulong)Fdp.Toolkit.Combat.Tests.M8Rounds.Heat), DamagePerHit = 1200 }),
                Gun("APFSDS gun", ApfsdsOnlyType, new WeaponMountDto { Range = 2000, AmmoGuid = unchecked((ulong)Fdp.Toolkit.Combat.Tests.M8Rounds.Apfsds), DamagePerHit = 1100 }),
                Gun("Javelin team", JavelinTeamType, new WeaponMountDto { Range = 2500, AmmoGuid = unchecked((ulong)JavelinRound), DamagePerHit = 2000 }),
            };
            all.AddRange(Fdp.Toolkit.Combat.Tests.M8Rounds.Templates());
            _world.UseTkb(all.ToArray());
        }

        /// <summary>
        /// ⭐ M8 + M2/M3 — a gun's danger is its BEST round, through that round's penetrator: an HE-only gun (50 mm) cannot hurt a T-72's
        /// 500 mm front (0.1) but a gun with HE LOADED that also carries APFSDS (600 mm) can (1); against an 800 mm CE front a HEAT gun (600 mm shaped
        /// charge) cannot (0.1) where an APFSDS gun (600 mm kinetic vs 500) still can (1).
        /// </summary>
        [Fact]
        public void V1_ADangerIsTheBestRound_ThroughItsOwnPenetrator()
        {
            UseHitModelTkb();
            var tank = Tank();
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, Shooter(HeOnlyType, new Vector3(1000, 0, 0))), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, Shooter(TankGunType, new Vector3(1000, 0, 0))), precision: 4);

            var era = Tank();
            _world.Repo.GetComponentRW<TkbIdentity>(era).TkbType = CeTankType;
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, era, Shooter(HeatOnlyType, new Vector3(1000, 0, 0))), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, era, Shooter(ApfsdsOnlyType, new Vector3(1000, 0, 0))), precision: 4);
        }

        /// <summary>
        /// ⭐ M1 / M4 — the face is 3-D: the RPG man 5 m in FRONT on a 10 m roof shoots down onto the TOP (the rear's 150 mm — the T-72
        /// declares no top), 1 where the same man at ground level meets the 500 mm front (0.1). A top-attack Javelin (400 mm) at 1000 m
        /// dives on the top (1) though the front stops it; inside its 65 m minimum range it is no threat even from BEHIND (0.1).
        /// </summary>
        [Fact]
        public void V1_TheFaceIs3D_ARoofShotAndATopAttackMissileReadMyTop_AndAMissileInsideItsMinRangeIsHarmless()
        {
            UseHitModelTkb();
            var tank = Tank();
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, Shooter(RpgManType, new Vector3(5, 0, 0))), precision: 4);
            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, Shooter(RpgManType, new Vector3(5, 0, 10))), precision: 4);

            Assert.Equal(1f, ThreatDanger.Of(_world.Repo, tank, Shooter(JavelinTeamType, new Vector3(1000, 0, 0))), precision: 4);
            Assert.Equal(ThreatDanger.ArmedHarmless, ThreatDanger.Of(_world.Repo, tank, Shooter(JavelinTeamType, new Vector3(-50, 0, 0))), precision: 4);
        }

        /// <summary>⭐ The fade itself: 1 inside the range (or an unknown range of 0), linear to 0 at 1.2 × the range.</summary>
        [Fact]
        public void V1_InRange_IsOneInside_FadesLinearly_AndZeroBeyondTheFade()
        {
            Assert.Equal(1f, ThreatDanger.InRange(300f, 300f));
            Assert.Equal(1f, ThreatDanger.InRange(0f, 9999f));
            Assert.Equal(0.5f, ThreatDanger.InRange(300f, 330f), precision: 4);
            Assert.Equal(0f, ThreatDanger.InRange(300f, 360f));
        }
    }
}
