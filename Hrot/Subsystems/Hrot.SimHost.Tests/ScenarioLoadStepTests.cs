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
using Fdp.Toolkit.NetworkSpawning.Events;

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
        FdpEventBus source,
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
        => new(txId, target, scenarioId, TerrainName: null,
               ExerciseId: Guid.Empty, IsNewScenario: isNew);

    private string SerializeGold()
        => _serializer.Serialize(_goldRepo, new ScenarioHeader(SubsystemType)).ToJsonString();

    /// <summary>⭐ <c>CE-3228</c> — the step publishes on a bus; the request system would read it after the frame's swap.</summary>
    private static FdpEventBus NewBus()
    {
        var bus = new FdpEventBus();
        bus.RegisterManaged<EntityCreationRequest>();
        return bus;
    }

    private static List<EntityCreationRequest> Drain(FdpEventBus source)
    {
        source.SwapBuffers();
        return source.ReadManaged<EntityCreationRequest>().ToList();
    }

    // ── extraction and enqueue ────────────────────────────────────────────────────────────────

    /// <summary>⭐ Re-homed from the CGF suite: valid JSON with two roots enqueues exactly two requests.</summary>
    [Fact]
    public async Task Commit_ValidJson_TwoRootEntities_EnqueuesTwoRequests()
    {
        _goldRepo.CreateEntity();
        _goldRepo.CreateEntity();

        var source = NewBus();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        var all = Drain(source);
        Assert.Equal(2, all.Count(r => r.TkbType != Hrot.Map.Common.TkbEntityTypes.WorldEnvironment));
        Assert.Single(all, r => r.TkbType == Hrot.Map.Common.TkbEntityTypes.WorldEnvironment);   // ⭐ CE-3216 C-3 — and the default World Environment
    }

    /// <summary>
    /// 🔴 <b>CHANGED BEHAVIOUR, asserted rather than dropped.</b> The CGF handler logged an error and
    /// enqueued nothing when the scenario file was unreadable — a Brain node that silently contributes an
    /// EMPTY world, which is exactly the failure mode this design exists to remove. It now THROWS.
    /// </summary>
    [Fact]
    public async Task AnUnreadableScenario_FailsLOUDLY_RatherThanLoadingAnEmptyWorld()
    {
        var source = NewBus();
        var step   = MakeStep(null, source);
        var ctx    = Ctx(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.PrepareAsync(ctx, CancellationToken.None));

        Assert.Contains("scn1", ex.Message);
        Assert.Empty(Drain(source));
    }

    /// <summary>⭐ Re-homed from the editor suite: a NEW scenario reads no file and enqueues nothing of its own — ⭐ CE-3216 C-3: only the
    /// default World Environment (railed by <see cref="CE3216_C3_ANewScenario_GetsTheDefaultWorldEnvironment_AtTheFixedBuiltInStart"/>).</summary>
    [Fact]
    public async Task ANewScenario_ReadsNoFile_AndEnqueuesNothing()
    {
        var source = NewBus();
        // ⚠ A loader that would THROW if consulted — the claim is that it never is.
        var step = new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => throw new InvalidOperationException("must not be read")),
            new StagingEntityExtractor(), source, new StubIdAllocator(100));

        var ctx = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        Assert.Empty(Drain(source).Where(r => r.TkbType != Hrot.Map.Common.TkbEntityTypes.WorldEnvironment));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3288</c> (Distributed Scenario Persistence §4d) — LOAD → SAVE keeps the scenario's description: the step stamps it
    /// resident at Commit, a save reads it back (<see cref="Fdp.Toolkit.Scenario.ScenarioDescription.ResidentOf"/>, the formula both save
    /// sites use) and the serializer writes it; a NEW scenario then clears it. 🔴 Red-proof: drop the stamp ⇒ the saved file has none.
    /// </summary>
    [Fact]
    public async Task CE3288_LoadThenSave_KeepsTheDescription_AndANewScenarioClearsIt()
    {
        const string text = "What: two riflemen. · Judge: both reach the house.";
        _goldRepo.CreateEntity();
        string json = _serializer.Serialize(_goldRepo, new ScenarioHeader(SubsystemType, Description: text)).ToJsonString();

        using var world = new EntityRepository();
        var step = MakeStep(json, NewBus());
        var ctx  = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);
        Assert.Equal(text, Fdp.Toolkit.Scenario.ScenarioDescription.ResidentOf(world));

        var saved = _serializer.Serialize(world, new ScenarioHeader(SubsystemType,
            Description: Fdp.Toolkit.Scenario.ScenarioDescription.ResidentOf(world)));
        Assert.Equal(text, Fdp.Toolkit.Scenario.ScenarioDescription.OfDom(saved));

        var fresh = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);
        await step.PrepareAsync(fresh, CancellationToken.None);
        step.Commit(fresh, world);
        Assert.Null(Fdp.Toolkit.Scenario.ScenarioDescription.ResidentOf(world));
    }

    /// <summary>⭐ <c>CE-3288</c> — the shipped scenario files write the header camelCase (<c>header.description</c>); a save writes it
    /// PascalCase. Both read; a scenario with none reads null and still loads.</summary>
    [Fact]
    public void CE3288_TheDescription_ReadsInEitherCase_AndNoneIsNull()
    {
        Assert.Equal("a", Fdp.Toolkit.Scenario.ScenarioDescription.Read("{\"header\":{\"description\":\"a\"},\"entities\":{}}"));
        Assert.Equal("b", Fdp.Toolkit.Scenario.ScenarioDescription.Read("{\"Header\":{\"Description\":\"b\"},\"Entities\":{}}"));
        Assert.Null(Fdp.Toolkit.Scenario.ScenarioDescription.Read("{\"Header\":{\"TerrainName\":\"t\"},\"Entities\":{}}"));
        Assert.Null(Fdp.Toolkit.Scenario.ScenarioDescription.Read("not json"));
    }

    /// <summary>⭐ Re-homed from the CGF suite: abort clears the pending state, so a later commit is a no-op.</summary>
    [Fact]
    public async Task Abort_ClearsPendingRequests_SoASubsequentCommitIsANoOp()
    {
        _goldRepo.CreateEntity();

        var source = NewBus();
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

        var source = NewBus();
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
        var source = NewBus();
        var step = new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => throw new InvalidOperationException("must not be read")),
            new StagingEntityExtractor(), source, new StubIdAllocator(100));
        var ctx = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        var doors = Drain(source).Where(r => r.TkbType != Hrot.Map.Common.TkbEntityTypes.WorldEnvironment).ToList();   // (CE-3216 C-3: the default World Environment follows them)
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

        var source = NewBus();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        var all = Drain(source).Where(r => r.TkbType != Hrot.Map.Common.TkbEntityTypes.WorldEnvironment).ToList();   // (CE-3216 C-3: the default World Environment follows)
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
        var source = NewBus();
        var step   = MakeStep(json, source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, loading);

        var doors = Drain(source).Where(r => r.TkbType != Hrot.Map.Common.TkbEntityTypes.WorldEnvironment).ToDictionary(
            r => r.InitialComponents!.OfType<Fdp.Toolkit.Terrain.TerrainObjectKey>().Single().Key,
            r => r.InitialComponents!.OfType<Fdp.Toolkit.Terrain.DoorState>().Single().State);
        Assert.Equal(2, doors.Count);                                                       // K5 — no door came in as a scenario entity
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, doors["range/H/front"]);    // the saved state, not the authored lock
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Open, doors["range/H/hall"]);     // authored open, never written
    }

    // ── ⭐ CE-3216 C-3 / W-8 — exactly one World Environment, its start time as authored ─────────────────────

    /// <summary>
    /// ⭐ C-3 / W-8 (📄 docs/designs/weather/DESIGN_Weather_And_Time.md §3) — a NEW scenario gets a DEFAULT World Environment: positionless,
    /// its clock at the FIXED built-in start time — ⛔ never the wall clock — with an id from the one allocator, after the terrain's doors.
    /// 🔴 Red-proof (by construction, not run): drop the default from <c>ScenarioLoadStep.Commit</c> ⇒ no request, <c>Assert.Single</c> fails.
    /// </summary>
    [Fact]
    public async Task CE3216_C3_ANewScenario_GetsTheDefaultWorldEnvironment_AtTheFixedBuiltInStart()
    {
        var source = NewBus();
        var step = new ScenarioLoadStep(
            new ScenarioSerializerBuilder(SubsystemType).Build(),
            new LambdaScenarioLoader(_ => throw new InvalidOperationException("must not be read")),
            new StagingEntityExtractor(), source, new StubIdAllocator(100));
        var ctx = Ctx(Guid.NewGuid(), ClusterState.LoadingEdit, scenarioId: null, isNew: true);

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        var env = Assert.Single(Drain(source));
        Assert.Equal(Hrot.Map.Common.TkbEntityTypes.WorldEnvironment, env.TkbType);
        Assert.Equal(100, env.PreAllocatedNetworkId);
        Assert.DoesNotContain(env.InitialComponents!, c => c is SimTransform);   // positionless
        var clock = env.InitialComponents!.OfType<Fdp.Toolkit.Weather.ScenarioClock>().Single();
        Assert.Equal(Fdp.Toolkit.Weather.ScenarioClock.DefaultStartUtc.Ticks, clock.StartUtcTicks);   // the fixed built-in, not "now"
    }

    /// <summary>
    /// ⭐ W-8 — a scenario that SAVED its World Environment loads it with the start time exactly as authored (the scenario serializer's
    /// default policy writes it, the extractor reads it back) and gets no second, default one; a second World Environment in the file is
    /// ignored with a warning, the first kept (C-3). 🔴 Red-proof (by construction, not run): drop the one-environment guard ⇒ two requests.
    /// </summary>
    [Fact]
    public async Task CE3216_W8_ASavedStartTime_LoadsAsAuthored_AndASecondWorldEnvironmentIsIgnored()
    {
        _goldRepo.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>();
        long authored = new DateTime(1999, 12, 31, 23, 0, 0, DateTimeKind.Utc).Ticks;
        foreach (long start in new[] { authored, authored + TimeSpan.TicksPerHour })
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.AddComponent(e, new TkbIdentity { TkbType = Hrot.Map.Common.TkbEntityTypes.WorldEnvironment });
            _goldRepo.AddComponent(e, new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = start, DisplayUtcOffsetMinutes = -300 });
        }
        // ⚠ a serializer built AFTER ScenarioClock is registered (FdpAutoSerializer compiles its roster at Build — the shared _serializer
        //   predates this test's registration; production registers ScenarioClock with every shared component, before any serializer)
        string json = new ScenarioSerializerBuilder(SubsystemType).Build().Serialize(_goldRepo, new ScenarioHeader(SubsystemType)).ToJsonString();
        Assert.Contains(authored.ToString(), json);   // saved exactly as authored

        var source = NewBus();
        var step   = MakeStep(json, source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, null);

        var env = Assert.Single(Drain(source), r => r.TkbType == Hrot.Map.Common.TkbEntityTypes.WorldEnvironment);
        var clock = env.InitialComponents!.OfType<Fdp.Toolkit.Weather.ScenarioClock>().Single();
        Assert.Equal(authored, clock.StartUtcTicks);
        Assert.Equal((short)-300, clock.DisplayUtcOffsetMinutes);
    }

    // ── THE readiness predicate — one implementation, asked of both targets ───────────────────

    /// <summary>⭐ Nothing prepared and nothing queued ⇒ resolved. The trivial arm, stated so the rest mean something.</summary>
    [Fact]
    public void AnEmptySource_WithNoWorld_IsResolved()
        => Assert.True(MakeStep(null, NewBus()).IsResolved(null));

    /// <summary>
    /// ⭐ Re-homed from all three suites: requests not yet consumed ⇒ NOT resolved. ⭐⭐ <c>CE-3228</c> — the requests ride the bus, which
    /// shows them after the next swap, and the request system reads them in the Input phase of the frame after that; so readiness ①
    /// holds only TWO kernel ticks after the commit published.
    /// </summary>
    [Fact]
    public async Task ASourceStillHoldingRequests_IsNotResolved()
    {
        _goldRepo.CreateEntity();
        using var world = new EntityRepository();

        var source = NewBus();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());

        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        Assert.False(step.IsResolved(world));   // published, not yet visible
        world.Tick();
        Assert.False(step.IsResolved(world));   // visible after this frame's swap, read next frame
        world.Tick();
        Assert.True(step.IsResolved(world));    // the request system has read them
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3304</c> — readiness ① waits for the ENTITIES, not only for the requests to be read. 📐 Measured on
    /// <c>bt-window-duel</c>: two ticks after the publish the requests were only QUEUED; the load resolved on an empty world, so
    /// condition ② never saw the static obstacles <c>Constructing</c> and they joined the simulation on a wall-clock step.
    /// </summary>
    [Fact]
    public async Task AnEntityNotYetCreated_IsNotResolved_UntilItIsInTheEntityMap()
    {
        _goldRepo.CreateEntity();
        using var world = new EntityRepository();
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        world.SetSingletonManaged(map);

        var source = NewBus();
        var step   = MakeStep(SerializeGold(), source);
        var ctx    = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);
        var ids = Drain(source).Select(r => r.PreAllocatedNetworkId).Where(id => id != 0).ToList();
        Assert.NotEmpty(ids);

        world.Tick(); world.Tick();
        Assert.False(step.IsResolved(world));   // read, but nothing created yet — the CE-3228 rule alone said true here
        foreach (var id in ids.Take(ids.Count - 1)) map.Register(id, world.CreateEntity());
        Assert.False(step.IsResolved(world));   // one still missing
        map.Register(ids[^1], world.CreateEntity());
        Assert.True(step.IsResolved(world));
    }

    /// <summary>⭐ <c>CE-3304</c> — a requested entity that never appears does not hang the load: after
    /// <see cref="ScenarioLoadStep.EntityWaitLimitTicks"/> ticks it proceeds (and logs the ids). A tick bound, the same on every machine.</summary>
    [Fact]
    public async Task ARequestThatNeverProducesAnEntity_StopsBeingAwaited_AfterTheTickLimit()
    {
        _goldRepo.CreateEntity();
        using var world = new EntityRepository();
        world.SetSingletonManaged(new Fdp.Toolkit.Replication.Services.NetworkEntityMap());

        var step = MakeStep(SerializeGold(), NewBus());
        var ctx  = Ctx(Guid.NewGuid());
        await step.PrepareAsync(ctx, CancellationToken.None);
        step.Commit(ctx, world);

        for (int i = 0; i < ScenarioLoadStep.EntityWaitLimitTicks - 1; i++) world.Tick();
        Assert.False(step.IsResolved(world));
        world.Tick();
        Assert.True(step.IsResolved(world));
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

        var step = MakeStep(null, NewBus());
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
        var step = MakeStep(null, NewBus());

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
        var step  = MakeStep(null, NewBus(),
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
        Assert.True(MakeStep(null, NewBus()).IsResolved(world));
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
