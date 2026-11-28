using System;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2191</c> (📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7f) — ground air defence: WHICH units may engage an aircraft
    /// (<see cref="AirDefenceNodes.CanEngageAir(EntityRepository, Entity, float)"/>, read on the REAL catalogue types with their seat
    /// presets filled — the carried launcher and the station gun are the controlled-weapon path the demo uses) and the
    /// <see cref="AirDefenceNodes.EngageAircraft"/> node: it fires at the aircraft it remembers, stops when it is gone, and ends when the
    /// sky is clear.
    /// </summary>
    public sealed class AirDefenceNodesTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w = new();
        private readonly ITkbDatabase _tkb = HrotEnvironment.CreateTkb();
        private readonly Fdp.Toolkit.Replication.Services.NetworkEntityMap _map = new();
        private int _nextNid = 900;

        public AirDefenceNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            void Reg<T>() where T : unmanaged { if (!_w.IsComponentTypeRegistered<T>()) _w.RegisterComponent<T>(); }
            Reg<PassengerBuffer>(); Reg<IsEmbarkedTag>(); Reg<CarrierSeats>(); Reg<CarrierStations>(); Reg<OpenPassengerCabin>();
            Reg<PersonCarrier>(); Reg<PresetSeatIntent>(); Reg<WeaponSystemParams>(); Reg<Fdp.Toolkit.Combat.Mountable>(); Reg<WeaponSystemState>(); Reg<Health>();
            Reg<TkbIdentity>(); Reg<TargetMemory>(); Reg<WeaponChannel>();
            if (!_w.Bus.IsRegistered<WeaponFireIntent>()) _w.Bus.Register<WeaponFireIntent>();
            _w.SetSingletonManaged<ITkbDatabase>(_tkb);
        }

        public void Dispose() => _w.Dispose();

        /// <summary>A placed unit of <paramref name="type"/> with every seat preset of its type filled (its carried launcher, its station
        /// gun), as <c>SeatPresetSystem</c> fills them on the node that services the placement.</summary>
        private Entity Place(long type, Vector3 at)
        {
            var unit = _w.CreateEntity();
            int nid = _nextNid++;
            _w.AddComponent(unit, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            _w.AddComponent(unit, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(unit, new Health { Current = 100, Max = 100 });
            var template = _tkb.GetByType(type);
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(_w, unit, template);
            _map.Register(nid, unit);
            var carrier = template.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>();
            if (carrier != null)
                for (int seat = 0; seat < carrier.Seats.Count; seat++)
                {
                    long start = carrier.Seats[seat].StartWith;
                    if (start == 0 || !_tkb.TryGetByType(start, out var weaponType) || weaponType!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSystemDto>() == null) continue;
                    var weapon = _w.CreateEntity();
                    _w.AddComponent(weapon, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(start) });
                    new WeaponSystemTkbTranslator().Inject(_w, weapon, weaponType);
                    _w.AddComponent(weapon, new PresetSeatIntent { HostNetworkId = nid, Seat = (byte)seat });
                }
            new Hrot.Common.EntityCreation.SeatPresetSystem(_map).Execute(_w, 0f);
            return unit;
        }

        /// <summary>
        /// ⭐ <c>CE-2191</c> — the SOP row's gate: a Stinger or Igla gunner (his carried SAM launcher) and the ZU-23-2 technical (its station
        /// gun, +90°) CAN engage an aircraft; a rifleman (an M4), an M2 / DShK technical (a 70° pintle), a bare pickup CANNOT; lowering the
        /// gun threshold to 70° admits the M2.
        /// ✅ Red-proofs (mutants): any machine gun counts ⇒ the M2 technical answers true ⇒ red; the controlled weapons not walked ⇒ the
        /// gunners and the ZU-23 answer false ⇒ red; an ATGM launcher counted ⇒ red on the ATGM technical. (⚠ The Ground-domain SAM arm
        /// has no catalogue type to exercise it — no SAM launcher carries a Ground-only missile.)
        /// </summary>
        [Fact]
        public void CE2191_AMANPADSGunnerAndAZU23Technical_CanEngageAircraft_ARiflemanAnM2TechnicalAnATGMTechnicalCannot()
        {
            const float Threshold = 80f;
            Assert.True(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Infantry_StingerGunner, new Vector3(0, 0, 0)), Threshold));
            Assert.True(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Infantry_IglaGunner, new Vector3(10, 0, 0)), Threshold));
            Assert.True(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Technical_Zu23, new Vector3(20, 0, 0)), Threshold));
            Assert.False(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Infantry_Rifleman, new Vector3(30, 0, 0)), Threshold));
            var m2 = Place(TkbEntityTypes.Technical_Hmg, new Vector3(40, 0, 0));
            Assert.False(AirDefenceNodes.CanEngageAir(_w, m2, Threshold));
            Assert.False(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Technical_Dshk, new Vector3(50, 0, 0)), Threshold));
            Assert.False(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Technical_Atgm, new Vector3(60, 0, 0)), Threshold));
            Assert.False(AirDefenceNodes.CanEngageAir(_w, Place(TkbEntityTypes.Pickup_Toyota, new Vector3(70, 0, 0)), Threshold));
            Assert.True(AirDefenceNodes.CanEngageAir(_w, m2, 70f), "a 70° gun counts once the threshold allows it");
        }

        /// <summary>⭐ <c>CE-2309</c> (HQ-2, Command Chain §8d) — the staff's SPOT reads the SAME rule (moved to the toolkit,
        /// <c>AirDefenceWeapons</c>): the ZU-23 technical and an Igla team flag AirDefence; the DShK technical and a rifleman do not. (The
        /// Vehicle flag is the staff suite's rail: this world gives its types no kinematics.) ✅ Red-proof: the SPOT's air-defence flag not
        /// read ⇒ the ZU-23 has none.</summary>
        [Fact]
        public void CE2309_ASpotSaysWhatItSaw_TheZu23AndTheIglaAreAirDefence()
        {
            var A = Fdp.Toolkit.Squad.Command.ElementEquipment.AirDefence;
            bool Air(long type, float x) => (Fdp.Toolkit.Squad.Command.StaffComposerSystem.EquipmentOf(_w, Place(type, new Vector3(x, 0, 0))) & A) != 0;
            Assert.True(Air(TkbEntityTypes.Technical_Zu23, 0));
            Assert.True(Air(TkbEntityTypes.Infantry_IglaGunner, 10));
            Assert.False(Air(TkbEntityTypes.Technical_Dshk, 20));
            Assert.False(Air(TkbEntityTypes.Infantry_Rifleman, 30));
        }

        /// <summary>An aircraft in the unit's memory, seen at this tick.</summary>
        private unsafe void Remember(Entity unit, Entity aircraft, Vector3 at, uint tick)
        {
            if (!_w.HasComponent<TargetMemory>(unit)) _w.AddComponent(unit, new TargetMemory());
            ref var mem = ref _w.GetComponentRW<TargetMemory>(unit);
            int slot = -1;
            for (int i = 0; i < mem.Count; i++) if (mem.EntityIds[i] == (long)aircraft.PackedValue) slot = i;
            if (slot < 0) slot = mem.Count++;
            mem.EntityIds[slot] = (long)aircraft.PackedValue;
            mem.PositionsX[slot] = at.X; mem.PositionsY[slot] = at.Y; mem.PositionsZ[slot] = at.Z;
            mem.LastSeenTick[slot] = tick;
            mem.Freshness[slot] = 1f;
        }

        private Entity Helicopter(Vector3 at)
        {
            var h = _w.CreateEntity();
            _w.AddComponent(h, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(h, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            _w.AddComponent(h, new Health { Current = 800, Max = 800 });
            _w.AddComponent(h, new EntityInfo { Name = "Helicopter", ForceId = ForceId.Friend });
            return h;
        }

        /// <summary>
        /// ⭐ <c>CE-2191</c> — <see cref="AirDefenceNodes.EngageAircraft"/> on a ZU-23 technical with a helicopter in its memory: it issues
        /// AimAndFire at the HELICOPTER (not at the top threat — a nearer truck in memory is ignored), never moves, stops the fire when the
        /// helicopter is dead, and succeeds once nothing has been in the air for <c>ClearSeconds</c>.
        /// ✅ Red-proofs (mutants): firing at the top threat ⇒ the truck ⇒ red; never clearing ⇒ Running past 10 s ⇒ red; a dead
        /// aircraft still about (entity liveness, not Health) ⇒ still firing after the kill ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE2191_EngageAircraft_FiresAtTheAircraft_NotTheTopThreat_AndEndsWhenTheSkyIsClear()
        {
            var tech = Place(TkbEntityTypes.Technical_Zu23, new Vector3(300, 300, 0));
            _w.AddComponent(tech, new WeaponChannel());
            _w.AddComponent(tech, new EntityInfo { Name = "AA", ForceId = ForceId.Hostile });
            var heli = Helicopter(new Vector3(100, 300, 60));
            var truck = _w.CreateEntity();
            _w.AddComponent(truck, new SimTransform { Position = new Vector3(290, 300, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(truck, new Health { Current = 100, Max = 100 });
            Remember(tech, truck, new Vector3(290, 300, 0), 5);
            Remember(tech, heli, new Vector3(100, 300, 60), 5);

            var p = default(EngageAircraftParams);
            var ws = default(EngageAircraftState);
            var status = NodeStatus.Running;
            int frame = 0, firedAt = -1;
            Entity aimedAt = Entity.Null;
            for (; frame < 60 * 30 && status == NodeStatus.Running; frame++)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
                if (frame < 60 * 5) Remember(tech, heli, new Vector3(100 + frame * 0.5f, 300, 60), (uint)(10 + frame));   // seen until 5 s
                if (frame == 60 * 5) _w.GetComponentRW<Health>(heli).Current = 0f;
                status = AirDefenceNodes.EngageAircraft(ref p, ref ws, tech, _w);
                ref readonly var ch = ref _w.GetComponentRO<WeaponChannel>(tech);
                if (ch.ActiveAction == CombatConstants.ActionIdAimAndFire && firedAt < 0)
                {
                    firedAt = frame;
                    aimedAt = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref _w.GetComponentRW<WeaponChannel>(tech).Params[0]).Target;
                }
                if (frame == 60 * 5 + 2)
                    Assert.NotEqual(CombatConstants.ActionIdAimAndFire, _w.GetComponentRO<WeaponChannel>(tech).ActiveAction);   // the aircraft is down: no fire
            }
            Assert.True(firedAt >= 0, "it fired");
            Assert.Equal(heli, aimedAt);
            Assert.Equal(NodeStatus.Success, status);
            Assert.InRange(frame * Dt, 5f + (float)AirDefenceNodes.Defaults.ClearSeconds - 0.5f, 5f + (float)AirDefenceNodes.Defaults.ClearSeconds + 1f);
            Assert.Equal<System.Numerics.Vector3>(new Vector3(300, 300, 0), _w.GetComponentRO<SimTransform>(tech).Position);   // it never moved
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4699</c> — the aircraft ALREADY ENGAGED is kept while it is still seen: with two in sight and the newest sighting flipping
        /// between them every frame, the fire stays on the first. 📐 ua-air-defence: the Stinger's 6.33 s aim reset every tick as its target flipped
        /// Apache ↔ UH-60 and the one missile went to the UH-60. 🔴 Red-proof: the keep removed ⇒ the target changes ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE4699_TwoAircraftInSight_TheEngagedOneIsKept_NotTheNewestEachFrame()
        {
            var tech = Place(TkbEntityTypes.Technical_Zu23, new Vector3(300, 300, 0));
            _w.AddComponent(tech, new WeaponChannel());
            _w.AddComponent(tech, new EntityInfo { Name = "AA", ForceId = ForceId.Hostile });
            var a = Helicopter(new Vector3(100, 300, 60));
            var b = Helicopter(new Vector3(500, 300, 60));
            var p = default(EngageAircraftParams);
            var ws = default(EngageAircraftState);
            Entity first = Entity.Null;
            int switches = 0;
            for (int frame = 0; frame < 60 * 4; frame++)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
                uint tick = (uint)(10 + frame);
                // both seen, the newer of the two flipping every frame
                Remember(tech, a, new Vector3(100, 300, 60), frame % 2 == 0 ? tick + 1 : tick);
                Remember(tech, b, new Vector3(500, 300, 60), frame % 2 == 0 ? tick : tick + 1);
                AirDefenceNodes.EngageAircraft(ref p, ref ws, tech, _w);
                ref var ch = ref _w.GetComponentRW<WeaponChannel>(tech);
                if (ch.ActiveAction != CombatConstants.ActionIdAimAndFire) continue;
                var target = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref ch.Params[0]).Target;
                if (first.IsNull) first = target;
                else if (!target.Equals(first)) switches++;
            }
            Assert.False(first.IsNull, "it fired");
            Assert.Equal(0, switches);
        }

        // ── ⭐⭐ CE-4617 (B-3; docs/designs/air-attack/DESIGN_Radar_SAM.md RS-4 / RS-5 / RV-1 / RV-10 / RV-11) — the radar SAM ─────────────

        private void RegRadar()
        {
            void Reg<T>() where T : unmanaged { if (!_w.IsComponentTypeRegistered<T>()) _w.RegisterComponent<T>(); }
            Reg<ActiveSensorTracks>(); Reg<Fdp.Toolkit.Perception.Radar.RadarEmission>(); Reg<Fdp.Toolkit.Perception.Radar.RadarSignature>();
            Reg<WeaponState>(); Reg<Fdp.Toolkit.Motion.Locomotion>(); Reg<EntityInfo>();
        }

        /// <summary>The radar SAM vehicle (type 114) with its four missiles, a weapon channel, on the hostile side.</summary>
        private Entity RadarSam(Vector3 at)
        {
            RegRadar();
            var sam = Place(TkbEntityTypes.AirDefence_RadarSam, at);
            _w.AddComponent(sam, new WeaponState { Ammo = 4, MaxAmmo = 4 });
            _w.AddComponent(sam, new WeaponChannel());
            _w.AddComponent(sam, new EntityInfo { Name = "SAM", ForceId = ForceId.Hostile });
            _w.AddComponent(sam, new ActiveSensorTracks());   // its sight model: nothing held yet (none at all would read "seen")
            return sam;
        }

        /// <summary>An aircraft of a real catalogue <paramref name="type"/>: its locomotion and its stamped cross-section.</summary>
        private Entity Aircraft(long type, Vector3 at, Fdp.Toolkit.Tkb.Domain.LocomotionClass cls)
        {
            RegRadar();
            var a = Helicopter(at);
            _w.GetComponentRW<Fdp.Toolkit.Motion.Locomotion>(a).Class = cls;
            _w.AddComponent(a, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            float rcs = _tkb.GetByType(type).GetDescriptor<Fdp.Toolkit.Tkb.Domain.SignaturesDto>()?.Radar?.RcsM2 ?? 10f;
            _w.AddComponent(a, new Fdp.Toolkit.Perception.Radar.RadarSignature { RcsM2 = rcs });
            return a;
        }

        /// <summary><paramref name="unit"/>'s sensors hold <paramref name="target"/> now, by <paramref name="kind"/> (what the brain's track set says).</summary>
        private unsafe void Track(Entity unit, Entity target, SensorModality kind)
        {
            if (!_w.HasComponent<ActiveSensorTracks>(unit)) _w.AddComponent(unit, new ActiveSensorTracks());
            ref var t = ref _w.GetComponentRW<ActiveSensorTracks>(unit);
            int slot = -1;
            for (int i = 0; i < t.Count; i++) if (t.EntityIds[i] == (long)target.PackedValue) slot = i;
            if (slot < 0) slot = t.Count++;
            t.EntityIds[slot] = (long)target.PackedValue;
            t.Modalities[slot] = (byte)kind;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4617</c> (RV-1) — THE fire-control rule on real catalogue mounts: a helicopter held by the RADAR only is seen by the radar
        /// SAM's launcher (a radar-guided round) and NOT by the M2 on a technical, nor by a Stinger gunner's launcher (IR) — the eyes' rule
        /// (<see cref="Fdp.Toolkit.Perception.SightNow"/>) is unchanged for all of them; once the eyes hold it too, every mount sees it.
        /// ✅ Red-proof (mutant): the radar half without the guidance check ⇒ the M2 sees a radar-only contact ⇒ red.
        /// </summary>
        [Fact]
        public void CE4617_ARadarOnlyContact_IsSeenByTheRadarSamsLauncher_NeverByAnMgOrAnIrLauncher()
        {
            var sam = RadarSam(new Vector3(0, 0, 0));
            var m2 = Place(TkbEntityTypes.Technical_Hmg, new Vector3(20, 0, 0));
            var stinger = Place(TkbEntityTypes.Infantry_StingerGunner, new Vector3(40, 0, 0));
            var heli = Aircraft(TkbEntityTypes.Heli_UH60, new Vector3(3000, 0, 150), Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            foreach (var u in new[] { sam, m2, stinger }) Track(u, heli, SensorModality.Radar);

            Assert.True(FireControlSight.Sees(_w, sam, heli, CombatTkb.MountOf(_w, sam, 0)), "the radar SAM fires on its radar track");
            Assert.True(FireControlSight.SeesForAnyMount(_w, sam, heli));
            Assert.False(Fdp.Toolkit.Perception.SightNow.Sees(_w, sam, heli), "the eyes' rule is unchanged");
            var mg = WeaponSystems.WeaponOf(_w, m2);
            Assert.False(FireControlSight.Sees(_w, m2, heli, CombatTkb.MountOf(_w, mg, 0)), "an MG never fires at a radar-only contact");
            Assert.False(FireControlSight.SeesForAnyMount(_w, m2, heli));
            var stingerLauncher = WeaponSystems.WeaponOf(_w, stinger);
            Assert.False(FireControlSight.Sees(_w, stinger, heli, CombatTkb.MountOf(_w, stingerLauncher, 0)), "an IR launcher needs the eyes");

            Track(m2, heli, SensorModality.Radar | SensorModality.Visual);
            Assert.True(FireControlSight.Sees(_w, m2, heli, CombatTkb.MountOf(_w, mg, 0)), "seen by the eyes: every mount");
        }

        /// <summary>
        /// ⭐ <c>CE-4617</c> (RV-11, RS-4) — the radar SAM can engage aircraft and is a radar SAM while it has a missile; EMPTY, neither (it falls
        /// through to the shelter row). Its salvo: 2 at a manned helicopter, 1 at a recon or bomber drone, NONE at an FPV (0.01 m² — below its
        /// seeker's 0.03 m² lock).
        /// ✅ Red-proofs (mutants): the ammunition not read ⇒ the empty launcher still counts ⇒ red; the lock floor dropped ⇒ an FPV gets a missile ⇒ red.
        /// </summary>
        [Fact]
        public void CE4617_TheRadarSam_EngagesOnlyWithAMissileLeft_TwoAtAManned_OneAtADrone_NoneAtAnFpv()
        {
            var sam = RadarSam(new Vector3(0, 0, 0));
            Assert.True(AirDefenceNodes.CanEngageAir(_w, sam, 80f));
            Assert.True(RadarSamNodes.IsRadarSam(_w, sam));
            Assert.False(RadarSamNodes.IsRadarSam(_w, Place(TkbEntityTypes.Technical_Zu23, new Vector3(30, 0, 0))), "a gun is no radar SAM");

            var rotor = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft;
            Assert.Equal(2, RadarSamNodes.SalvoFor(_w, sam, Aircraft(TkbEntityTypes.Heli_UH60, new Vector3(3000, 0, 150), rotor)));
            Assert.Equal(2, RadarSamNodes.SalvoFor(_w, sam, Aircraft(TkbEntityTypes.Jet_F16, new Vector3(4000, 0, 800), Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing)));
            Assert.Equal(1, RadarSamNodes.SalvoFor(_w, sam, Aircraft(TkbEntityTypes.Drone_Recon, new Vector3(1000, 0, 100), rotor)));
            Assert.Equal(1, RadarSamNodes.SalvoFor(_w, sam, Aircraft(TkbEntityTypes.Drone_Bomber, new Vector3(1000, 50, 100), rotor)));
            Assert.Equal(0, RadarSamNodes.SalvoFor(_w, sam, Aircraft(TkbEntityTypes.Drone_Fpv, new Vector3(800, 0, 50), rotor)));

            _w.GetComponentRW<WeaponState>(sam).Ammo = 0;
            Assert.False(AirDefenceNodes.CanEngageAir(_w, sam, 80f), "an empty launcher does not win the air row");
            Assert.False(RadarSamNodes.IsRadarSam(_w, sam));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4617</c> (RS-5, RV-10) — <see cref="RadarSamNodes.RadarSamEngage"/>: cued, its radar comes ON (Search); once the radar holds
        /// the helicopter — with NO visual track — Track on it and ONE salvo of TWO at it; when the helicopter is down (the outcome) the radar
        /// goes OFF and it relocates (Success); the deactivator leaves the radar off. A second run with no track at all relocates after the
        /// 20 s of quiet. ✅ Red-proofs (mutants): the eyes' rule in place of the fire-control rule ⇒ no salvo ⇒ red; no relocation ⇒ Running
        /// past the outcome ⇒ red.
        /// </summary>
        [Fact]
        public unsafe void CE4617_RadarSamEngage_RadarOn_ASalvoOfTwoOnARadarTrack_ThenOffAndRelocate()
        {
            var sam = RadarSam(new Vector3(0, 0, 0));
            var heli = Aircraft(TkbEntityTypes.Heli_UH60, new Vector3(3000, 0, 150), Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            Remember(sam, heli, new Vector3(3000, 0, 150), 5);   // the cue: a report
            var p = default(RadarSamParams);
            var ws = default(RadarSamState);
            var status = NodeStatus.Running;
            Fdp.Toolkit.Perception.Radar.RadarEmission Emission() => _w.GetComponentRO<Fdp.Toolkit.Perception.Radar.RadarEmission>(sam);

            _w.SetSingleton(new GlobalTime { TotalTime = 1.0 });
            status = RadarSamNodes.RadarSamEngage(ref p, ref ws, sam, _w);
            Assert.Equal(Fdp.Toolkit.Perception.Radar.RadarMode.Search, Emission().Mode);
            Assert.NotEqual(CombatConstants.ActionIdAimAndFire, _w.GetComponentRO<WeaponChannel>(sam).ActiveAction);   // nothing tracked yet

            Track(sam, heli, SensorModality.Radar);   // the radar holds it; the eyes do not
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0 });
            status = RadarSamNodes.RadarSamEngage(ref p, ref ws, sam, _w);
            Assert.Equal(Fdp.Toolkit.Perception.Radar.RadarMode.Track, Emission().Mode);
            Assert.Equal(heli, Emission().TrackedTarget);
            ref var ch = ref _w.GetComponentRW<WeaponChannel>(sam);
            Assert.Equal(CombatConstants.ActionIdAimAndFire, ch.ActiveAction);
            var fire = System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.AimAndFireParams>(ref ch.Params[0]);
            Assert.Equal((heli, 2), (fire.Target, fire.Rounds));

            ch.Status = NodeStatus.Success;   // the weapon brain fired the two
            _w.SetSingleton(new GlobalTime { TotalTime = 8.0 });
            status = RadarSamNodes.RadarSamEngage(ref p, ref ws, sam, _w);
            Assert.Equal(NodeStatus.Running, status);
            _w.GetComponentRW<Health>(heli).Current = 0f;   // the outcome: down
            for (double t = 9.0; t < 60.0 && status == NodeStatus.Running; t += 0.5)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = t });
                status = RadarSamNodes.RadarSamEngage(ref p, ref ws, sam, _w);
                if (ws.Stage == 1) Assert.False(Emission().IsOn, "the radar is off while it relocates");
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.False(Emission().IsOn);

            // the quiet path: cued, nothing ever tracked ⇒ relocate after QuietSeconds
            var ws2 = default(RadarSamState);
            var heli2 = Aircraft(TkbEntityTypes.Heli_UH60, new Vector3(-3000, 0, 150), Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            _w.GetComponentRW<ActiveSensorTracks>(sam).Count = 0;
            Remember(sam, heli2, new Vector3(-3000, 0, 150), 50);
            double started = 100.0, ended = -1;
            var s2 = NodeStatus.Running;
            for (double t = started; t < started + 60 && s2 == NodeStatus.Running; t += 0.5)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = t });
                s2 = RadarSamNodes.RadarSamEngage(ref p, ref ws2, sam, _w);
                if (ws2.Stage == 1 && ended < 0) ended = t;
            }
            Assert.InRange(ended - started, (double)RadarSamNodes.Defaults.QuietSeconds - 0.5, (double)RadarSamNodes.Defaults.QuietSeconds + 1.0);
            RadarSamNodes.Deactivate_RadarSamEngage(ref p, ref ws2, sam, _w);
            Assert.False(Emission().IsOn);
        }

        /// <summary>
        /// ⭐ <c>CE-4617</c> (found by <c>sam-site</c>) — a radar SAM DESTROYED while tracking radiates nothing: its last <c>RadarEmission</c> says
        /// Track, but <see cref="Fdp.Toolkit.Perception.Radar.RadarEmissions.IsOn"/> answers off; the node releases (radar written Off,
        /// Failure) and the SOP row no longer fires for it. ✅ Red-proof (mutant): the in-fight test dropped from the one rule ⇒ "on" ⇒ red.
        /// </summary>
        [Fact]
        public void CE4617_ADestroyedRadarSam_RadiatesNothing_AndItsEngagementEnds()
        {
            var sam = RadarSam(new Vector3(0, 0, 0));
            var heli = Aircraft(TkbEntityTypes.Heli_UH60, new Vector3(3000, 0, 150), Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            Fdp.Toolkit.Perception.Radar.RadarEmissions.Set(_w, sam, Fdp.Toolkit.Perception.Radar.RadarMode.Track, heli,
                Fdp.Toolkit.Perception.Radar.RadarEmitterClass.AirDefence, 8000f, 1.0);
            Assert.True(Fdp.Toolkit.Perception.Radar.RadarEmissions.IsOn(_w, sam));
            _w.GetComponentRW<Health>(sam).Current = 0f;
            Assert.False(Fdp.Toolkit.Perception.Radar.RadarEmissions.IsOn(_w, sam), "a destroyed radar radiates nothing");
            Assert.False(RadarSamNodes.IsRadarSam(_w, sam));
            var p = default(RadarSamParams);
            var ws = default(RadarSamState);
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0 });
            Assert.Equal(NodeStatus.Failure, RadarSamNodes.RadarSamEngage(ref p, ref ws, sam, _w));
            Assert.Equal(Fdp.Toolkit.Perception.Radar.RadarMode.Off, _w.GetComponentRO<Fdp.Toolkit.Perception.Radar.RadarEmission>(sam).Mode);
        }

        // ── ⭐⭐ CE-2436 AU-7 (A-10; DESIGN_Assault_Under_Fire §9.5) — the infantry against drones ─────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-7 (A-10b) — "can engage a DRONE" beside "can engage aircraft": the anti-drone man (his shotgun, 60 m) may at 50 m
        /// and not at 70 m; the automatic rifleman (his M249, 800 m) at 200 m; a rifleman only inside 20 m; the Stinger gunner (an aircraft
        /// weapon) at any range. The drone gunners (A-10c) are the shotgun man and the MG, not the rifleman.
        /// ✅ Red-proofs (mutants): the rifle's 20 m dropped ⇒ the rifleman engages at 30 m ⇒ red; the shotgun's range ignored ⇒ 70 m true ⇒ red;
        /// the MG not counted ⇒ the M249 at 200 m false ⇒ red; the pellets not counted as a drone gunner ⇒ red.
        /// </summary>
        [Fact]
        public void CE2436_AU7_CanEngageADrone_TheShotgunInItsRange_TheMg_ARifleOnlyClose()
        {
            var shotgun = Place(TkbEntityTypes.Infantry_AntiDrone, new Vector3(0, 0, 0));
            var mg = Place(TkbEntityTypes.Infantry_AutomaticRifleman, new Vector3(10, 0, 0));
            var rifle = Place(TkbEntityTypes.Infantry_Rifleman, new Vector3(20, 0, 0));
            var stinger = Place(TkbEntityTypes.Infantry_StingerGunner, new Vector3(30, 0, 0));
            Assert.True(AirDefenceWeapons.CanEngageDrone(_w, shotgun, 50f));
            Assert.False(AirDefenceWeapons.CanEngageDrone(_w, shotgun, 70f));
            Assert.True(AirDefenceWeapons.CanEngageDrone(_w, mg, 200f));
            Assert.True(AirDefenceWeapons.CanEngageDrone(_w, rifle, 15f));
            Assert.False(AirDefenceWeapons.CanEngageDrone(_w, rifle, 30f));
            Assert.True(AirDefenceWeapons.CanEngageDrone(_w, stinger, 300f));
            Assert.True(AirDefenceWeapons.IsDroneGunner(_w, shotgun));
            Assert.True(AirDefenceWeapons.IsDroneGunner(_w, mg));
            Assert.False(AirDefenceWeapons.IsDroneGunner(_w, rifle));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-7 (A-10a, R-2110) — the SAME hit model the shot uses: at a drone crossing at 30 m/s, 30 m away, one shotgun shot
        /// (12 pellets over 15 mils) has a real chance (&gt; 0.4) where one rifle round has almost none (&lt; 0.1, the aim error ×4 by its speed and
        /// 2-D in the air); past the shotgun's 60 m it has none; a hovering drone is easier for both; a round at a man on the ground is the old
        /// <see cref="HitModel.HitChance"/> exactly. ✅ Red-proofs: the air's 2-D dropped ⇒ the rifle's 0.24 ⇒ red; the range not applied ⇒ red.
        /// </summary>
        [Fact]
        public void CE2436_AU7_TheShotgunHitsAFastDroneClose_TheRifleRarely_NothingPastItsRange()
        {
            var gun = _tkb.GetByType(TkbEntityTypes.Infantry_AntiDrone).GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>()!.Mounts[1];
            var m4 = _tkb.GetByType(TkbEntityTypes.Infantry_Rifleman).GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>()!.Mounts[0];
            Assert.Equal(12, gun.Pellets);
            const float Radius = 0.175f;
            float fast = 1f + 30f / HitModel.AirSpeedPerFactor;   // 4
            float Shot(Fdp.Toolkit.Tkb.Domain.WeaponMountDto m, float factor, float range)
                => HitModel.ShotHitChance(m.DispersionMils / 1000f * factor, range, Radius, air: true, m.Pellets, m.PelletSpreadMils, m.Range);
            Assert.True(Shot(gun, fast, 30f) > 0.4f, $"shotgun {Shot(gun, fast, 30f):0.00}");
            Assert.True(Shot(m4, fast, 30f) < 0.1f, $"rifle {Shot(m4, fast, 30f):0.00}");
            Assert.Equal(0f, Shot(gun, fast, 70f));
            Assert.True(Shot(gun, 1f, 30f) > Shot(gun, fast, 30f));
            Assert.Equal(HitModel.HitChance(0.006f, 100f, 0.3f), HitModel.ShotHitChance(0.006f, 100f, 0.3f, air: false, 0, 0f, 0f));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-7 (A-10b) — the shelter's fire at a drone follows the rule (<see cref="AirSense.MayFireAtDrone"/>): a rifleman
        /// holds his fire at a drone 40 m off and fires at one 15 m off; the anti-drone man fires at 40 m; a unit with no weapon data is left to the
        /// weapon brain. ✅ Red-proof: the gate dropped from <c>ShelterFromAir</c> is the node rail's (<c>ShelterFromAirNodesTests</c>).
        /// </summary>
        [Fact]
        public void CE2436_AU7_ARiflemanHoldsFireAtADistantDrone_TheShotgunManFires()
        {
            var rifle = Place(TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
            var shotgun = Place(TkbEntityTypes.Infantry_AntiDrone, Vector3.Zero);
            var bare = _w.CreateEntity();
            _w.AddComponent(bare, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            Assert.False(AirSense.MayFireAtDrone(_w, rifle, new Vector3(0, 40, 10)));
            Assert.True(AirSense.MayFireAtDrone(_w, rifle, new Vector3(0, 12, 8)));
            Assert.True(AirSense.MayFireAtDrone(_w, shotgun, new Vector3(0, 40, 10)));
            Assert.True(AirSense.MayFireAtDrone(_w, bare, new Vector3(0, 400, 10)));
        }
    }
}
