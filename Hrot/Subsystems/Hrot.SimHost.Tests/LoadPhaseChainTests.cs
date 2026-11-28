using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common.ClusterLoad;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <c>L7</c> — the rails for the load-phase chain. <b>These are the ones that would have caught the
/// defect</b>, and each names the silence it replaces.
///
/// <para>🔴 The measured failure, <c>2026-09-18</c>: <c>ClusterSlave</c> gives a load step to the FIRST
/// claimant and returns, so registering several prerequisite loaders as handlers made them compete. On CGF
/// the terrain loader cancelled the scenario loader and the cluster loaded <b>zero entities</b>; on SimHost
/// the knowledge-base loader cancelled the terrain loader, so terrain had <b>never</b> loaded. Neither
/// logged anything, and every unit suite stayed green — because every suite tested a handler in isolation
/// and none tested what a NODE ends up running.</para>
/// </summary>
public sealed class LoadPhaseChainTests
{
    // ── Asset management B1/B2 (CE-3020) — the asset tokens DERIVED from this class's requirement table ─────────
    //    📄 docs/DESIGN_Asset_Management.md §7.3a (adapter + ANY rule), §7.3b (subtraction, BOUNDED), §10 D1/D3/D4.

    private static readonly (string, string?)[] BrainContributors =
    {
        ("blueprint", "/assets/Blueprints"),
        ("btree", null), ("btree", "/assets/BTrees"),     // ⭐ two contributors, one assembly-backed (null) — ANY wins
        ("hsm", null),                                    // ⭐ only an assembly-backed contributor ⇒ rootless ⇒ no token
    };

    [Fact]
    public void AssetTokens_Map2D_GetsTheKnowledgeBaseAndTheTerrain_AndNothingElse_B1()
    {
        var tokens = AssetNeeds.Tokens(NodeRole.Map2D, BrainContributors, AssetAuthoring.None);
        Assert.Equal(new[] { "hrot.asset.needs.tkb", "hrot.asset.needs.terrain" }, tokens);   // §10 D1: terrain is universal
    }

