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
            Reg<PersonCarrier>(); Reg<PresetSeatIntent>(); Reg<WeaponSystemParams>(); Reg<WeaponSystemState>(); Reg<Health>();
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
            _w.AddComponent(unit, new TkbIdentity { TkbType = type });
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
                    _w.AddComponent(weapon, new TkbIdentity { TkbType = start });
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
        /// gunners and the ZU-23 answer false ⇒ red; a Ground-domain SAM counted ⇒ red on the ATGM technical.
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
        /// ✅ Red-proofs (mutants): firing at the top threat ⇒ the truck ⇒ red; never clearing ⇒ Running past 10 s ⇒ red.
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
            Assert.InRange(frame * Dt, 5f + AirDefenceNodes.Defaults.ClearSeconds - 0.5f, 5f + AirDefenceNodes.Defaults.ClearSeconds + 1f);
            Assert.Equal(new Vector3(300, 300, 0), _w.GetComponentRO<SimTransform>(tech).Position);   // it never moved
        }
    }
}
