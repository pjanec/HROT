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
    ///
    /// <para>⭐ CE-11027 S12-2 — <b>ONE fixture over BOTH transports</b> (Q114 D): this abstract class holds every rail; <see cref="GameplayDeployFlowOverDdsTests"/> runs them on a real DDS pair and
    /// <see cref="GameplayDeployFlowOverLocalHubTests"/> on the editor's in-process <see cref="LocalGameplayHub"/> (the server end and one client end). The three hooks below are what differs.</para>
    public abstract class GameplayDeployFlowTests : IDisposable
    {
        protected static int _domain = 160;
        private const int ServerNode = 5, Station = 30, Rifleman = 100;

        protected IGameplayWire _serverWire = null!, _clientWire = null!;
        private readonly EntityRepository _server = new(), _client = new();
        private readonly NetworkEntityMap _serverMap = new(), _clientMap = new();
        private readonly DeployProcessorSystem _proc;
        private readonly PlayerSlotReaderSystem _clientSlots;
        private readonly DeployFlowSystem _flow;
        private readonly List<int> _possessCalls = new();
        private long _serverMs = 1_000, _clientMs = 1_000;
        private bool _gateOpen = true, _live = true, _serverExists = true, _possessOk = true;
        private Entity _serverUnit, _clientUnit;

        /// <summary>Makes the transport: the server's wire and the client's wire.</summary>
        protected abstract void CreateTransport(out IGameplayWire server, out IGameplayWire client);
        /// <summary>DDS: asserts the client created NO gameplay endpoint (the lazy wire). A hub has no endpoints to count: no-op.</summary>
        protected virtual void AssertClientCreatedNoEndpoint() { }
        /// <summary>DDS: asserts the client's request writer exists. A hub: no-op.</summary>
        protected virtual void AssertClientRequestWriterCreated() { }
        /// <summary>Starts a second, independent look at the requests the server receives (DDS: a raw reader on the server's participant; a hub: the recording of what the processor took).</summary>
        protected abstract void BeginRawRequestTap();
        protected abstract bool TryReadRawRequest(out DeployRequestTopic request);

        protected GameplayDeployFlowTests()
        {
            CreateTransport(out _serverWire, out _clientWire);
            foreach (var w in new[] { _server, _client })
            {
                w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<PossessedBy>(); w.RegisterComponent<Health>(); w.RegisterEvent<NodeDeparted>(); w.RegisterComponent<GameplaySettings>();
                w.SetSingleton(new GlobalTime { TotalTime = 100 });
            }
            _server.RegisterComponent<Playable>();
            _proc = new DeployProcessorSystem(_serverWire, _serverMap, ServerNode, election: null, nowMs: () => _serverMs);
            var env = new DeployFlowEnvironment(() => _gateOpen, () => _live, () => _serverExists, unit => { _possessCalls.Add(unit); return (_possessOk, _possessOk ? "" : "refused by the brain"); }, () => _clientMs);
            _flow = new DeployFlowSystem(_clientWire, _client, _clientMap, Station, env);
            _clientSlots = new PlayerSlotReaderSystem(_clientWire, () => _flow.ServerExists);
            _flow.Slots = _clientSlots.TryGetSlot;   // the pack's wiring: the flow reads its own slot through the one reader
            _serverUnit = Unit(_server, _serverMap, Rifleman, ForceId.Friend, "Rifleman 1", playable: true);
            _clientUnit = Unit(_client, _clientMap, Rifleman, ForceId.Friend, "", playable: false);
        }

        public virtual void Dispose() { _client.Dispose(); _server.Dispose(); }

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
            AssertClientCreatedNoEndpoint();
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
            AssertClientCreatedNoEndpoint();
        }

        /// <summary>
        /// ⭐ <c>CE-11027</c> S12-2 (Q114 D) — a node may host several stations: <c>Of(world, station)</c> finds each flow, <c>Of(world)</c> stays the world's OWN (the first, <c>register: true</c>), and a flow made with
        /// <c>register: false</c> (a gateway seat) never replaces it; an <c>Unregister</c>ed seat is forgotten. Its <c>Side</c> is the side of its last request. 🔴 Mutation: <c>register: false</c> still overwrites <c>Of(world)</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE11027_ASeatFlowWithRegisterFalse_IsFoundByStation_AndNeverReplacesTheWorldsOwnFlow()
        {
            Assert.Same(_flow, DeployFlowSystem.Of(_client));
            Assert.Same(_flow, DeployFlowSystem.Of(_client, Station));
            var env = new DeployFlowEnvironment(() => true, () => true, () => true, _ => (true, ""), () => _clientMs);
            var seat = new DeployFlowSystem(_clientWire, _client, _clientMap, Station + 1, env, register: false);
            Assert.Same(_flow, DeployFlowSystem.Of(_client));              // the seat did not take over the world's own
            Assert.Same(seat, DeployFlowSystem.Of(_client, Station + 1));  // but it is found by its station
            Assert.Same(_flow, DeployFlowSystem.Of(_client, Station));
            Assert.Null(DeployFlowSystem.Of(_client, Station + 2));
            var seat2 = new DeployFlowSystem(_clientWire, _client, _clientMap, Station + 2, env);   // register: true (the default) — the latest own wins, as before
            Assert.Same(seat2, DeployFlowSystem.Of(_client));
            seat.Unregister(); seat2.Unregister();
            Assert.Null(DeployFlowSystem.Of(_client, Station + 1));
            Assert.Null(DeployFlowSystem.Of(_client));                      // seat2 was the own one
            Assert.Same(_flow, DeployFlowSystem.Of(_client, Station));
        }

        /// <summary>
        /// ⭐ <c>CE-11027</c> S12-3 — the flow's <c>Leave()</c> verb: the server frees the station's slot (and publishes the release) and the flow is back to Choosing. From Waiting it does nothing.
        /// </summary>
        [Fact]
        public void CE11027_Leave_FreesTheServersSlot_AndTheFlowGoesBackToChoosing()
        {
            Assert.False(_flow.Leave());                       // Waiting: nothing joined
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            PumpUntil(() => _proc.TryGetSlot(Station, out _, out _), "the slot");
            Assert.True(_flow.Leave());
            Assert.Equal(DeployFlowState.Choosing, _flow.State);
            PumpUntil(() => !_proc.TryGetSlot(Station, out _, out _), "the slot freed by the leave");
        }

        /// <summary>⭐ <c>CE-11027</c> S12-2 — the flow's <c>Side</c> is Neutral until it asks, then the side of its last request.</summary>
        [Fact]
        public void CE11027_TheFlowsSide_IsTheSideOfItsLastRequest()
        {
            Assert.Equal(ForceId.Neutral, _flow.Side);
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman));
            Assert.Equal(ForceId.Friend, _flow.Side);
        }

        /// <summary>With a server the Player CHOOSES and sees the server's offers: the placed unit, its side, name and holder (free). ✅ Red-proof: the offer reader never read ⇒ no offers ⇒ red.</summary>
        [Fact]
        public void WithAServer_Chooses_AndReadsTheOffers()
        {
            ToChoosing();
            var o = Assert.Single(_flow.Offers);
            Assert.Equal(new DeployOffer(Rifleman, ForceId.Friend, "Rifleman 1", 0), o);
            AssertClientRequestWriterCreated();
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
            _client.AddComponent(_clientUnit, new PossessedBy { StationNodeId = (Fdp.Core.StationId)Station, Mode = PossessionMode.Fps, Epoch = 1 });
            PumpUntil(() => _flow.State == DeployFlowState.Playing, "Playing");
            Assert.Equal(Rifleman, _flow.UnitNetId);
        }

        /// <summary>A refusal comes back to Choosing with the server's reason and the holder named; the Possess is never called. ✅ Red-proof: the refusal branch sending on to Possess ⇒ red.</summary>
        [Fact]
        public void ARefusal_ReturnsToChoosing_NamingTheReasonAndTheHolder()
        {
            _server.AddComponent(_serverUnit, new PossessedBy { StationNodeId = (Fdp.Core.StationId)77, Mode = PossessionMode.Fps, Epoch = 1 });
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
            var held = new PossessedBy { StationNodeId = (Fdp.Core.StationId)Station, Mode = PossessionMode.Fps, Epoch = 1 };
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
            var answer = _serverWire.Status!;   // the server's own status writer: the answer is written by hand
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
            BeginRawRequestTap();
            ToChoosing();
            Assert.True(_flow.Request(ForceId.Friend, Rifleman, kit: 2, areaNetId: 55));
            Assert.Equal((byte)2, _flow.RequestedKit); Assert.Equal(55, _flow.RequestedAreaNetId);
            DeployRequestTopic seen = default;
            PumpUntil(() => TryReadRawRequest(out seen), "the raw request");
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

    /// <summary>⭐ CE-11027 S12-2 — every <see cref="GameplayDeployFlowTests"/> rail over a REAL DDS pair (the original transport).</summary>
    public sealed class GameplayDeployFlowOverDdsTests : GameplayDeployFlowTests
    {
        private DdsParticipant _serverP = null!, _clientP = null!;
        private GameplayWire _serverDds = null!, _clientDds = null!;
        private DdsReader<DeployRequestTopic>? _raw;

        protected override void CreateTransport(out IGameplayWire server, out IGameplayWire client)
        {
            uint domain = (uint)Interlocked.Increment(ref _domain);
            _serverP = new DdsParticipant(domain); _clientP = new DdsParticipant(domain);
            _serverDds = new GameplayWire(_serverP); _clientDds = new GameplayWire(_clientP);
            server = _serverDds; client = _clientDds;
        }

        protected override void AssertClientCreatedNoEndpoint() { Assert.Equal(0, _clientDds.CreatedCount); Assert.False(_clientDds.RequestWriterCreated); }
        protected override void AssertClientRequestWriterCreated() => Assert.True(_clientDds.RequestWriterCreated);
        protected override void BeginRawRequestTap() => _raw = new DdsReader<DeployRequestTopic>(_serverP);

        protected override bool TryReadRawRequest(out DeployRequestTopic request)
        {
            request = default;
            using var l = _raw!.Take();
            foreach (var s in l) if (s.IsValid) { request = s.Data; return true; }
            return false;
        }

        public override void Dispose() { _raw?.Dispose(); _clientDds.Dispose(); _serverDds.Dispose(); _clientP.Dispose(); _serverP.Dispose(); base.Dispose(); }
    }

    /// <summary>⭐ CE-11027 S12-2 — every <see cref="GameplayDeployFlowTests"/> rail over the editor's in-process <see cref="LocalGameplayHub"/>.</summary>
    public sealed class GameplayDeployFlowOverLocalHubTests : GameplayDeployFlowTests
    {
        private LocalGameplayEnd _serverEnd = null!, _clientEnd = null!;
        private readonly List<DeployRequestTopic> _tapped = new();

        protected override void CreateTransport(out IGameplayWire server, out IGameplayWire client)
        {
            var hub = new LocalGameplayHub();
            _serverEnd = hub.ServerEnd(); _clientEnd = hub.ClientEnd();
            server = new TappedServer(_serverEnd, _tapped); client = _clientEnd;
        }

        protected override void BeginRawRequestTap() => _tapped.Clear();

        protected override bool TryReadRawRequest(out DeployRequestTopic request)
        {
            request = _tapped.Count > 0 ? _tapped[0] : default;
            return _tapped.Count > 0;
        }

        public override void Dispose() { _clientEnd.Dispose(); _serverEnd.Dispose(); base.Dispose(); }

        /// <summary>The server's wire with a recording of every request the processor took — the hub's stand-in for a second raw reader.</summary>
        private sealed class TappedServer : IGameplayWire
        {
            private readonly IGameplayWire _inner;
            private readonly Tap _requests;
            public TappedServer(IGameplayWire inner, List<DeployRequestTopic> sink) { _inner = inner; _requests = new Tap(inner.Requests!, sink); }
            public IGameplayIn<DeployRequestTopic>? Requests => _requests;
            public IGameplayOut<DeployStatusTopic>? Status => _inner.Status;
            public IGameplayOut<DeployOfferTopic>? Offers => _inner.Offers;
            public IGameplayOut<PlayerSlotTopic>? SlotWriter => _inner.SlotWriter;
            public IGameplayOut<DeployRequestTopic>? RequestWriter => _inner.RequestWriter;
            public IGameplayIn<DeployStatusTopic>? StatusReader => _inner.StatusReader;
            public IGameplayIn<DeployOfferTopic>? OfferReader => _inner.OfferReader;
            public IGameplayIn<PlayerSlotTopic>? SlotReader => _inner.SlotReader;

            private sealed class Tap : IGameplayIn<DeployRequestTopic>
            {
                private readonly IGameplayIn<DeployRequestTopic> _in; private readonly List<DeployRequestTopic> _sink;
                public Tap(IGameplayIn<DeployRequestTopic> inner, List<DeployRequestTopic> sink) { _in = inner; _sink = sink; }
                public int TakeInto(List<GameplaySample<DeployRequestTopic>> into)
                {
                    int before = into.Count; int n = _in.TakeInto(into);
                    for (int i = before; i < into.Count; i++) if (into[i].Alive) _sink.Add(into[i].Data);
                    return n;
                }
            }
        }
    }
}
