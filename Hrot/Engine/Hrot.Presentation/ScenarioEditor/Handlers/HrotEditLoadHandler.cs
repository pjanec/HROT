using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;

namespace Hrot.ScenarioEditor.Handlers;

/// <summary>
/// HROT-specific edit-load handler for the <c>LoadingEdit</c> cluster state.
///
/// <para>
/// When constructed with an <see cref="IScenarioEntityExtractor"/>,
/// <see cref="ScenarioEntityCreationRequestSource"/>, and <see cref="INetworkIdAllocator"/>
/// (the <em>unified</em> path), entity creation requests are extracted from the
/// scenario JSON via the staging pipeline and enqueued for time-sliced processing by
/// the genesis pipeline (<c>CreateEntityRequestSystem</c> â†’
/// <c>NetworkSpawningSystem</c>).  This path ensures the
/// <c>EntityLifecycleModule</c> handshake is honoured and that
/// <c>AuthorityMask</c> is correctly stamped on every loaded entity.
/// </para>
/// <para>
/// When constructed without those optional dependencies (the <em>legacy</em> path),
/// the handler falls back to calling
/// <see cref="ScenarioSerializer.Deserialize(EntityRepository, string)"/> directly
/// on the target repository â€” matching the previous behaviour required by
/// <c>NodeBootstrapper</c>.
/// </para>
/// <para>
/// Zone data from the scenario envelope is always applied synchronously in
/// <see cref="Commit"/> because zones are not ECS entities and do not participate
/// in the genesis pipeline.
/// </para>
/// <para>
/// Implements <see cref="ITickableClusterStateHandler"/> to intercept the
/// <c>PrepareState(OperatingEdit)</c> transition and hold the cluster in
/// <c>LoadingEdit</c> until all ECS entities created during scenario loading
/// have left the <c>Constructing</c> lifecycle phase.
/// </para>
/// </summary>
public sealed class HrotEditLoadHandler : ITickableClusterStateHandler
{
    private readonly ScenarioSerializer _serializer;
    private readonly IScenarioLoader _scenarioLoader;
    private readonly IZoneManagerService _zoneService;
    private readonly EntityRepository? _world;

    // Unified-path dependencies (null = legacy path).
    private readonly IScenarioEntityExtractor? _extractor;
    private readonly ScenarioEntityCreationRequestSource? _source;
    private readonly INetworkIdAllocator? _idAllocator;

    private IReadOnlyList<EntityCreationRequest>? _pendingRequests;
    private string? _pendingJson;
    private Guid? _pendingTransactionId;
    private bool _pendingIsNew;
    private System.Threading.Tasks.TaskCompletionSource<object?>? _operatingEditTcs;

    // â”€â”€ Legacy constructor (backward compatible with NodeBootstrapper) â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Constructs the handler in <em>legacy</em> mode: scenario entities are
    /// deserialised directly into the target repository via
    /// <see cref="ScenarioSerializer.Deserialize(EntityRepository, string)"/>.
    /// </summary>
    public HrotEditLoadHandler(
        ScenarioSerializer serializer,
        IScenarioLoader scenarioLoader,
        IZoneManagerService zoneService,
        EntityRepository? world = null)
    {
        _serializer     = serializer     ?? throw new ArgumentNullException(nameof(serializer));
        _scenarioLoader = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
        _zoneService    = zoneService    ?? throw new ArgumentNullException(nameof(zoneService));
        _world          = world;
    }

