using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Gameplay;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ Player Join §9 (S1 part A) — the gameplay SERVER over a REAL DDS participant on its own test domain: the deploy processor (accept / refuse / slot life), the
    /// slot reader that fills <see cref="StationReservations"/>, and the WIRE rail — a node with the pack composed and the feature OFF creates no gameplay topic.
    /// Each test is its own instance ⇒ its own DDS domain (the station is a SECOND participant on it, as a Player node is).
    /// </summary>
    public sealed class GameplayDeployServerTests : IDisposable
    {
        private static int _domain = 140;

        private const int Server = 5, StationA = 20, StationB = 21;
        private const int Rifleman = 100, Plane = 101;

        private readonly DdsParticipant _serverP, _stationP;
        private readonly GameplayWire _wire;
        private readonly EntityRepository _w = new();
        private readonly NetworkEntityMap _map = new();
        private readonly DeployProcessorSystem _proc;
        private readonly ServerPlayerSlotReaderSystem _slots;
        private readonly DdsWriter<DeployRequestTopic> _request;
        private readonly DdsReader<DeployStatusTopic> _status;
        private readonly DdsReader<PlayerSlotTopic> _slotSamples;
        private readonly List<DeployStatusTopic> _statuses = new();
        private readonly List<(bool valid, PlayerSlotTopic slot)> _slotLog = new();
        private double _now = 100;
        private long _wallMs = 1_000;

        public GameplayDeployServerTests()
        {
            uint domain = (uint)Interlocked.Increment(ref _domain);
            _serverP = new DdsParticipant(domain);
            _stationP = new DdsParticipant(domain);
            _w.RegisterComponent<Playable>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterComponent<PossessedBy>();
            _w.RegisterComponent<Health>();
            _w.RegisterEvent<NodeDeparted>();
            SetTime(_now);
            _wire = new GameplayWire(_serverP);
            _proc = new DeployProcessorSystem(_wire, _map, Server, election: null, nowMs: () => _wallMs);
            _slots = new ServerPlayerSlotReaderSystem(_wire);
            _request = new DdsWriter<DeployRequestTopic>(_stationP);
            _status = new DdsReader<DeployStatusTopic>(_stationP);
            _slotSamples = new DdsReader<PlayerSlotTopic>(_stationP);
            Tick();   // creates the server's request reader (the wire is lazy) so the station's volatile writer can match it
            Until(() => _request.CurrentStatus.CurrentCount > 0, "the station's request writer must match the server's reader");
        }

        public void Dispose()
        {
            _request.Dispose(); _status.Dispose(); _slotSamples.Dispose(); _wire.Dispose(); _stationP.Dispose(); _serverP.Dispose(); _w.Dispose();
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────

        private void SetTime(double t) { _now = t; _w.SetSingleton(new GlobalTime { TotalTime = t }); }

        private Entity Unit(int netId, ForceId side, string slot = "Rifleman", float health = 100f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new NetworkIdentity { Value = netId });
            _w.AddComponent(e, new Playable { Side = side, SlotName = new FixedString32(slot) });
            _w.AddComponent(e, new Health { Current = health, Max = 100f });
            _map.Register(netId, e);
            return e;
        }

        private void Hold(Entity unit, int station)
            => _w.AddComponent(unit, new PossessedBy { StationNodeId = (NodeId)station, Mode = PossessionMode.Fps, Epoch = 1 });

        private void Tick()
        {
            _wallMs += 1_100;   // each tick is past the 1 s cadences
            _w.Bus.SwapBuffers();
            _proc.Execute(_w, 0.016f);
            _slots.Execute(_w, 0.016f);
            Drain();
        }

        private void Drain()
        {
            using (var l = _status.Take()) foreach (var s in l) if (s.IsValid) _statuses.Add(s.Data);
            using (var l = _slotSamples.Take())
                foreach (var s in l)
                    _slotLog.Add(s.IsValid ? (true, s.Data) : (false, DdsTypeSupport.FromNative<PlayerSlotTopic>(s.NativePtr)));
        }

        private void Until(Func<bool> done, string what, int timeoutMs = 15_000)
        {
            var end = Environment.TickCount64 + timeoutMs;
            while (!done())
            {
                Assert.True(Environment.TickCount64 < end, "timed out: " + what);
                Thread.Sleep(5);
                if (done()) return;
                Drain();
            }
        }

        private void PumpUntil(Func<bool> done, string what, int timeoutMs = 15_000)
        {
            var end = Environment.TickCount64 + timeoutMs;
            while (!done())
            {
                Assert.True(Environment.TickCount64 < end, "timed out: " + what);
                Tick();
                Thread.Sleep(5);
            }
        }

        private DeployStatusTopic Request(int station, ForceId side, int unitNetId, bool observe = false)
        {
            string id = Guid.NewGuid().ToString("N");
            _request.Write(new DeployRequestTopic { RequestId = id, StationNodeId = station, Side = (byte)side, UnitNetId = unitNetId, Observe = observe });
            DeployStatusTopic answer = default;
            PumpUntil(() => { foreach (var s in _statuses) if (s.RequestId == id) { answer = s; return true; } return false; }, "a status for the request");
            return answer;
        }

        // ── the processor ───────────────────────────────────────────────────────────────────

        /// <summary>A request for a free playable unit of the station's side is ACCEPTED: the status says so, the slot is TakingControl on that unit reserved 30 s of SIM time ahead, and the
        /// reservation is in this world's <see cref="StationReservations"/>. ✅ Red-proof: the processor never writing the slot (or the status) ⇒ red.</summary>
        [Fact]
        public void APlayableUnitOfTheStationsSide_IsAccepted_AndReservedFor30SecondsOfSimTime()
        {
            Unit(Rifleman, ForceId.Friend, "Rifleman 1");
            var status = Request(StationA, ForceId.Friend, Rifleman);
            Assert.True(status.Accepted, status.Reason);
            Assert.Equal(Rifleman, status.UnitNetId);
            PumpUntil(() => _slotLog.Exists(s => s.valid && s.slot.StationNodeId == StationA), "the slot sample");
            var slot = _slotLog.Find(s => s.valid && s.slot.StationNodeId == StationA).slot;
            Assert.Equal((byte)PlayerSlotState.TakingControl, slot.State);
            Assert.Equal(Rifleman, slot.UnitNetId);
            Assert.Equal((byte)ForceId.Friend, slot.Side);
            Assert.Equal("Rifleman 1", slot.Name);
            Assert.Equal(100 + DeployProcessorSystem.ReservationSeconds, slot.ReservedUntilSim, 3);
            Assert.True(StationReservations.Of(_w).TryReservedFor(Rifleman, out int holder)); Assert.Equal(StationA, holder);
        }

        /// <summary>A request naming the OTHER side than the unit's is refused <c>WrongSide</c>; a missing unit, a non-playable one and a dead one are refused by name; no slot is made.
        /// ✅ Red-proof: the side comparison removed ⇒ the wrong-side request is accepted ⇒ red.</summary>
        [Fact]
        public void AWrongSide_ANonPlayable_AMissingAndADeadUnit_AreRefusedByName()
        {
            Unit(Rifleman, ForceId.Friend);
            var plain = _w.CreateEntity(); _w.AddComponent(plain, new NetworkIdentity { Value = 102 }); _map.Register(102, plain);
            Unit(103, ForceId.Friend, health: 0f);

            var wrong = Request(StationA, ForceId.Hostile, Rifleman);
            Assert.False(wrong.Accepted); Assert.Equal("WrongSide", wrong.Reason);
            Assert.Equal("NotPlayable", Request(StationA, ForceId.Friend, 102).Reason);
            Assert.Equal("NoSuchUnit", Request(StationA, ForceId.Friend, 999).Reason);
            Assert.Equal("Dead", Request(StationA, ForceId.Friend, 103).Reason);
            Assert.Equal(0, _proc.SlotCount);
            Assert.False(StationReservations.Of(_w).TryReservedFor(Rifleman, out _));
        }

        /// <summary>A unit another station HOLDS is refused <c>HeldByAnotherStation</c> with that station named; a unit RESERVED for another station (accepted a moment ago, not yet possessed) is refused
        /// <c>ReservedForAnother</c> with the reserving station named — the second Player of UC-26. ✅ Red-proof: <c>StationClaims.ForPossess</c> not consulted ⇒ both accepted ⇒ red.</summary>
        [Fact]
        public void AUnitHeldOrReservedByAnotherStation_IsRefused_NamingTheHolder()
        {
            var held = Unit(Rifleman, ForceId.Friend);
            Unit(Plane, ForceId.Friend, "A-10");
            Hold(held, 77);
            var a = Request(StationA, ForceId.Friend, Rifleman);
            Assert.False(a.Accepted); Assert.Equal("HeldByAnotherStation", a.Reason); Assert.Equal(77, a.HolderStation);

            Assert.True(Request(StationA, ForceId.Friend, Plane).Accepted);
            var b = Request(StationB, ForceId.Friend, Plane);
            Assert.False(b.Accepted); Assert.Equal("ReservedForAnother", b.Reason); Assert.Equal(StationA, b.HolderStation);
        }

        /// <summary>The slot goes TakingControl → Playing once the unit is <c>PossessedBy</c> that station — and the offer shows the holder. ✅ Red-proof: the Progress step removed ⇒ it stays TakingControl ⇒ red.</summary>
        [Fact]
        public void TheSlotBecomesPlaying_WhenTheUnitIsPossessedByTheStation()
        {
            var unit = Unit(Rifleman, ForceId.Friend);
            Assert.True(Request(StationA, ForceId.Friend, Rifleman).Accepted);
            Assert.True(_proc.TryGetSlot(StationA, out var state, out _)); Assert.Equal(PlayerSlotState.TakingControl, state);
            Hold(unit, StationA);
            PumpUntil(() => _slotLog.Exists(s => s.valid && s.slot.State == (byte)PlayerSlotState.Playing), "a Playing slot sample");
            Assert.True(_proc.TryGetSlot(StationA, out state, out int u)); Assert.Equal(PlayerSlotState.Playing, state); Assert.Equal(Rifleman, u);
        }

        /// <summary>A reservation not taken within 30 s of SIM time lapses: the slot is gone, a DISPOSE reaches the station, and the unit is free again for another station. ✅ Red-proof: the lapse comparison
        /// removed (or against wall time) ⇒ the slot stays ⇒ red.</summary>
        [Fact]
        public void AReservationNotTaken_LapsesAfter30SecondsOfSimTime_AndTheUnitIsFreeAgain()
        {
            Unit(Rifleman, ForceId.Friend);
            Assert.True(Request(StationA, ForceId.Friend, Rifleman).Accepted);
            SetTime(100 + 29); Tick();
            Assert.Equal(1, _proc.SlotCount);                      // not yet
            SetTime(100 + 31);
            PumpUntil(() => _proc.SlotCount == 0, "the lapse");
            PumpUntil(() => _slotLog.Exists(s => !s.valid && s.slot.StationNodeId == StationA), "a dispose sample");
            Assert.False(StationReservations.Of(_w).TryReservedFor(Rifleman, out _));
            Assert.True(Request(StationB, ForceId.Friend, Rifleman).Accepted);
        }

        /// <summary>A station whose node DEPARTED (<c>NodeDeparted</c> on the world bus) loses its slot — a dispose is sent and the reservation is released. ✅ Red-proof: the departure read removed ⇒ the slot stays ⇒ red.</summary>
        [Fact]
        public void ADepartedStation_HasItsSlotDisposed()
        {
            Unit(Rifleman, ForceId.Friend);
            Assert.True(Request(StationA, ForceId.Friend, Rifleman).Accepted);
            _w.Bus.Publish(new NodeDeparted { NodeId = (NodeId)StationA });
            _w.Bus.SwapBuffers();
            _proc.Execute(_w, 0.016f);
            Assert.Equal(0, _proc.SlotCount);
            Assert.False(StationReservations.Of(_w).TryReservedFor(Rifleman, out _));
            PumpUntil(() => _slotLog.Exists(s => !s.valid && s.slot.StationNodeId == StationA), "a dispose sample");
        }

        /// <summary>An observer request (<c>Observe</c>) makes a Choosing slot of the side with no unit. ✅ Red-proof: the observe branch removed ⇒ the request is refused as no such unit ⇒ red.</summary>
        [Fact]
        public void AnObserveRequest_MakesAChoosingSlotWithNoUnit()
        {
            var status = Request(StationA, ForceId.Hostile, 0, observe: true);
            Assert.True(status.Accepted);
            Assert.True(_proc.TryGetSlot(StationA, out var state, out int unit)); Assert.Equal(PlayerSlotState.Choosing, state); Assert.Equal(0, unit);
            PumpUntil(() => _slotLog.Exists(s => s.valid && s.slot.State == (byte)PlayerSlotState.Choosing && s.slot.Side == (byte)ForceId.Hostile), "a Choosing slot sample");
        }

        // ── the reader ──────────────────────────────────────────────────────────────────────

        /// <summary>The slot reader FILLS <see cref="StationReservations"/> from the slot topic (Choosing ⇒ a side only, a unit slot ⇒ side + unit) and CLEARS it when the slot is disposed. A station's own
        /// writer plays the server here. ✅ Red-proof: the Remove on a not-alive sample removed ⇒ the reservation survives the dispose ⇒ red.</summary>
        [Fact]
        public void TheSlotReader_FillsAndClearsTheReservations()
        {
            using var writer = new DdsWriter<PlayerSlotTopic>(_stationP);
            writer.Write(new PlayerSlotTopic { StationNodeId = StationA, Name = "x", Side = (byte)ForceId.Friend, State = (byte)PlayerSlotState.TakingControl, UnitNetId = Rifleman });
            writer.Write(new PlayerSlotTopic { StationNodeId = StationB, Name = "y", Side = (byte)ForceId.Hostile, State = (byte)PlayerSlotState.Choosing });
            var res = StationReservations.Of(_w);
            PumpUntil(() => res.Count == 2, "both slots");
            Assert.True(res.TryOf(StationA, out var a)); Assert.Equal(new StationReservations.Reservation(ForceId.Friend, Rifleman), a);
            Assert.True(res.TryOf(StationB, out var b)); Assert.Equal(new StationReservations.Reservation(ForceId.Hostile, 0), b);
            Assert.True(res.TryReservedFor(Rifleman, out int st)); Assert.Equal(StationA, st);

            writer.DisposeInstance(new PlayerSlotTopic { StationNodeId = StationA });
            PumpUntil(() => res.Count == 1, "the dispose");
            Assert.False(res.TryOf(StationA, out _));
            Assert.False(res.TryReservedFor(Rifleman, out _));
        }

        // ── the wire ────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ THE WIRE RAIL (§9, "no gameplay topic on the wire"): a node with the pack COMPOSED and its feature OFF creates NO reader or writer of any gameplay topic — asserted on
        /// <see cref="GameplayWire"/>'s created flags AND on a second participant that watches every gameplay topic and sees no peer. With the feature ON the same node creates them and the
        /// observer's endpoints match (so the probe CAN see them). ✅ Red-proof (compiles): <c>[NodeFeature]</c> removed from <c>DeployProcessorSystem</c> (or the wire made eager) ⇒ the OFF half sees a request
        /// reader ⇒ red.</summary>
        [Fact]
        public void APackComposedWithTheFeatureOff_PutsNoGameplayTopicOnTheWire_AndWithItOn_It_Does()
        {
            uint domain = (uint)Interlocked.Increment(ref _domain);
            using var node = new DdsParticipant(domain);
            using var watcher = new DdsParticipant(domain);   // a second participant on the node's domain, holding the OPPOSITE end of the node's endpoints (request writer, status reader, slot writer, offer reader — one end per topic, so they never match each other)
            using var reqW = new DdsWriter<DeployRequestTopic>(watcher);
            using var stR = new DdsReader<DeployStatusTopic>(watcher);
            using var slW = new DdsWriter<PlayerSlotTopic>(watcher);
            using var ofR = new DdsReader<DeployOfferTopic>(watcher);
            int PeersSeen() => (int)(reqW.CurrentStatus.CurrentCount + stR.CurrentStatus.CurrentCount + slW.CurrentStatus.CurrentCount + ofR.CurrentStatus.CurrentCount);

            var features = new NodeFeatures(new FeatureRegistry(new[] { GameplayFeatures.DeployServer }), NodeRole.Brain);
            Assert.True(features.On(GameplayFeatures.DeployServerId));          // ON by default (R-4115)
            var pack = new GameplayServerPack();
            Assert.Equal(new[] { GameplayFeatures.DeployServer }, pack.Features);
            var systems = new List<IEcsModuleSystem>();
            var world = new EntityRepository();
            world.RegisterComponent<Playable>(); world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<PossessedBy>();
            var map = new NetworkEntityMap();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new NetworkIdentity { Value = 7 }); world.AddComponent(unit, new Playable { Side = ForceId.Friend, SlotName = new FixedString32("p") }); map.Register(7, unit);
            pack.Compose(node, map, nodeId: 9, election: null, systems);
            Assert.Equal(2, systems.Count);

            var sch = new SystemScheduler { FeatureGate = features };
            foreach (var s in systems) sch.RegisterSystem(s);
            sch.BuildExecutionOrders();
            void Frames(int n) { for (int i = 0; i < n; i++) { world.Bus.SwapBuffers(); sch.ExecutePhase(SystemPhase.Input, world, 0.016f); Thread.Sleep(20); } }

            features.SetNodeOverride(GameplayFeatures.DeployServerId, "{\"Enabled\":false}");
            Frames(60);                                                         // ~1.2 s: far more than DDS discovery needs on loopback
            Assert.Equal(0, pack.Wire!.CreatedCount);
            Assert.False(pack.Wire.RequestsCreated || pack.Wire.SlotReaderCreated || pack.Wire.OffersCreated || pack.Wire.StatusCreated || pack.Wire.SlotWriterCreated);
            Assert.Equal(0, PeersSeen());

            features.SetNodeOverride(GameplayFeatures.DeployServerId, "{\"Enabled\":true}");
            var end = Environment.TickCount64 + 15_000;
            while ((reqW.CurrentStatus.CurrentCount == 0 || ofR.CurrentStatus.CurrentCount == 0 || slW.CurrentStatus.CurrentCount == 0) && Environment.TickCount64 < end) Frames(1);
            Assert.True(pack.Wire.RequestsCreated); Assert.True(pack.Wire.SlotReaderCreated); Assert.True(pack.Wire.OffersCreated);
            Assert.True(reqW.CurrentStatus.CurrentCount > 0, "the node's request reader must match the watcher's request writer");
            Assert.True(ofR.CurrentStatus.CurrentCount > 0, "the node's offer writer must match the watcher's offer reader");
            Assert.True(slW.CurrentStatus.CurrentCount > 0, "the node's slot reader must match the watcher's slot writer");
            pack.Wire.Dispose(); world.Dispose();
        }

        /// <summary>The election: the lowest node id that advertises the feature AND reports it ON serves; a candidate with a LOWER id stands this one down, one that reports it OFF does not. ✅ Red-proof: the
        /// <c>featureOn</c> test dropped ⇒ the OFF peer stands us down ⇒ red; the id comparison reversed ⇒ red.</summary>
        [Fact]
        public void OnlyTheLowestNodeWithTheFeatureOn_Serves()
        {
            var on = new HashSet<int> { 3 }; var supports = new HashSet<int> { 3, 4 };
            var e = new DeployServerElection(() => new[] { 3, 4, 9 }, (n, _) => supports.Contains(n), (n, _) => on.Contains(n));
            Assert.False(e.IsServer(9));    // 3 has it ON and is lower
            Assert.True(e.IsServer(3));     // nobody lower
            Assert.True(e.IsServer(2));
            on.Clear();                     // 3 now reports it OFF (advertises, does not serve)
            Assert.True(e.IsServer(9));
        }
    }
}
