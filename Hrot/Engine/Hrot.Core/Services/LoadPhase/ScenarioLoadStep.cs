using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations.Adapters;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.Core.Network;
using Hrot.Map.Common.Services;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Hrot.Map.Common.ClusterLoad;

/// <summary>
/// ⭐⭐⭐ <c>L4a</c> — <b>THE scenario step. One implementation, for every host that loads a scenario.</b>
/// ⭐ Role-derived: required by <c>Brain</c> only, because <c>Brain</c> is the only role that also EDITS
/// and SAVES the scenario. Every other role receives a replicated world.
///
/// <para>⛔⛔ <b>It replaces THREE near-copies, and two of them had already drifted</b> — measured
/// <c>2026-09-18</c>, and this is the argument for collapsing them rather than reconciling them:</para>
///
/// <list type="table">
///   <item><description><c>HrotScenarioLoadHandler</c> (SimHost) — all four readiness conditions.</description></item>
///   <item><description><c>CgfScenarioLoadHandler</c> (CGF) — 🔴 <b>never made its zones' terrain
///   resident</b>, though the design places that call exactly here.</description></item>
///   <item><description><c>HrotEditLoadHandler</c> (editor + CGF edit) — 🔴 <b>missing the six
///   cross-reference intent conditions entirely</b>, so an edit load could reach <c>OperatingEdit</c> with
///   passengers, vehicles, hierarchy, targets, routes and subordinates unresolved. ⚠ Not mere
///   carelessness: those DTO types lived in an assembly <c>Hrot.Presentation</c> could not see, which is
///   why they moved down to <c>Hrot.Core</c> with this step.</description></item>
/// </list>
///
/// <para>⭐⭐⭐ <b>What makes this FORCE rather than merely state the rules:</b></para>
/// <list type="number">
///   <item><description><b>One readiness predicate</b> (<see cref="IsResolved"/>) — there is no second
///   place to write it, which is exactly how conditions ③ and ④ were lost.</description></item>
///   <item><description><b>Required collaborators are non-optional</b> — the id allocator especially.
///   🔴 <c>HN-037</c>: CGF once constructed its own allocator seeded at 1 and produced ids <b>2–9</b>
///   where the editor produced <b>1000–1007</b>. It is now impossible to construct this step without the
///   cluster's authority.</description></item>
///   <item><description><b>Edit versus live is an argument</b>, not a class — the former edit handler was
///   a separate type purely because it targeted a different cluster state.</description></item>
///   <item><description><b>Host differences are injected</b> — the behaviour remapper and the terrain
///   service. A host needing different behaviour supplies a different collaborator; it never writes a
///   second handler.</description></item>
/// </list>
///
/// <para>📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1c.</para>
/// </summary>
public sealed class ScenarioLoadStep : ILoadPartProvider
{
    private readonly ScenarioSerializer _serializer;
    private readonly IScenarioLoader _scenarioLoader;
    private readonly IScenarioEntityExtractor _extractor;
    private readonly FdpEventBus _bus;   // ⭐ CE-3228 — the requests go on the world's bus (Entity Creation §3.4d)
    private readonly INetworkIdAllocator _idAllocator;
    private readonly ScenarioBehaviorRemapper? _remapper;
    private readonly ReadOnlyMigrationAdapter? _readOnlyAdapter;
    private readonly TerrainLoadService? _terrainLoadService;

    private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
    private IReadOnlyDictionary<string, Fdp.Toolkit.Terrain.TerrainDoorState>? _pendingDoorStates;   // ⭐ 5e — the scenario's TerrainObjects
    private Guid? _pendingTransactionId;
    private string? _pendingDescription;   // ⭐ CE-3288 — the file's Header.Description, stamped resident at Commit (§4d)
    private string? _pendingName, _pendingHash;   // ⭐ CE-4058 ② — the loaded scenario's identity, stamped with its description
    private uint? _publishedAtTick;   // ⭐ CE-3228 — the SimulationTick at which Commit published (readiness ①)

    // ⭐⭐ CE-3304 — readiness ① waits for the ENTITIES, not for the requests to be read: the network ids this load published that
    //   have not yet appeared in the node's entity map. 📐 Measured: two ticks after the publish the requests were only QUEUED in
    //   CreateEntityRequestSystem; NetworkSpawningSystem created the entities a frame later — the load resolved on an EMPTY world
    //   (`constructing=0 all=0`), so condition ② never saw a static obstacle Constructing and the vans of bt-window-duel joined the
    //   simulation on whatever step their wall-clock bake finished (3 under load, 9 idle, 20 in a batch). R-243 promises the opposite.
    private readonly HashSet<long> _awaitedNetworkIds = new();

