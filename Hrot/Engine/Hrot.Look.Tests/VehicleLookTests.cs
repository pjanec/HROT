using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Look.Vehicles;

namespace Hrot.Look.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4670</c> (vehicle appearance VA-S1; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §3a, §6c) — the ONE reader of how a vehicle's
    /// damage looks, over the replicated state only: a part's level against its zone's threshold, the kill kind, the flames and the smoke after them.
    /// </summary>
    public sealed class VehicleLookTests : IDisposable
    {
        private const long Tank = 1;
        private readonly EntityRepository _w = new();
        private readonly TkbDatabase _db = new();

        public VehicleLookTests()
        {
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<PartDamage>();
            _w.RegisterComponent<FireState>();
            var t = new TkbTemplate("T1", Tank) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 1 } };
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            _db.Register(t);
            _w.SetSingletonManaged<ITkbDatabase>(_db);
        }

        public void Dispose() => _w.Dispose();

        private Entity Vehicle(float health = 1000f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)Tank });
            _w.AddComponent(e, new Health { Current = health, Max = 1000f });
            return e;
        }

        private void Damage(Entity e, byte part, byte level)
        {
            if (!_w.HasComponent<PartDamage>(e)) _w.AddComponent(e, new PartDamage { Plan = BodyPlan.TrackedVehicle });
            _w.GetComponentRW<PartDamage>(e).Set(part, level);
        }

        /// <summary>⭐ VA-6 — a part reads Intact (0) · Damaged (under its zone's threshold) · Destroyed (at / over it): the left track at 40 is past the
        /// running gear's 32 (the same threshold that makes the mobility kill), the turret at 100 is under its 160, the right track untouched.
        /// 🔴 Red-proof: comparing against 255 instead of the zone's threshold reads the destroyed track as Damaged.</summary>
        [Fact]
        public void CE4670_APartsLook_IsItsLevelAgainstItsZonesThreshold()
        {
            var v = Vehicle();
            Assert.Equal(PartLook.Intact, VehicleLook.PartState(_w, v, VehiclePart.RunningGearL));   // no PartDamage yet
            Damage(v, VehiclePart.RunningGearL, 40);
            Damage(v, VehiclePart.Turret, 100);
            Assert.Equal(PartLook.Destroyed, VehicleLook.PartState(_w, v, VehiclePart.RunningGearL));
            Assert.Equal(PartLook.Intact, VehicleLook.PartState(_w, v, VehiclePart.RunningGearR));
            Assert.Equal(PartLook.Damaged, VehicleLook.PartState(_w, v, VehiclePart.Turret));
            Damage(v, VehiclePart.Hull, 254);
            Assert.Equal(PartLook.Damaged, VehicleLook.PartState(_w, v, VehiclePart.Hull));            // the hull has no threshold but 255
            Damage(v, VehiclePart.Hull, 255);
            Assert.Equal(PartLook.Destroyed, VehicleLook.PartState(_w, v, VehiclePart.Hull));
            Assert.True(Wounds.Has(_w, v, WoundEffect.Mobility));   // the sim's own reading of the same track agrees
        }

        /// <summary>⭐ VA-7 — the kill kind is read only for a dead vehicle; Catastrophic throws the turret.</summary>
        [Fact]
        public void CE4670_TheKillKind_IsReadForAWreckOnly()
        {
            var v = Vehicle();
            _w.GetComponentRW<Health>(v).Destruction = DestructionKind.Catastrophic;   // an inconsistent sample: alive
            Assert.Equal(DestructionKind.None, VehicleLook.Destruction(_w, v));
            _w.GetComponentRW<Health>(v).Current = 0f;
            Assert.Equal(DestructionKind.Catastrophic, VehicleLook.Destruction(_w, v));
            Assert.True(VehicleLook.TurretThrown(_w, v));
        }

        /// <summary>⭐ VA-10 — a wreck burns while a spot burns (a part under the spot reads Burning), then SMOKES for the class's smoulder seconds (a tank 1800 s)
        /// after its last spot went out, then neither.</summary>
        [Fact]
        public void CE4670_AWreck_Burns_ThenSmokes_ForItsSmoulderSeconds()
        {
            var v = Vehicle(health: 0f);
            ref var hp = ref _w.GetComponentRW<Health>(v);
            hp.Destruction = DestructionKind.BurntOut; hp.StateSince = 100.0;
            var fire = default(FireState);
            fire.TryAdd(new FireSpot(0f, -1f, 1f, 2f, new SimInstant(100.0), 60f, 1f), 100.0);
            _w.AddComponent(v, fire);

            Assert.True(VehicleLook.Burning(_w, v, 130.0));
            Assert.False(VehicleLook.Smoking(_w, v, 130.0));   // flames, not smoke
            Assert.Equal(PartLook.Burning, VehicleLook.PartState(_w, v, VehiclePart.Engine, new Vector3(0f, -1.5f, 1f), 130.0));
            Assert.Equal(PartLook.Intact, VehicleLook.PartState(_w, v, VehiclePart.Gun, new Vector3(0f, 6f, 2f), 130.0));   // out of the spot
            Assert.False(VehicleLook.Burning(_w, v, 170.0));
            Assert.True(VehicleLook.Smoking(_w, v, 170.0));
            Assert.True(VehicleLook.Smoking(_w, v, 160.0 + 1799.0));
            Assert.False(VehicleLook.Smoking(_w, v, 160.0 + 1801.0));
        }

        /// <summary>⭐ The reader allocates nothing in the steady state (a renderer calls it per part per frame).</summary>
        [Fact]
        public void CE4670_TheReader_AllocatesNothing()
        {
            var v = Vehicle(health: 0f);
            Damage(v, VehiclePart.RunningGearL, 40);
            _w.GetComponentRW<Health>(v).Destruction = DestructionKind.KnockedOut;
            for (int i = 0; i < 10; i++) Read(v);   // warm (the type's body is resolved once)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Read(v);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        private int Read(Entity v)
        {
            int n = 0;
            for (byte p = 0; p < 6; p++) n += (int)VehicleLook.PartState(_w, v, p, new Vector3(0f, 0f, 1f), 50.0);
            n += (int)VehicleLook.Destruction(_w, v);
            n += VehicleLook.Smoking(_w, v, 50.0) ? 1 : 0;
            return n;
        }
    }
}
