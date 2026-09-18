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
    private readonly ScenarioEntityCreationRequestSource _source;
    private readonly INetworkIdAllocator _idAllocator;
    private readonly ScenarioBehaviorRemapper? _remapper;
    private readonly ReadOnlyMigrationAdapter? _readOnlyAdapter;
    private readonly TerrainLoadService? _terrainLoadService;

    private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
    private Guid? _pendingTransactionId;

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
        ScenarioEntityCreationRequestSource source,
        INetworkIdAllocator idAllocator,
        ScenarioBehaviorRemapper? behaviorRemapper = null,
        ReadOnlyMigrationAdapter? readOnlyMigrationAdapter = null,
        TerrainLoadService? terrainLoadService = null)
    {
        _serializer         = serializer     ?? throw new ArgumentNullException(nameof(serializer));
        _scenarioLoader     = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
        _extractor          = extractor      ?? throw new ArgumentNullException(nameof(extractor));
        _source             = source         ?? throw new ArgumentNullException(nameof(source));
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
        _pendingTransactionId = null;

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
        _pendingTransactionId = context.TransactionId;

        FdpLog<ScenarioLoadStep>.Info(
            "[LoadPhase/Scenario] Extracted {0} entity request(s) from '{1}'.",
            _pendingRequests?.Count ?? 0, context.ScenarioId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐ <b>Commit only ENQUEUES.</b> The genesis pipeline drains the queue over LATER frames, which is
    /// why the readiness predicate exists at all — and why terrain residency cannot be stamped here.
    /// </remarks>
    public void Commit(LoadPhaseContext context, EntityRepository? world)
    {
        if (_pendingRequests == null || _pendingTransactionId != context.TransactionId) return;

        try
        {
            foreach (var request in _pendingRequests)
                _source.Enqueue(request);
        }
        finally
        {
            _pendingRequests      = null;
            _pendingTransactionId = null;
        }
    }

    /// <inheritdoc/>
    public void Abort(LoadPhaseContext context)
    {
        _pendingRequests      = null;
        _pendingTransactionId = null;
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
        // ① every extracted request has been consumed by CreateEntityRequestSystem.
        if (!_source.IsEmpty) return false;

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