    /// <summary>⭐ CE-3304 — a published request that never produces an entity (rejected, routed to a node that is gone) stops being
    /// waited for after this many ticks; the load then proceeds and names it. Ticks, not seconds: the bound is the same on every machine.</summary>
    public const int EntityWaitLimitTicks = 300;
    private bool _prepared;   // ⭐ CE-3216 C-3 — a Prepare ran and was neither committed nor aborted (a new scenario sets no transaction id)

    /// <param name="idAllocator">
    /// 🔴 <b>Required, and it must be the CLUSTER'S authority</b> — not a locally constructed sequence.
    /// 📐 <c>HN-037</c>, measured: a standalone allocator seeded at 1 gave <c>--mode all</c> ids 2–9 for
    /// the same scenario the editor numbered 1000–1007, and put authored ids inside the runtime band.
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11d ②.
    /// </param>
    /// <param name="terrainLoadService">
    /// ⭐ Optional — a host that composes no zone loader simply skips the call. ⚠ When present, zone
    /// terrain is made resident at the ONE moment it can be (see <see cref="IsResolved"/>).
    /// </param>
    public ScenarioLoadStep(
        ScenarioSerializer serializer,
        IScenarioLoader scenarioLoader,
        IScenarioEntityExtractor extractor,
        FdpEventBus bus,
        INetworkIdAllocator idAllocator,
        ScenarioBehaviorRemapper? behaviorRemapper = null,
        ReadOnlyMigrationAdapter? readOnlyMigrationAdapter = null,
        TerrainLoadService? terrainLoadService = null)
    {
        _serializer         = serializer     ?? throw new ArgumentNullException(nameof(serializer));
        _scenarioLoader     = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
        _extractor          = extractor      ?? throw new ArgumentNullException(nameof(extractor));
        _bus                = bus            ?? throw new ArgumentNullException(nameof(bus));
        _idAllocator        = idAllocator    ?? throw new ArgumentNullException(nameof(idAllocator));
        _remapper           = behaviorRemapper;
        _readOnlyAdapter    = readOnlyMigrationAdapter;
        _terrainLoadService = terrainLoadService;
    }

    /// <inheritdoc/>
    public LoadPart Part => LoadPart.ScenarioEntities;

