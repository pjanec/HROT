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
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Components;
using System.Linq;
using System.Numerics;
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
        private readonly List<int> _statusDisposed = new();
        private readonly List<(bool valid, PlayerSlotTopic slot)> _slotLog = new();
        private readonly DdsReader<DeployOfferTopic> _offerSamples;
        private readonly Dictionary<int, DeployOfferTopic> _offersSeen = new();
        private readonly List<(long Tkb, SimTransform At, object[] Components, int NetId)> _spawnCalls = new();
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
            _w.RegisterComponent<DeployRole>(); _w.RegisterComponent<SpawnArea>(); _w.RegisterComponent<GameplaySettings>();
            _w.RegisterComponent<EntityInfo>(); _w.RegisterComponent<TkbIdentity>(); _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<SimTransform>(); _w.RegisterManagedComponent<EditablePolyline>();
            SetTime(_now);
            _wire = new GameplayWire(_serverP);
            _proc = new DeployProcessorSystem(_wire, _map, Server, election: null, nowMs: () => _wallMs,
                spawner: (tkb, at, comps) => { int id = 200 + _spawnCalls.Count; _spawnCalls.Add((tkb, at, comps.ToArray(), id)); return id; }, rng: new Random(7));
            _slots = new ServerPlayerSlotReaderSystem(_wire);
            _request = new DdsWriter<DeployRequestTopic>(_stationP);
            _status = new DdsReader<DeployStatusTopic>(_stationP);
            _slotSamples = new DdsReader<PlayerSlotTopic>(_stationP);
            _offerSamples = new DdsReader<DeployOfferTopic>(_stationP);
            Tick();   // creates the server's request reader (the wire is lazy) so the station's volatile writer can match it
            Until(() => _request.CurrentStatus.CurrentCount > 0, "the station's request writer must match the server's reader");
        }

        public void Dispose()
        {
            _request.Dispose(); _status.Dispose(); _slotSamples.Dispose(); _offerSamples.Dispose(); _wire.Dispose(); _stationP.Dispose(); _serverP.Dispose(); _w.Dispose();
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
            using (var l = _offerSamples.Take())
                foreach (var s in l)
                {
                    if (s.IsValid) _offersSeen[s.Data.UnitNetId] = s.Data;
                    else _offersSeen.Remove(DdsTypeSupport.FromNative<DeployOfferTopic>(s.NativePtr).UnitNetId);
                }
            using (var l = _status.Take())
                foreach (var s in l)
                    if (s.IsValid) _statuses.Add(s.Data);
                    else _statusDisposed.Add(DdsTypeSupport.FromNative<DeployStatusTopic>(s.NativePtr).StationNodeId);
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

        // ── S2 helpers ──────────────────────────────────────────────────────────────────────────

        private Entity Area(ForceId side, float facing, Vector3 origin, float half = 20f, Vector2[]? shape = null, int netId = 0, string? name = null)
        {
            var e = _w.CreateEntity();
            if (netId != 0) { _w.AddComponent(e, new NetworkIdentity { Value = netId }); _map.Register(netId, e); }
            if (name is not null) _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
            _w.AddComponent(e, new SimTransform { Position = origin, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SpawnArea { Side = side, FacingDeg = facing });
            _w.SetManagedComponent(e, new EditablePolyline { Points = shape ?? new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) } });
            return e;
        }

        private Entity RoleEntity(int netId, ForceId side, long tkb, string name, byte max, long kit1 = 0, string kit1Name = "", long kit2 = 0, long kit3 = 0)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new NetworkIdentity { Value = netId });
            _w.AddComponent(e, new DeployRole { Side = side, PersonTkbType = tkb, Name = new FixedString32(name), MaxCount = max,
                Kit1Tkb = kit1, Kit1Name = new FixedString32(kit1Name), Kit2Tkb = kit2, Kit3Tkb = kit3 });
            _map.Register(netId, e);
            return e;
        }

        /// <summary>What the creation path would do: the man exists under the network id the spawner pre-allocated (CE-4266).</summary>
        private Entity Materialise(int call)
        {
            var c = _spawnCalls[call];
            int netId = c.NetId;
            var e = _w.CreateEntity();
            _w.AddComponent(e, c.At);
            _w.AddComponent(e, (EntityInfo)c.Components.First(x => x is EntityInfo));
            _w.AddComponent(e, new NetworkIdentity { Value = netId });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _map.Register(netId, e);
            return e;
        }

        private string Ask(int station, ForceId side, int unitNetId, byte kit = 0, int areaNetId = 0)
        {
            string id = Guid.NewGuid().ToString("N");
            _request.Write(new DeployRequestTopic { RequestId = id, StationNodeId = station, Side = (byte)side, UnitNetId = unitNetId, Kit = kit, AreaNetId = areaNetId });
            return id;
        }

        private DeployStatusTopic Answer(string id)
        {
            DeployStatusTopic answer = default;
            PumpUntil(() => { foreach (var s in _statuses) if (s.RequestId == id) { answer = s; return true; } return false; }, "a status for the request");
            return answer;
        }

        private PlayerSlotTopic LastSlot(int station, Func<PlayerSlotTopic, bool>? where = null)
        {
            PlayerSlotTopic found = default;
            PumpUntil(() =>
            {
                for (int i = _slotLog.Count - 1; i >= 0; i--)
                    if (_slotLog[i].valid && _slotLog[i].slot.StationNodeId == station && (where is null || where(_slotLog[i].slot))) { found = _slotLog[i].slot; return true; }
                return false;
            }, "the slot sample");
            return found;
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

        /// <summary>
        /// ⭐ CE-4270 ③ (the scripted full cycle's step 10) — a station that LEAVES has its answer (<c>DeployStatusTopic</c>, durable) deleted
        /// too, with a slot or without one (a refused station). 🔴 Red-proof: drop <c>DisposeAnswer</c> from the departure read ⇒ no dispose.
        /// </summary>
        [Fact]
        public void ADepartedStation_HasItsAnswerDeleted_WithOrWithoutASlot()
        {
            Unit(Rifleman, ForceId.Friend);
            Assert.True(Request(StationA, ForceId.Friend, Rifleman).Accepted);
            Assert.False(Request(StationB, ForceId.Friend, Rifleman).Accepted);   // refused: an answer, no slot
            _w.Bus.Publish(new NodeDeparted { NodeId = (NodeId)StationA });
            _w.Bus.Publish(new NodeDeparted { NodeId = (NodeId)StationB });
            _w.Bus.SwapBuffers();
            _proc.Execute(_w, 0.016f);
            PumpUntil(() => _statusDisposed.Contains(StationA) && _statusDisposed.Contains(StationB), "both stations' answers deleted");
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

        // ── ⭐ S2 — roles, spawn areas, death and respawn ─────────────────────────────────────────

        /// <summary>A request that names a ROLE makes ONE man through the creation path: at a point INSIDE a spawn area of the side, ≥ 3 m from anyone, on the ground (the area's own Z here — no terrain), facing the area's
        /// angle, as the person type of the role, named "&lt;role&gt; &lt;n&gt;" on the side — and the answer waits until he EXISTS (Accepted with HIS network id, the slot TakingControl on him). ✅ Red-proof: the answer sent
        /// at once (not waiting for the man) ⇒ no unit id ⇒ red; the clearance dropped ⇒ the blocker case red.</summary>
        [Fact]
        public void ARoleRequest_SpawnsAManInTheArea_ThenAnswersWithHisNetId()
        {
            Area(ForceId.Friend, 90f, new Vector3(100f, 200f, 5f));
            var blocker = _w.CreateEntity(); _w.AddComponent(blocker, new SimTransform { Position = new Vector3(100f, 200f, 0f), Rotation = Quaternion.Identity });
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 4);

            string id = Ask(StationA, ForceId.Friend, 300);
            PumpUntil(() => _spawnCalls.Count == 1, "the spawn request");
            var call = _spawnCalls[0];
            Assert.Equal(2002L, call.Tkb);
            Assert.InRange(call.At.Position.X, 80f, 120f); Assert.InRange(call.At.Position.Y, 180f, 220f);
            Assert.True(Vector2.Distance(new Vector2(call.At.Position.X, call.At.Position.Y), new Vector2(100f, 200f)) >= 3f, "3 m clear of the blocker");
            Assert.Equal(5f, call.At.Position.Z);
            Assert.Equal(90f, 2f * MathF.Atan2(call.At.Rotation.Z, call.At.Rotation.W) * 180f / MathF.PI, 1);   // facing the area's angle, about Z
            var info = (EntityInfo)call.Components.First(x => x is EntityInfo);
            Assert.Equal("Rifleman 1", info.Name.ToString()); Assert.Equal(ForceId.Friend, info.ForceId);
            Assert.DoesNotContain(_statuses, st => st.RequestId == id);   // not answered before the man exists
            Assert.True(_proc.TryGetSlot(StationA, out _, out _) == false);

            Materialise(0);
            var status = Answer(id);
            Assert.True(status.Accepted, status.Reason); Assert.Equal(200, status.UnitNetId);
            Assert.True(_proc.TryGetSlot(StationA, out var state, out int unit)); Assert.Equal(PlayerSlotState.TakingControl, state); Assert.Equal(200, unit);
        }

        /// <summary>A role whose Max is reached is refused <c>RoleFull</c> (alive men this server made for it); a role of the OTHER side is refused <c>WrongSide</c>; a side with no spawn area is refused
        /// "no spawn area for &lt;side&gt;" — nothing is spawned in any of them. ✅ Red-proof: the Max comparison dropped ⇒ the second man is made ⇒ red.</summary>
        [Fact]
        public void ARoleThatIsFull_AWrongSide_OrASideWithNoArea_IsRefused_AndNothingIsSpawned()
        {
            Area(ForceId.Friend, 0f, new Vector3(100f, 200f, 5f));
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 1);
            RoleEntity(301, ForceId.Hostile, 2003, "Insurgent", 0);
            RoleEntity(302, ForceId.Friend, 2004, "Grenadier", 0);

            string first = Ask(StationA, ForceId.Friend, 300);
            PumpUntil(() => _spawnCalls.Count == 1, "the first spawn");
            Materialise(0);
            Assert.True(Answer(first).Accepted);

            var full = Answer(Ask(StationB, ForceId.Friend, 300));
            Assert.False(full.Accepted); Assert.Equal("RoleFull", full.Reason);
            var wrong = Answer(Ask(StationB, ForceId.Friend, 301));
            Assert.False(wrong.Accepted); Assert.Equal("WrongSide", wrong.Reason);
            Assert.Equal(1, _spawnCalls.Count);

            var noArea = Answer(Ask(StationB, ForceId.Hostile, 301 ));   // a Hostile role, Friend-only areas
            Assert.False(noArea.Accepted); Assert.Equal("no spawn area for Hostile", noArea.Reason);
            Assert.Equal(1, _spawnCalls.Count);
        }

        /// <summary>A spawn whose man never appears is refused "spawn timed out" after 10 s. ✅ Red-proof: the deadline check dropped ⇒ it waits forever ⇒ red.</summary>
        [Fact]
        public void ASpawnThatNeverArrives_IsRefusedAfter10Seconds()
        {
            Area(ForceId.Friend, 0f, new Vector3(0f, 0f, 0f));
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 0);
            var status = Answer(Ask(StationA, ForceId.Friend, 300));
            Assert.False(status.Accepted); Assert.Equal(DeployProcessorSystem.SpawnTimedOut, status.Reason);
            Assert.Equal(1, _spawnCalls.Count);
        }

        /// <summary>With NO Deploy Role declared on a side, the server offers ONE implicit role per person TKB type present on it (id <c>-(type)</c>, unlimited, Kind Role) — and a request for it spawns that type; a side that
        /// DECLARES roles offers those only (Kind Role, its entity id, Count alive / Max). ✅ Red-proof: the declared-side test dropped ⇒ the implicit offer shows beside a declared role ⇒ red.</summary>
        [Fact]
        public void ImplicitRoles_AreOfferedWhenNoneDeclared_AndDeclaredRolesCarryTheirCountAndMax()
        {
            Area(ForceId.Friend, 0f, new Vector3(100f, 200f, 0f));
            var man = _w.CreateEntity();
            _w.AddComponent(man, new TkbIdentity { TkbType = (TkbTypeId)2002L });
            _w.AddComponent(man, new EntityInfo { Name = new FixedString64("Joe"), ForceId = ForceId.Friend });
            _w.AddComponent(man, new Locomotion { Class = LocomotionClass.Pedestrian });
            _w.AddComponent(man, new SimTransform { Position = new Vector3(500f, 500f, 0f), Rotation = Quaternion.Identity });

            PumpUntil(() => _offersSeen.ContainsKey(-2002), "the implicit offer");
            var o = _offersSeen[-2002];
            Assert.Equal((byte)DeployOfferKind.Role, o.Kind); Assert.Equal((byte)ForceId.Friend, o.Side); Assert.Equal(0, o.Max);
            string id = Ask(StationA, ForceId.Friend, -2002);
            PumpUntil(() => _spawnCalls.Count == 1, "the implicit role's spawn");
            Assert.Equal(2002L, _spawnCalls[0].Tkb);
            Materialise(0); Assert.True(Answer(id).Accepted);

            RoleEntity(300, ForceId.Friend, 2004, "Grenadier", 3);   // now the side DECLARES a role: the implicit offer goes
            PumpUntil(() => !_offersSeen.ContainsKey(-2002) && _offersSeen.ContainsKey(300), "the declared offer replaces the implicit one");
            Assert.Equal(3, _offersSeen[300].Max); Assert.Equal(0, _offersSeen[300].Count);
        }

        private Entity Playing(int station, ForceId side, int netId)
        {
            var unit = Unit(netId, side);
            Assert.True(Request(station, side, netId).Accepted);
            Hold(unit, station);
            PumpUntil(() => _proc.TryGetSlot(station, out var st, out _) && st == PlayerSlotState.Playing, "Playing");
            return unit;
        }

        /// <summary>Death (§10.2): a Playing slot whose unit dies becomes Dead with <c>RespawnAtSim = now + 10 s</c> (no GameplaySettings ⇒ 10), the reservation drops to the side only; at that sim time it is Choosing again and the
        /// unit's seat is free for another station. ✅ Red-proof: the death check dropped ⇒ it stays Playing ⇒ red; the respawn comparison dropped ⇒ it stays Dead ⇒ red.</summary>
        [Fact]
        public void ADeadUnit_MakesTheSlotDead_ThenChoosingAtTheRespawnTime()
        {
            var unit = Playing(StationA, ForceId.Friend, Rifleman);
            _w.GetComponentRW<Health>(unit).Current = 0f;
            Tick();
            Assert.True(_proc.TryGetSlot(StationA, out var st, out _)); Assert.Equal(PlayerSlotState.Dead, st);
            var dead = LastSlot(StationA, x => x.State == (byte)PlayerSlotState.Dead);
            Assert.Equal(110.0, dead.RespawnAtSim, 3);
            Assert.False(StationReservations.Of(_w).TryReservedFor(Rifleman, out _));   // the dead man is no claim

            SetTime(109.5); Tick();
            Assert.True(_proc.TryGetSlot(StationA, out st, out _)); Assert.Equal(PlayerSlotState.Dead, st);
            SetTime(110.0); Tick();
            Assert.True(_proc.TryGetSlot(StationA, out st, out int u)); Assert.Equal(PlayerSlotState.Choosing, st); Assert.Equal(0, u);
            Assert.True(StationReservations.Of(_w).TryOf(StationA, out var r)); Assert.Equal(ForceId.Friend, r.Side);   // its side stays
        }

        /// <summary>The delay is <c>GameplaySettings.RespawnDelaySeconds</c> of the World Environment: 4 ⇒ <c>RespawnAtSim = now + 4</c>; 0 ⇒ the slot stays Dead for good (RespawnAtSim 0 = never). ✅ Red-proof: the settings
        /// ignored ⇒ 10 s ⇒ the first assertion red.</summary>
        [Fact]
        public void TheRespawnDelayComesFromTheWorldEnvironment_AndZeroMeansDeadForGood()
        {
            var env = _w.CreateEntity(); _w.AddComponent(env, new GameplaySettings { RespawnDelaySeconds = 4f });
            var unit = Playing(StationA, ForceId.Friend, Rifleman);
            _w.GetComponentRW<Health>(unit).Current = 0f; Tick();
            Assert.Equal(104.0, LastSlot(StationA, x => x.State == (byte)PlayerSlotState.Dead).RespawnAtSim, 3);
            SetTime(104.0); Tick();
            Assert.True(_proc.TryGetSlot(StationA, out var st, out _)); Assert.Equal(PlayerSlotState.Choosing, st);

            _w.GetComponentRW<GameplaySettings>(env).RespawnDelaySeconds = 0f;
            var second = Playing(StationA, ForceId.Friend, 102);
            _w.GetComponentRW<Health>(second).Current = 0f; Tick();
            Assert.Equal(0.0, LastSlot(StationA, x => x.State == (byte)PlayerSlotState.Dead && x.UnitNetId == 102).RespawnAtSim);
            SetTime(1000.0); Tick(); Tick();
            Assert.True(_proc.TryGetSlot(StationA, out st, out _)); Assert.Equal(PlayerSlotState.Dead, st);   // for good
        }

        /// <summary>The station RELEASING its unit (no longer <c>PossessedBy</c> it) sends the Playing slot back to Choosing and frees the reservation — §9.4's open gap. ✅ Red-proof: the release check dropped ⇒ it stays Playing ⇒ red.</summary>
        [Fact]
        public void AReleasedUnit_SendsThePlayingSlotBackToChoosing_AndFreesTheUnit()
        {
            var unit = Playing(StationA, ForceId.Friend, Rifleman);
            _w.RemoveComponent<PossessedBy>(unit); Tick();
            Assert.True(_proc.TryGetSlot(StationA, out var st, out int u)); Assert.Equal(PlayerSlotState.Choosing, st); Assert.Equal(0, u);
            Assert.False(StationReservations.Of(_w).TryReservedFor(Rifleman, out _));
            Assert.True(Request(StationB, ForceId.Friend, Rifleman).Accepted);   // free for another station
        }

        /// <summary>HOT PATH (R-2084): a steady server frame with slots in Playing allocates nothing — measured on this thread across 200 frames (the 1 s cadences do not fire: the wall clock stands still). ✅ Red-proof: a
        /// per-frame <c>new</c>/LINQ in <c>Progress</c> ⇒ red.</summary>
        [Fact]
        public void ASteadyFrame_WithPlayingSlots_AllocatesNothing()
        {
            Playing(StationA, ForceId.Friend, Rifleman);
            for (int i = 0; i < 50; i++) { _proc.Execute(_w, 0.016f); }   // warm-up (JIT, DDS take buffers)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) _proc.Execute(_w, 0.016f);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(used == 0, $"a steady frame allocated {used} bytes over 200 frames");
        }

        // ── ⭐ S3 — kits and the chosen spawn area ────────────────────────────────────────────────

        /// <summary>A KIT spawns ITS person type (kit 1 ⇒ Kit1Tkb) yet the man is named after the ROLE ("Rifleman 1", the kit label is not in it); an unset kit (2) and an out-of-range one (5) are refused <c>NoSuchKit</c>;
        /// kits SHARE the role's Max — kit 1 then kit 0 on a max-1 role ⇒ the second is <c>RoleFull</c>. ✅ Red-proof: the kit ignored (always the role's own type) ⇒ the Tkb assertion red; the unset-kit check dropped ⇒ red;
        /// the Max counted per kit ⇒ the RoleFull assertion red.</summary>
        [Fact]
        public void AKitSpawnsItsOwnPersonType_AnUnsetKitIsRefused_AndKitsShareTheRolesMax()
        {
            Area(ForceId.Friend, 0f, new Vector3(100f, 200f, 5f));
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 1, kit1: 2004, kit1Name: "Grenadier");

            Assert.Equal("NoSuchKit", Answer(Ask(StationB, ForceId.Friend, 300, kit: 2)).Reason);
            Assert.Equal("NoSuchKit", Answer(Ask(StationB, ForceId.Friend, 300, kit: 5)).Reason);
            Assert.Empty(_spawnCalls);

            string id = Ask(StationA, ForceId.Friend, 300, kit: 1);
            PumpUntil(() => _spawnCalls.Count == 1, "the kit's spawn");
            Assert.Equal(2004L, _spawnCalls[0].Tkb);
            Assert.Equal("Rifleman 1", ((EntityInfo)_spawnCalls[0].Components.First(x => x is EntityInfo)).Name.ToString());
            Materialise(0); Assert.True(Answer(id).Accepted);

            var full = Answer(Ask(StationB, ForceId.Friend, 300, kit: 0));   // the role's own kit: the SAME role, already at its max of 1
            Assert.False(full.Accepted); Assert.Equal("RoleFull", full.Reason);
        }

        /// <summary>A request that NAMES an area spawns the man inside THAT area only (two areas of the side, far apart — asserted for each in turn); another side's area ⇒ <c>WrongSide</c>; an entity that is not a spawn area, or an
        /// unknown id ⇒ <c>NoSuchArea</c>; 0 ⇒ some area of the side. ✅ Red-proof: the named area ignored ⇒ the "which" assertions red; the side check dropped ⇒ the WrongSide case red.</summary>
        [Fact]
        public void ANamedArea_IsTheOnlyOneUsed_WithItsRefusals()
        {
            Area(ForceId.Friend, 0f, new Vector3(100f, 200f, 0f), netId: 400, name: "North gate");
            Area(ForceId.Friend, 0f, new Vector3(1000f, 2000f, 0f), netId: 401, name: "Church");
            Area(ForceId.Hostile, 0f, new Vector3(5000f, 5000f, 0f), netId: 402, name: "Enemy camp");
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 0);

            int call = 0;
            foreach (var (areaId, cx, cy) in new[] { (400, 100f, 200f), (401, 1000f, 2000f), (400, 100f, 200f) })
            {
                Ask(StationA, ForceId.Friend, 300, areaNetId: areaId);
                PumpUntil(() => _spawnCalls.Count == call + 1, "a spawn in the named area");
                var p = _spawnCalls[call].At.Position;
                Assert.InRange(p.X, cx - 20f, cx + 20f); Assert.InRange(p.Y, cy - 20f, cy + 20f);
                call++;
            }
            Assert.Equal("WrongSide", Answer(Ask(StationB, ForceId.Friend, 300, areaNetId: 402)).Reason);
            Assert.Equal("NoSuchArea", Answer(Ask(StationB, ForceId.Friend, 300, areaNetId: 300)).Reason);   // the ROLE entity is no area
            Assert.Equal("NoSuchArea", Answer(Ask(StationB, ForceId.Friend, 300, areaNetId: 9999)).Reason);
            Assert.Equal(3, _spawnCalls.Count);

            Ask(StationB, ForceId.Friend, 300, areaNetId: 0);                                                 // any area of the side
            PumpUntil(() => _spawnCalls.Count == 4, "a spawn in any area");
            var any = _spawnCalls[3].At.Position;
            Assert.True((Math.Abs(any.X - 100f) <= 20f && Math.Abs(any.Y - 200f) <= 20f) || (Math.Abs(any.X - 1000f) <= 20f && Math.Abs(any.Y - 2000f) <= 20f));
        }

        /// <summary>Every <c>SpawnArea</c> is offered as Kind Area (all sides; its id, side, name) and a role WITH kits carries its labels by kit number — the role's name for kit 0, the kit's name or "Kit n", "" for an unset
        /// kit. ✅ Red-proof: the area offers dropped ⇒ red; the labels dropped ⇒ red.</summary>
        [Fact]
        public void EverySpawnAreaIsOffered_AndARoleCarriesItsKitLabels()
        {
            Area(ForceId.Friend, 0f, new Vector3(100f, 200f, 0f), netId: 400, name: "North gate");
            Area(ForceId.Hostile, 0f, new Vector3(5000f, 5000f, 0f), netId: 402, name: "Enemy camp");
            RoleEntity(300, ForceId.Friend, 2002, "Rifleman", 4, kit1: 2004, kit1Name: "Grenadier", kit3: 2005);

            PumpUntil(() => _offersSeen.ContainsKey(400) && _offersSeen.ContainsKey(402) && _offersSeen.ContainsKey(300), "the area and role offers");
            Assert.Equal((byte)DeployOfferKind.Area, _offersSeen[400].Kind); Assert.Equal("North gate", _offersSeen[400].SlotName); Assert.Equal((byte)ForceId.Friend, _offersSeen[400].Side);
            Assert.Equal((byte)DeployOfferKind.Area, _offersSeen[402].Kind); Assert.Equal((byte)ForceId.Hostile, _offersSeen[402].Side);
            Assert.Equal(new[] { "Rifleman", "Grenadier", "", "Kit 3" }, _offersSeen[300].Kits);
        }

        /// <summary>The kit fields are AUTHORED world state: a <c>DeployRole</c> with kits saves into the scenario and loads back with all three pairs. ✅ Red-proof: a kit field not serialised ⇒ red.</summary>
        [Fact]
        public void ADeployRoleWithKits_RoundTripsThroughTheScenario()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<DeployRole>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new DeployRole { Side = ForceId.Hostile, PersonTkbType = 2003, Name = new FixedString32("Insurgent"), MaxCount = 3,
                Kit1Tkb = 2004, Kit1Name = new FixedString32("Grenadier"), Kit2Tkb = 2005, Kit2Name = new FixedString32("Mortar"), Kit3Tkb = 2006, Kit3Name = new FixedString32("Javelin") });
            var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.SimHost").Build();
            var dom = serializer.Serialize(repo, new Fdp.Toolkit.Scenario.ScenarioHeader("Hrot.SimHost"));
            using var fresh = new EntityRepository();
            fresh.RegisterComponent<DeployRole>();
            serializer.Deserialize(fresh, dom);
            DeployRole back = default; bool found = false;
            for (int i = 0; i <= fresh.MaxEntityIndex && !found; i++)
            {
                var c = new Entity(i, fresh.GetEntityIndex().GetMetadata(i).Generation);
                if (fresh.IsAlive(c) && fresh.HasComponent<DeployRole>(c)) { back = fresh.GetComponentRO<DeployRole>(c); found = true; }
            }
            Assert.True(found);
            Assert.Equal((ForceId.Hostile, 2003L, "Insurgent", (byte)3), (back.Side, back.PersonTkbType, back.Name.ToString(), back.MaxCount));
            Assert.Equal((2004L, "Grenadier", 2005L, "Mortar", 2006L, "Javelin"), (back.Kit1Tkb, back.Kit1Name.ToString(), back.Kit2Tkb, back.Kit2Name.ToString(), back.Kit3Tkb, back.Kit3Name.ToString()));
        }
    }
}
