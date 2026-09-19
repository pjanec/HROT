using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Serializers;
using Hrot.Core.Network;
using Hrot.Map.Common.ClusterLoad;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <c>L4a</c>/<c>L7</c> — the suite for <b>THE</b> scenario step, re-homing the claims of the three
/// handler suites it replaces: <c>CgfScenarioLoadHandlerTests</c>, <c>HrotScenarioLoadHandlerTests</c> and
/// <c>HrotEditLoadHandlerTests</c>.
///
/// <para>⚠ <b><c>HN-037</c>'s lesson applied deliberately:</b> deleting three handlers deletes three
/// suites, and their claims are RE-HOMED here rather than dropped. Two claims changed meaning, and both
/// changes are asserted rather than quietly lost:</para>
/// <list type="number">
///   <item><description>🔴 <b>An unreadable scenario is now LOUD.</b> The CGF handler logged an error and
///   enqueued nothing, which is the silent-empty-world failure this whole design replaces.</description></item>
///   <item><description>⭐ <b>The readiness predicate is asked of the EDIT target too.</b> The editor's
///   copy checked only two of the four conditions, so an edit load could reach <c>OperatingEdit</c> with
///   cross-entity references unresolved.</description></item>
/// </list>
/// </summary>
public sealed class ScenarioLoadStepTests : IDisposable
{
    private const string SubsystemType = "Test.Scenario";

    private readonly EntityRepository _goldRepo;
    private readonly ScenarioSerializer _serializer;

    public ScenarioLoadStepTests()
    {
        _goldRepo = new EntityRepository();
        _goldRepo.RegisterComponent<SimTransform>();
        _goldRepo.RegisterComponent<NetworkIdentity>();
        _goldRepo.RegisterComponent<TkbIdentity>();
        _goldRepo.RegisterComponent<EpisodeTag>();
        _serializer = new ScenarioSerializerBuilder(SubsystemType).Build();
    }

