using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;

namespace Hrot.ScenarioEditor.Handlers;

/// <summary>
/// HROT-specific edit-load handler for the <c>LoadingEdit</c> cluster state.
///
/// <para>
/// Replaces <see cref="ReferenceEditLoadHandler"/> to add zone support via
/// <see cref="IZoneManagerService.LoadZones"/> while maintaining the
/// <b>single JSON parse discipline</b>: the raw JSON is parsed into a DOM exactly
/// once and that same DOM is passed both to DTO deserialisation and to
/// <see cref="ScenarioSerializer.Deserialize(EntityRepository, JsonObject)"/>.
/// </para>
/// </summary>
public sealed class HrotEditLoadHandler : IClusterStateHandler
{
    private const int LoadingEditState = 10;

    private readonly ScenarioSerializer _serializer;
    private readonly IScenarioLoader _scenarioLoader;
    private readonly IZoneManagerService _zoneService;
    private readonly EntityRepository? _world;

    private string? _pendingJson;
    private Guid? _pendingTransactionId;
    private bool _pendingIsNew;

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
        _pendingJson = null;
        _pendingTransactionId = null;
        _pendingIsNew = false;

        if (intent.DomainPayload is not EditLoadHandlerPayload payload)
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        if (payload.TargetState != LoadingEditState)
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        var isNew = payload.IsNewScenario;
        var scenarioId = payload.ScenarioId;

        _pendingTransactionId = intent.TransactionId;
        _pendingIsNew = isNew;

        if (isNew || string.IsNullOrWhiteSpace(scenarioId))
            return System.Threading.Tasks.Task.FromResult<object?>(null);

        _pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
        if (_pendingJson == null)
        {
            throw new InvalidOperationException(
                $"[HrotEditLoadHandler] no scenario file found for ScenarioId='{scenarioId}'. " +
                "Ensure PrefetchFiles completed before LoadingEdit.");
        }

        return System.Threading.Tasks.Task.FromResult<object?>(null);
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        if (_pendingTransactionId != intent.TransactionId) return;

        if (_pendingIsNew || _pendingJson == null)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            return;
        }

        var targetRepo = repo ?? _world;
        if (targetRepo == null)
        {
            _pendingJson = null;
            _pendingTransactionId = null;
            throw new InvalidOperationException(
                "[HrotEditLoadHandler] Commit: EntityRepository is null but scenario deserialization is required.");
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
        _pendingIsNew = false;
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
