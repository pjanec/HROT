using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Kernel.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
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
    private readonly IRecordReplayController? _controller;
    private readonly string _storageDirectory;

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
        EntityRepository? world = null,
        IRecordReplayController? controller = null,
        string storageDirectory = @"C:\FDP_Temp")
    {
        _serializer        = serializer     ?? throw new ArgumentNullException(nameof(serializer));
        _scenarioLoader    = scenarioLoader ?? throw new ArgumentNullException(nameof(scenarioLoader));
        _zoneService       = zoneService    ?? throw new ArgumentNullException(nameof(zoneService));
        _world             = world;
        _controller        = controller;
        _storageDirectory  = storageDirectory ?? @"C:\FDP_Temp";
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;

    /// <inheritdoc />
    public async Task<object?> PrepareAsync(
        ExecuteNodeOpIntent intent,
        CancellationToken ct)
    {
        _prepareCallCount++;
        _pendingJson = null;
        _pendingTransactionId = null;

        var scenarioId = intent.DomainPayload is EditLoadHandlerPayload elp
            ? elp.ScenarioId
            : intent.DomainPayload as string;

        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            _pendingJson = _scenarioLoader.TryLoadScenarioJson(scenarioId);
            if (_pendingJson != null)
                _pendingTransactionId = intent.TransactionId;
        }

        // Start recording when an exercise ID is provided (bus-mode path).
        // This mirrors what ReferenceLiveLoadHandler.PrepareAsync does for the
        // "cold PrepareLive" case (no scenario serializer registered).
        if (_controller != null)
        {
            var exerciseId = ResolveExerciseId(intent.DomainPayload);
            if (exerciseId != Guid.Empty)
                await _controller.PrepareRecordingAsync(exerciseId, _storageDirectory)
                    .ConfigureAwait(false);
        }

        return null;
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Guid ResolveExerciseId(object? domainPayload) =>
        domainPayload switch
        {
            Guid g => g,
            EditLoadHandlerPayload p when p.ExerciseId != null =>
                Guid.TryParse(p.ExerciseId, out var parsed) ? parsed : GuidFromString(p.ExerciseId),
            _ => Guid.Empty,
        };

    private static Guid GuidFromString(string s)
    {
        var hashBytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hashBytes);
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
