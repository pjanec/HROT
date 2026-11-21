using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
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
            _w.RegisterComponent<VehicleOpenings>();
            _w.RegisterComponent<VehicleLights>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<NetworkVelocity>();
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

        /// <summary>⭐ VA-4 — an opening's progress is read BY ITS TKB ID (a renderer's key), through THE one progress rule with the type's open seconds: the
        /// MBT class's driver hatch (id 2, slot 1, 1.5 s) half open 0.75 s after it began opening; an unknown id and a vehicle without openings read 0.
        /// 🔴 Red-proof: reading slot <c>id</c> instead of the slot whose id matches reads the gunner's closed hatch (slot 2).</summary>
        [Fact]
        public void CE4670_AnOpeningsProgress_IsReadByItsId()
        {
            var v = Vehicle();
            Assert.Equal(0f, VehicleLook.OpeningProgress(_w, v, 2, 10.0));   // no VehicleOpenings yet
            var o = new VehicleOpenings { Count = 3 };
            o.Slots[1] = new OpeningSlot { State = OpeningState.Opening, Since = 10.0 };
            _w.AddComponent(v, o);
            Assert.Equal(0.5f, VehicleLook.OpeningProgress(_w, v, 2, 10.75), 3);
            Assert.Equal(1f, VehicleLook.OpeningProgress(_w, v, 2, 20.0));
            Assert.Equal(0f, VehicleLook.OpeningProgress(_w, v, 3, 20.0));   // the gunner's, closed
            Assert.Equal(0f, VehicleLook.OpeningProgress(_w, v, 9, 20.0));   // no such opening
        }

        /// <summary>⭐ VA-3 — brake lights are DERIVED, never stored: the MBT class's Brake group (bit 2) joins the stored mask while the vehicle decelerates
        /// (10 → 8 m/s over 0.5 s = 4 m/s²), stays lit for the hold, goes out when it drives steadily, never lights while coasting gently (0.2 m/s²) or under a
        /// Blackout discipline, and the stored mask is all a reader without a tracker sees. A seek backwards restarts the sample.
        /// 🔴 Red-proof: testing <c>speed - previous</c> (acceleration) instead of <c>previous - speed</c> never lights the stop lights.</summary>
        [Fact]
        public void CE4670_BrakeLights_AreDerivedFromDeceleration()
        {
            var v = Vehicle();
            _w.AddComponent(v, new VehicleLights { OnMask = 0b11, Discipline = LightDiscipline.Normal });
            _w.AddComponent(v, new SimVelocity { Linear = new Vector3(10f, 0f, 0f) });
            var brakes = new BrakeLights();
            Assert.Equal(0b11u, VehicleLook.Lights(_w, v, 0.0, brakes));          // first sight: no history
            Assert.Equal(0b11u, VehicleLook.Lights(_w, v, 0.5, brakes));          // steady
            _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(8f, 0f, 0f);
            Assert.Equal(0b111u, VehicleLook.Lights(_w, v, 1.0, brakes));         // 4 m/s² ⇒ the stop lights
            Assert.Equal(0b11u, VehicleLook.Lights(_w, v, 1.0, null));            // no tracker ⇒ the stored mask only
            Assert.Equal(0b111u, VehicleLook.Lights(_w, v, 1.3, brakes));         // held
            Assert.Equal(0b11u, VehicleLook.Lights(_w, v, 1.6, brakes));          // steady again, the hold over
            _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(7.9f, 0f, 0f);
            Assert.Equal(0b11u, VehicleLook.Lights(_w, v, 2.1, brakes));          // 0.2 m/s²: coasting
            _w.GetComponentRW<VehicleLights>(v).Discipline = LightDiscipline.Blackout;
            _w.GetComponentRW<VehicleLights>(v).OnMask = 0;
            _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(4f, 0f, 0f);
            Assert.Equal(0u, VehicleLook.Lights(_w, v, 2.6, brakes));             // blacked out: no stop light
            _w.GetComponentRW<VehicleLights>(v).Discipline = LightDiscipline.Normal;
            _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(1f, 0f, 0f);
            Assert.Equal(0u, VehicleLook.Lights(_w, v, 0.5, brakes));             // a seek back: the sample restarts, nothing lit
        }

        /// <summary>⭐ A replica has no SimVelocity: the tracker reads the received <see cref="NetworkVelocity"/>.</summary>
        [Fact]
        public void CE4670_BrakeLights_OnAReplica_ReadTheReceivedVelocity()
        {
            var v = Vehicle();
            _w.AddComponent(v, new VehicleLights { Discipline = LightDiscipline.Normal });
            _w.AddComponent(v, new NetworkVelocity { Value = new Vector3(0f, 12f, 0f) });
            var brakes = new BrakeLights();
            VehicleLook.Lights(_w, v, 5.0, brakes);
            _w.GetComponentRW<NetworkVelocity>(v).Value = new Vector3(0f, 10f, 0f);
            Assert.Equal(0b100u, VehicleLook.Lights(_w, v, 5.5, brakes));
        }

        /// <summary>⭐ The opening and light readers allocate nothing in the steady state (a renderer calls them per vehicle per frame).</summary>
        [Fact]
        public void CE4670_TheOpeningAndLightReaders_AllocateNothing()
        {
            var v = Vehicle();
            _w.AddComponent(v, new VehicleOpenings { Count = 3 });
            _w.AddComponent(v, new VehicleLights { OnMask = 1, Discipline = LightDiscipline.Normal });
            _w.AddComponent(v, new SimVelocity { Linear = new Vector3(10f, 0f, 0f) });
            var brakes = new BrakeLights();
            double t = 0.0;
            for (int i = 0; i < 20; i++)   // warm: the type's catalogues resolve once, on the first brake
            {
                _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(10f - i * 0.5f, 0f, 0f);
                VehicleLook.OpeningProgress(_w, v, 2, t); VehicleLook.Lights(_w, v, t, brakes); t += 0.1;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            uint n = 0;
            for (int i = 0; i < 1000; i++)
            {
                _w.GetComponentRW<SimVelocity>(v).Linear = new Vector3(10f - (i % 20) * 0.5f, 0f, 0f);
                n += (uint)VehicleLook.OpeningProgress(_w, v, 2, t) + VehicleLook.Lights(_w, v, t, brakes);
                t += 0.1;
            }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(n > 0);
        }

        private const long Heli = 2;

        private void RegisterHeli(EntityRepository w, TkbDatabase db)
        {
            if (!w.IsComponentTypeRegistered<HeatSource>()) w.RegisterComponent<HeatSource>();
            if (db.TryGetByType(Heli, out _)) return;
            var t = new TkbTemplate("H1", Heli) { DisType = new DISEntityType { Kind = 1, Domain = 2, Category = 21 } };
            t.AddDescriptor(new SignaturesDto { Thermal = new ThermalSignatureDto { HasPowerPlant = true, SpinUpSeconds = 20f, SpinDownSeconds = 30f } });
            db.Register(t);
        }

        private Entity Heli1(EntityRepository w, HeatSourceState state, double since, double motionSince = -1.0)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)Heli });
            w.AddComponent(e, HeatSource.Of(state, (SimInstant)since, (SimInstant)(motionSince < 0 ? since : motionSince)));
            return e;
        }

        /// <summary>
        /// ⭐ VA-5 (VA-S4) — a rotor's speed is DERIVED from the engine state and its time: started (Idle since 10 s) it runs up at full scale per 20 s to the
        /// ground idle 0.6 (0.3 after 6 s); flying (Running since 30 s) it goes on to 1 (0.8 after 4 s); shut down (Off since 50 s) it runs down at full scale
        /// per 30 s (0.3 after 9 s, 0 after 18 s); a dead engine from full; a STOP (Since = MotionSince) comes down from full — to idle
        /// crewed (never a dip to 0), to 0 empty; a state held from the scenario's start is at its target at once (a parked
        /// helicopter never runs down at load, one loaded flying never runs up); no power plant ⇒ 0. 🔴 Red-proof: ignore the time since the change (jump to the target)
        /// ⇒ the 0.3 / 0.8 samples read 0.6 / 1.
        /// </summary>
        [Fact]
        public void CE4670_ARotorsSpeed_RunsUpAndDown_FromTheEngineStateAndItsTime()
        {
            RegisterHeli(_w, _db);
            var h = Heli1(_w, HeatSourceState.Off, 0.0);
            Assert.Equal(0f, VehicleLook.RotorSpeed(_w, h, 5.0));   // parked from the start: still, never a run-down at load
            var flying = Heli1(_w, HeatSourceState.Running, 0.0);
            Assert.Equal(1f, VehicleLook.RotorSpeed(_w, flying, 0.5));   // loaded flying: full at once, never a run-up
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Idle, (SimInstant)10.0, (SimInstant)0.0));   // crew aboard: Since alone
            Assert.Equal(0f, VehicleLook.RotorSpeed(_w, h, 10.0), 4);
            Assert.Equal(0.3f, VehicleLook.RotorSpeed(_w, h, 16.0), 4);
            Assert.Equal(0.6f, VehicleLook.RotorSpeed(_w, h, 29.0), 4);
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Running, (SimInstant)30.0, (SimInstant)30.0));
            Assert.Equal(0.8f, VehicleLook.RotorSpeed(_w, h, 34.0), 4);
            Assert.Equal(1f, VehicleLook.RotorSpeed(_w, h, 45.0), 4);
            Assert.Equal(1f, VehicleLook.EngineLevel(_w, h, 45.0), 4);
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Off, (SimInstant)50.0, (SimInstant)30.0));   // crew out while idling: Since alone
            Assert.Equal(0.3f, VehicleLook.RotorSpeed(_w, h, 59.0), 4);
            Assert.Equal(0f, VehicleLook.RotorSpeed(_w, h, 68.0), 4);
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Dead, (SimInstant)70.0, (SimInstant)70.0));
            Assert.Equal(0.5f, VehicleLook.RotorSpeed(_w, h, 85.0), 4);
            // ⭐ a STOP (Since = MotionSince): landed and stopped at 90 s, still crewed ⇒ Idle, coming DOWN from full to idle — never a dip to 0
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Idle, (SimInstant)90.0, (SimInstant)90.0));
            Assert.Equal(0.8f, VehicleLook.RotorSpeed(_w, h, 96.0), 4);
            Assert.Equal(0.6f, VehicleLook.RotorSpeed(_w, h, 120.0), 4);
            // ⭐ stopped and EMPTY (Since = MotionSince, Off): from full to 0
            _w.SetComponent(h, HeatSource.Of(HeatSourceState.Off, (SimInstant)130.0, (SimInstant)130.0));
            Assert.Equal(0.5f, VehicleLook.RotorSpeed(_w, h, 145.0), 4);
            var tank = Vehicle();   // the tank type has no thermal data of its own and no locomotion ⇒ no power plant
            Assert.Equal(0f, VehicleLook.RotorSpeed(_w, tank, 85.0));
        }

        /// <summary>⭐ VA-5 — a REPLICA computes the owner's speed at every sample: the reader reads only the replicated engine state and the type (here a second
        /// world holding the same HeatSource, as the wire's EntityHeatSource gives it); and the class default is a rotor's 20 s / 30 s.</summary>
        [Fact]
        public void CE4670_ARotorsSpeed_IsTheSameOnAReplica_AtEverySample()
        {
            RegisterHeli(_w, _db);
            using var replica = new EntityRepository();
            replica.RegisterComponent<TkbIdentity>();
            var db2 = new TkbDatabase();
            RegisterHeli(replica, db2);
            replica.SetSingletonManaged<ITkbDatabase>(db2);
            var owner = Heli1(_w, HeatSourceState.Idle, 3.0, 0.0);
            var copy = Heli1(replica, HeatSourceState.Idle, 3.0, 0.0);
            for (double now = 0.0; now < 40.0; now += 0.37)
                Assert.Equal(VehicleLook.RotorSpeed(_w, owner, now), VehicleLook.RotorSpeed(replica, copy, now));
            Assert.Equal(20f, Fdp.Toolkit.Perception.Signatures.ThermalDefaults.For(LocomotionClass.Rotorcraft)!.SpinUpSeconds);
            Assert.Equal(30f, Fdp.Toolkit.Perception.Signatures.ThermalDefaults.For(LocomotionClass.Rotorcraft)!.SpinDownSeconds);
        }

        /// <summary>⭐ The rotor reader allocates nothing (a renderer calls it per aircraft per frame).</summary>
        [Fact]
        public void CE4670_TheRotorReader_AllocatesNothing()
        {
            RegisterHeli(_w, _db);
            var h = Heli1(_w, HeatSourceState.Running, 1.0);
            float sum = 0f;
            for (int i = 0; i < 10; i++) sum += VehicleLook.RotorSpeed(_w, h, i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) sum += VehicleLook.RotorSpeed(_w, h, i * 0.1);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sum > 0f);
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
