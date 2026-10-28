using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
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

        public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
        {
            _order?.Add(Part);
            OnPrepare?.Invoke();
            return Task.CompletedTask;
        }

        public Action? OnPrepare { get; init; }

        public Action<EntityRepository?>? OnCommit { get; init; }

        public void Commit(LoadPhaseContext context, EntityRepository? world) => OnCommit?.Invoke(world);
        public void Abort(LoadPhaseContext context) => Aborted = true;
        public bool IsResolved(EntityRepository? world) => Resolved;
    }
}