    // â”€â”€ Unified constructor (genesis-pipeline path) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Constructs the handler in <em>unified</em> mode: entity creation requests
    /// are extracted via <paramref name="extractor"/> and enqueued into
    /// <paramref name="source"/> for the genesis pipeline.
    /// </summary>
    public HrotEditLoadHandler(
        ScenarioSerializer serializer,
        IScenarioLoader scenarioLoader,
        IZoneManagerService zoneService,
        IScenarioEntityExtractor extractor,
        ScenarioEntityCreationRequestSource source,
        INetworkIdAllocator idAllocator,
        EntityRepository? world = null)
    {
        _serializer     = serializer     ?? throw new ArgumentNullException(nameof(serializer));
        _scenarioLoader = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
        _zoneService    = zoneService    ?? throw new ArgumentNullException(nameof(zoneService));
        _extractor      = extractor      ?? throw new ArgumentNullException(nameof(extractor));
        _source         = source         ?? throw new ArgumentNullException(nameof(source));
        _idAllocator    = idAllocator    ?? throw new ArgumentNullException(nameof(idAllocator));
        _world          = world;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareState ||
        operation == NodeOpType.PrepareEdit  ||
        operation == NodeOpType.FinalizeEdit;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<object?> PrepareAsync(
        ExecuteNodeOpIntent intent,
        System.Threading.CancellationToken ct)
    {
        _pendingRequests      = null;
        _pendingJson          = null;
        _pendingTransactionId = null;
        _pendingIsNew         = false;

        if (intent.DomainPayload is not EditLoadHandlerPayload payload)
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        // Intercept PrepareState targeting OperatingEdit: hold the cluster in
        // LoadingEdit until DrainDeferredAcks confirms all ECS entities have
        // left the Constructing lifecycle phase.
        if (payload.TargetState == ClusterState.OperatingEdit)
        {
            _operatingEditTcs = new System.Threading.Tasks.TaskCompletionSource<object?>(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            return _operatingEditTcs.Task;
        }

        if (payload.TargetState != ClusterState.LoadingEdit)
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        var isNew      = payload.IsNewScenario;
        var scenarioId = payload.ScenarioId;

        _pendingTransactionId = intent.TransactionId;
        _pendingIsNew         = isNew;

        if (isNew || string.IsNullOrWhiteSpace(scenarioId))
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        var json = _scenarioLoader.TryLoadScenarioJson(scenarioId);
        if (json == null)
        {
            throw new InvalidOperationException(
                $"[HrotEditLoadHandler] no scenario file found for ScenarioId='{scenarioId}'. " +
                "Ensure PrefetchFiles completed before LoadingEdit.");
        }

        _pendingJson = json;

        if (_extractor != null && _idAllocator != null)
        {
            // Unified path: extract entity creation requests via the staging pipeline.
            _pendingRequests = _extractor.Extract(_serializer, json, _idAllocator);
        }
        // Legacy path: no pre-computation needed; direct deserialization happens in Commit.

        return System.Threading.Tasks.Task.FromResult<object?>(null);
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        if (_pendingTransactionId != intent.TransactionId) return;

        if (_pendingIsNew || _pendingJson == null)
        {
            _pendingJson          = null;
            _pendingRequests      = null;
            _pendingTransactionId = null;
            return;
        }

        var targetRepo = repo ?? _world;

        try
        {
            if (_extractor != null && _source != null)
            {
                // Unified path: load zones synchronously, then enqueue entity requests.
                // Zones are not ECS entities and do not participate in the genesis pipeline.
                if (targetRepo != null)
                {
                    var dom      = JsonNode.Parse(_pendingJson)?.AsObject();
                    var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotSerializerOptions.HrotJsonOptions);
                    if (envelope?.Zones != null)
                        _zoneService.LoadZones(targetRepo, envelope.Zones);
                }

                // Enqueue entity creation requests for genesis pipeline processing.
                // CreateEntityRequestSystem drains them and publishes SpawnEntityCommand
                // events; NetworkSpawningSystem stamps AuthorityMask = ComponentMask for
                // locally owned entities.
                if (_pendingRequests != null)
                {
                    foreach (var req in _pendingRequests)
                        _source.Enqueue(req);
                }
            }
            else
            {
                // Legacy path: single-parse deserialisation directly into the live repo.
                if (targetRepo == null)
                {
                    throw new InvalidOperationException(
                        "[HrotEditLoadHandler] Commit: EntityRepository is null but scenario deserialization is required.");
                }

                CommitLegacyLoad(targetRepo, _pendingJson);
            }
        }
        finally
        {
            _pendingJson          = null;
            _pendingRequests      = null;
            _pendingTransactionId = null;
        }
    }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        _pendingRequests      = null;
        _pendingJson          = null;
        _pendingTransactionId = null;
        _pendingIsNew         = false;
        _operatingEditTcs?.TrySetCanceled();
        _operatingEditTcs = null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called from the main ECS thread each frame.  When in unified mode, waits until
    /// all pending entity creation requests have been consumed by
    /// <c>CreateEntityRequestSystem</c> and all ECS entities have left the
    /// <c>Constructing</c> lifecycle phase.  In legacy mode, only the Constructing
    /// check is performed (no source to drain).
    /// </remarks>
    public void DrainDeferredAcks()
    {
        if (_operatingEditTcs == null) return;

        // Unified-mode condition: all entity creation requests must be consumed first.
        if (_source != null && !_source.IsEmpty) return;

        if (_world != null)
        {
            // Condition: ELM handshakes are complete â€” no entities are still Constructing.
            foreach (var _ in _world.Query().WithLifecycle(EntityLifecycle.Constructing).Build())
                return;
        }

        _operatingEditTcs.TrySetResult(null);
        _operatingEditTcs = null;
    }

    // â”€â”€ Legacy deserialisation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void CommitLegacyLoad(EntityRepository repo, string rawJson)
    {
        // 1. Parse exactly once.
        var dom = JsonNode.Parse(rawJson)?.AsObject();

        // 2. Deserialise DTO from the already-parsed DOM â€” no second string parse.
        var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotSerializerOptions.HrotJsonOptions);

        // 3. Load zones before entities.
        if (envelope?.Zones != null)
            _zoneService.LoadZones(repo, envelope.Zones);

        // 4. Pass pre-parsed DOM to the FDP serialiser â€” no third string parse.
        if (dom != null)
            _serializer.Deserialize(repo, dom);
    }
}
