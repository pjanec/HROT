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
    /// ⭐ Terrain is ROLE-derived, and only the two roles with a measured road-graph consumer require it.
    /// 🔒 User: <i>"load nothing where nothing reads it."</i>
    /// </summary>
    [Theory]
    [InlineData(NodeRole.MuscleGround,     true)]
    [InlineData(NodeRole.NavigationSolver, true)]
    [InlineData(NodeRole.Brain,            false)]
    [InlineData(NodeRole.Perception,       false)]
    [InlineData(NodeRole.Map2D,            false)]
    public void TerrainIsRequiredOnlyWhereSomethingReadsIt(NodeRole role, bool required)
        => Assert.Equal(required, RoleLoadRequirements.PartsFor(role).Contains(LoadPart.Terrain));

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

        Assert.Equal(new[] { LoadPart.KnowledgeBase }, chain.Parts);
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
            NodeRole.Map2D, new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase) }, world: null);

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
            new ILoadPartProvider[] { new FakeStep(LoadPart.KnowledgeBase), blocker },
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
            NodeRole.Map2D, new ILoadPartProvider[] { a }, world: null);

        var hold = chain.PrepareAsync(
            Intent(NodeOpType.PrepareState, ClusterState.OperatingLive), CancellationToken.None);

        chain.Abort(LoadIntent(), null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hold);
        Assert.True(a.Aborted);
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
            return Task.CompletedTask;
        }

        public void Commit(LoadPhaseContext context, EntityRepository? world) { }
        public void Abort(LoadPhaseContext context) => Aborted = true;
        public bool IsResolved(EntityRepository? world) => Resolved;
    }
}
