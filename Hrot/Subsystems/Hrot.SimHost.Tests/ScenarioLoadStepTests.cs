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

    // ── Buildings Stage 5b — the terrain's doors become entities at commit ────────────────────

    private const string TwoDoorHouse = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H", "doors": { "front": "locked" },
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "openings": [ { "kind": "door", "at": 4.5, "width": 1, "doorId": "front" } ] },
                  { "from": [5,0], "to": [5,8], "thickness": 0.15, "openings": [ { "kind": "door", "at": 6, "width": 0.9, "doorId": "hall" } ] } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static EntityRepository TerrainRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<Fdp.Toolkit.Terrain.DoorState>();
        repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();
        repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(TwoDoorHouse, "range"));
        return repo;
    }

    /// <summary>
    /// ⭐ Stage 5b (📄 docs/DESIGN_Building_Interiors.md §3b K1/K3/K5, §3j) — a NEW scenario still gets the terrain's doors: one
    /// transient <c>Door</c> request per door, in KEY order, ids from the one allocator, carrying the door's live state, its key
    /// and its position.
    /// </summary>
    [Fact]
    public async Task Stage5b_ANewScenario_GetsOneDoorEntityPerTerrainDoor_InKeyOrder()
    {
        using var world = TerrainRepo();
        var source = new ScenarioEntityCreationRequestSource();
        var step = new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => throw new InvalidOperationException("must not be read")),
            new StagingEntityExtractor(), source, new StubIdAllocator(100));
        var ctx = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        var doors = Drain(source);
        Assert.Equal(new[] { "range/H/front", "range/H/hall" },
            doors.Select(r => r.InitialComponents!.OfType<Fdp.Toolkit.Terrain.TerrainObjectKey>().Single().Key));
        Assert.All(doors, r => Assert.Equal(Hrot.Map.Common.TkbEntityTypes.Door, r.TkbType));
        Assert.All(doors, r => Assert.True(r.IsTransient));                     // K5 — never written into a scenario
        Assert.Equal(new long[] { 100, 101 }, doors.Select(r => r.PreAllocatedNetworkId));   // K1 — the one allocator
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Locked,
            doors[0].InitialComponents!.OfType<Fdp.Toolkit.Terrain.DoorState>().Single().State);   // the instance override
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open,
            doors[1].InitialComponents!.OfType<Fdp.Toolkit.Terrain.DoorState>().Single().State);
        var at = doors[0].InitialComponents!.OfType<SimTransform>().Single().Position;
        Assert.Equal(25f, at.X, 2); Assert.Equal(20f, at.Y, 2);                  // the doorway's centre
    }

    /// <summary>⭐ Stage 5b — the doors follow the scenario's own requests (K3), and a door that already has an entity is never doubled.</summary>
    [Fact]
    public async Task Stage5b_DoorsFollowTheScenariosEntities_AndAnExistingDoorIsNotCreatedTwice()
    {
        _goldRepo.CreateEntity();
        using var world = TerrainRepo();
        var existing = world.CreateEntity();
        world.SetManagedComponent(existing, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = "range/H/hall" });

        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        var all = Drain(source);
        Assert.Equal(2, all.Count);
        Assert.NotEqual(Hrot.Map.Common.TkbEntityTypes.Door, all[0].TkbType);   // the scenario's entity first
        Assert.Equal("range/H/front", all[1].InitialComponents!.OfType<Fdp.Toolkit.Terrain.TerrainObjectKey>().Single().Key);
    }

    /// <summary>
    /// ⭐⭐ Stage 5e (📄 docs/DESIGN_Building_Interiors.md §3b K5, §3j "5e") — a door's state survives save → load. The saving world
    /// opened the authored-LOCKED front door; the save writes it to <c>TerrainObjects</c>; the load creates the door entity in that
    /// state, while the untouched hall door starts as the terrain authored it.
    /// </summary>
    [Fact]
    public async Task Stage5e_ADoorsStateSurvivesSaveAndLoad_AnUntouchedDoorStartsAsAuthored()
    {
        using var saving = TerrainRepo();
        saving.RegisterComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>();
        foreach (var (key, state) in new[] { ("range/H/front", Fdp.Toolkit.Terrain.TerrainDoorState.Open), ("range/H/hall", Fdp.Toolkit.Terrain.TerrainDoorState.Open) })
        {
            var e = saving.CreateEntity();
            saving.AddComponent(e, new Fdp.Toolkit.Terrain.DoorState { State = state });
            saving.SetManagedComponent(e, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = key });
            saving.AddComponent(e, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
        }
        string json = _serializer.Serialize(saving, new ScenarioHeader(SubsystemType)).ToJsonString();
        Assert.Contains("\"TerrainObjects\"", json);

        using var loading = TerrainRepo();
        var source = new ScenarioEntityCreationRequestSource();
        var step   = MakeStep(json, source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, loading);

        var doors = Drain(source).ToDictionary(
            r => r.InitialComponents!.OfType<Fdp.Toolkit.Terrain.TerrainObjectKey>().Single().Key,
            r => r.InitialComponents!.OfType<Fdp.Toolkit.Terrain.DoorState>().Single().State);
        Assert.Equal(2, doors.Count);                                                       // K5 — no door came in as a scenario entity
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, doors["range/H/front"]);    // the saved state, not the authored lock
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, doors["range/H/hall"]);     // authored open, never written
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
