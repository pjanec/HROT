using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Xunit;

namespace Fdp.Toolkit.Physics.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (S1b — docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-4, HL-6..HL-8, HL-12) — a person's damage
    /// BY PART: the zones resolve through the type's override, its own armour, its DIS-class profile and the engine (each value says where
    /// it came from); a head hit counts ×4 and meets the helmet on the face entered; the Health owner keeps a level per part; a wounded leg
    /// slows him and a wounded arm spoils his aim; the muscle leaves a mark where the round struck.
    /// </summary>
    public sealed class BodyPartDamageTests : IDisposable
    {
        private const long Soldier = 1, Civilian = 2, Militia = 3, Vested = 4, Truck = 5, Tank = 6, Hmg = 7, Atgm = 8;
        private readonly EntityRepository _world = new();
        private readonly TkbDatabase _db = new();

        public BodyPartDamageTests()
        {
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<MobilityKill>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterComponent<TkbIdentity>();
            _world.RegisterComponent<InjuryThresholds>();
            _world.RegisterComponent<PartDamage>();
            _world.RegisterComponent<HitMarks>();
            _world.RegisterEvent<DetonationNotification>();
            _world.RegisterEvent<DamageAssessedEvent>();
            _world.RegisterManagedEvent<DestroyEntityCommand>();

            _db.Register(Type(Soldier, new DISEntityType { Kind = 3, Domain = 1, Category = 1 }));
            _db.Register(Type(Civilian, new DISEntityType { Kind = 3, Domain = 1 }));
            var militia = Type(Militia, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            militia.AddDescriptor(new BodyZonesDto { Zones = { new BodyZoneDto { Zone = "Torso", ArmourFront = 0, ArmourRear = 0 } } });
            _db.Register(militia);
            var vested = Type(Vested, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            vested.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100f, ArmorFront = 8f });
            _db.Register(vested);
            _db.Register(Type(Truck, new DISEntityType { Kind = 1, Domain = 1, Category = 7 }));
            var tank = Type(Tank, new DISEntityType { Kind = 1, Domain = 1, Category = 1 });
            tank.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            _db.Register(tank);
            foreach (var (id, sub) in new[] { (Hmg, (byte)1), (Atgm, (byte)3) })   // ⭐ CE-3214 — weapon systems (§5b's casing and health)
            {
                var w = Type(id, new DISEntityType { Kind = 1, Domain = 1, Category = 50, Subcategory = sub });
                w.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 80f, ArmorFront = 1.5f, ArmorSide = 1.5f, ArmorRear = 1.5f, ArmorTop = 1.5f });
                _db.Register(w);
            }
            _world.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _world.Dispose();

        private static TkbTemplate Type(long id, DISEntityType dis) => new($"T{id}", id) { DisType = dis };

        private Entity Body(long type, float health = 100f)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new TkbIdentity { TkbType = type });
            _world.AddComponent(e, new Health { Current = health, Max = health });
            _world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _world.AddComponent(e, new NetworkIdentity(9000 + e.Index));
            _world.SetAuthority<Health>(e, true);
            return e;
        }

        // ── the zones resolve, and say where each value came from ─────────────────────────────────────────────────────────────

        [Fact]
        public void CE3210_ASoldier_GetsTheSoldierProfile_HelmetAndVest_ByDis()
        {
            var body = BodyZones.Of(_db.GetByType(Soldier))!;
            Assert.Equal(BodyPlan.Person, body.Plan);
            Assert.Equal(ParameterProvenance.ReferenceByDis, body.ProfileProvenance);
            Assert.StartsWith("profile Soldier", body.Profile);
            var head = body.ZoneOf(PersonPart.Head)!;
            Assert.Equal("Head", head.Name);
            Assert.Equal(4f, head.Multiplier);
            Assert.Equal(0f, head.ArmourOn(Facing.Front));   // the face is open
            Assert.Equal(3f, head.ArmourOn(Facing.Side));    // the helmet
            Assert.Equal(5f, body.ZoneOf(PersonPart.Torso)!.ArmourOn(Facing.Front));   // the vest
            Assert.Equal("Leg", body.ZoneOf(PersonPart.ShinL)!.Name);
            Assert.Equal("Arm", body.ZoneOf(PersonPart.ForearmR)!.Name);
        }

        [Fact]
        public void CE3210_ACivilian_GetsThePersonProfile_NoArmour()
        {
            var body = BodyZones.Of(_db.GetByType(Civilian))!;
            Assert.StartsWith("profile Person", body.Profile);
            Assert.Equal(0f, body.ZoneOf(PersonPart.Head)!.ArmourOn(Facing.Side));
            Assert.Equal(0f, body.ZoneOf(PersonPart.Torso)!.ArmourOn(Facing.Front));
            Assert.Equal(4f, body.ZoneOf(PersonPart.Head)!.Multiplier);
        }

        [Fact]
        public void CE3210_ATypesSparseOverride_ChangesOnlyItsFields_AndIsExplicit()
        {
            var torso = BodyZones.Of(_db.GetByType(Militia))!.Zone("Torso")!;
            Assert.Equal(0f, torso.ArmourOn(Facing.Front));   // no vest
            Assert.Equal(ParameterProvenance.Explicit, torso.Sources["Armour.Front"].Provenance);
            var head = BodyZones.Of(_db.GetByType(Militia))!.Zone("Head")!;
            Assert.Equal(3f, head.ArmourOn(Facing.Side));     // the helmet is still the profile's
            Assert.Equal(ParameterProvenance.ReferenceByDis, head.Sources["Armour.Side"].Provenance);
        }

        [Fact]
        public void CE3210_APersonsOwnPlatformArmour_IsItsTorsos()
        {
            var torso = BodyZones.Of(_db.GetByType(Vested))!.Zone("Torso")!;
            Assert.Equal(8f, torso.ArmourOn(Facing.Front));
            Assert.Equal((ParameterProvenance.Explicit, "Combat.PlatformDef.ArmorFront"), torso.Sources["Armour.Front"]);
            Assert.Equal(5f, torso.ArmourOn(Facing.Rear));    // the profile's, where the type said nothing
        }

        [Fact]
        public void CE3210_AVehicle_HasNoPersonBody_EvenThroughTheEntityLookup()
        {
            Assert.Null(BodyZones.Of(_db.GetByType(Truck)));
            Assert.Null(BodyZones.Of(_world, Body(Truck)));
            var untyped = _world.CreateEntity();
            Assert.NotNull(BodyZones.Of(_world, untyped));   // ⭐ a person with no type: the engine's person body
        }

        [Fact]
        public void CE3210_TkbResolve_ReportsTheBodyZones_WithTheirSources()
        {
            var all = ParameterResolver.ResolveAll(_db.GetByType(Militia));
            var vest = all.Single(p => p.Name == "Body.Torso.Armour.Front");
            Assert.Equal(0f, vest.Value);
            Assert.Equal(ParameterProvenance.Explicit, vest.Provenance);
            var head = all.Single(p => p.Name == "Body.Head.Multiplier");
            Assert.Equal(4f, head.Value);
            Assert.Equal(ParameterProvenance.ReferenceByDis, head.Provenance);
            Assert.DoesNotContain(ParameterResolver.ResolveAll(_db.GetByType(Truck)), p => p.Name.StartsWith("Body."));
        }

        // ── the damage step: the zone's armour on the face entered, × its multiplier ─────────────────────────────────────────

        private float Assess(Entity target, byte part, Facing face, float penetration, float damage = 10f)
        {
            _world.Bus.Publish(new DetonationNotification
            {
                Target = target, HasBodyPart = part == PersonPart.None ? (byte)0 : (byte)1, BodyPart = part, Face = (byte)face,
                Penetration = penetration, Damage = damage,
            });
            _world.Bus.SwapBuffers();
            new DamageCalculationSystem().Execute(_world, 0.016f);
            _world.Bus.SwapBuffers();
            var assessed = Assert.Single(_world.Bus.Read<DamageAssessedEvent>().ToArray());
            Assert.Equal(part == PersonPart.None ? 0 : 1, assessed.HasBodyPart);
            if (part != PersonPart.None) Assert.Equal(part, assessed.BodyPart);
            return assessed.TotalDamage;
        }

        [Fact]
        public void CE3210_AHeadHit_CountsFourTimes_ALegHalf()
        {
            var man = Body(Civilian);
            Assert.Equal(40f, Assess(man, PersonPart.Head, Facing.Front, penetration: 0f));
            Assert.Equal(5f, Assess(man, PersonPart.ThighL, Facing.Front, penetration: 0f));
            Assert.Equal(10f, Assess(man, PersonPart.None, Facing.Front, penetration: 0f));   // ⛔ no part ⇒ the whole body, as before
        }

        [Fact]
        public void CE3210_TheHelmet_StopsMoreFromTheSideThanTheOpenFace()
        {
            var man = Body(Soldier);
            float face = Assess(man, PersonPart.Head, Facing.Front, penetration: 3f);
            float side = Assess(man, PersonPart.Head, Facing.Side, penetration: 3f);
            Assert.True(side < face, $"helmet side {side} should take less than the open face {face}");
        }

        // ── the Health owner keeps a level per part ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void CE3210_HealthApplication_WritesTheLevel_OnThePartHit()
        {
            var man = Body(Soldier, health: 100f);
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = man, TotalDamage = 25f, HasBodyPart = 1, BodyPart = PersonPart.ShinR });
            _world.Bus.SwapBuffers();
            new HealthApplicationSystem().Execute(_world, 0.016f);

            Assert.Equal(75f, _world.GetComponentRO<Health>(man).Current);
            var parts = _world.GetComponentRO<PartDamage>(man);
            Assert.Equal(BodyPlan.Person, parts.Plan);
            Assert.Equal(64, parts.LevelOf(PersonPart.ShinR));   // a quarter of his health ⇒ the starter wound threshold
            Assert.Equal(0, parts.LevelOf(PersonPart.ShinL));
        }

        [Fact]
        public void CE3210_PartDamage_IsWrittenOnTheHealthOwnerOnly()
        {
            var replica = _world.CreateEntity();
            _world.AddComponent(replica, new Health { Current = 100f, Max = 100f });
            _world.AddComponent(replica, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = replica, TotalDamage = 25f, HasBodyPart = 1, BodyPart = PersonPart.Head });
            _world.Bus.SwapBuffers();
            new HealthApplicationSystem().Execute(_world, 0.016f);
            Assert.False(_world.HasComponent<PartDamage>(replica));   // ⭐ the owner's level arrives by replication (dtPartDamage)
        }

        [Fact]
        public void CE3210_AHitWithNoPart_AddsNoPartDamage()
        {
            var man = Body(Soldier);
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = man, TotalDamage = 25f });
            _world.Bus.SwapBuffers();
            new HealthApplicationSystem().Execute(_world, 0.016f);
            Assert.False(_world.HasComponent<PartDamage>(man));
        }

        // ── the derived wounds ──────────────────────────────────────────────────────────────────────────────────────────────

        private void Wound(Entity man, byte part, int level)
        {
            if (!_world.HasComponent<PartDamage>(man)) _world.AddComponent(man, new PartDamage { Plan = BodyPlan.Person });
            _world.GetComponentRW<PartDamage>(man).Add(part, level);
        }

        [Fact]
        public void CE3210_AWoundedLeg_SlowsHim_BelowTheThreshold_NothingChanges()
        {
            var man = Body(Soldier);
            Wound(man, PersonPart.ShinL, 63);
            Assert.Equal(1f, Injury.SpeedFactor(_world, man));
            Wound(man, PersonPart.ShinL, 1);
            Assert.Equal(Wounds.WoundedLegSpeedFactor, Injury.SpeedFactor(_world, man));
            Assert.False(Wounds.ArmWounded(_world, man));
        }

        [Fact]
        public void CE3210_AWoundedArm_WidensHisAim_NotHisSpeed()
        {
            var man = Body(Soldier);
            Assert.Equal(1f, Wounds.AimFactor(_world, man));
            Wound(man, PersonPart.ForearmR, 200);
            Assert.Equal(Wounds.WoundedAimFactor, Wounds.AimFactor(_world, man));
            Assert.Equal(1f, Injury.SpeedFactor(_world, man));
        }

        // ── the marks the rounds leave ──────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void CE3210_HitMarks_IsARing_TheOldestGoesFirst_TheSequenceRuns()
        {
            var marks = new HitMarks();
            for (int i = 0; i < HitMarks.Capacity + 3; i++)
                marks.Add((byte)(i % 10), new Vector3(0.25f, -0.5f, 0.125f), (byte)Facing.Side, HitMarkKind.Bullet);
            Assert.Equal(HitMarks.Capacity + 3, marks.NextSeq);
            Assert.Equal(HitMarks.Capacity, marks.Count);
            Assert.False(marks.TryGet(2, out _, out _, out _, out _));   // overwritten
            Assert.True(marks.TryGet(3, out var part, out var local, out var face, out var kind));
            Assert.Equal(3, part);
            Assert.Equal((byte)Facing.Side, face);
            Assert.Equal(HitMarkKind.Bullet, kind);
            Assert.InRange(local.X, 0.2499f, 0.2501f);
            Assert.InRange(local.Y, -0.5001f, -0.4999f);
            Assert.False(marks.TryGet(HitMarks.Capacity + 3, out _, out _, out _, out _));   // not written yet
        }

        [Fact]
        public void CE3210_FragmentMarks_AreSampled_Capped_OnTheFaceTowardTheBurst_AndRepeat()
        {
            var a = Body(Soldier);
            var b = Body(Soldier);
            AreaEffectSystem.MarkFragments(_world, a, hits: 40, burstSeq: 7, Facing.Side);
            Assert.Equal(AreaEffectSystem.MaxFragmentMarks, _world.GetComponentRO<HitMarks>(a).NextSeq);
            for (int seq = 0; seq < AreaEffectSystem.MaxFragmentMarks; seq++)
            {
                Assert.True(_world.GetComponentRO<HitMarks>(a).TryGet(seq, out var part, out var local, out var face, out var kind));
                Assert.InRange(part, 0, PersonPart.Count - 1);
                Assert.Equal(((byte)Facing.Side, HitMarkKind.Fragment), (face, kind));
                Assert.InRange(local.Y, 0.4999f, 0.5001f);   // on the side face of the part
            }
            // the same burst and body draw the same marks (a replay agrees); a later mark is added after, never over, the earlier ones
            var first = _world.GetComponentRO<HitMarks>(a);
            Assert.False(_world.HasComponent<HitMarks>(b));
            AreaEffectSystem.MarkFragments(_world, a, hits: 1, burstSeq: 7, Facing.Side);
            Assert.Equal(AreaEffectSystem.MaxFragmentMarks + 1, _world.GetComponentRO<HitMarks>(a).NextSeq);
            Assert.True(_world.GetComponentRO<HitMarks>(a).TryGet(AreaEffectSystem.MaxFragmentMarks, out var p1, out var l1, out _, out _));
            Assert.True(first.TryGet(0, out var p0, out var l0, out _, out _));
            Assert.Equal((p0, l0), (p1, l1));
        }

        [Fact]
        public void CE3210_HitResolution_LeavesAMark_OnThePartStruck()
        {
            _world.RegisterEvent<RaycastResultEvent>();
            _world.RegisterEvent<HitEvent>();
            var man = Body(Soldier);
            var miss = Body(Soldier);
            _world.Bus.Publish(new RaycastResultEvent { Hit = new RaycastHit
            {
                HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(999), HitEntity = man, T = 0.5f,
                Start = Vector3.Zero, End = new Vector3(10, 0, 0),
                HasBodyPart = 1, BodyPart = PersonPart.Head, Face = (byte)Facing.Front, LocalPoint = new Vector3(0.1f, 0f, 0.3f),
            } });
            _world.Bus.SwapBuffers();
            new HitResolutionSystem().Execute(_world, 0.016f);

            var marks = _world.GetComponentRO<HitMarks>(man);
            Assert.Equal(1, marks.NextSeq);
            Assert.True(marks.TryGet(0, out var part, out var local, out _, out var kind));
            Assert.Equal(PersonPart.Head, part);
            Assert.Equal(HitMarkKind.Bullet, kind);
            Assert.InRange(local.Z, 0.299f, 0.301f);
            Assert.False(_world.HasComponent<HitMarks>(miss));   // ⭐ only a body that was struck carries marks
        }
    
        // ── ⭐⭐ CE-3211 (§9 S2) — a ground vehicle's compartments ──────────────────────────────────────────────────────────────────

        [Fact]
        public void CE3211_ATank_GetsTheTankProfile_ItsKit_AndInheritsItsPlatformArmour()
        {
            var body = BodyZones.Of(_db.GetByType(Tank))!;
            Assert.Equal((BodyPlan.TrackedVehicle, "Tank", EngineEnd.Rear), (body.Plan, body.Kit, body.EngineEnd));
            Assert.True(float.IsNaN(body.ZoneOf(VehiclePart.Turret)!.ArmourOn(Facing.Front)));   // the platform's, kept by ArmorModel
            Assert.Equal("Engine", body.ZoneOf(VehiclePart.Engine)!.Name);
            Assert.Equal(WoundEffect.Mobility, body.ZoneOf(VehiclePart.RunningGearR)!.Effect);
            Assert.NotNull(VehicleBody.Of(_db.GetByType(Tank)));
            Assert.Null(BodyZones.Of(_db.GetByType(Truck)));   // ⛔ a vehicle with no combat platform has no body (S2-5)

            var resolved = ParameterResolver.ResolveAll(_db.GetByType(Tank));
            var turretFront = resolved.Single(p => p.Name == "Body.Turret.Armour.Front");
            Assert.Equal((500f, ParameterProvenance.Explicit), (turretFront.Value, turretFront.Provenance));
            Assert.StartsWith("inherited", turretFront.Source);
        }

        [Fact]
        public void CE3211_ATurretHit_MeetsThePlatformArmour_OnTheFaceEntered_AndAGunHitCountsAFifth()
        {
            if (!_world.IsComponentTypeRegistered<SimTransform>()) _world.RegisterComponent<SimTransform>();
            var tank = Body(Tank, health: 1000f);
            _world.AddComponent(tank, new SimTransform { Rotation = Quaternion.Identity });   // the damage step reads the armour only on a placed body
            float wholeSide = Assess(tank, VehiclePart.Hull, Facing.Side, penetration: 300f, damage: 100f);   // the hull: × 1, the platform's side
            Assert.True(wholeSide > 50f, $"a 300 mm round should get through 80 mm of side armour: {wholeSide}");
            Assert.Equal(wholeSide, Assess(tank, VehiclePart.Turret, Facing.Side, penetration: 300f, damage: 100f), 3);
            Assert.True(Assess(tank, VehiclePart.Turret, Facing.Front, penetration: 300f, damage: 100f) < wholeSide / 2f);   // 500 mm in front
            Assert.Equal(wholeSide * 0.2f, Assess(tank, VehiclePart.Gun, Facing.Side, penetration: 300f, damage: 100f), 3);
        }

        private Entity Armed(long type)
        {
            var e = Body(type, health: 1000f);
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            return e;
        }

        private void Apply(Entity e, byte part, float damage)
        {
            _world.Bus.Publish(new DamageAssessedEvent { HitEntity = e, TotalDamage = damage, HasBodyPart = 1, BodyPart = part });
            _world.Bus.SwapBuffers();
            new HealthApplicationSystem().Execute(_world, 0.016f);
        }

        [Fact]
        public void CE3211_AWreckedTrack_IsTheMobilityKill_AWreckedGun_TheFirepowerKill()
        {
            var a = Armed(Tank);
            Apply(a, VehiclePart.RunningGearL, 100f);   // 10 % of its health on the track ⇒ level 26, under 32: still moves
            Assert.Equal(ActorCapabilities.CanMove | ActorCapabilities.CanShoot, _world.GetComponentRO<ActorCapabilityState>(a).Capabilities);
            Apply(a, VehiclePart.RunningGearL, 50f);    // level 39 ⇒ the mobility kill
            Assert.Equal(ActorCapabilities.CanShoot, _world.GetComponentRO<ActorCapabilityState>(a).Capabilities);
            Assert.Equal(BodyPlan.TrackedVehicle, _world.GetComponentRO<PartDamage>(a).Plan);

            var b = Armed(Tank);
            Apply(b, VehiclePart.Gun, 150f);
            Assert.Equal(ActorCapabilities.CanMove, _world.GetComponentRO<ActorCapabilityState>(b).Capabilities);
            var c = Armed(Tank);
            Apply(c, VehiclePart.Hull, 400f);             // the hull has no effect: health only
            Assert.Equal(ActorCapabilities.CanMove | ActorCapabilities.CanShoot, _world.GetComponentRO<ActorCapabilityState>(c).Capabilities);
        }

        // ── ⭐⭐ CE-3214 (§11 S2b) — a weapon system's parts ─────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ <c>CE-3214</c> (S2b-4, S2b-5) — a weapon type gets its kind's profile by DIS class (the HMG and the ATGM tripod), the
        /// weapon-system plan, its kit and its casing as the inherited armour.</summary>
        [Fact]
        public void CE3214_AWeaponType_GetsItsKindsProfile_ByDis()
        {
            var hmg = BodyZones.Of(_db.GetByType(Hmg))!;
            Assert.Equal((BodyPlan.WeaponSystem, "Hmg"), (hmg.Plan, hmg.Kit));
            Assert.Equal("Atgm", BodyZones.Of(_db.GetByType(Atgm))!.Kit);
            Assert.Equal(WoundEffect.Firepower, hmg.ZoneOf(WeaponPart.Weapon)!.Effect);
            Assert.Equal(0.5f, hmg.ZoneOf(WeaponPart.Mount)!.Multiplier);
            Assert.True(float.IsNaN(hmg.ZoneOf(WeaponPart.Weapon)!.ArmourOn(Facing.Side)));   // the casing, kept by ArmorModel
            Assert.NotNull(VehicleBody.Of(_db.GetByType(Hmg))?.Stowed);
        }

        /// <summary>
        /// ⭐ <c>CE-3214</c> (S2b-6) — a round through the WEAPON (receiver, barrel, sight) past its threshold breaks it — the fire gate
        /// (<c>CanFire</c> ⇒ <c>Destroyed</c>) reads it, though its health is far from 0; a leg of its tripod shot off does not.
        /// 🔴 Red-proof: with <c>IsBroken</c> reading health alone the weapon still fires.
        /// </summary>
        [Fact]
        public void CE3214_AWrecked_Weapon_Part_BreaksTheWeapon_ATripodLegDoesNot()
        {
            _world.RegisterComponent<WeaponSystemParams>();
            var w = Body(Hmg, health: 80f);
            _world.AddComponent(w, new WeaponSystemParams());
            Apply(w, WeaponPart.Mount, 30f);    // the tripod: level 96, no effect
            Assert.False(WeaponSystems.IsBroken(_world, w));
            Apply(w, WeaponPart.Weapon, 25f);   // level 80 ≥ 64 ⇒ the firepower wound
            Assert.True(_world.GetComponentRO<Health>(w).Current > 0f);
            Assert.True(WeaponSystems.IsBroken(_world, w));
            Assert.False(WeaponSystems.CanFire(_world, w, out var why));
            Assert.Equal(WeaponHold.Destroyed, why);
        }
}
}