    /// <inheritdoc/>
    public Task PrepareAsync(LoadPhaseContext context, CancellationToken ct)
    {
        _pendingRequests      = null;
        _pendingDoorStates    = null;
        _pendingTransactionId = null;
        _pendingDescription   = null;
        _pendingName = _pendingHash = null;
        _prepared             = true;

        // ⭐ A NEW scenario has no file to read — the world starts empty and the operator authors into it.
        if (context.IsNewScenario || string.IsNullOrWhiteSpace(context.ScenarioId))
            return Task.CompletedTask;

        // ⭐⭐⭐ L8 — ONE ATTEMPT, NO RETRY LOOP. The retry that stood here predates the deterministic
        //    ordering: the load step is now dispatched only after ClusterMaster has been told that every
        //    node acknowledged its files, so a scenario that cannot be read at this instant is genuinely
        //    absent. 🔒 User: "it cannot depend on timeouts where can easily wait deterministically."
        string? json = _scenarioLoader.TryLoadScenarioJson(context.ScenarioId);

        if (json == null)
        {
            // ⛔ LOUD. A Brain node that cannot read the scenario it was told to load has nothing to
            //   contribute, and a silent empty world is exactly the failure this whole design replaces.
            throw new InvalidOperationException(
                $"[LoadPhase/Scenario] No scenario file found for '{context.ScenarioId}'. The load step runs "
              + "only after the distribution of the scenario has been acknowledged by every node, so this is "
              + "a broken deployment rather than a race (see docs/DESIGN_Cluster_Load_Phase.md §7).");
        }

        _pendingRequests      = _extractor.Extract(_serializer, json, _idAllocator, _remapper);
        _pendingDoorStates    = Fdp.Toolkit.Terrain.TerrainObjectsSection.ReadDoors(json);   // ⭐ 5e — applied when the doors are created
        _pendingDescription   = ScenarioDescription.Read(json);   // ⭐ CE-3288 — held by the world so a save writes it back
        _pendingName          = context.ScenarioId;              // ⭐ CE-4058 ② — a recording names its scenario
        _pendingHash          = ScenarioDescription.HashOf(json);
        _pendingTransactionId = context.TransactionId;

        FdpLog<ScenarioLoadStep>.Info(
            "[LoadPhase/Scenario] Extracted {0} entity request(s) from '{1}'.",
            _pendingRequests?.Count ?? 0, context.ScenarioId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ <b>Commit only PUBLISHES</b> (on the bus, <c>CE-3228</c>). The genesis pipeline drains the requests over LATER frames, which is
    /// why the readiness predicate exists at all — and why terrain residency cannot be stamped here.
    /// </remarks>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
    {
        // ⛔ a prepared batch of ANOTHER transaction is stale — neither it nor this transaction's doors are ours to enqueue
        if (_pendingTransactionId != null && _pendingTransactionId != context.TransactionId) return;

        _publishedAtTick = null;
        _awaitedNetworkIds.Clear();
        try
        {
            // ⭐ CE-3288 (§4d) — the loaded scenario's description is RESIDENT, so both save sites write it back; a new scenario (or
            //   one without a description) clears the previous one's.
            if (world != null && _prepared) ScenarioDescription.Stamp(world, _pendingDescription, _pendingName, _pendingHash);

            bool hasEnvironment = world != null && Fdp.Toolkit.Weather.ScenarioTime.TryClock(world, out _);
            if (_pendingRequests != null)
                foreach (var request in _pendingRequests)
                {
                    // ⭐ CE-3216 C-3 — exactly ONE World Environment: a second is ignored with a warning, the first kept.
                    if (request.TkbType == TkbEntityTypes.WorldEnvironment)
                    {
                        if (hasEnvironment)
                        {
                            FdpLog<ScenarioLoadStep>.Warn(
                                "[LoadPhase/Scenario] '{0}' holds a second World Environment — ignored, the first is kept (C-3).", context.ScenarioId ?? "(new scenario)");
                            continue;
                        }
                        hasEnvironment = true;
                    }
                    Publish(world, request);
                }

            // ⭐ Buildings Stage 5b — the TERRAIN's entities (its doors), right after the scenario's own, in key order, ids from the
            //   same allocator (§3b K1/K3). ⭐ Also for a NEW scenario: the terrain has doors whether or not the scenario has units.
            //   The terrain is resident here — TerrainLoadStep commits first. 📄 docs/DESIGN_Building_Interiors.md §3j.
            if (world != null && world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>())
            {
                var terrain = world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>();
                if (terrain != null && terrain.Doors.Count > 0)
                    foreach (var request in TerrainObjectRequests.ForDoors(
                                 terrain, Fdp.Toolkit.Terrain.TerrainObjects.ExistingKeys(world), _idAllocator, _pendingDoorStates))
                        Publish(world, request);
            }

            // ⭐ CE-3216 C-3 (R-3012) — a scenario with no World Environment gets a DEFAULT one, so the clock is always there to change
            //   (and is saved next time). ⭐ W-8: the FIXED built-in start time, ⛔ never the wall clock. After the doors, so their ids
            //   are what they were before the clock existed. Also for a NEW scenario. 📄 docs/designs/weather/DESIGN_Weather_And_Time.md.
            if (!hasEnvironment && _prepared)
                Publish(world, WorldEnvironmentRequest(_idAllocator));
        }
        finally
        {
            _pendingRequests      = null;
            _pendingDoorStates    = null;
            _pendingTransactionId = null;
            _pendingDescription   = null;
            _pendingName = _pendingHash = null;
            _prepared             = false;
        }
    }

    /// <summary>⭐ <c>CE-3228</c> (Entity Creation §3.4d) — a request goes on the bus; the tick it went on is what readiness ① waits on.</summary>
    private void Publish(EntityRepository? world, EntityCreationRequest request)
    {
        _bus.PublishManaged(request);
        if (world != null) _publishedAtTick ??= world.SimulationTick;
        if (request.PreAllocatedNetworkId != 0) _awaitedNetworkIds.Add(request.PreAllocatedNetworkId);   // ⭐ CE-3304
    }

    /// <summary>⭐ CE-3216 C-3 — the creation request for a DEFAULT World Environment: the built-in start time, an id from the cluster's
    /// allocator. Positionless: no <c>SimTransform</c> rides with it.</summary>
    public static EntityCreationRequest WorldEnvironmentRequest(INetworkIdAllocator idAllocator) => new()
    {
        RequestId             = Guid.NewGuid(),
        TkbType               = TkbEntityTypes.WorldEnvironment,
        PreAllocatedNetworkId = idAllocator.AllocateId(),
        InitialComponents     = new List<object> { Fdp.Toolkit.Weather.ScenarioClock.Default, Fdp.Toolkit.Weather.WeatherConditions.Standard },
    };

    /// <inheritdoc/>
    public void Abort(LoadPhaseContext context)
    {
        _publishedAtTick      = null;
        _awaitedNetworkIds.Clear();
        _pendingRequests      = null;
        _pendingDoorStates    = null;
        _pendingTransactionId = null;
        _pendingDescription   = null;
        _pendingName = _pendingHash = null;
        _prepared             = false;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE readiness predicate — the one implementation, and the reason this type exists.</b>
    /// The cluster may leave <c>Loading*</c> only when all four conditions hold.
    ///
    /// <para>⚠ Condition ③ is the one the editor's copy had lost, and condition ④ the one CGF's copy had
    /// lost. Both are here once, so neither can go missing on one host again.</para>
    /// </summary>
    public bool IsResolved(EntityRepository? world)
    {
        // ① every extracted request has been consumed by CreateEntityRequestSystem. ⭐ CE-3228 (Entity Creation §3.4d): the requests
        //    went on the bus, which shows them after the NEXT swap; the request system reads them in the Input phase of the frame after
        //    that. ⇒ two kernel ticks after the publish, all of them have been read (a publish inside a frame costs one spare frame).
        if (_publishedAtTick is { } at && world != null)
        {
            uint waited = unchecked(world.SimulationTick - at);
            if (waited < 2) return false;
            // ⭐⭐ CE-3304 — and every entity this load asked for EXISTS on this node (condition ② can only see what exists).
            if (_awaitedNetworkIds.Count > 0 && world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
                && world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>() is { } map)
            {
                _awaitedNetworkIds.RemoveWhere(id => map.TryGetEntity(id, out _));
                if (_awaitedNetworkIds.Count > 0)
                {
                    if (waited < EntityWaitLimitTicks) return false;
                    FdpLog<ScenarioLoadStep>.Error(
                        $"[LoadPhase/Scenario] {_awaitedNetworkIds.Count} requested entit(ies) never appeared after {EntityWaitLimitTicks} ticks "
                        + $"(network ids {string.Join(", ", _awaitedNetworkIds)}) — the load proceeds without them (CE-3304).");
                    _awaitedNetworkIds.Clear();
                }
            }
            _publishedAtTick = null;
        }

        if (world != null)
        {
            // ② the lifecycle handshakes are complete — nothing is still awaiting peer acknowledgement.
            foreach (var _ in world.Query().WithLifecycle(EntityLifecycle.Constructing).Build())
                return false;

            // ③ GenesisMaterializationSystem has resolved every cross-entity reference and removed the
            //    transient Intent DTO components. 🔴 ABSENT from the former edit handler.
            foreach (var _ in world.Query().WithManaged<InitialPassengersIntent>().Build())       return false;
            foreach (var _ in world.Query().WithManaged<InitialVehicleIntent>().Build())          return false;
            foreach (var _ in world.Query().WithManaged<InitialHierarchyIntent>().Build())        return false;
            foreach (var _ in world.Query().WithManaged<InitialTargetsIntent>().Build())          return false;
            foreach (var _ in world.Query().WithManaged<InitialRouteIntent>().Build())            return false;
            foreach (var _ in world.Query().WithManaged<InitialUnitSubordinateIntent>().Build())  return false;
        }

        // ④ zone terrain becomes resident HERE — the first moment the zone ENTITIES provably exist.
        //    🔴 ABSENT from the former CGF handler. ⛔ Not a NodeOp: we are already inside the cluster's
        //    own load transaction and a nested two-phase round would deadlock. Same service the operator
        //    -driven round uses, so the two paths cannot drift.
        if (_terrainLoadService != null && world != null)
            _terrainLoadService.EnsureAllLoaded(world);

        return true;
    }
}