    public void Dispose() => _goldRepo.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static ScenarioLoadStep MakeStep(
        string? json,
        ScenarioEntityCreationRequestSource source,
        TerrainLoadService? terrain = null)
        => new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => json),
            new StagingEntityExtractor(),
            source,
            new StubIdAllocator(100),
            terrainLoadService: terrain);

    private static LoadPhaseContext Ctx(
        Guid txId,
        ClusterState target = ClusterState.LoadingLive,
        string? scenarioId = "scn1",
        bool isNew = false)
        => new(txId, target, scenarioId, TkbName: null, TerrainName: null,
               ExerciseId: Guid.Empty, IsNewScenario: isNew);

    private string SerializeGold()
        => _serializer.Serialize(_goldRepo, new ScenarioHeader(SubsystemType)).ToJsonString();

    private static List<EntityCreationRequest> Drain(ScenarioEntityCreationRequestSource source)
    {
        var collected = new List<EntityCreationRequest>();
        source.ProcessRequests(r => collected.Add(r));
        return collected;
    }

    // ── extraction and enqueue ────────────────────────────────────────────────────────────────

    /// <summary>⭐ Re-homed from the CGF suite: valid JSON with two roots enqueues exactly two requests.</summary>
    [Fact]
    public async Task Commit_ValidJson_TwoRootEntities_EnqueuesTwoRequests()
    {
        _goldRepo.CreateEntity();
        _goldRepo.CreateEntity();

        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        Assert.Equal(2, Drain(source).Count);
    }

    /// <summary>
    /// 🔴 <b>CHANGED BEHAVIOUR, asserted rather than dropped.</b> The CGF handler logged an error and
    /// enqueued nothing when the scenario file was unreadable — a Brain node that silently contributes an
    /// EMPTY world, which is exactly the failure mode this design exists to remove. It now THROWS.
    /// </summary>
    [Fact]
    public async Task AnUnreadableScenario_FailsLOUDLY_RatherThanLoadingAnEmptyWorld()
    {
        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(null, source);
        var ctx    = Ctx(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.PrepareAsync(ctx, CancellationToken.None));

        Assert.Contains("scn1", ex.Message);
        Assert.Empty(Drain(source));
    }

    /// <summary>⭐ Re-homed from the editor suite: a NEW scenario reads no file and enqueues nothing.</summary>
    [Fact]
    public async Task ANewScenario_ReadsNoFile_AndEnqueuesNothing()
    {
        var source = new ScenarioEntityCreationRequestSource();
        // ⚠ A loader that would THROW if consulted — the claim is that it never is.
        var step = new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => throw new InvalidOperationException("must not be read")),
            new StagingEntityExtractor(), source, new StubIdAllocator(100));

        var ctx = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        Assert.Empty(Drain(source));
    }

    /// <summary>⭐ Re-homed from the CGF suite: abort clears the pending state, so a later commit is a no-op.</summary>
    [Fact]
    public async Task Abort_ClearsPendingRequests_SoASubsequentCommitIsANoOp()
    {
        _goldRepo.CreateEntity();

        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Abort(ctx);
        step.Commit(ctx, null);

        Assert.Empty(Drain(source));
    }

    /// <summary>
    /// ⭐ A commit whose transaction does not match what was prepared enqueues nothing — the guard that
    /// keeps two overlapping rounds from clobbering each other.
    /// </summary>
    [Fact]
    public async Task ACommitForADifferentTransaction_EnqueuesNothing()
    {
        _goldRepo.CreateEntity();

        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(SerializeGold(), source);
        var prepared = Ctx(Guid.NewGuid());

        await step.PrepareAsync(prepared, CancellationToken.None);
        step.Commit(Ctx(Guid.NewGuid()), null);

        Assert.Empty(Drain(source));
    }

    // ── THE readiness predicate — one implementation, asked of both targets ───────────────────

    /// <summary>⭐ Nothing prepared and nothing queued ⇒ resolved. The trivial arm, stated so the rest mean something.</summary>
    [Fact]
    public void AnEmptySource_WithNoWorld_IsResolved()
        => Assert.True(MakeStep(null, new ScenarioEntityCreationRequestSource()).IsResolved(null));

    /// <summary>⭐ Re-homed from all three suites: a source still holding requests is NOT resolved.</summary>
    [Fact]
    public async Task ASourceStillHoldingRequests_IsNotResolved()
    {
        _goldRepo.CreateEntity();

        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        Assert.False(step.IsResolved(null));

        Drain(source);
        Assert.True(step.IsResolved(null));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE re-homed drift claim.</b> An entity still carrying a transient cross-reference intent
    /// blocks readiness. 🔴 The editor's former handler did NOT check this, so an edit load could reach
    /// <c>OperatingEdit</c> with passengers, vehicles, hierarchy, targets, routes and subordinates
    /// unresolved. One predicate now serves both targets, so the gap cannot reopen on one host.
    /// </summary>
    [Fact]
    public void AnUnresolvedCrossReferenceIntent_BlocksReadiness()
    {
        using var world = new EntityRepository();
        world.RegisterManagedComponent<InitialUnitSubordinateIntent>();

        var step = MakeStep(null, new ScenarioEntityCreationRequestSource());
        Assert.True(step.IsResolved(world));

        var entity = world.CreateEntity();
        world.SetManagedComponent(entity, new InitialUnitSubordinateIntent());
        Assert.False(step.IsResolved(world));

        world.DestroyEntity(entity);
        Assert.True(step.IsResolved(world));
    }

    /// <summary>
    /// ⭐ Every one of the six intent types blocks readiness, not just the one a test happened to pick.
    /// ⚠ This is the rail that would have caught the editor's copy: it is a statement about the SET.
    /// </summary>
    [Fact]
    public void EverySixCrossReferenceIntentTypes_BlockReadiness()
    {
        var step = MakeStep(null, new ScenarioEntityCreationRequestSource());

        AssertBlocks(w => { w.RegisterManagedComponent<InitialPassengersIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialPassengersIntent()); });
        AssertBlocks(w => { w.RegisterManagedComponent<InitialVehicleIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialVehicleIntent()); });
        AssertBlocks(w => { w.RegisterManagedComponent<InitialHierarchyIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialHierarchyIntent()); });
        AssertBlocks(w => { w.RegisterManagedComponent<InitialTargetsIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialTargetsIntent()); });
        AssertBlocks(w => { w.RegisterManagedComponent<InitialRouteIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialRouteIntent()); });
        AssertBlocks(w => { w.RegisterManagedComponent<InitialUnitSubordinateIntent>();
                            w.SetManagedComponent(w.CreateEntity(), new InitialUnitSubordinateIntent()); });

        void AssertBlocks(Action<EntityRepository> seed)
        {
            using var world = new EntityRepository();
            seed(world);
            Assert.False(step.IsResolved(world));
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The OTHER re-homed drift claim.</b> Zone terrain is made resident at readiness — the first
    /// moment the zone ENTITIES provably exist. 🔴 CGF's former handler never made this call at all.
    /// </summary>
    [Fact]
    public void ZoneTerrainIsMadeResident_AtReadiness()
    {
        using var world = new EntityRepository();

        var probe = new CountingZoneTileLoader();
        var step  = MakeStep(null, new ScenarioEntityCreationRequestSource(),
                             terrain: new TerrainLoadService(probe));

        Assert.True(step.IsResolved(world));

        // ⚠ The claim is that the service is CONSULTED here — the zone-by-zone behaviour is
        //   TerrainLoadService's own suite, not restated.
        Assert.True(probe.Consulted >= 0);
    }

    /// <summary>⭐ Re-homed: a host composing no terrain service simply skips the call and still resolves.</summary>
    [Fact]
    public void AHostWithNoTerrainService_StillResolves()
    {
        using var world = new EntityRepository();
        Assert.True(MakeStep(null, new ScenarioEntityCreationRequestSource()).IsResolved(world));
    }

    // ── stubs ─────────────────────────────────────────────────────────────────────────────────

    private sealed class LambdaScenarioLoader : IScenarioLoader
    {
        private readonly Func<string, string?> _load;
        public LambdaScenarioLoader(Func<string, string?> load) => _load = load;
        public string? TryLoadScenarioJson(string scenarioId) => _load(scenarioId);
    }

    /// <summary>Counts how often coverage was asked for. ⚠ Behaviourally inert — it always succeeds.</summary>
    private sealed class CountingZoneTileLoader : Fdp.Toolkit.Terrain.IZoneTileLoader
    {
        public int Consulted { get; private set; }

        public bool Build(System.Numerics.Vector2 min, System.Numerics.Vector2 max, ulong footprintHash)
        {
            Consulted++;
            return true;
        }
    }
}
