using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;   // Q65 obstacle 1: the request tier moved here
using Hrot.Core.Network;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.SimHost.Tests
{
    // â”€â”€â”€ Stubs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    internal sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;
        public long LastAllocatedId { get; private set; }

        public StubIdAllocator(long startId = 100) => _next = startId;
        public long AllocateId() { LastAllocatedId = _next; return _next++; }
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    internal sealed class StubRequestSource : IEntityCreationRequestSource
    {
        private readonly List<EntityCreationRequest> _pending = new();

        public void Enqueue(EntityCreationRequest r) => _pending.Add(r);

        public void ProcessRequests(Action<EntityCreationRequest> handler)
        {
            foreach (var req in _pending)
                handler(req);
            _pending.Clear();
        }

        public void Dispose() { }
    }

    internal struct AckRecord
    {
        public Guid RequestId;
        public int  EntityId;   // stored as int to match pre-existing assertions
        public int  StatusCode; // same values as EntityOperationStatus
    }

    internal sealed class StubAckSink : IEntityAckSink
    {
        public List<AckRecord> WrittenAcks { get; } = new();

        public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
            => WrittenAcks.Add(new AckRecord
            {
                RequestId  = requestId,
                EntityId   = (int)entityId,
                StatusCode = (int)status,
            });

        public void Dispose() { }
    }

    internal sealed class StubOwnershipStrategy : Fdp.Toolkit.Replication.Abstractions.IOwnershipDistributionStrategy
    {
        private readonly List<DescriptorGrant> _grants = new();

        public void AddGrant(long descriptorTypeId, int nodeId)
            => _grants.Add(new DescriptorGrant { DescriptorTypeId = descriptorTypeId, NodeId = nodeId });

        public IReadOnlyList<DescriptorGrant> GetInitialGrants(in Fdp.Toolkit.Replication.Abstractions.GrantRequest request)
            => _grants;
    }

    // â”€â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class CreateEntityRequestSystemTests
    {
        private const long  ValidTkbType  = 42L;
        // ValidDisType is kept as ulong for engine-side assertions.
        private const ulong ValidDisType  = 0x0100_0000_0000_0001UL;
        private const int   LocalNodeId   = 7;

        // â”€â”€ Fixture helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", ValidTkbType));
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            // Register component types that SpawnEntityCommand will carry through the bus
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            // Register events used by NetworkSpawningSystem if it were running
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static EntityCreationRequest MakeValidRequest(long tkbType = ValidTkbType) =>
            new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = tkbType,
                DisType            = ValidDisType,
            };

        private static (CreateEntityRequestSystem system, StubAckSink ackSink, StubIdAllocator idAlloc)
            BuildSystem(ITkbDatabase tkb, StubRequestSource requestSource)
        {
            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                requestSource, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);
            return (system, ackSink, idAlloc);
        }

        // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // ── ⭐⭐ Weapon systems WS-4 (W-I; STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets; DESIGN_Weapon_Systems §4d) ──────────

        private const long PresetHost = 9104L, PresetGun = 9600L, PresetMan = 9200L;

        /// <summary>
        /// ⭐⭐ Air Ops slice 6a F4 (DESIGN_Air_Operations §7a; 🔒 user `2026-10-14` "extend the wire capacity") — a placed doors-off UH-60
        /// (404, the REAL catalogue) spawns its two door gunners and two M134s into seats 8–11, beyond the old 8-wide list: its 8 rider seats
        /// stay free for a squad. Boarded, each M134 is controlled by the helicopter and fires while its door gunner is in the fight; a door
        /// gunner shot ⇒ his gun falls silent (<c>NoGunner</c>), the other still fires. ✅ Red-proof: Capacity 8 ⇒ the translator refuses 12
        /// seats ⇒ red.
        /// </summary>
        [Fact]
        public void F4_APlacedDoorsOffBlackHawk_HoldsTwoM134s_AndTwoDoorGunners_ItsRidersSeatsStayFree()
        {
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = Hrot.Map.Common.TkbEntityTypes.Heli_UH60_DoorsOff, DisType = ValidDisType,
                InitialComponents = new List<object> { new EntityInfo { Name = "H1", ForceId = ForceId.Friend } },
            });
            var (system, _, _) = BuildSystem(tkb, source);
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(5, commands.Count);   // the helicopter + 2 door gunners + 2 M134s
            var seats = commands.Where(c => c.TkbType != Hrot.Map.Common.TkbEntityTypes.Heli_UH60_DoorsOff)
                .Select(c => (c.TkbType, ((Fdp.Toolkit.Behavior.Components.PresetSeatIntent)c.InitialComponents!.Single(i => i is Fdp.Toolkit.Behavior.Components.PresetSeatIntent)).Seat))
                .OrderBy(x => x.Seat).ToArray();
            Assert.Equal(new[] { (Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, (byte)8), (Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M134Minigun, (byte)9),
                                 (Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, (byte)10), (Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M134Minigun, (byte)11) }, seats);

            // boarded by SeatPresetSystem on the real types
            using var w = new EntityRepository();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.OpenPassengerCabin>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            var placed = new List<(Entity, byte)>();
            foreach (var (type, seat) in seats)
            {
                var e = w.CreateEntity();
                if (type == Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M134Minigun)
                    new Fdp.Toolkit.Combat.WeaponSystemTkbTranslator().Inject(w, e, tkb.GetByType(type));
                else w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100, Max = 100 });
                w.AddComponent(e, new Fdp.Toolkit.Behavior.Components.PresetSeatIntent { HostNetworkId = 700, Seat = seat });
                placed.Add((e, seat));
            }
            var heli = w.CreateEntity();
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(w, heli, tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Heli_UH60_DoorsOff));
            map.Register(700, heli);
            new Hrot.Common.EntityCreation.SeatPresetSystem(map).Execute(w, 0f);

            var list = w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(heli);
            foreach (var (e, seat) in placed) Assert.Equal(e, list.Passengers[seat]);
            Assert.Equal(8, Fdp.Toolkit.Behavior.Embarkation.CapacityOf(w, heli));        // the riders' seats
            Assert.Equal(0, Fdp.Toolkit.Behavior.Embarkation.FreeSeat(w, heli));          // … all still free
            var (leftGunner, leftGun, rightGun) = (placed[0].Item1, placed[1].Item1, placed[3].Item1);
            Assert.Equal(heli, Fdp.Toolkit.Combat.WeaponSystems.ControllerOf(w, leftGun));
            Assert.True(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, leftGun));
            Assert.True(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, rightGun));

            w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(leftGunner).Current = 0f;   // the left door gunner is shot
            Assert.False(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, leftGun, out var why));
            Assert.Equal(Fdp.Toolkit.Combat.WeaponHold.NoGunner, why);
            Assert.True(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, rightGun));
        }

        /// <summary>
        /// ⭐⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a; the twin of the 104 rails above, on the REAL catalogue) — a placed armed variant
        /// spawns its weapon and its gunner into the seats its type names: the HMMWV (102) its rifleman into the roof-ring gunner seat 3
        /// and an M2 into the ring station 4 (its three passenger seats 0–2 stay free); the new technicals 108 / 109 a DShK / an M134 into
        /// the bed station 2 and a rifleman into gunner seat 1. Boarded, the weapon is controlled by the vehicle and may fire. A save /
        /// load (the request carries SeatPresetsFilled) adds NEITHER again. (Red by construction — not run: 102 with the old
        /// PassengerSeats(3) ⇒ one command.)
        /// </summary>
        [Theory]
        [InlineData(Hrot.Map.Common.TkbEntityTypes.Truck_HMMWV, Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M2Hmg, 3, 4, 3, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman)]
        [InlineData(Hrot.Map.Common.TkbEntityTypes.Technical_Dshk, Hrot.Map.Common.TkbEntityTypes.WeaponSystem_DshkHmg, 1, 2, 0, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman)]
        [InlineData(Hrot.Map.Common.TkbEntityTypes.Technical_Minigun, Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M134Minigun, 1, 2, 0, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman)]
        [InlineData(Hrot.Map.Common.TkbEntityTypes.Technical_Zu23, Hrot.Map.Common.TkbEntityTypes.WeaponSystem_Zu23, 1, 2, 0, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman)]   // ⭐ CE-2190
        // ⭐ BR-16 — the urban APC's 12.7 mm RWS: an M2 in roof station 9, worked by an enclosed soldier in gunner seat 8; its squad's 8 seats stay free
        [InlineData((long)Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbMilitaryApc, Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M2Hmg, 8, 9, 8,
                    (long)Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbInfantrySoldier)]
        public void P1_APlacedArmedVariant_HoldsItsWeaponAndGunner_AndALoadAddsNeither(long hostType, long weaponType, int gunnerSeat,
                                                                                        int stationSeat, int riders, long gunnerType)
        {
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            SpawnEntityCommand[] Place(object mark)
            {
                var repo = CreateWorld();
                var source = new StubRequestSource();
                source.Enqueue(new EntityCreationRequest
                {
                    RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = hostType, DisType = ValidDisType, PreAllocatedNetworkId = 900,
                    InitialComponents = new List<object> { mark },
                });
                var (system, _, _) = BuildSystem(tkb, source);
                system.Execute(repo, 0f);
                repo.Bus.SwapBuffers();
                return ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>().ToArray();
            }

            var commands = Place(new EntityInfo { Name = "V1", ForceId = ForceId.Friend });
            Assert.Equal(3, commands.Length);   // the vehicle + its weapon + its gunner
            var seats = commands.Where(c => c.TkbType != hostType)
                .Select(c => (c.TkbType, ((Fdp.Toolkit.Behavior.Components.PresetSeatIntent)c.InitialComponents!.Single(i => i is Fdp.Toolkit.Behavior.Components.PresetSeatIntent)).Seat))
                .OrderBy(x => x.Seat).ToArray();
            Assert.Equal(new[] { (gunnerType, (byte)gunnerSeat), (weaponType, (byte)stationSeat) }, seats);
            Assert.Single(Place(new Fdp.Toolkit.Behavior.Components.SeatPresetsFilled { Filled = 1 }));   // the saved vehicle: neither again

            using var w = new EntityRepository();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.OpenPassengerCabin>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            Entity gun = Entity.Null;
            foreach (var (type, seat) in seats)
            {
                var e = w.CreateEntity();
                if (type == weaponType) { new Fdp.Toolkit.Combat.WeaponSystemTkbTranslator().Inject(w, e, tkb.GetByType(type)); gun = e; }
                else w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100, Max = 100 });
                w.AddComponent(e, new Fdp.Toolkit.Behavior.Components.PresetSeatIntent { HostNetworkId = 900, Seat = seat });
            }
            var vehicle = w.CreateEntity();
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(w, vehicle, tkb.GetByType(hostType));
            map.Register(900, vehicle);
            new Hrot.Common.EntityCreation.SeatPresetSystem(map).Execute(w, 0f);

            Assert.Equal(gun, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(vehicle).Passengers[stationSeat]);
            Assert.Equal(vehicle, Fdp.Toolkit.Combat.WeaponSystems.ControllerOf(w, gun));
            Assert.True(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, gun));
            Assert.Equal(riders, Fdp.Toolkit.Behavior.Embarkation.CapacityOf(w, vehicle));   // the HMMWV's three passenger seats …
            if (riders > 0) Assert.Equal(0, Fdp.Toolkit.Behavior.Embarkation.FreeSeat(w, vehicle));   // … all still free
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2187</c> (DESIGN_Weapon_Systems §5c) — a placed MANPADS gunner (202) comes with his Stinger (613): ONE more spawn command
        /// (the launcher, for his carry station — seat 1), and once the preset fills it the launcher rides on his back, HE controls it
        /// (it fires carried) and it can fire. The first soldier preset with a weapon on his back.
        /// </summary>
        [Fact]
        public void CE2187_APlacedStingerGunner_CarriesALauncherHeCanFire()
        {
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            var repo = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = Hrot.Map.Common.TkbEntityTypes.Infantry_StingerGunner,
                DisType = ValidDisType, PreAllocatedNetworkId = 901, InitialComponents = new List<object> { new EntityInfo { Name = "S1", ForceId = ForceId.Friend } },
            });
            var (system, _, _) = BuildSystem(tkb, source);
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>().ToArray();
            Assert.Equal(2, commands.Length);   // the soldier + his launcher
            var launcherCmd = Assert.Single(commands, c => c.TkbType == Hrot.Map.Common.TkbEntityTypes.WeaponSystem_Stinger);
            Assert.Equal(1, ((Fdp.Toolkit.Behavior.Components.PresetSeatIntent)launcherCmd.InitialComponents!
                .Single(i => i is Fdp.Toolkit.Behavior.Components.PresetSeatIntent)).Seat);

            using var w = new EntityRepository();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.OpenPassengerCabin>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            w.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            var launcher = w.CreateEntity();
            new Fdp.Toolkit.Combat.WeaponSystemTkbTranslator().Inject(w, launcher, tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.WeaponSystem_Stinger));
            w.AddComponent(launcher, new Fdp.Toolkit.Behavior.Components.PresetSeatIntent { HostNetworkId = 901, Seat = 1 });
            var soldier = w.CreateEntity();
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(w, soldier, tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Infantry_StingerGunner));
            w.AddComponent(soldier, new Fdp.Toolkit.Combat.Components.Health { Current = 100, Max = 100 });
            map.Register(901, soldier);
            new Hrot.Common.EntityCreation.SeatPresetSystem(map).Execute(w, 0f);

            Assert.Equal(launcher, w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(soldier).Passengers[1]);
            Assert.Equal(soldier, Fdp.Toolkit.Combat.WeaponSystems.ControllerOf(w, launcher));
            Assert.True(Fdp.Toolkit.Combat.WeaponSystems.CanFire(w, launcher));
        }

        /// <summary>A technical's TKB: a host with a driver seat, a gunner seat starting with a rifleman and a pintle station starting
        /// with an M2; the M2 and the rifleman.</summary>
        private static TkbDatabase PresetTkb()
        {
            var db = new TkbDatabase();
            var host = new TkbTemplate("Technical", PresetHost);
            host.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CarrierDefDto { Seats =
            {
                new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Driver },
                new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = PresetMan },
                new Fdp.Toolkit.Tkb.Domain.SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station,
                    Accepts = Fdp.Toolkit.Tkb.Domain.MountKinds.Pintle, OperatedBy = 1, NeedsGunner = true, StartWith = PresetGun },
            } });
            db.Register(host);
            var gun = new TkbTemplate("M2", PresetGun);
            gun.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSystemDto { MountKinds = Fdp.Toolkit.Tkb.Domain.MountKinds.Pintle });
            db.Register(gun);
            db.Register(new TkbTemplate("Rifleman", PresetMan));
            return db;
        }

        /// <summary>
        /// ⭐⭐ WS-4 (W-I) — a placed technical is ONE request, and the node servicing it spawns its seat presets beside it: the M2 for the
        /// station, a rifleman of the HOST'S SIDE for the gunner seat, each naming its seat on its host (PresetSeatIntent); the host is
        /// marked filled (SeatPresetsFilled, saved). ✅ Red-proof: without the 3.6/4c steps ⇒ one command ⇒ red.
        /// </summary>
        [Fact]
        public void WS4_APlacedHost_SpawnsItsSeatPresets_OfItsSide_AndIsMarkedFilled()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = PresetHost, DisType = ValidDisType,
                InitialComponents = new List<object> { new EntityInfo { Name = "T1", ForceId = ForceId.Hostile } },
            });
            var (system, _, _) = BuildSystem(PresetTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Equal(3, commands.Count);
            var host = commands.Single(c => c.TkbType == PresetHost);
            Assert.Contains(host.InitialComponents!, c => c is Fdp.Toolkit.Behavior.Components.SeatPresetsFilled);
            foreach (var (type, seat) in new[] { (PresetMan, 1), (PresetGun, 2) })
            {
                var preset = commands.Single(c => c.TkbType == type);
                var intent = (Fdp.Toolkit.Behavior.Components.PresetSeatIntent)preset.InitialComponents!.Single(c => c is Fdp.Toolkit.Behavior.Components.PresetSeatIntent);
                Assert.Equal((host.NetworkId, (byte)seat), (intent.HostNetworkId, intent.Seat));
                Assert.Equal(ForceId.Hostile, ((EntityInfo)preset.InitialComponents!.Single(c => c is EntityInfo)).ForceId);
                Assert.Equal(host.OwnerNodeId, preset.OwnerNodeId);
            }
        }

        /// <summary>
        /// ⭐⭐ WS-4 (W-I, the steer's rail) — a save / load adds NEITHER again: the loaded host's request carries the saved
        /// SeatPresetsFilled, so only the host is spawned (its saved M2 and gunner come back as their own entities, boarded by the saved
        /// passenger list). ✅ Red-proof: ToFill ignoring the mark ⇒ three commands ⇒ red.
        /// </summary>
        [Fact]
        public void WS4_ALoadedHost_AlreadyFilled_SpawnsNoPresetsAgain()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = PresetHost, DisType = ValidDisType,
                PreAllocatedNetworkId = 555,
                InitialComponents = new List<object> { new Fdp.Toolkit.Behavior.Components.SeatPresetsFilled { Filled = 1 } },
            });
            var (system, _, _) = BuildSystem(PresetTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            Assert.Single(((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>());
        }

        /// <summary>
        /// ⭐⭐ WS-4 (W-I) — SeatPresetSystem boards each preset into the seat its TYPE names (not the first free one of a role): the M2
        /// into the station, the rifleman into the gunner seat, once the host is there; then the intents are gone. A preset whose host has
        /// not arrived waits. ✅ Red-proof: boarding with TryEmbark (first free seat of the passenger role) ⇒ the rifleman finds no
        /// passenger seat ⇒ red.
        /// </summary>
        [Fact]
        public void WS4_SeatPresetSystem_BoardsEachPresetIntoItsSeat_OnceTheHostIsThere()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.OpenPassengerCabin>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            repo.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            var db = PresetTkb();
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            var gun = repo.CreateEntity();
            new Fdp.Toolkit.Combat.WeaponSystemTkbTranslator().Inject(repo, gun, db.GetByType(PresetGun));
            repo.AddComponent(gun, new Fdp.Toolkit.Behavior.Components.PresetSeatIntent { HostNetworkId = 700, Seat = 2 });
            var man = repo.CreateEntity();
            repo.AddComponent(man, new Fdp.Toolkit.Behavior.Components.PresetSeatIntent { HostNetworkId = 700, Seat = 1 });
            var system = new Hrot.Common.EntityCreation.SeatPresetSystem(map);

            system.Execute(repo, 0f);   // the host is not here yet: both wait
            Assert.True(repo.HasComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>(gun));

            var host = repo.CreateEntity();
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(repo, host, db.GetByType(PresetHost));
            map.Register(700, host);
            system.Execute(repo, 0f);

            var list = repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(host);
            Assert.Equal((man, gun), (list.Passengers[1], list.Passengers[2]));
            Assert.False(repo.HasComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>(gun));
            Assert.False(repo.HasComponent<Fdp.Toolkit.Behavior.Components.PresetSeatIntent>(man));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-143</c> — <b>the default is <c>AllPeers</c>, so adoption changes NOTHING.</b>
        /// 📌 Both publish sites hardcoded <c>ReliableInitType.AllPeers</c>; the request now carries the
        /// axis. ⛔ If the default ever drifts, every existing caller silently changes its wire
        /// behaviour — this rail is what makes acceptance ⑥'s "byte-identical default" checkable here.
        /// </summary>
        [Fact]
        public void InitType_DefaultsToAllPeers_SoExistingCallersAreUnchanged()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(MakeValidRequest());          // ⭐ does NOT set InitType
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.Equal(Fdp.Toolkit.Replication.ReliableInitType.AllPeers, commands[0].InitType);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-143</c> — <b>an explicit <c>None</c> REACHES the published command.</b>
        /// 📌 This is the capability the row exists for: an IG map drawing is a single-owner presentation
        /// entity, and waiting for every peer to ACK it is pointless latency and a stall risk when a peer
        /// is absent. ⛔ Without this rail the field could be added and silently ignored — the
        /// silent-default shape.
        /// </summary>
        [Fact]
        public void InitType_WhenTheRequestSaysNone_ThePublishedCommandSaysNone()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                InitType           = Fdp.Toolkit.Replication.ReliableInitType.None,
            });
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.Equal(Fdp.Toolkit.Replication.ReliableInitType.None, commands[0].InitType);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-143</c> — <b>NEITHER publish site may hardcode the value again.</b>
        ///
        /// <para>📌 There are TWO sites: the root entity and each auto-spawned TKB child. The child site
        /// is not reachable from this fixture's single-template TKB, so a behavioural rail would cover
        /// only half the change. ⭐ This scan covers both, and it is the guard against the specific
        /// regression of someone re-introducing a literal during a merge.</para>
        ///
        /// <para>⚠ Comments are stripped first (<c>CE-156</c>): the doc comments above the two sites
        /// legitimately mention <c>AllPeers</c>, and a raw scan would redden on the prose explaining the
        /// fix.</para>
        /// </summary>
        [Fact]
        public void InitType_IsNotHardcodedAtEitherPublishSite()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(
                    "Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs"));

            Assert.Equal(3,   // ⭐ WS-4 — the parent, the children, and the seat presets (W-I)
                System.Text.RegularExpressions.Regex.Matches(
                code, @"InitType\s*=\s*pending\.Request\.InitType").Count);
            Assert.DoesNotContain("InitType          = ReliableInitType.AllPeers", code);
        }

        // ── D2 — the throwaway flag (R-140) ─────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>D2</c> — <b>the default is <c>false</c>, so nothing existing becomes transient.</b>
        /// ⛔ If this default ever drifts, every scenario-load request silently starts producing entities
        /// the serializer skips — i.e. <b>saving a scenario would quietly lose entities</b>. That failure
        /// is invisible until someone reloads, which is exactly why it gets a rail.
        /// </summary>
        [Fact]
        public void IsTransient_DefaultsToFalse_SoExistingCallersAreUnchanged()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(MakeValidRequest());          // ⭐ does NOT set IsTransient
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.False(commands[0].IsTransient);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>D2</c> — <b>an explicit <c>true</c> REACHES the published command.</b>
        /// 📌 The capability the flag exists for: a passive node's sketch must not reach a saved scenario
        /// (<c>R-140</c>). ⛔ Without this rail the field could be added and silently ignored — the
        /// silent-default shape, and the ninth instance of it in this programme.
        /// </summary>
        [Fact]
        public void IsTransient_WhenTheRequestSaysTrue_ThePublishedCommandSaysTrue()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                IsTransient        = true,
            });
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.True(commands[0].IsTransient);
        }

        /// <summary>
        /// ⭐⭐ <c>D2</c> — <b>NEITHER publish site may drop the flag.</b>
        ///
        /// <para>📌 Same two-site problem <c>CE-143</c> has: the root entity and each auto-spawned TKB
        /// child. ⭐ The child site is unreachable from this fixture's single-template TKB, so a
        /// behavioural rail covers only half. ⚠ And the child half MATTERS — a sketch's children are part
        /// of the same sketch, so a template with <c>N</c> children would otherwise save <c>N</c>
        /// orphans.</para>
        /// </summary>
        [Fact]
        public void IsTransient_IsForwardedAtBothPublishSites()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(
                    "Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs"));

            Assert.Equal(3,   // ⭐ WS-4 — the parent, the children, and the seat presets (W-I)
                System.Text.RegularExpressions.Regex.Matches(
                code, @"IsTransient\s*=\s*pending\.Request\.IsTransient").Count);
        }

        /// <summary>⭐ <c>CE-515</c> ③ — the request's reliable-init timeout (<c>CE-292</c>) REACHES the spawn order: the
        /// debug API's <c>POST /entities/spawn</c> now goes through the pack and must keep its <c>reliableTimeoutSeconds</c>
        /// knob. ⛔ Added and silently ignored would be the silent-default shape.</summary>
        [Fact]
        public void ReliableInitTimeout_WhenTheRequestCarriesOne_ThePublishedCommandCarriesIt()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId           = Guid.NewGuid(),
                OwnerAppInstanceId  = LocalNodeId,
                TkbType             = ValidTkbType,
                DisType             = ValidDisType,
                InitType            = Fdp.Toolkit.Replication.ReliableInitType.AllPeers,
                ReliableInitTimeout = TimeSpan.FromSeconds(7),
            });
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            Assert.Equal(TimeSpan.FromSeconds(7), commands[0].ReliableInitTimeout);
        }

        /// <summary><c>CE-515</c> ③ — both publish sites (the root and each TKB child) carry the timeout, as they carry
        /// <c>InitType</c>.</summary>
        [Fact]
        public void ReliableInitTimeout_IsForwardedAtBothPublishSites()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(
                    "Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs"));

            Assert.Equal(3,   // ⭐ WS-4 — the parent, the children, and the seat presets (W-I)
                System.Text.RegularExpressions.Regex.Matches(
                code, @"ReliableInitTimeout\s*=\s*pending\.Request\.ReliableInitTimeout").Count);
        }

        /// <summary>⭐ <c>CE-1017</c> S2 — the request's birth-height request REACHES the spawn order (resolved by
        /// <c>NetworkSpawningSystem</c>); absent stays absent. 📄 docs/DESIGN_Add_Entity_Picker.md §2e.</summary>
        /// <summary>
        /// ⭐ <c>CE-2139</c> (C3, rule K2) — a scenario saved before C3 stores a unit as <c>1.1.225</c> (a land platform). A
        /// COMPOSITE therefore always spawns with its TEMPLATE's DIS type; any other type keeps what its request carries.
        /// </summary>
        [Fact]
        public void CE2139_AComposite_SpawnsWithItsTemplatesDisType_NotTheRequests()
        {
            const long unitType = 43L;
            var unitDis = Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Armor, Hrot.Core.Tkb.UnitEchelon.Platoon, 225);
            var tkb = CreateTkb();
            var unit = new TkbTemplate("Platoon", unitType) { DisType = unitDis };
            unit.AddDescriptor(new Hrot.Map.Definitions.Tkb.TkbCompositionDef { Echelon = "Platoon" });
            tkb.Register(unit);

            var stale = new DISEntityType { Kind = 1, Domain = 1, Country = 225 }.Value;   // what a pre-C3 save holds
            var repo = CreateWorld();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = LocalNodeId, TkbType = unitType, DisType = stale,
            });
            source.Enqueue(MakeValidRequest());
            var (system, _, _) = BuildSystem(tkb, source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Equal(unitDis.Value, commands.Single(c => c.TkbType == unitType).DisType);
            Assert.Equal(ValidDisType, commands.Single(c => c.TkbType == ValidTkbType).DisType);   // a non-composite: unchanged
        }

        [Fact]
        public void CE1017_SpawnHeight_WhenTheRequestCarriesOne_ThePublishedCommandCarriesIt()
        {
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            var height = new Fdp.Toolkit.NetworkSpawning.SpawnHeight(Fdp.Toolkit.NetworkSpawning.SpawnHeightMode.OnLevel, 2);
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                SpawnHeight        = height,
            });
            source.Enqueue(MakeValidRequest());
            var (system, _, _) = BuildSystem(CreateTkb(), source);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Equal(2, commands.Count);
            Assert.Equal(height, commands[0].SpawnHeight);
            Assert.Null(commands[1].SpawnHeight);
        }

        /// <summary><c>CE-1017</c> S2 — both publish sites (the root and each TKB child) carry it, so children resolve the
        /// same level at their own x,y.</summary>
        [Fact]
        public void CE1017_SpawnHeight_IsForwardedAtBothPublishSites()
        {
            var code = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(
                    "Hrot/Engine/Hrot.Common/Systems/CreateEntityRequestSystem.cs"));

            Assert.Equal(3,   // ⭐ WS-4 — the parent, the children, and the seat presets (W-I)
                System.Text.RegularExpressions.Regex.Matches(
                code, @"SpawnHeight\s*=\s*pending\.Request\.SpawnHeight").Count);
        }

        [Fact]
        public void ProcessRequest_ValidTkbType_PublishesSpawnEntityCommand()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            var request = MakeValidRequest();
            source.Enqueue(request);

            var (system, _, _) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand must be in the write buffer â€” swap to make it visible
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var cmd = commands[0];
            Assert.Equal(ValidTkbType,  cmd.TkbType);
            Assert.Equal(ValidDisType,  cmd.DisType);
            Assert.Equal(LocalNodeId,   cmd.OwnerNodeId);
            Assert.Equal(request.RequestId, cmd.RequestId);
        }

        [Fact]
        public void ProcessRequest_UnknownTkbType_SendsErrorAck()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();        // only ValidTkbType is registered
            var source  = new StubRequestSource();
            var request = MakeValidRequest(tkbType: 9999L);  // unknown type
            source.Enqueue(request);

            var (system, ackSink, _) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: error ACK sent, no SpawnEntityCommand published
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal((int)EntityOperationStatus.UnknownDescriptorType, ackSink.WrittenAcks[0].StatusCode);
            Assert.Equal(request.RequestId, ackSink.WrittenAcks[0].RequestId);

            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Empty(commands);
        }

        [Fact]
        public void ProcessRequest_AllocatesNewNetworkId()
        {
            // Arrange
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var (system, ackSink, idAlloc) = BuildSystem(tkb, source);

            // Act
            system.Execute(repo, 0f);

            // Assert: allocator was called and Phase-1 InProgress ACK carries the allocated ID
            Assert.Equal(100L, idAlloc.LastAllocatedId);
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(100, ackSink.WrittenAcks[0].EntityId);
            Assert.Equal((int)EntityOperationStatus.InProgress, ackSink.WrittenAcks[0].StatusCode);
        }

        // â”€â”€ GC04: Time-slicing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void TimeSlicing_OnThousandRequests_DispatchesExactlyMaxPerTickOnFirstFrame()
        {
            // Arrange â€” enqueue 1000 valid requests.
            const int TotalRequests = 1000;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < TotalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act â€” single tick.
            system.Execute(repo, 0f);

            // Assert: exactly MaxRequestsPerTick commands published this tick.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, commands.Count);

            // Assert: remaining requests are still in the queue.
            int expectedRemaining = TotalRequests - CreateEntityRequestSystem.MaxRequestsPerTick;
            Assert.Equal(expectedRemaining, system.PendingQueueCount);
        }

        [Fact]
        public void TimeSlicing_OnThousandRequests_AcksAllSentOnFirstFrame()
        {
            // Arrange â€” enqueue 1000 valid requests.
            const int TotalRequests = 1000;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < TotalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var idAlloc = new StubIdAllocator(startId: 1);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);

            // Act â€” single tick.
            system.Execute(repo, 0f);

            // Assert: all 1000 Phase-1 InProgress ACKs sent synchronously, even though
            // only MaxRequestsPerTick SpawnEntityCommands were dispatched this frame.
            Assert.Equal(TotalRequests, ackSink.WrittenAcks.Count);

            // Every Phase-1 ACK must be InProgress.
            foreach (var ack in ackSink.WrittenAcks)
                Assert.Equal((int)EntityOperationStatus.InProgress, ack.StatusCode);
        }

        [Fact]
        public void TimeSlicing_SecondFrame_ProcessesRemainingRequests()
        {            // Arrange â€” enqueue exactly MaxRequestsPerTick + 1 requests.
            int totalRequests = CreateEntityRequestSystem.MaxRequestsPerTick + 1;
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            for (int i = 0; i < totalRequests; i++)
                source.Enqueue(MakeValidRequest());

            var (system, _, _) = BuildSystem(tkb, source);

            // Act â€” first tick: should dispatch MaxRequestsPerTick, leave 1 in queue.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var firstFrameCmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            // Capture count *before* a second swap clears the backing buffer.
            int firstFrameCount = firstFrameCmds.Count;

            // Act â€” second tick: source is empty, queue has 1 item.
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            var secondFrameCmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            // Assert
            Assert.Equal(CreateEntityRequestSystem.MaxRequestsPerTick, firstFrameCount);
            Assert.Single(secondFrameCmds);
            Assert.Equal(0, system.PendingQueueCount);
        }

        // â”€â”€ ATTR-JSON: JSON attribute path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static EntityRepository CreateWorldWithIgEntityData()
        {
            var repo = CreateWorld();
            repo.RegisterComponent<EntityInfo>();
            return repo;
        }

        /// <summary>
        /// When <c>InitialAttributesJson</c> contains a Name patch the entity spawns
        /// with the correct name via the JSON compiler path.
        /// </summary>
        [Fact]
        public void ProcessRequest_Json_PatchesName()
        {
            // Arrange
            var repo   = CreateWorldWithIgEntityData();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            var request = new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                InitialAttributesJson = "{\"Name\":\"Delta\"}",
            };
            source.Enqueue(request);

            var jsonCompiler = AttributeCompilerFactory.Build(null);
            var idAlloc      = new StubIdAllocator(startId: 100);
            var ackSink      = new StubAckSink();
            var system       = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: jsonCompiler);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand carries EntityInfo with Name = "Delta".
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);

            var initialComponents = commands[0].InitialComponents;
            Assert.NotNull(initialComponents);
            var entityData = Assert.Single(initialComponents!.OfType<EntityInfo>());
            Assert.Equal("Delta", entityData.Name.ToString());
        }

        // â”€â”€ BD1-P7T1: Delegate caching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// BD1-P7T1 SC1: The delegate passed to <c>ProcessRequests</c> must be the same
        /// cached instance on every <c>Execute</c> call â€” ReferenceEquals must return
        /// <c>true</c> across two separate Execute invocations.
        /// </summary>
        [Fact]
        public void ProcessRequests_UsesPreCachedDelegate()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new CapturingRequestSource();
            var ackSink = new StubAckSink();
            var idAlloc = new StubIdAllocator(startId: 100);

            var system = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null);

            // Execute twice so the source can capture the delegate both times.
            system.Execute(repo, 0f);
            system.Execute(repo, 0f);

            Assert.Equal(2, source.CapturedDelegates.Count);
            Assert.True(
                ReferenceEquals(source.CapturedDelegates[0], source.CapturedDelegates[1]),
                "The delegate passed to ProcessRequests must be the same cached instance on every Execute call.");
        }

        /// <summary>
        /// BD1-P7T1 SC2: Refactored path must preserve existing behaviour â€”
        /// a valid request still produces a SpawnEntityCommand and an ACK.
        /// </summary>
        [Fact]
        public void ProcessRequests_DelegateCache_BehaviourRegression()
        {
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var (system, ackSink, _) = BuildSystem(tkb, source);
            system.Execute(repo, 0f);

            // ACK sent (Phase-1 InProgress â€” Phase-2 is dispatched by NedRequestFinalizationSystem).
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal((int)EntityOperationStatus.InProgress, ackSink.WrittenAcks[0].StatusCode);

            // SpawnEntityCommand produced.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
        }

        [Fact]
        public void ProcessRequest_DefaultProcessor_PublishesDeferredTakeOwnershipForNavigationStatus()
        {
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var ackSink = new StubAckSink();
            var idAlloc = new StubIdAllocator(startId: 100);
            var ownershipStrategy = new StubOwnershipStrategy();
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.WorldPos, 11);
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.NavigationStatus, 11);

            var system = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null,
                finalizationSystem: null,
                isDefaultProcessor: true,
                ownershipStrategy: ownershipStrategy);

            system.Execute(repo, 0f);

            repo.Bus.SwapBuffers();
            var dtoCommands = ((ISimulationView)repo).ReadManagedEvents<DeferredTakeOwnershipCommand>();

            Assert.Single(dtoCommands);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.WorldPos && g.NodeId == 11);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.NavigationStatus && g.NodeId == 11);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-271</c> — <b>a NON-ARBITER local owner distributes its non-role components via
        /// the auto-takeover grant, exactly as the arbiter does.</b>
        ///
        /// <para>🔒 <c>R-138</c> (canon): <i>"THE SYSTEM IS FULLY DISTRIBUTED: EVERY ECS NODE CAN CREATE
        /// ENTITIES … a node that originates its own entity … authority moves at runtime over the
        /// <c>OwnershipUpdate</c> topic, with the previous owner symmetrically yielding."</i> A request
        /// TARGETED AT THIS NODE (<c>OwnerAppInstanceId == LocalNodeId</c>) is serviced locally and this
        /// node OWNS the entity — so it must hand off the components its role does not cover, the same
        /// birth-critical handoff the design names (<c>DESIGN_Role_Affinity_Ownership.md</c> §3.1).</para>
        ///
        /// <para>⛔ <b>The defect this rail reproduces:</b> grant publication was gated on
        /// <c>isDefaultProcessor</c> — the broadcast TIEBREAKER for <c>Owner == 0</c> requests, NOT an
        /// authority gate (<c>Q65</c> §4). That is the pre-auto-takeover <i>"the arbiter is the sole
        /// owner"</i> assumption, valid only before <c>DeferredTakeOwnership</c> existed. A non-arbiter
        /// creator (e.g. IG under <c>R-138</c>) therefore kept every component and handed off nothing, so
        /// a Muscle could never take <c>dtWorldPos</c> and the entity was frozen from the cluster's view.</para>
        ///
        /// <para>⚠ It is impossible for two nodes to publish grants for one entity: exactly one node
        /// services a given creation (the level-1 routing guard, <c>IsHandledLocally</c>), and that node
        /// is the one publishing here. So un-gating cannot double-grant.</para>
        /// </summary>
        [Fact]
        public void ProcessRequest_NonArbiterLocalOwner_PublishesDeferredTakeOwnership()
        {
            var repo    = CreateWorld();
            var tkb     = CreateTkb();
            var source  = new StubRequestSource();
            source.Enqueue(MakeValidRequest());   // OwnerAppInstanceId == LocalNodeId ⇒ handled locally, this node owns it

            var ackSink = new StubAckSink();
            var idAlloc = new StubIdAllocator(startId: 100);
            var ownershipStrategy = new StubOwnershipStrategy();
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.WorldPos, 11);
            ownershipStrategy.AddGrant(DescriptorTypeOrdinals.NavigationStatus, 11);

            var system = new CreateEntityRequestSystem(
                source, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: null,
                finalizationSystem: null,
                isDefaultProcessor: false,          // ⛔ NOT the arbiter — a plain local creating owner
                ownershipStrategy: ownershipStrategy);

            system.Execute(repo, 0f);

            repo.Bus.SwapBuffers();
            var dtoCommands = ((ISimulationView)repo).ReadManagedEvents<DeferredTakeOwnershipCommand>();

            Assert.Single(dtoCommands);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.WorldPos && g.NodeId == 11);
            Assert.Contains(dtoCommands[0].Grants, g => g.DescriptorTypeId == DescriptorTypeOrdinals.NavigationStatus && g.NodeId == 11);
        }

        // -- C013: PreAllocatedNetworkId and ChildComponentOverrides ──────────────────────────

        private static TkbDatabase CreateTkbWithChild(int childInstanceId = 2, long childTkbType = 43L)
        {
            var db     = new TkbDatabase();
            var parent = new TkbTemplate("TestParent", ValidTkbType);
            parent.ChildBlueprints.Add(new ChildBlueprintDefinition(childInstanceId, childTkbType));
            db.Register(parent);
            db.Register(new TkbTemplate("TestChild", childTkbType));
            return db;
        }

        /// <summary>C013 SC1: Normal request (PreAllocatedNetworkId == 0) still calls AllocateId().</summary>
        [Fact]
        public void C013_NormalRequest_AllocateIdCalled()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            source.Enqueue(MakeValidRequest());

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, tkb, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);

            // AllocateId() was called and returned 100.
            Assert.Equal(100L, idAlloc.LastAllocatedId);

            // ACK carries the allocated ID.
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(100, ackSink.WrittenAcks[0].EntityId);

            // SpawnEntityCommand uses the allocated network ID.
            repo.Bus.SwapBuffers();
            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(cmds);
            Assert.Equal(100L, cmds[0].NetworkId);
        }

        /// <summary>C013 SC2: Pre-allocated ID bypasses AllocateId().</summary>
        [Fact]
        public void C013_PreAllocatedNetworkId_BypassesAllocator()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkb();
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                PreAllocatedNetworkId = 5555L,
            });

            var idAlloc = new StubIdAllocator(startId: 999);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, tkb, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);

            // AllocateId() must NOT have been called (LastAllocatedId stays at 0).
            Assert.Equal(0L, idAlloc.LastAllocatedId);

            // ACK carries the pre-allocated ID.
            Assert.Single(ackSink.WrittenAcks);
            Assert.Equal(5555, ackSink.WrittenAcks[0].EntityId);

            // SpawnEntityCommand uses the pre-allocated network ID.
            repo.Bus.SwapBuffers();
            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(cmds);
            Assert.Equal(5555L, cmds[0].NetworkId);
        }

        /// <summary>C013 SC3: Child uses pre-allocated ID and override components merged.</summary>
        [Fact]
        public void C013_ChildOverride_UsesPreAllocatedIdAndMergesComponents()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkbWithChild(childInstanceId: 2);
            var source = new StubRequestSource();
            var overrideComp = new SimTransform { Position = new System.Numerics.Vector3(1.0f, 2.0f, 0f) };
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                ChildComponentOverrides = new Dictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>
                {
                    { 2, (9001L, new List<object> { overrideComp }) },
                },
            });

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, tkb, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            // 1 parent + 1 child
            Assert.Equal(2, cmds.Count);

            var childCmd = cmds.FirstOrDefault(c => c.NetworkId == 9001L);
            Assert.Equal(9001L, childCmd.NetworkId);
            Assert.NotNull(childCmd.InitialComponents);
            Assert.Contains(childCmd.InitialComponents!, c => c is SimTransform);

            // AllocateId() called only once (for the parent at 100); NOT called for the child.
            Assert.Equal(100L, idAlloc.LastAllocatedId);
        }

        /// <summary>C013 SC4: PreAllocatedId == 0 in override entry falls through to AllocateId().</summary>
        [Fact]
        public void C013_ChildOverride_ZeroPreAllocatedId_FallsBackToAllocator()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkbWithChild(childInstanceId: 2);
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                PreAllocatedNetworkId = 5555L,   // parent pre-allocated — avoids parent alloc
                ChildComponentOverrides = new Dictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>
                {
                    { 2, (0L, new List<object>()) },   // 0 means "fall through"
                },
            });

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, tkb, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(2, cmds.Count);

            // AllocateId() called exactly once — for the child (parent was pre-allocated).
            Assert.Equal(100L, idAlloc.LastAllocatedId);
            var childCmd = cmds.First(c => c.NetworkId != 5555L);
            Assert.Equal(100L, childCmd.NetworkId);
        }

        /// <summary>C013 SC5: Null ChildComponentOverrides — AllocateId() called for each child.</summary>
        [Fact]
        public void C013_NullChildComponentOverrides_AllocatorCalledPerChild()
        {
            var repo = CreateWorld();
            var db   = new TkbDatabase();
            var parent = new TkbTemplate("TestParent2", ValidTkbType);
            parent.ChildBlueprints.Add(new ChildBlueprintDefinition(1, 43L));
            parent.ChildBlueprints.Add(new ChildBlueprintDefinition(2, 44L));
            db.Register(parent);
            db.Register(new TkbTemplate("Child1", 43L));
            db.Register(new TkbTemplate("Child2", 44L));

            var source = new StubRequestSource();
            // ChildComponentOverrides is null (default)
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
            });

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, db, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            // 1 parent + 2 children
            Assert.Equal(3, cmds.Count);

            // AllocateId() called for parent (100) and both children (101, 102).
            // LastAllocatedId will be 102 (the last call).
            Assert.Equal(102L, idAlloc.LastAllocatedId);
        }

        /// <summary>
        /// C013 SC6: When a scenario-load request (⭐ CE-3324: <c>IsScenarioLoad</c>, no longer inferred from the id) has a
        /// ChildComponentOverrides dict that does NOT contain an entry for a child's InstanceId,
        /// that child is intentionally SKIPPED (ORBAT-dedup path — subordinate was extracted as a
        /// root entity). Only the parent SpawnEntityCommand is published; AllocateId() is not called.
        /// </summary>
        // Production intentionally skips children with no override entry on scenario load
        // (CreateEntityRequestSystem: "Prevent duplicate ORBAT entities on scenario load" — the
        // subordinate was extracted as a root entity). Architect-confirmed 2026-07-12 (DECISIONS D-14).
        [Fact]
        public void C013_ChildOverride_KeyAbsent_ChildSkipped_OnScenarioLoad()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkbWithChild(childInstanceId: 2);
            var source = new StubRequestSource();
            // Override has key 99 — not matching child InstanceId 2
            source.Enqueue(new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                PreAllocatedNetworkId = 5555L,
                IsScenarioLoad        = true,   // ⭐ CE-3324 — what StagingEntityExtractor says
                ChildComponentOverrides = new Dictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>
                {
                    { 99, (9999L, new List<object>()) },  // no entry for InstanceId 2
                },
            });

            var idAlloc = new StubIdAllocator(startId: 100);
            var ackSink = new StubAckSink();
            var system  = new CreateEntityRequestSystem(source, ackSink, tkb, idAlloc, LocalNodeId);

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            // On scenario load, a child with no override entry
            // is SKIPPED — only the parent is spawned. AllocateId() is not called.
            Assert.Single(cmds);
            Assert.Equal(5555L, cmds[0].NetworkId);
            Assert.Equal(0L, idAlloc.LastAllocatedId);
        }

        /// <summary>
        /// ⭐ CE-3324 — a RUNTIME request that fixes its own id (a traffic conversion, terrain objects, a test hook — and since CE-4024
        /// that id crosses the wire) is NOT a scenario load: the template's children are spawned as on any request. ⛔ It used to be
        /// read as one (<c>PreAllocatedNetworkId != 0</c>) and the children were silently skipped.
        /// </summary>
        [Fact]
        public void CE3324_ARuntimeRequestWithAFixedId_StillGetsItsChildren()
        {
            var repo   = CreateWorld();
            var tkb    = CreateTkbWithChild(childInstanceId: 2);
            var source = new StubRequestSource();
            source.Enqueue(new EntityCreationRequest
            {
                RequestId             = Guid.NewGuid(),
                OwnerAppInstanceId    = LocalNodeId,
                TkbType               = ValidTkbType,
                DisType               = ValidDisType,
                PreAllocatedNetworkId = 5555L,   // fixed by its author — and nothing says "scenario load"
            });

            var idAlloc = new StubIdAllocator(startId: 100);
            var system  = new CreateEntityRequestSystem(source, new StubAckSink(), tkb, idAlloc, LocalNodeId);
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cmds = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(2, cmds.Count);                                   // the parent AND its child
            Assert.Contains(cmds, c => c.NetworkId == 5555L);
            Assert.Contains(cmds, c => c.NetworkId == 100L);               // the child's id from the allocator
        }
    }

    // â”€â”€ Delegate capture test helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// A <see cref="ICreateEntityRequestSource"/> stub that records the delegate
    /// instance passed to <see cref="ProcessRequests"/> on each call, allowing
    /// tests to verify the delegate is cached (same instance on repeated calls).
    /// </summary>
    internal sealed class CapturingRequestSource : IEntityCreationRequestSource
    {
        public List<Action<EntityCreationRequest>> CapturedDelegates { get; } = new();

        public void ProcessRequests(Action<EntityCreationRequest> handler)
            => CapturedDelegates.Add(handler);

        public void Dispose() { }
    }
}