    [Fact]
    public void AssetTokens_ARuntimeBrain_IsMirroredEveryRootedKind_ByTheAnyRule_B1()
    {
        var p = Fdp.Toolkit.Orchestration.Assets.AssetTokens.Parse(
            AssetNeeds.Tokens(NodeRole.Brain, BrainContributors, AssetAuthoring.None));

        Assert.Equal(new[] { "blueprint", "btree", "scenario", "terrain", "tkb" }, p.Needs.OrderBy(k => k));
        Assert.Equal("/assets/BTrees", p.Roots["btree"]);
        Assert.False(p.Roots.ContainsKey("hsm"));                                      // rootless ⇒ no token, no throw
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror, p.ModeFor("blueprint"));   // rail ② NON-VACUITY
    }

    [Fact]
    public void AssetTokens_AnAuthoringBrain_IsAddOnlyForItsKinds_ButNeverForALoadPart_B2()
    {
        var p = Fdp.Toolkit.Orchestration.Assets.AssetTokens.Parse(
            AssetNeeds.Tokens(NodeRole.Brain, BrainContributors, AssetAuthoring.AllRooted));
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly, p.ModeFor("blueprint"));
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly, p.ModeFor("btree"));
        Assert.Null(p.ModeFor("hsm"));

        // ⭐ rail ④ BOUNDED — even an explicit claim on the scenario never takes it away (ScenarioLoadStep would throw).
        var bounded = Fdp.Toolkit.Orchestration.Assets.AssetTokens.Parse(
            AssetNeeds.Tokens(NodeRole.Brain, BrainContributors, AssetAuthoring.Of(new[] { "scenario", "blueprint" })));
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror, bounded.ModeFor("scenario"));
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.Mirror, bounded.ModeFor("btree"));   // not claimed
        Assert.Equal(Fdp.Toolkit.Orchestration.Assets.AssetSyncMode.AddOnly, bounded.ModeFor("blueprint"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("all", true)]
    [InlineData("none", false)]
    [InlineData("blueprint, hsm", true)]
    public void AssetAuthoring_ParsesTheSetting_DefaultingToAllRooted_D3(string? value, bool authorsBlueprint)
        => Assert.Equal(authorsBlueprint, AssetAuthoring.Parse(value).Authors("blueprint"));

    // ── the requirement table ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The knowledge base is required by <b>every</b> role, including ones with no other requirement —
    /// it is derived from being an ECS node, never from the role.
    /// 🔒 User: <i>"every ECS enable node should be able to create entities so every needs the TKB loaded."</i>
    /// 🔴 The rail that would have caught CGF and IG having no knowledge-base loader at all.
    /// </summary>
    [Theory]
    [InlineData(NodeRole.Brain)]
    [InlineData(NodeRole.MuscleGround)]
    [InlineData(NodeRole.Perception)]
    [InlineData(NodeRole.NavigationSolver)]
    [InlineData(NodeRole.Map2D)]
    [InlineData(NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver)]
    public void EveryRole_RequiresTheKnowledgeBase(NodeRole role)
        => Assert.Contains(LoadPart.KnowledgeBase, RoleLoadRequirements.PartsFor(role));

    /// <summary>
    /// ⭐⭐ Terrain is UNIVERSAL (2026-10-03, R-182/R-183): every role has a reader of the terrain world — the
    /// map on every host (🔒 <i>"Cgf must render the map as well"</i>), LOS, the movement model.
    /// ⛔ SUPERSEDED: "terrain is required only by MuscleGround / NavigationSolver".
    /// 📄 docs/DESIGN_Terrain_World.md §5.
    /// </summary>
    [Theory]
    [InlineData(NodeRole.MuscleGround)]
    [InlineData(NodeRole.NavigationSolver)]
    [InlineData(NodeRole.Brain)]
    [InlineData(NodeRole.Perception)]
    [InlineData(NodeRole.Map2D)]
    public void EveryRole_RequiresTheTerrainWorld(NodeRole role)
        => Assert.Contains(LoadPart.Terrain, RoleLoadRequirements.PartsFor(role));

    /// <summary>⭐ Only <c>Brain</c> reads the scenario file, because only <c>Brain</c> also edits and saves it.</summary>
    [Theory]
    [InlineData(NodeRole.Brain,            true)]
    [InlineData(NodeRole.MuscleGround,     false)]
    [InlineData(NodeRole.Map2D,            false)]
    [InlineData(NodeRole.NavigationSolver, false)]
    public void OnlyBrainLoadsTheScenarioEntities(NodeRole role, bool required)
        => Assert.Equal(required, RoleLoadRequirements.PartsFor(role).Contains(LoadPart.ScenarioEntities));

    /// <summary>
    /// ⚠ <b>The ORDER is load-bearing, not cosmetic.</b> Entities resolve templates from the knowledge base
    /// and are clamped against terrain, so both must precede them.
    /// </summary>
    [Fact]
    public void ThePartsAreOrdered_KnowledgeBase_Terrain_ThenEntities()
    {
        var parts = RoleLoadRequirements.PartsFor(
            NodeRole.Brain | NodeRole.MuscleGround).ToList();

        Assert.Equal(
            new[] { LoadPart.KnowledgeBase, LoadPart.Terrain, LoadPart.ScenarioEntities },
            parts);
    }

    // ── composition: a missing part is LOUD, not silent ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail.</b> A host that cannot satisfy a part its roles require fails at COMPOSITION.
    /// 🔴 The defect it replaces produced an empty world and <c>ok:true</c>, with nothing in any log.
    /// </summary>
    [Fact]
    public void AHostMissingARequiredPart_ThrowsAtComposition()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LoadPhaseChain.FromRoles(
                NodeRole.MuscleGround,
                new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase) },   // no terrain provider
                world: null, hostLabel: "TestHost"));

        Assert.Contains("Terrain", ex.Message);
        Assert.Contains("TestHost", ex.Message);
    }

    /// <summary>
    /// ⭐ A host may compose MORE than its roles require — §3.1's rule that a role never denies a
    /// capability. The extra provider is simply unused.
    /// </summary>
    [Fact]
    public void AHostMayComposeMoreThanItsRolesRequire()
    {
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D,
            new ILoadPartProvider[]
            {
                new FakeStep(LoadPart.KnowledgeBase),
                new FakeStep(LoadPart.Terrain),
                new FakeStep(LoadPart.ScenarioEntities),
            },
            world: null);

        // ⭐ 2026-10-03: terrain is universal (R-182), so Map2D requires it too; the scenario step stays unused.
        Assert.Equal(new[] { LoadPart.KnowledgeBase, LoadPart.Terrain }, chain.Parts);
    }

    // ── one claimant, EVERY step, one acknowledgement ────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The shadowing rail.</b> EVERY required step runs, in order — the property that was
    /// impossible when each loader was a competing handler.
    /// </summary>
    [Fact]
    public async Task EveryRequiredStepRuns_InOrder()
    {
        var order = new List<LoadPart>();
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Brain | NodeRole.MuscleGround,
            new ILoadPartProvider[]
            {
                new FakeStep(LoadPart.ScenarioEntities, order),
                new FakeStep(LoadPart.Terrain,          order),
                new FakeStep(LoadPart.KnowledgeBase,    order),
            },
            world: null);

        await chain.PrepareAsync(LoadIntent(), CancellationToken.None);

        // ⚠ Registration order above is deliberately scrambled: the CHAIN orders them, not the caller.
        Assert.Equal(
            new[] { LoadPart.KnowledgeBase, LoadPart.Terrain, LoadPart.ScenarioEntities },
            order);
    }

    /// <summary>⭐ The chain claims the two load operations and the transition that ends a load — and nothing else.</summary>
    [Fact]
    public void ItClaimsTheLoadOperations_AndNotTheReplayOrIdleTransitions()
    {
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain) }, world: null);

        Assert.True(chain.CanHandle(NodeOpType.PrepareLive));
        Assert.True(chain.CanHandle(NodeOpType.PrepareEdit));
        Assert.False(chain.CanHandle(NodeOpType.FinalizeLive));      // the fallback handler keeps this
        Assert.False(chain.CanHandle(NodeOpType.SerializeLocal));

        Assert.True(chain.CanHandle(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive)));
        Assert.True(chain.CanHandle(Intent(NodeOpType.PrepareState, ClusterState.OperatingEdit)));

        // ⛔ The replay handler is registered before the chain and owns these.
        Assert.False(chain.CanHandle(Intent(NodeOpType.PrepareState, ClusterState.OperatingReplay)));
        Assert.False(chain.CanHandle(Intent(NodeOpType.PrepareState, ClusterState.Idle)));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The hold.</b> The transition that ends a load does not complete until EVERY step reports
    /// resolved — the property that stops a node going Operating with a half-built world.
    /// </summary>
    [Fact]
    public async Task TheOperatingTransition_WaitsForEVERYStep()
    {
        var blocker = new FakeStep(LoadPart.ScenarioEntities) { Resolved = false };
        var chain   = LoadPhaseChain.FromRoles(
            NodeRole.Brain,
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain), blocker },
            world: null);

        var hold = chain.PrepareAsync(
            Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);

        chain.DrainDeferredAcks();
        Assert.False(hold.IsCompleted);

        blocker.Resolved = true;
        chain.DrainDeferredAcks();

        await hold;
        Assert.True(hold.IsCompleted);
    }

    /// <summary>
    /// ⭐⭐⭐ CE-4090 (AQ108 D14, §5a) — <b>LIVE DOES NOT IMPLY RECORDING, on the node.</b> The recorder starts when the load's operating transition
    /// completes — unless the load's payload said <c>RecordingOff</c>: then NO recorder starts on this node (so no checkpoint / review / restore handler
    /// finds one, and nothing is written under <c>exercises/&lt;id&gt;</c>). The same load without the flag starts exactly one (the control).
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task CE4090_ALiveLoadStartsTheRecorder_UnlessThePayloadSaysRecordingOff(bool recordingOff, int recorders)
    {
        var recorder = new CountingRecorder();
        var exercise = Guid.NewGuid();
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain) },
            world: null, recordingController: recorder, storageDirectory: ".");

        await chain.PrepareAsync(new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.PrepareLive, TransactionId = Guid.NewGuid(),
            DomainPayload = new EditLoadHandlerPayload("scn", false, ClusterState.LoadingLive, ExerciseId: (Fdp.Core.ExerciseId)(exercise), RecordingOff: recordingOff),
        }, CancellationToken.None);
        var hold = chain.PrepareAsync(new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.PrepareState, TransactionId = Guid.NewGuid(),
            DomainPayload = new EditLoadHandlerPayload("scn", false, ClusterState.OperatingLive, ExerciseId: (Fdp.Core.ExerciseId)(exercise), RecordingOff: recordingOff),
        }, CancellationToken.None);
        chain.DrainDeferredAcks();
        await hold;

        Assert.Equal(recorders, recorder.Started);
        if (recorders == 1) Assert.Equal(exercise, recorder.LastExercise);
    }

    private sealed class CountingRecorder : Fdp.Core.Orchestration.IRecordReplayController
    {
        public int Started { get; private set; }
        public Guid LastExercise { get; private set; }
        public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory) { Started++; LastExercise = exerciseId; return Task.CompletedTask; }
        public Task FinalizeRecordingAsync(int maxNetworkId = 0) => Task.CompletedTask;
        public Task PrepareReplayAsync(Guid exerciseId, string storageDirectory) => Task.CompletedTask;
        public void ProcessPlaybackTick(GlobalTime currentTime) { }
        public Task TeardownReplayAsync() => Task.CompletedTask;
        public bool IsReplayActive => false;
        public GlobalTime GetCurrentReplayTime() => default;
        public float ActiveReplayDurationSeconds => 0f;
        public long ActiveRecordingStartWallTicks => 0;
        public long ActiveRecordingEndWallTicks => 0;
        public long ReplayAnchorWallTicks { get; set; }
        public string? ActiveReplayFilePath => null;
        public bool IsRestoringReplay => false;
        public int ActiveMaxNetworkId => 0;
        public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks) => Task.FromResult(default(GlobalTime));
        public Task<string?> AwaitRetractionAcksAsync(TimeSpan timeout) => Task.FromResult<string?>(null);
        public void HoldEgress(string reason) { }
        public void ReleaseEgress() { }
        public void ReleaseReplayExit() { }
    }

    /// <summary>⭐ An abort cancels the hold and rolls every step back, rather than leaving the cluster waiting.</summary>
    [Fact]
    public async Task AnAbort_CancelsTheHold_AndAbortsEveryStep()
    {
        var a = new FakeStep(LoadPart.KnowledgeBase) { Resolved = false };
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D, new ILoadPartProvider[] { a, new FakeStep(LoadPart.Terrain) }, world: null);

        var hold = chain.PrepareAsync(
            Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);

        chain.Abort(LoadIntent(), null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);
        Assert.True(a.Aborted);
    }

    /// <summary>
    /// ⭐⭐ CE-2101 — a load at the WORLD BOUNDARY (entered from Idle) starts from an empty world, before any step commits:
    /// every entity destroyed (generations move on, so a stale handle goes dead) and the network map cleared. A load
    /// that is NOT at the boundary (live-from-replay) keeps the world. 📄 DESIGN_Deterministic_Network_Ids §11b.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWorldBoundary_ClearsTheWorldBeforeAnyStepPrepares_CE2101(bool boundary)
    {
        var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var stale = world.CreateEntity();
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        map.Register(1000, stale);
        int seenAtPrepare = -1, seenAtCommit = -1;
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D,
            new ILoadPartProvider[]
            {
                new FakeStep(LoadPart.KnowledgeBase) { OnPrepare = () => seenAtPrepare = world.EntityCount, OnCommit = w => seenAtCommit = w!.EntityCount },
                new FakeStep(LoadPart.Terrain),
            },
            world, entityMap: map);

        var load = BoundaryLoad(boundary);
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);

        Assert.Equal(boundary ? 0 : 1, seenAtPrepare);             // ⭐ the step PREPARED on the cleared world
        Assert.Equal(boundary ? 0 : 1, seenAtCommit);
        Assert.Equal(!boundary, world.IsAlive(stale));
        Assert.Equal(!boundary, map.TryGetEntity(1000, out _));
        if (boundary)
        {
            var fresh = world.CreateEntity();
            Assert.False(fresh.Equals(stale), "the stale handle must not alias the next entity (generation moved on)");
        }
    }

    /// <summary>
    /// ⭐⭐⭐ CE-2101 × P4 — an entity that arrives DURING the load window (a faster node's committed entity, replicated in
    /// while this node's off-thread step still works) belongs to the NEW world and survives this node's Commit.
    /// 🔴 Red before the fix: the clear ran at Commit and destroyed it — on a SimHost a queued blueprint application then
    /// threw "Entity is not alive" (TimeControlIntegrationTests CE3068, 3 of 12 runs).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task TheWorldBoundary_KeepsAnEntityThatArrivesDuringTheLoadWindow_CE2101_P4()
    {
        var world = new EntityRepository();
        var stale = world.CreateEntity();
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        var blocking = new BlockingStep(LoadPart.Terrain);
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), blocking },
            world, entityMap: map, hostLabel: "TestHost");

        var load = BoundaryLoad(true);
        var prepare = chain.PrepareAsync(load, CancellationToken.None);
        Assert.True(blocking.Started.Wait(5_000));
        Assert.False(world.IsAlive(stale));                 // ⭐ the previous run is gone as soon as the load starts

        var arrived = world.CreateEntity();                  // the window: a replica of a faster node's new world
        map.Register(1000, arrived);

        blocking.Release.SetResult();
        await prepare;
        chain.Commit(load, null);

        Assert.True(world.IsAlive(arrived), "an entity of the new world must survive the Commit");
        Assert.True(map.TryGetEntity(1000, out var mapped) && mapped.Equals(arrived));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-2417</c> — a step works inside the node's progress: ACTIVE while its worker prepares (so the frame's beat needs progress), not
    /// after. ⛔ Entities that replicate in DURING the prepare are not its progress (a hung loader must not pass for a working one); ⭐ from the
    /// commit on, entity creation is. 🔴 Red-proof: <c>Watch(_world)</c> at prepare instead of <c>Watch(null)</c> ⇒ the replica counts.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task CE2417_AStepPreparesActive_ReplicasAreNotItsProgress_TheCommitsCreationIs()
    {
        var world = new EntityRepository();
        var blocking = new BlockingStep(LoadPart.Terrain);
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), blocking }, world, hostLabel: "TestHost");
        var progress = new LoadProgress();
        progress.Watch(world);                  // what the previous load left watched
        LoadProgress.Current = progress;        // as the node's ClusterSlave sets it (an async test: restored on return)

        var load = BoundaryLoad(false);
        var prepare = chain.PrepareAsync(load, CancellationToken.None);
        Assert.True(blocking.Started.Wait(5_000));
        Assert.True(progress.IsActive, "the worker's step is working");
        long before = progress.Value;
        for (int i = 0; i < 5; i++) world.CreateEntity();   // a faster node's entities replicate in
        Assert.Equal(before, progress.Value);

        blocking.Release.SetResult();
        await prepare;
        Assert.False(progress.IsActive, "prepared: the node now waits for the cluster — not working");

        chain.Commit(load, null);
        long committed = progress.Value;
        for (int i = 0; i < 5; i++) world.CreateEntity();   // genesis materialises the scenario
        Assert.True(progress.Value >= committed + 5, $"{committed} -> {progress.Value}");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4000</c> F2 / <c>R-4019</c> — the chain is the world owner the slave tells when the node enters Idle: it empties
    /// the world (every entity destroyed, the map cleared, the <c>WorldEpoch</c> moved — which is what makes the network
    /// cleanup delete the node's own entities on the wire).
    /// </summary>
    [Fact]
    public void CE4000_EnteringIdle_EmptiesTheWorld()
    {
        var world = new EntityRepository();
        var stale = world.CreateEntity();
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        map.Register(1001, stale);
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain) }, world, entityMap: map);
        int epochBefore = Fdp.Toolkit.Replication.Services.WorldEpoch.Of(world);

        Assert.IsAssignableFrom<IIdleResetHandler>(chain);
        chain.OnEnteredIdle();

        Assert.False(world.IsAlive(stale));
        Assert.False(map.TryGetEntity(1001, out _));
        Assert.NotEqual(epochBefore, Fdp.Toolkit.Replication.Services.WorldEpoch.Of(world));
    }

    /// <summary>
    /// ⭐⭐ C7 (Q91 §4-M, <c>R-4015</c>) — a late joiner's CATCH-UP load runs the universal parts only: the knowledge base and the
    /// terrain prepare and commit, the scenario step does neither — the world arrives by replication, and a Brain re-running
    /// genesis would duplicate every unit.
    /// </summary>
    [Fact]
    public async Task C7_ACatchUpLoad_RunsTheUniversalPartsOnly_NeverTheScenario()
    {
        var order = new List<LoadPart>();
        var kb = new FakeStep(LoadPart.KnowledgeBase, order) { OnCommit = _ => order.Add(LoadPart.KnowledgeBase) };
        var terrain = new FakeStep(LoadPart.Terrain, order) { OnCommit = _ => order.Add(LoadPart.Terrain) };
        var scenario = new FakeStep(LoadPart.ScenarioEntities, order) { OnCommit = _ => order.Add(LoadPart.ScenarioEntities) };
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Brain | NodeRole.MuscleGround, new ILoadPartProvider[] { kb, terrain, scenario }, world: null);

        var load = new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.PrepareLive, TransactionId = Guid.NewGuid(),
            DomainPayload = new EditLoadHandlerPayload(null, TargetState: ClusterState.LoadingLive, IsCatchUp: true),
        };
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);

        Assert.DoesNotContain(LoadPart.ScenarioEntities, order);
        Assert.Contains(LoadPart.KnowledgeBase, order);
        Assert.Contains(LoadPart.Terrain, order);
    }

    private static ExecuteNodeOpIntent BoundaryLoad(bool boundary) => new()
    {
        Operation     = NodeOpType.PrepareLive,
        TransactionId = Guid.NewGuid(),
        DomainPayload = new EditLoadHandlerPayload(ScenarioId: "scn", TargetState: ClusterState.LoadingLive, IsWorldBoundary: boundary),
    };

    // ── P4 (Q91 §4-B2, R-4001) — the frame thread coordinates; only a pure step leaves it ─────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>P4</c> — while an off-thread step is still working, the chain's prepare is an INCOMPLETE task (so
    /// <c>ClusterSlave</c> polls it and the frame — and the heartbeat — keep ticking), the inline step has already
    /// run, and the off-thread one runs on a worker. ⛔ Before: every step ran inline, so the frame stopped for the
    /// whole read.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task P4_AnOffThreadStep_LeavesThePrepareIncomplete_AndRunsOffTheCallingThread()
    {
        var order    = new List<LoadPart>();
        var blocking = new BlockingStep(LoadPart.Terrain);
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround,
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase, order), blocking },
            world: null, hostLabel: "TestHost");

        int callingThread = Environment.CurrentManagedThreadId;
        var prepare = chain.PrepareAsync(LoadIntent(), CancellationToken.None);

        Assert.True(blocking.Started.Wait(5_000), "the off-thread step never started");
        Assert.False(prepare.IsCompleted, "the prepare must stay pending while the off-thread step works");
        Assert.Equal(new[] { LoadPart.KnowledgeBase }, order);   // ⭐ the inline step already ran
        Assert.NotEqual(callingThread, blocking.ThreadId);        // ⭐ and the pure one is on a worker

        blocking.Release.SetResult();
        await prepare;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>P8</c> (Q91 §4-H, H1/H9) — an abort naming the load the chain holds, arriving while a step is still working:
    /// the chain claims it (and ONLY it — an abort of another transaction is not its), every step's <c>Abort</c> runs, and
    /// the chain forgets the load. ⛔ Before: the chain claimed no abort, so the node auto-ACKed and nothing rolled back.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task P8_AnAbortOfTheLoadInFlight_AbortsEveryStep_AndOnlyThatLoadsAbortIsClaimed()
    {
        var kb       = new FakeStep(LoadPart.KnowledgeBase);
        var blocking = new BlockingStep(LoadPart.Terrain);
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.MuscleGround, new ILoadPartProvider[] { kb, blocking }, world: null, hostLabel: "TestHost");

        var load = LoadIntent();
        var prepare = chain.PrepareAsync(load, CancellationToken.None);
        Assert.True(blocking.Started.Wait(5_000));

        var other = new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.AbortTransaction, TransactionId = Guid.NewGuid(),
            DomainPayload = new AbortTransactionPayload(Guid.NewGuid()),
        };
        Assert.False(chain.CanHandle(other));   // ⭐ not its load

        var abort = new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.AbortTransaction, TransactionId = Guid.NewGuid(),
            DomainPayload = new AbortTransactionPayload(load.TransactionId),
        };
        Assert.True(chain.CanHandle(abort));
        await chain.PrepareAsync(abort, CancellationToken.None);
        chain.Commit(abort, null);

        Assert.True(kb.Aborted);
        Assert.True(blocking.Aborted);
        Assert.False(chain.CanHandle(abort));   // ⭐ the load is forgotten — a second abort is nobody's

        blocking.Release.SetResult();
        await prepare;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> (R-3072, AQ110 A) — <b>the chain names the step it is on</b>, which the node reports on its heartbeat and in its progress ack: the steps in the chain's order while the load prepares (a step run on a WORKER included),
    /// then — while the Operating prepare is held — the first step still unresolved; <see langword="null"/> once the hold resolves and after an abort.
    /// 🔴 Mutations (each compiles): the assignment before <c>step.PrepareAsync</c> removed ⇒ the in-order cell reds; the one in <c>DrainDeferredAcks</c> removed ⇒ the hold cell reds; <c>_currentStep = null</c> removed from the resolve ⇒ the "resolved" cell reds;
    /// from <c>AbortLoad</c> ⇒ the "abort" cell reds.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task CE3345_TheChainNamesTheStepItIsOn_InOrder_WhileHeld_AndNothingOnceResolvedOrAborted()
    {
        LoadPhaseChain? chain = null;
        var seen = new List<string?>();
        FakeStep Named(LoadPart part) => new(part) { OnPrepare = () => seen.Add(chain!.CurrentLoadStep) };
        var terrain = Named(LoadPart.Terrain);
        terrain.Resolved = false;   // the MIDDLE step drains on: the held name must differ from the last step prepared
        chain = LoadPhaseChain.FromRoles(NodeRole.Brain | NodeRole.MuscleGround,
            new ILoadPartProvider[] { Named(LoadPart.ScenarioEntities), terrain, Named(LoadPart.KnowledgeBase) }, world: null);
        Assert.Null(chain.CurrentLoadStep);                                         // no load, no step

        await chain.PrepareAsync(LoadIntent(), CancellationToken.None);
        Assert.Equal(new[] { "KnowledgeBase", "Terrain", "ScenarioEntities" }, seen);   // the chain's order, each named while it ran

        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.False(hold.IsCompleted);
        Assert.Equal("Terrain", chain.CurrentLoadStep);                             // held: the step still draining is the one reported

        terrain.Resolved = true;
        chain.DrainDeferredAcks();
        await hold;
        Assert.Null(chain.CurrentLoadStep);                                         // resolved: nothing to report

        // a step that runs on a WORKER is named while it blocks there; an abort clears the name
        var blocking = new BlockingStep(LoadPart.Terrain);
        var chain2 = LoadPhaseChain.FromRoles(NodeRole.MuscleGround, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), blocking }, world: null);
        var load = LoadIntent();
        var prepare = chain2.PrepareAsync(load, CancellationToken.None);
        Assert.True(blocking.Started.Wait(5_000));
        Assert.Equal("Terrain", chain2.CurrentLoadStep);
        await chain2.PrepareAsync(new ExecuteNodeOpIntent
            { Operation = NodeOpType.AbortTransaction, TransactionId = Guid.NewGuid(), DomainPayload = new AbortTransactionPayload(load.TransactionId) }, CancellationToken.None);
        Assert.Null(chain2.CurrentLoadStep);
        blocking.Release.SetResult();
        await prepare;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-11038</c> — a long stage that reports how far it has got (the navmesh bake's tiles) puts it in the step text the node's heartbeat, its progress ack and the director's
    /// stall clock all read: <i>"Terrain: navmesh 12000/59834"</i>; a step that reports nothing reads as its plain name; the text is the same reference while the detail is.
    /// 🔴 Mutations: <c>CurrentLoadStep</c> returning <c>_currentStep</c> alone ⇒ the detail cell reds; <c>_loadProgress</c> not set at the prepare ⇒ the same; the baker not reporting ⇒ <c>RecastNavmeshBakerTests</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public async Task CE11038_AStageThatReportsHowFarItIs_IsInTheStepText()
    {
        var progress = new LoadProgress();
        LoadProgress.Current = progress;   // as the node's ClusterSlave sets it
        LoadPhaseChain? chain = null;
        var seen = new List<string?>();
        var terrain = new FakeStep(LoadPart.Terrain)
        {
            OnPrepare = () =>
            {
                LoadProgress.ReportDetail("navmesh", 12000, 59834);
                seen.Add(chain!.CurrentLoadStep);
                seen.Add(chain.CurrentLoadStep);
            },
        };
        chain = LoadPhaseChain.FromRoles(NodeRole.MuscleGround,
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase) { OnPrepare = () => seen.Add(chain!.CurrentLoadStep) }, terrain }, world: null);
        await chain.PrepareAsync(LoadIntent(), CancellationToken.None);
        Assert.Equal("KnowledgeBase", seen[0]);                    // nothing reported: the plain name
        Assert.Equal("Terrain: navmesh 12000/59834", seen[1]);
        Assert.Same(seen[1], seen[2]);                             // not rebuilt per read
    }

    /// <summary>⭐ A pure step that waits on a gate, recording the thread it ran on.</summary>
    private sealed class BlockingStep : ILoadPartProvider
    {
        public BlockingStep(LoadPart part) => Part = part;
        public LoadPart Part { get; }
        public bool PreparesOffFrameThread => true;
        public ManualResetEventSlim Started { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ThreadId { get; private set; }

        public async Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Started.Set();
            await Release.Task;
        }

        public void Commit(LoadPhaseContext context, EntityRepository? world) { }
        public bool Aborted { get; private set; }
        public void Abort(LoadPhaseContext context) => Aborted = true;
        public bool IsResolved(EntityRepository? world) => true;
    }

    // ── CE-4093 (AQ108 D20) — the unsaved-changes BASELINE hooks: a load into editing commits · a late joiner's catch-up completes ──────────────

    private static (LoadPhaseChain Chain, Fdp.Toolkit.Replay.WorldChangeCheck Check, EntityRepository World, FakeStep Step) BaselineChain()
    {
        var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var check = new Fdp.Toolkit.Replay.WorldChangeCheck();
        // the load's own content: the step's COMMIT builds a unit — the baseline must be taken AFTER it (the loaded world IS the saved one)
        var step = new FakeStep(LoadPart.KnowledgeBase)
        {
            Resolved = false,
            OnCommit = w => w!.SetComponent(w.CreateEntity(), new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1000 }),
        };
        var chain = LoadPhaseChain.FromRoles(
            NodeRole.Map2D, new ILoadPartProvider[] { step, new FakeStep(LoadPart.Terrain) }, world, changeCheck: check);
        return (chain, check, world, step);
    }

    private static ExecuteNodeOpIntent Loading(ClusterState loading, bool catchUp) => new()
    {
        Operation     = NodeOpType.PrepareEdit,
        TransactionId = Guid.NewGuid(),
        DomainPayload = new EditLoadHandlerPayload(ScenarioId: "scn", TargetState: loading, IsCatchUp: catchUp),
    };

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4093</c> hook (a) and (c) — ONE call site in the chain every host runs: when the load into EDITING resolves (every step reported
    /// resolved, in the frame <c>DrainDeferredAcks</c> releases the hold), the world's content hash becomes the node's baseline — taken AFTER the
    /// steps' commits (the loaded world is the saved one), never before they resolve. The same for a late joiner's catch-up (the chain flagged
    /// <c>IsCatchUp</c>). 🔴 Mutation: no <c>BeginSettling</c> in <c>DrainDeferredAcks</c> ⇒ <c>IsSettling</c> / <c>HasBaseline</c> never happen; no <c>SettleStep</c> at its top ⇒ <c>HasBaseline</c> stays false.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CE4093_ALoadIntoEditing_AndACatchUp_TakeTheBaseline_WhenEveryStepResolved(bool catchUp)
    {
        var (chain, check, world, step) = BaselineChain();
        var load = Loading(ClusterState.LoadingEdit, catchUp);
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);
        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingEdit), CancellationToken.None);

        chain.DrainDeferredAcks();
        Assert.False(hold.IsCompleted);
        Assert.False(check.HasBaseline, "a step is still unresolved: the world is not the loaded one yet");

        step.Resolved = true;
        chain.DrainDeferredAcks();
        await hold;

        // the load resolved: the world SETTLES first (a loaded world still gets its first frame of initial work), then it is the baseline
        Assert.True(check.IsSettling);
        Assert.False(check.HasBaseline);
        chain.DrainDeferredAcks();                                 // frame 1 of the settling: the first hash
        Assert.False(check.HasBaseline);
        chain.DrainDeferredAcks();                                 // frame 2: the world did not move — it is the baseline
        Assert.True(check.HasBaseline);
        Assert.False(check.IsSettling);
        Assert.Equal(1, world.EntityCount);                       // the step's commit ran before the baseline
        Assert.False(check.HasChanged(world), "the loaded world is the baseline: a quiet world reads unchanged");
        world.SetComponent(world.CreateEntity(), new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1001 });
        Assert.True(check.HasChanged(world));
    }

    /// <summary>⭐ A load into LIVE takes no baseline: a live run is not edited, and its return to editing is a load of its own.
    /// 🔴 Mutation: the <c>LoadingEdit</c> condition dropped ⇒ this fails.</summary>
    [Fact]
    public async Task CE4093_ALiveLoad_TakesNoBaseline()
    {
        var (chain, check, _, step) = BaselineChain();
        step.Resolved = true;
        var load = Loading(ClusterState.LoadingLive, catchUp: false);
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);
        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        await hold;

        Assert.False(check.HasBaseline);
        Assert.False(check.IsSettling);
    }

    /// <summary>⭐ Entering Idle empties the world ⇒ the baseline goes with it (an emptied world has nothing saved to compare with).
    /// 🔴 Mutation: no <c>ClearBaseline</c> in <c>OnEnteredIdle</c> ⇒ this fails.</summary>
    [Fact]
    public void CE4093_EnteringIdle_ForgetsTheBaseline()
    {
        var (chain, check, world, _) = BaselineChain();
        check.TakeBaseline(world);
        Assert.True(check.HasBaseline);

        chain.OnEnteredIdle();

        Assert.False(check.HasBaseline);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static ExecuteNodeOpIntent LoadIntent()
        => Intent(NodeOpType.PrepareLive, ClusterState.LoadingLive);

    private static ExecuteNodeOpIntent Intent(NodeOpType op, ClusterState target)
        => new()
        {
            Operation     = op,
            TransactionId = Guid.NewGuid(),
            DomainPayload = new EditLoadHandlerPayload(
                ScenarioId: "scn", TargetState: target),
        };

    // ── ⭐ CE-4078 / CE-1446 (R-1046, DESIGN_Cluster_Load_Phase §11.9b) — the player's load waits for its 3-D map ────────────────────────────

    private sealed class FakeMapStatus : IMapBuildStatus
    {
        public bool IsBuilt { get; set; }
        public string? Failure { get; set; }
        public string Step { get; set; } = "";
        public int Requests { get; private set; }
        public void Request3D() => Requests++;
    }

    private static LoadPhaseChain MapChain(FakeMapStatus? status, NodeRole roles = NodeRole.Map2D, bool withProfilePart = true)
        => LoadPhaseChain.FromRoles(
            roles,
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain), new MapBuildLoadStep(() => status, () => withProfilePart) },
            world: null,
            profileParts: withProfilePart ? new[] { LoadPart.Map3D } : null);

    /// <summary>⭐ The map step HOLDS the load until the map reports built — and a headless node (no status) is resolved at once.</summary>
    [Fact]
    public async Task CE4078_TheLoadHoldsUntilTheMapIsBuilt()
    {
        var status = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        var chain  = MapChain(status);
        var hold   = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);

        chain.DrainDeferredAcks();
        Assert.NotSame(hold, await Task.WhenAny(hold, Task.Delay(200)));   // (the hold's continuation is asynchronous: IsCompleted alone could read false right after a release)

        status.IsBuilt = true;
        chain.DrainDeferredAcks();
        await hold;

        // headless: no map attached ⇒ nothing to wait for
        var headless = MapChain(null);
        var h2 = headless.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        headless.DrainDeferredAcks();
        await h2;
    }

    /// <summary>⭐ A map build failure FAILS the load with the map's reason, through the chain's one failure path (the hold faults).</summary>
    [Fact]
    public async Task CE4078_AMapBuildFailureFailsTheLoadWithItsReason()
    {
        var status = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        var chain  = MapChain(status);
        var hold   = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.False(hold.IsCompleted);

        status.Failure = "mesh upload exploded";
        chain.DrainDeferredAcks();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hold);
        Assert.Contains("mesh upload exploded", ex.Message);
        Assert.Null(chain.CurrentLoadStep);
    }

    /// <summary>⭐ While the map is the step still draining, the node reports the map's OWN step, not "Map3D".</summary>
    [Fact]
    public void CE4078_WhileWaiting_TheReportedStepIsTheMapsOwn()
    {
        var status = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        var chain  = MapChain(status);
        _ = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();

        Assert.Equal("3-D map: terrain", chain.CurrentLoadStep);
    }

    /// <summary>⭐ A node whose profile does not ask for the part does not wait for it — even when a provider is registered.</summary>
    [Fact]
    public async Task CE4078_WithoutTheProfilePart_TheMapStepIsAbsent()
    {
        var status = new FakeMapStatus { IsBuilt = false };
        var chain  = MapChain(status, withProfilePart: false);
        var hold   = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        await hold;   // the never-built map did not hold it
    }

    private static LoadPhaseChain MapInLoadChain(FakeMapStatus status, bool required)
        => LoadPhaseChain.FromRoles(
            NodeRole.Brain,
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain), new FakeStep(LoadPart.ScenarioEntities), new MapBuildLoadStep(() => status, () => required)   },
            world: null,
            profileParts: new[] { LoadPart.Map3D });   // as EcsHostClusterHandlers composes it for any host that supplies a map

    /// <summary>
    /// ⭐ <c>CE-3354</c> ③ — a node NOT in the master's map-in-load set (<c>context.MapInLoad</c> false) and whose profile does not ask for the map: the map step resolves at once, <c>Request3D</c> is NEVER called, the load does not wait.
    /// A node IN the set: <c>Request3D</c> is called exactly once at the load's commit, and the load HOLDS until the map reports built. A profile node (the Player) waits without the flag.
    /// 🔴 Mutations (each compiles): the step ignoring <c>MapInLoad</c> ⇒ the held cell never holds; <c>Request3D</c> called in prepare / per poll / never ⇒ the count cells red; <c>IsResolved</c> ignoring the gate ⇒ the first cell holds.
    /// </summary>
    [Fact]
    public async Task CE3354_AMapInLoadNode_RequestsItsMapAtCommit_AndHoldsUntilBuilt_AnyOtherNodeDoesNot()
    {
        // not named: no wait, no build
        var plain = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        var chain = MapInLoadChain(plain, required: false);
        var load = LoadIntent();
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);
        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        await hold;                                  // the never-built map did not hold it
        Assert.Equal(0, plain.Requests);

        // named: Request3D once at commit, the load holds until built
        var named = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        chain = MapInLoadChain(named, required: true);
        load = LoadIntent();
        await chain.PrepareAsync(load, CancellationToken.None);
        Assert.Equal(0, named.Requests);             // not at prepare
        chain.Commit(load, null);
        Assert.Equal(1, named.Requests);
        hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.NotSame(hold, await Task.WhenAny(hold, Task.Delay(200)));
        Assert.Equal(1, named.Requests);             // polling does not re-ask
        named.IsBuilt = true;
        chain.DrainDeferredAcks();
        await hold;

        // the profile (the Player) waits without the flag
        var player = new FakeMapStatus { IsBuilt = false };
        chain = MapInLoadChain(player, required: true);
        load = LoadIntent();
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);
        hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.NotSame(hold, await Task.WhenAny(hold, Task.Delay(200)));
        player.IsBuilt = true;
        chain.DrainDeferredAcks();
        await hold;
    }

    /// <summary>
    /// ⭐ <c>CE-3354</c> — a LATE JOINER waits for its 3-D map too: the master's catch-up (<c>IsCatchUp</c>, <c>ClusterMaster.InSync.cs</c> C7) skips only the scenario step, so a Player's
    /// catch-up — and a joiner named in the map-in-load set — asks for the map at commit and holds its answer (⇒ the master marks it in sync) until the map reports built.
    /// 🔴 Mutation (compiles): <c>StepsFor</c> skipping <see cref="LoadPart.Map3D"/> on a catch-up ⇒ both holds release at once and <c>Requests</c> stays 0.
    /// </summary>
    [Theory]
    [InlineData(true,  false)]   // the Player: its profile always waits
    [InlineData(false, true)]    // any node the master's map-in-load set names
    public async Task CE3354_ALateJoinersCatchUp_HoldsUntilItsMapIsBuilt(bool profileAlways, bool mapInLoad)
    {
        var status = new FakeMapStatus { IsBuilt = false, Step = "3-D map: terrain" };
        var chain  = MapInLoadChain(status, profileAlways || mapInLoad);
        var catchUp = new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.PrepareLive, TransactionId = Guid.NewGuid(),
            DomainPayload = new EditLoadHandlerPayload(null, false, ClusterState.LoadingLive, IsCatchUp: true),
        };
        await chain.PrepareAsync(catchUp, CancellationToken.None);
        chain.Commit(catchUp, null);
        Assert.Equal(1, status.Requests);

        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.NotSame(hold, await Task.WhenAny(hold, Task.Delay(200)));
        Assert.Equal("3-D map: terrain", chain.CurrentLoadStep);

        status.IsBuilt = true;
        chain.DrainDeferredAcks();
        await hold;
    }

    /// <summary>⭐ A requested profile part with no provider throws at composition, like a role part.</summary>
    [Fact]
    public void CE4078_AProfilePartWithNoProvider_ThrowsAtComposition()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LoadPhaseChain.FromRoles(
                NodeRole.Map2D,
                new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain) },
                world: null,
                profileParts: new[] { LoadPart.Map3D }));
        Assert.Contains("Map3D", ex.Message);
    }

    // ── ⭐ CE-11026 (R-11009, DESIGN_Linked_Client_Director §2d-1, §2e) — the load waits for the node's LINKED CLIENT ─────────────────────────────

    /// <summary>A linked client for the rails: says what the test tells it, and records every call in order.</summary>
    private sealed class FakeLinkedClient : ILinkedClient
    {
        public List<string>? Order { get; init; }
        public string Name => "fake";
        public bool Ready { get; set; }
        public string? Failure { get; set; }
        public string? Step { get; set; }
        public List<Guid> Begun { get; } = new();
        public int Polls { get; private set; }
        public int PollsBeforeBegin { get; private set; }
        public void BeginWorld(Guid loadId) { Begun.Add(loadId); Order?.Add("begin"); }
        public bool IsWorldReady(Guid loadId, out string? failure, out string? step)
        {
            Polls++;
            if (Begun.Count == 0) PollsBeforeBegin++;
            failure = Failure; step = Step;
            return Ready;
        }
    }

    private static LoadPhaseChain LinkedChain(LinkedClients clients, List<string>? order = null)
        => LoadPhaseChain.FromRoles(
            NodeRole.Map2D,
            new ILoadPartProvider[]
            {
                new FakeStep(LoadPart.KnowledgeBase),
                new FakeStep(LoadPart.Terrain) { OnCommit = _ => order?.Add("terrain") },
                new LinkedClientLoadStep(clients),
            },
            world: null,
            profileParts: new[] { LoadPart.LinkedClient });

    private static async Task<bool> Completes(Task hold) => ReferenceEquals(hold, await Task.WhenAny(hold, Task.Delay(300)));

    /// <summary>a — no client offered: the step resolves at once and asks nobody; the load takes exactly as many polls as a chain without the step.</summary>
    [Fact]
    public async Task CE11026_NoLinkedClient_ResolvesAtOnce_AndTheLoadsTimingIsUnchanged()
    {
        var clients = new LinkedClients();
        var with = LinkedChain(clients);
        var without = LoadPhaseChain.FromRoles(NodeRole.Map2D, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), new FakeStep(LoadPart.Terrain) }, world: null);

        foreach (var chain in new[] { with, without })
        {
            var load = LoadIntent();
            await chain.PrepareAsync(load, CancellationToken.None);
            chain.Commit(load, null);
            var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
            chain.DrainDeferredAcks();                       // ONE poll
            Assert.True(await Completes(hold), "the load did not complete after one poll");
        }

        Assert.Equal(new[] { LoadPart.KnowledgeBase, LoadPart.Terrain, LoadPart.LinkedClient }, with.Parts);
    }

    /// <summary>
    /// b — a client is told exactly once per load, with the load's transaction id, at the commit and after the terrain step's; the load holds until the client says ready, fails with exactly the client's reason, and reports the client's step.
    /// 🔴 Mutations (each compiles): <c>BeginWorld</c> skipped ⇒ <c>Begun</c> empty; called in prepare / per poll ⇒ the order or count cells; failure ignored ⇒ the failing load holds for ever.
    /// </summary>
    [Fact]
    public async Task CE11026_ALinkedClient_IsToldOncePerLoadAtTheCommit_AndTheLoadHoldsUntilItsWorldIsReady()
    {
        var order = new List<string>();
        var client = new FakeLinkedClient { Order = order, Step = "terrain city-battle" };
        var clients = new LinkedClients();
        clients.Offer(client);
        var chain = LinkedChain(clients, order);
        var load = LoadIntent();

        await chain.PrepareAsync(load, CancellationToken.None);
        Assert.Empty(client.Begun);                          // not at prepare
        chain.Commit(load, null);
        Assert.Equal(new[] { "terrain", "begin" }, order);   // after the terrain commit
        Assert.Equal(new[] { load.TransactionId }, client.Begun);

        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.False(await Completes(hold));                 // not ready: the load holds
        Assert.Equal("terrain city-battle", chain.CurrentLoadStep);
        chain.DrainDeferredAcks();
        Assert.Equal(new[] { load.TransactionId }, client.Begun);   // polling does not tell it again

        client.Ready = true;
        chain.DrainDeferredAcks();
        Assert.True(await Completes(hold));
    }

    [Fact]
    public async Task CE11026_AClientsFailure_FailsTheLoadWithExactlyItsReason()
    {
        var client = new FakeLinkedClient { Step = "terrain" };
        var clients = new LinkedClients();
        clients.Offer(client);
        var chain = LinkedChain(clients);
        var load = LoadIntent();
        await chain.PrepareAsync(load, CancellationToken.None);
        chain.Commit(load, null);
        var hold = chain.PrepareAsync(Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);
        chain.DrainDeferredAcks();
        Assert.False(hold.IsCompleted);

        client.Failure = "no such terrain on the client";
        chain.DrainDeferredAcks();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => hold);
        Assert.Contains("no such terrain on the client", ex.Message);
        Assert.Null(chain.CurrentLoadStep);
    }

    /// <summary>
    /// c — THE RACE (DESIGN §2d-1): a client that answers "ready" for anything until it is told a world begins. The step must never ask before its commit, so the old world can never be read as ready.
    /// 🔴 Mutation (compiles): <c>IsResolved</c> polling the current client without the commit's "asked" mark ⇒ <c>PollsBeforeBegin</c> is 1 and the step resolves before the commit.
    /// </summary>
    [Fact]
    public void CE11026_TheStepNeverAsksBeforeItsCommit_SoTheOldWorldIsNeverReadAsReady()
    {
        var client = new FakeLinkedClient { Ready = true };   // "ready" for the OLD world
        var clients = new LinkedClients();
        clients.Offer(client);
        var step = new LinkedClientLoadStep(clients);

        Assert.True(step.IsResolved(null));                    // nothing was asked yet: nothing to wait for …
        Assert.Equal(0, client.Polls);                         // … and the client was NOT asked (its answer would be about the old world)

        client.Ready = false;                                  // from the begin on it says not ready until its new world is there
        step.Commit(LoadContext(), null);
        Assert.Equal(1, client.Begun.Count);
        Assert.False(step.IsResolved(null));
        Assert.Equal(0, client.PollsBeforeBegin);
    }

    /// <summary>A client's failure ends the step's wait and is the step's <c>Failure</c>, exactly; before a failure it is neither.</summary>
    [Fact]
    public void CE11026_TheStep_ReportsAClientsFailure_AsResolvedAndAsItsFailure()
    {
        var client = new FakeLinkedClient { Step = "terrain" };
        var clients = new LinkedClients();
        clients.Offer(client);
        var step = new LinkedClientLoadStep(clients);
        step.Commit(LoadContext(), null);
        Assert.False(step.IsResolved(null));
        Assert.Null(step.Failure);
        Assert.Equal("terrain", step.ProgressStep);

        client.Failure = "no such terrain on the client";
        Assert.Equal("no such terrain on the client", step.Failure);
        Assert.True(step.IsResolved(null));
    }

    private static LoadPhaseContext LoadContext() => LoadPhaseContext.From(LoadIntent())!.Value;

    [Fact]
    public void CE11026_AnAbort_ForgetsTheAskedClient_SoNothingWaitsForAnAbortedLoad()
    {
        var client = new FakeLinkedClient();
        var clients = new LinkedClients();
        clients.Offer(client);
        var step = new LinkedClientLoadStep(clients);
        var context = LoadContext();
        step.Commit(context, null);
        Assert.False(step.IsResolved(null));

        step.Abort(context);

        Assert.True(step.IsResolved(null));
        Assert.Null(step.Failure);
        Assert.Null(step.ProgressStep);
    }

    /// <summary>e — HOT PATH: a node with no linked client polls the step every frame of a load; it allocates nothing.</summary>
    [Fact]
    public void CE11026_IsResolved_WithNoLinkedClient_AllocatesNothing()
    {
        var step = new LinkedClientLoadStep(new LinkedClients());
        for (int i = 0; i < 100; i++) step.IsResolved(null);   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) step.IsResolved(null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void CE11026_TheRegistryHoldsOneClient_ASecondOfferReplacesIt_AndAWithdrawOfAReplacedClientIsIgnored()
    {
        var registry = new LinkedClients();
        var a = new FakeLinkedClient();
        var b = new FakeLinkedClient();
        Assert.Null(registry.Current);
        registry.Offer(a);
        Assert.Same(a, registry.Current);
        registry.Offer(b);
        Assert.Same(b, registry.Current);
        registry.Withdraw(a);                       // a was replaced: its withdraw must not remove b
        Assert.Same(b, registry.Current);
        registry.Withdraw(b);
        Assert.Null(registry.Current);
    }

    private sealed class FakeStep : ILoadPartProvider
    {
        private readonly List<LoadPart>? _order;

        public FakeStep(LoadPart part, List<LoadPart>? order = null)
        {
            Part   = part;
            _order = order;
        }

        public LoadPart Part { get; }
        public bool Resolved { get; set; } = true;
        public bool Aborted  { get; private set; }

        public LoadPhaseContext? Prepared { get; private set; }   // ⭐ CE-11025 — the context the step was prepared with

        public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
        {
            Prepared = context;
            _order?.Add(Part);
            OnPrepare?.Invoke();
            return Task.CompletedTask;
        }

        public Action? OnPrepare { get; init; }

        public Action<EntityRepository?>? OnCommit { get; init; }

        public void Commit(LoadPhaseContext context, EntityRepository? world) => OnCommit?.Invoke(world);
        public void Abort(LoadPhaseContext context) => Aborted = true;
        public bool IsResolved(EntityRepository? world) => Resolved;
        public string? FailWith { get; set; }
        public string? Failure => FailWith;
    }

    /// <summary>
    /// ⭐ CE-11025 (Q113-J) — a replay open runs ONLY the world parts (terrain → map → linked client, never the knowledge base or the entities), the
    /// terrain prepared with the RECORDING's name and additions; the client is told after the terrain is in; the world is ready only when the map
    /// and the client are, and a client's failure fails the open with its reason.
    /// 🔴 Mutations: the KB in the replay parts ⇒ the order cell; the overlay dropped ⇒ the context cell; the drain not polling ⇒ the hold never completes.
    /// </summary>
    [Fact]
    public async Task CE11025_AReplayOpen_RunsTheWorldParts_ForTheRecordingsTerrain_AndIsReadyWhenTheMapAndClientAre()
    {
        var order = new List<LoadPart>();
        var commits = new List<string>();
        var kb = new FakeStep(LoadPart.KnowledgeBase, order) { OnCommit = _ => commits.Add("kb") };
        var terrain = new FakeStep(LoadPart.Terrain, order) { OnCommit = _ => commits.Add("terrain") };
        var map = new FakeStep(LoadPart.Map3D, order) { OnCommit = _ => commits.Add("map"), Resolved = false };
        var client = new FakeLinkedClient();
        var clients = new LinkedClients();
        clients.Offer(client);
        var chain = LoadPhaseChain.FromRoles(NodeRole.Map2D, new ILoadPartProvider[] { kb, terrain, map, new LinkedClientLoadStep(clients) }, world: null,
                                             profileParts: new[] { LoadPart.Map3D, LoadPart.LinkedClient });
        Fdp.Toolkit.Orchestration.Handlers.IReplayWorldLoad replay = chain;
        var id = Guid.NewGuid();

        await replay.PrepareAsync(id, "valley", "ab12", CancellationToken.None);
        Assert.Equal(new[] { LoadPart.Terrain, LoadPart.Map3D }, order);
        Assert.Equal(("valley", "ab12", true), (terrain.Prepared!.Value.TerrainName, terrain.Prepared!.Value.OverlayHash, terrain.Prepared!.Value.IsReplayWorld));

        replay.Commit(id, null);
        Assert.Equal(new[] { "terrain", "map" }, commits);
        Assert.Equal(new[] { id }, client.Begun);
        var ready = replay.WhenReadyAsync(id);
        chain.DrainDeferredAcks();
        Assert.False(ready.IsCompleted);                      // the map is building
        map.Resolved = true;
        chain.DrainDeferredAcks();
        Assert.False(ready.IsCompleted);                      // the client's world is not ready
        client.Ready = true;
        chain.DrainDeferredAcks();
        Assert.True(await Completes(ready));

        // a client that fails fails the open with its reason
        var id2 = Guid.NewGuid();
        await replay.PrepareAsync(id2, "valley", null, CancellationToken.None);
        replay.Commit(id2, null);
        client.Ready = false; client.Failure = "the client has no such terrain";
        var failing = replay.WhenReadyAsync(id2);
        chain.DrainDeferredAcks();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        Assert.Contains("the client has no such terrain", ex.Message);
    }
}
