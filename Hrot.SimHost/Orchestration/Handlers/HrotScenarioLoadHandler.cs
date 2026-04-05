using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;

namespace Hrot.SimHost.Orchestration.Handlers;

/// <summary>
/// HROT-specific scenario-load handler for the <c>LoadingLive</c> cluster state.
///
/// <para>
/// Replaces <see cref="ReferenceScenarioLoadHandler"/> to add zone support via
/// <see cref="IZoneManagerService.LoadZones"/> while maintaining the
/// <b>single JSON parse discipline</b>: the raw JSON is parsed into a DOM exactly
/// once and that same DOM is passed both to DTO deserialisation and to
/// <see cref="ScenarioSerializer.Deserialize(EntityRepository, JsonObject)"/>.
/// </para>
/// </summary>
public sealed class HrotScenarioLoadHandler : IClusterStateHandler
{
    private readonly ScenarioSerializer _serializer;
    private readonly IScenarioLoader _scenarioLoader;
    private readonly IZoneManagerService _zoneService;
    private readonly EntityRepository? _world;

    private string? _pendingJson;
    private Guid? _pendingTransactionId;
    private int _prepareCallCount;

    /// <summary>
    /// Number of times <see cref="PrepareAsync"/> has been invoked.
    /// For integration-test assertions only.
    /// </summary>
    public int PrepareCallCountForTest => _prepareCallCount;

    public HrotScenarioLoadHandler(
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

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<object?> PrepareAsync(
        ExecuteNodeOpIntent intent,
        System.Threading.CancellationToken ct)
    {
        _prepareCallCount++;
        _pendingJson = null;
        _pendingTransactionId = null;

        var scenarioId = intent.DomainPayload is EditLoadHandlerPayload elp
            ? elp.ScenarioId
            : intent.DomainPayload as string;

        if (string.IsNullOrWhiteSpace(scenarioId))
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        _pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
        if (_pendingJson != null)
            _pendingTransactionId = intent.TransactionId;

        return System.Threading.Tasks.Task.FromResult<object?>(null);
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        if (_pendingJson == null || _pendingTransactionId != intent.TransactionId) return;

        var targetRepo = repo ?? _world;
        if (targetRepo == null)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            return;
        }

        try
        {
            CommitLoad(targetRepo, _pendingJson);
        }
        finally
        {
            _pendingJson = null;
            _pendingTransactionId = null;
        }
    }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        _pendingJson = null;
        _pendingTransactionId = null;
    }

    // ── Single-parse core ─────────────────────────────────────────────────────

    private void CommitLoad(EntityRepository repo, string rawJson)
    {
        // 1. Parse exactly once.
        var dom = JsonNode.Parse(rawJson)?.AsObject();

        // 2. Deserialise DTO from the already-parsed DOM — no second string parse.
        var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotSerializerOptions.HrotJsonOptions);

        // 3. Load zones before entities.
        if (envelope?.Zones != null)
            _zoneService.LoadZones(repo, envelope.Zones);

        // 4. Pass pre-parsed DOM to the FDP serialiser — no third string parse.
        if (dom != null)
            _serializer.Deserialize(repo, dom);
    }
}
