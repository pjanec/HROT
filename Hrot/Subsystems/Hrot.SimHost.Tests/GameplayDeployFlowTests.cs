using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Gameplay;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ Player Join §9 (S1 part B) — the Player's <see cref="DeployFlowSystem"/> state machine over a REAL DDS pair: a server (<see cref="DeployProcessorSystem"/>, part A) on one participant and the
    /// flow on another, on a domain of its own per test. The node facts (gate, run state, "a server exists", Possess) and BOTH clocks are injected, so no test sleeps 10 s.
    /// </summary>
    public sealed class GameplayDeployFlowTests : IDisposable
    {
        private static int _domain = 160;
        private const int ServerNode = 5, Station = 30, Rifleman = 100;

        private readonly DdsParticipant _serverP, _clientP;
        private readonly GameplayWire _serverWire, _clientWire;
        private readonly EntityRepository _server = new(), _client = new();
        private readonly NetworkEntityMap _serverMap = new(), _clientMap = new();
        private readonly DeployProcessorSystem _proc;
        private readonly PlayerSlotReaderSystem _clientSlots;
        private readonly DeployFlowSystem _flow;
        private readonly List<int> _possessCalls = new();
        private long _serverMs = 1_000, _clientMs = 1_000;
        private bool _gateOpen = true, _live = true, _serverExists = true, _possessOk = true;
        private Entity _serverUnit, _clientUnit;

        public GameplayDeployFlowTests()
        {
            uint domain = (uint)Interlocked.Increment(ref _domain);
            _serverP = new DdsParticipant(domain); _clientP = new DdsParticipant(domain);
            foreach (var w in new[] { _server, _client })
            {
                w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<PossessedBy>(); w.RegisterComponent<Health>(); w.RegisterEvent<NodeDeparted>(); w.RegisterComponent<GameplaySettings>();
                w.SetSingleton(new GlobalTime { TotalTime = 100 });
            }
            _server.RegisterComponent<Playable>();
            _serverWire = new GameplayWire(_serverP); _clientWire = new GameplayWire(_clientP);
            _proc = new DeployProcessorSystem(_serverWire, _serverMap, ServerNode, election: null, nowMs: () => _serverMs);
            var env = new DeployFlowEnvironment(() => _gateOpen, () => _live, () => _serverExists, unit => { _possessCalls.Add(unit); return (_possessOk, _possessOk ? "" : "refused by the brain"); }, () => _clientMs);
            _flow = new DeployFlowSystem(_clientWire, _client, _clientMap, Station, env);
            _clientSlots = new PlayerSlotReaderSystem(_clientWire, () => _flow.ServerExists);
            _flow.Slots = _clientSlots.TryGetSlot;   // the pack's wiring: the flow reads its own slot through the one reader
            _serverUnit = Unit(_server, _serverMap, Rifleman, ForceId.Friend, "Rifleman 1", playable: true);
            _clientUnit = Unit(_client, _clientMap, Rifleman, ForceId.Friend, "", playable: false);
        }

        public void Dispose() { _clientWire.Dispose(); _serverWire.Dispose(); _clientP.Dispose(); _serverP.Dispose(); _client.Dispose(); _server.Dispose(); }

        private static Entity Unit(EntityRepository w, NetworkEntityMap map, int netId, ForceId side, string slot, bool playable)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new NetworkIdentity { Value = netId });
            w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            if (playable) w.AddComponent(e, new Playable { Side = side, SlotName = new FixedString32(slot) });
            map.Register(netId, e);
            return e;
        }

        /// <summary>One frame of both nodes: the server first, then the Player. The client clock moves a little each frame (10 ms), the server's a lot (its 1 s cadences fire).</summary>
        private void Frame(int clientStepMs = 10)
        {
            _serverMs += 1_100; _clientMs += clientStepMs;
            _server.Bus.SwapBuffers(); _client.Bus.SwapBuffers();
            _proc.Execute(_server, 0.016f);
            _clientSlots.Execute(_client, 0.016f);
            _flow.Execute(_client, 0.016f);
            Thread.Sleep(3);
        }

        private void PumpUntil(Func<bool> done, string what, int timeoutMs = 15_000)
        {
            var end = Environment.TickCount64 + timeoutMs;
            while (!done()) { Assert.True(Environment.TickCount64 < end, "timed out: " + what + $" (state {_flow.State}, refusal '{_flow.LastRefusal}')"); Frame(); }
        }

        private void ToChoosing()
        {
            PumpUntil(() => _flow.State == DeployFlowState.Choosing && _flow.Offers.Count > 0, "Choosing with an offer");
        }

        /// <summary>Waiting: the gate closed ⇒ Waiting with no text (the loading screen speaks); open but the cluster not Live ⇒ EXACTLY "Waiting for the server to load a scenario" (CE-4156); Live ⇒ out of Waiting.
        /// Nothing is created on the wire while waiting. ✅ Red-proof: the OperatingLive clause dropped ⇒ the open-not-live frame leaves Waiting ⇒ red.</summary>
        [Fact]
        public void Waiting_WhileTheGateIsClosed_OrOpenButNotLive_WithTheCe4156Text_AndNothingOnTheWire()
        {
            _gateOpen = false; _live = false;
            Frame();
            Assert.Equal(DeployFlowState.Waiting, _flow.State); Assert.Equal("", _flow.Reason);
            _gateOpen = true;
            Frame();
            Assert.Equal(DeployFlowState.Waiting, _flow.State);
            Assert.Equal("Waiting for the server to load a scenario", _flow.Reason);
            Assert.Equal(0, _clientWire.CreatedCount);
            _live = true;
            Frame();
            Assert.NotEqual(DeployFlowState.Waiting, _flow.State);
        }

        /// <summary>No server (R-4103): the Player OBSERVES, and creates no gameplay reader or writer at all (the request writer included). ✅ Red-proof: ServerExists ignored ⇒ it goes Choosing and creates the request writer ⇒ red.</summary>
        [Fact]
        public void NoServer_Observes_AndCreatesNoGameplayEndpoint()
        {
            _serverExists = false;
            for (int i = 0; i < 5; i++) Frame();
            Assert.Equal(DeployFlowState.Observing, _flow.State);
            Assert.Equal(0, _clientWire.CreatedCount);
            Assert.False(_clientWire.RequestWriterCreated);
        }

        /// <summary>With a server the Player CHOOSES and sees the server's offers: the placed unit, its side, name and holder (free). ✅ Red-proof: the offer reader never read ⇒ no offers ⇒ red.</summary>
        [Fact]
        public void WithAServer_Chooses_AndReadsTheOffers()
        {
            ToChoosing();
            var o = Assert.Single(_flow.Offers);
            Assert.Equal(new DeployOffer(Rifleman, ForceId.Friend, "Rifleman 1", 0), o);
            Assert.True(_clientWire.RequestWriterCreated);
        }

        /// <summary>The whole happy path: Request → Requesting → the server accepts → the Player's EXISTING Possess is called with the unit (once) → TakingControl → Playing when the unit is <c>PossessedBy</c> this station
        /// in the Player's OWN world. ✅ Red-proof: the Possess call dropped ⇒ it never reaches TakingControl's call rail; the PossessedBy check dropped ⇒ Playing without the hold ⇒ red.</summary>
        /// <summary>
        /// ⭐ CE-3446 (DESIGN_Threaded_Simulation_Presentation §15) — on a threaded node the UI reads the flow through its VIEW on the presentation world:
        /// the readout carries the flow's state (a new one only when it changed), and a request is a JOB on the flow — nothing reaches the flow until
        /// the sim drains it. 🔴 Red-proof: <c>DeployFlowView.Request</c> calling the flow directly ⇒ Requesting before the drain.
        /// </summary>
        [Fact]
        public void CE3446_TheStagedView_ReadsTheReadout_AndARequestIsAJobOnTheFlow()
        {
            ToChoosing();
            using var presentation = new EntityRepository();
            var jobs = new Fdp.Core.Threading.SimJobQueue();
            presentation.SetSingletonManaged(new Fdp.Core.Threading.SimLink(jobs, _client));
            var view = new DeployFlowView(presentation);

            var readout = DeployFlowReadout.ReadIfChanged(null, _flow)!;
            view.Apply(readout);
            Assert.Equal(DeployFlowState.Choosing, view.State);
            Assert.Same(_flow.Offers, view.Offers);
            Assert.Null(DeployFlowReadout.ReadIfChanged(readout, _flow));    // nothing changed: no new readout

            Assert.True(view.Request(ForceId.Friend, Rifleman));
            Assert.Equal(DeployFlowState.Choosing, _flow.State);              // only posted
            Assert.Equal(1, jobs.Drain(_client));
            Assert.Equal(DeployFlowState.Requesting, _flow.State);
            var next = DeployFlowReadout.ReadIfChanged(readout, _flow);
            Assert.NotNull(next);
            view.Apply(next!);
            Assert.Equal(DeployFlowState.Requesting, view.State);
            Assert.False(view.Request(ForceId.Friend, Rifleman));             // the flow's own precondition, from the state the UI shows
        }

        [Fact]
        public void ARequest_IsAccepted_ThenPossessIsCalled_ThenPlayingWhenHeld()
        {
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            Assert.Equal(DeployFlowState.Requesting, _flow.State);
            PumpUntil(() => _flow.State == DeployFlowState.TakingControl, "TakingControl");
            Assert.Equal(new[] { Rifleman }, _possessCalls);
            Assert.True(_proc.TryGetSlot(Station, out var slot, out _)); Assert.Equal(PlayerSlotState.TakingControl, slot);
            for (int i = 0; i < 5; i++) Frame();
            Assert.Equal(DeployFlowState.TakingControl, _flow.State);                  // not held yet ⇒ not Playing
            _client.AddComponent(_clientUnit, new PossessedBy { StationNodeId = (NodeId)Station, Mode = PossessionMode.Fps, Epoch = 1 });
            PumpUntil(() => _flow.State == DeployFlowState.Playing, "Playing");
            Assert.Equal(Rifleman, _flow.UnitNetId);
        }

        /// <summary>A refusal comes back to Choosing with the server's reason and the holder named; the Possess is never called. ✅ Red-proof: the refusal branch sending on to Possess ⇒ red.</summary>
        [Fact]
        public void ARefusal_ReturnsToChoosing_NamingTheReasonAndTheHolder()
        {
            _server.AddComponent(_serverUnit, new PossessedBy { StationNodeId = (NodeId)77, Mode = PossessionMode.Fps, Epoch = 1 });
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            PumpUntil(() => _flow.State == DeployFlowState.Choosing && _flow.LastRefusal.Length > 0, "the refusal");
            Assert.Equal("HeldByAnotherStation (held by station 77)", _flow.LastRefusal);
            Assert.Empty(_possessCalls);
            Assert.Equal(0, _flow.UnitNetId);   // ⭐ measured live 2026-11-05: the refused unit stayed reported as the slot's. 🔴 BackToChoosing without the reset ⇒ red
        }

        /// <summary>
        /// The Player's own Possess refused (the unit is not on this node YET — a man just spawned waits for his first pose, CE-4086) is RETRIED every 500 ms;
        /// once it succeeds the flow goes on; if it never does, the flow returns to Choosing with that text at the 10 s deadline.
        /// 🔴 Mutations: no retry ⇒ the late-arriving unit is never asked for ⇒ the first half red; the deadline dropped ⇒ the second half red.
        /// </summary>
        [Fact]
        public void APossessRefusedLocally_IsRetriedUntilTheUnitIsHere_ElseChoosingWithItsTextAtTheDeadline()
        {
            _possessOk = false;
            ToChoosing();
            _flow.Request(ForceId.Friend, Rifleman);
            PumpUntil(() => _flow.State == DeployFlowState.TakingControl && _possessCalls.Count >= 1, "TakingControl, first Possess refused");
            _possessOk = true;                                                   // the unit has arrived on this node
            PumpUntil(() => _possessCalls.Count >= 2, "the retry", timeoutMs: 15_000);
            Assert.Equal(DeployFlowState.TakingControl, _flow.State);

            BackToTheChoosingAndRequestAgain();
            _possessOk = false;
            _flow.Request(ForceId.Friend, Rifleman);
            PumpUntil(() => _flow.State == DeployFlowState.TakingControl, "TakingControl again");
            for (int i = 0; i < 40 && _flow.State == DeployFlowState.TakingControl; i++) Frame(clientStepMs: 400);   // past the 10 s deadline
            Assert.Equal(DeployFlowState.Choosing, _flow.State);
            Assert.Equal("refused by the brain", _flow.LastRefusal);
        }

        private void BackToTheChoosingAndRequestAgain()
        {
            for (int i = 0; i < 40 && _flow.State != DeployFlowState.Choosing; i++) Frame(clientStepMs: 400);   // the first take times out (nobody possesses here)
            _possessCalls.Clear();
            PumpUntil(() => _flow.State == DeployFlowState.Choosing, "Choosing again");
        }

        /// <summary>No answer within 10 s ⇒ Choosing, "the server did not answer" — the server is simply not run, the Player's clock is stepped past 10 s. ✅ Red-proof: the deadline check dropped ⇒ it waits forever ⇒ red.</summary>
        [Fact]
        public void NoAnswerIn10Seconds_ReturnsToChoosing()
        {
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            _clientMs += 9_000;
            _flow.Execute(_client, 0.016f);
            Assert.Equal(DeployFlowState.Requesting, _flow.State);
            _clientMs += 1_500;
            _flow.Execute(_client, 0.016f);
            Assert.Equal(DeployFlowState.Choosing, _flow.State);
            Assert.Equal(DeployFlowSystem.NoAnswer, _flow.LastRefusal);
        }

        /// <summary>The server going away sends any state but Playing back to Observing (the check is sampled once a second). ✅ Red-proof: the server-gone branch dropped ⇒ it stays Choosing ⇒ red.</summary>
        [Fact]
        public void TheServerGoing_SendsTheFlowBackToObserving()
        {
            ToChoosing();
            _serverExists = false;
            _clientMs += 1_200;
            _flow.Execute(_client, 0.016f);
            Assert.Equal(DeployFlowState.Observing, _flow.State);
        }

        /// <summary>The observer choice (D-13): <c>Observe(side)</c> asks the server, which makes a Choosing slot; the flow ends Observing and the server holds the slot. ✅ Red-proof: the observe flag not sent ⇒ the request is refused ⇒ red.</summary>
        [Fact]
        public void Observe_IsAcknowledged_AndTheServerHoldsAChoosingSlot()
        {
            ToChoosing();
            Assert.True(_flow.Observe(ForceId.Hostile));
            PumpUntil(() => _flow.State == DeployFlowState.Observing, "Observing");
            Assert.True(_proc.TryGetSlot(Station, out var slot, out int unit)); Assert.Equal(PlayerSlotState.Choosing, slot); Assert.Equal(0, unit);
            Assert.Empty(_possessCalls);
        }

        // ── ⭐ S2 — the Player mirrors the server's slot: death, respawn, release ────────────────

        private void ToPlaying()
        {
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            PumpUntil(() => _flow.State == DeployFlowState.TakingControl, "TakingControl");
            var held = new PossessedBy { StationNodeId = (NodeId)Station, Mode = PossessionMode.Fps, Epoch = 1 };
            _client.AddComponent(_clientUnit, held); _server.AddComponent(_serverUnit, held);
            PumpUntil(() => _flow.State == DeployFlowState.Playing && _proc.TryGetSlot(Station, out var s, out _) && s == PlayerSlotState.Playing, "Playing on both sides");
            for (int i = 0; i < 20; i++) Frame();   // the Player reads the Playing slot
        }

        /// <summary>
        /// ⭐ A ROLE request is answered with a DIFFERENT unit — the man the server spawned — and the flow takes THAT one (Possess and Playing on his id),
        /// never the role entity it asked for. 📐 Found live `2026-11-05` (S2): the flow possessed the asked id and timed out every time. The server is not
        /// ticked here: its answer is written by hand, as the processor writes it once the man exists. 🔴 Mutation: the answer's unit ignored ⇒ Possess(900) ⇒ red.
        /// </summary>
        [Fact]
        public void ARoleRequest_TakesTheManTheServerNames_NotTheRoleItAskedFor()
        {
            ToChoosing();
            const int RoleEntity = 900;
            Assert.True(_flow.Request(ForceId.Friend, RoleEntity));
            using var answer = new DdsWriter<DeployStatusTopic>(_serverP);
            for (int i = 0; i < 300 && _flow.State == DeployFlowState.Requesting; i++)
            {
                answer.Write(new DeployStatusTopic { StationNodeId = Station, RequestId = _flow.RequestId, Accepted = true, UnitNetId = Rifleman });
                _clientMs += 10; _client.Bus.SwapBuffers(); _clientSlots.Execute(_client, 0.016f); _flow.Execute(_client, 0.016f);
                Thread.Sleep(5);
            }
            Assert.Equal(DeployFlowState.TakingControl, _flow.State);
            Assert.Equal(new[] { Rifleman }, _possessCalls);
            Assert.Equal(Rifleman, _flow.UnitNetId);
        }

        private void SetSimTime(double t) { _server.SetSingleton(new GlobalTime { TotalTime = t }); _client.SetSingleton(new GlobalTime { TotalTime = t }); }

        /// <summary>Playing → Dead when the server's slot says the man died: the reason counts down ("killed — respawn in N s", N from RespawnAtSim and the Player's sim clock), and at the respawn the flow is Choosing again.
        /// ✅ Red-proof: the Dead transition dropped ⇒ it stays Playing ⇒ red; the countdown computed from the wrong clock ⇒ the text assertions red.</summary>
        [Fact]
        public void WhenTheManDies_PlayingBecomesDead_WithACountdown_ThenChoosing()
        {
            ToPlaying();
            _server.GetComponentRW<Health>(_serverUnit).Current = 0f;
            PumpUntil(() => _flow.State == DeployFlowState.Dead, "Dead");
            Assert.Equal(110.0, _flow.RespawnAtSim, 3);
            Assert.Equal("killed — respawn in 10 s", _flow.Reason);
            SetSimTime(104.0); Frame();
            Assert.Equal("killed — respawn in 6 s", _flow.Reason);
            SetSimTime(110.0);
            PumpUntil(() => _flow.State == DeployFlowState.Choosing, "Choosing at the respawn");
        }

        /// <summary>A respawn delay of 0 keeps the man dead for good: the flow stays Dead and says "killed — observing". ✅ Red-proof: the zero case counting down ⇒ the text assertion red.</summary>
        [Fact]
        public void WithARespawnDelayOfZero_TheFlowStaysDead_AndSaysObserving()
        {
            var env = _server.CreateEntity(); _server.AddComponent(env, new GameplaySettings { RespawnDelaySeconds = 0f });
            ToPlaying();
            _server.GetComponentRW<Health>(_serverUnit).Current = 0f;
            PumpUntil(() => _flow.State == DeployFlowState.Dead, "Dead");
            Assert.Equal(DeployFlowSystem.KilledObserving, _flow.Reason);
            SetSimTime(5000.0);
            for (int i = 0; i < 30; i++) Frame();
            Assert.Equal(DeployFlowState.Dead, _flow.State);
        }

        /// <summary>The station RELEASING its unit: the server's slot goes Choosing and the Player's flow follows (Playing → Choosing). ✅ Red-proof: the released-slot branch dropped ⇒ it stays Playing ⇒ red.</summary>
        [Fact]
        public void AReleasedUnit_SendsThePlayerBackToChoosing()
        {
            ToPlaying();
            _server.RemoveComponent<PossessedBy>(_serverUnit);
            PumpUntil(() => _flow.State == DeployFlowState.Choosing, "Choosing after the release");
        }

        // ── ⭐ S3 — kits and areas through the Player's flow ─────────────────────────────────────

        /// <summary>The flow's <c>Request(side, role, kit, area)</c> puts BOTH on the wire: a raw reader of the request topic sees <c>Kit</c> and <c>AreaNetId</c>. ✅ Red-proof: the fields not copied into the request ⇒ red.</summary>
        [Fact]
        public void TheFlow_SendsTheKitAndTheArea()
        {
            using var raw = new DdsReader<DeployRequestTopic>(_serverP);
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman, kit: 2, areaNetId: 55));
            Assert.Equal((byte)2, _flow.RequestedKit); Assert.Equal(55, _flow.RequestedAreaNetId);
            DeployRequestTopic seen = default;
            PumpUntil(() => { using var l = raw.Take(); foreach (var s in l) if (s.IsValid) { seen = s.Data; return true; } return false; }, "the raw request");
            Assert.Equal((byte)2, seen.Kit); Assert.Equal(55, seen.AreaNetId); Assert.Equal(Rifleman, seen.UnitNetId);
        }

        /// <summary>The Player reads the area offers and a role's kit labels, and <c>GET /deploy</c>'s document carries them (kind, count, max, kits). ✅ Red-proof: the kits dropped when the offer is read ⇒ red; the area kind not reported ⇒ red.</summary>
        [Fact]
        public void TheFlow_ReadsAreaOffersAndKitLabels_AndGetReportsThem()
        {
            _server.RegisterComponent<DeployRole>(); _server.RegisterComponent<SpawnArea>(); _server.RegisterComponent<EntityInfo>();
            var role = _server.CreateEntity();
            _server.AddComponent(role, new NetworkIdentity { Value = 300 });
            _server.AddComponent(role, new DeployRole { Side = ForceId.Friend, PersonTkbType = (Fdp.Core.TkbTypeId)(2002), Name = new FixedString32("Rifleman"), MaxCount = 4, Kit1Tkb = (Fdp.Core.TkbTypeId)(2004), Kit1Name = new FixedString32("Grenadier") });
            _serverMap.Register(300, role);
            var area = _server.CreateEntity();
            _server.AddComponent(area, new NetworkIdentity { Value = 400 });
            _server.AddComponent(area, new SpawnArea { Side = ForceId.Friend });
            _server.AddComponent(area, new EntityInfo { Name = new FixedString64("Church"), ForceId = ForceId.Friend });
            _serverMap.Register(400, area);

            PumpUntil(() => _flow.Offers.Any(o => o.UnitNetId == 400) && _flow.Offers.Any(o => o.UnitNetId == 300), "the area and role offers");
            var a = _flow.Offers.Single(o => o.UnitNetId == 400);
            Assert.True(a.IsArea); Assert.Equal("Church", a.SlotName);
            var r = _flow.Offers.Single(o => o.UnitNetId == 300);
            Assert.True(r.IsRole); Assert.Equal(4, r.Max); Assert.Equal(new[] { "Rifleman", "Grenadier", "", "" }, r.Kits);

            var doc = (System.Text.Json.Nodes.JsonObject)DeployFlowApi.Get(_flow);
            var offers = (System.Text.Json.Nodes.JsonArray)doc["offers"]!;
            var jr = offers.Cast<System.Text.Json.Nodes.JsonObject>().Single(o => (int)o["unitNetId"]! == 300);
            Assert.Equal("Role", (string?)jr["kind"]); Assert.Equal(4, (int)jr["max"]!);
            Assert.Equal(new[] { "Rifleman", "Grenadier", "", "" }, ((System.Text.Json.Nodes.JsonArray)jr["kits"]!).Select(k => (string)k!).ToArray());
            Assert.Equal("Area", (string?)offers.Cast<System.Text.Json.Nodes.JsonObject>().Single(o => (int)o["unitNetId"]! == 400)["kind"]);
        }
    }
}
