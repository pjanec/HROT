using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.Network.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;

namespace Hrot.Orchestrator;

/// <summary>
/// Cluster handler that serializes/deserializes the Orchestrator node's own global context
/// as part of a scenario save/load round.
///
/// <para>
/// <b>Save path</b> (<see cref="NodeOpType.SerializeLocal"/>): Writes
/// <c>GlobalContextDto</c> (simulation start wall ticks, scene identifier) to
/// <c>C:\FDP_Temp\&lt;ExerciseId&gt;\Orchestrator.json</c>.
/// Returns the file path as a single-entry <see cref="FileManifestEntry"/>
/// in <c>NodeOpStatus.ResultJson</c>.
/// </para>
///
/// <para>
/// <b>Load path</b> (<see cref="NodeOpType.CommitState"/> for
/// <see cref="ClusterState.LoadingLive"/> or <see cref="ClusterState.LoadingEdit"/>):
/// Parses the pre-fetched <c>Orchestrator.json</c> and populates
/// <see cref="LoadedStartWallTicks"/> / <see cref="LoadedSceneId"/> for
/// the hosting application to consume (e.g. <c>MasterTimeController.SeedState</c>).
/// Also publishes an updated <c>OrchestratorContextTopic</c> over DDS.
/// The hosting application is responsible for calling
/// <c>MasterTimeController.SeedState(LoadedStartWallTicks)</c> after this handler
/// completes.
/// </para>
/// </summary>
public sealed class GlobalContextClusterOpHandler : IClusterOpHandler
{
    /// <summary>
    /// Local working directory root; substituable in tests.
    /// In production this is <c>C:\FDP_Temp</c> (Windows NAS-mirror convention).
    /// </summary>
    public string LocalTempRoot { get; set; } = @"C:\FDP_Temp";

    private readonly DdsWriter<OrchestratorContextTopic> _contextWriter;
    private readonly string _scenarioId;

    // ── Seed state exposed for injection ────────────────────────────────────────
    /// <summary>
    /// Wall ticks value read from the most recently loaded <c>Orchestrator.json</c>.
    /// Consumers (e.g. <c>ClusterMaster</c> startup) should call
    /// <c>MasterTimeController.SeedState</c> after load.
    /// </summary>
    public long LoadedStartWallTicks { get; private set; }

    /// <summary>
    /// Scene identifier read from the most recently loaded <c>Orchestrator.json</c>.
    /// </summary>
    public string? LoadedSceneId { get; private set; }

    /// <summary>
    /// Elapsed simulation time in seconds read from the most recently loaded
    /// <c>Orchestrator.json</c> (CGF-1-BATCH-23 §A.4).
    /// </summary>
    public double LoadedScenarioTimeSeconds { get; private set; }

    /// <summary>
    /// Scenario identifier read from the most recently loaded <c>Orchestrator.json</c>
    /// (separate from <see cref="LoadedSceneId"/> which is the map/terrain identifier).
    /// </summary>
    public string? LoadedScenarioId { get; private set; }

    /// <summary>
    /// Raised at the end of a successful <see cref="CommitLoad"/> invocation.
    /// Arguments are <c>(startWallTicks, scenarioTimeSeconds)</c> from the loaded
    /// <c>Orchestrator.json</c>.  The hosting application (e.g.
    /// <c>OrchestratorSubsystem</c>) subscribes here to seed
    /// <c>MasterTimeController.SeedState</c> with the restored scenario timeline.
    /// </summary>
    public event Action<long, double>? OnContextLoaded;

    /// <summary>
    /// Elapsed simulation time in seconds at the point of the pending save.
    /// Set by callers (e.g. <c>OrchestratorSubsystem</c>) before
    /// <see cref="NodeOpType.SerializeLocal"/> is dispatched.
    /// </summary>
    public double ScenarioTimeSeconds { get; set; }

    /// <summary>Pending save ticks — populated during <see cref="PrepareAsync"/>.</summary>
    private long _pendingSaveWallTicks;
    private string? _pendingSaveSceneId;
    private double _pendingSaveScenarioTimeSeconds;
    private string? _pendingFilePath;

    /// <summary>
    /// Creates a <see cref="GlobalContextClusterOpHandler"/> for the given DDS participant
    /// and scenario identifier.
    /// </summary>
    /// <param name="participant">Participant used to publish <see cref="OrchestratorContextTopic"/>.</param>
    /// <param name="scenarioId">Scenario identifier stored in the global context file.</param>
    public GlobalContextClusterOpHandler(DdsParticipant participant, string scenarioId)
    {
        _contextWriter = new DdsWriter<OrchestratorContextTopic>(participant);
        _scenarioId    = scenarioId ?? string.Empty;
    }

    /// <summary>Test-only constructor that accepts a pre-built writer.</summary>
    internal GlobalContextClusterOpHandler(DdsWriter<OrchestratorContextTopic> contextWriter, string scenarioId)
    {
        _contextWriter = contextWriter;
        _scenarioId    = scenarioId ?? string.Empty;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType op)
        => op == NodeOpType.SerializeLocal
        || op == NodeOpType.CommitState;

    /// <inheritdoc />
    /// <remarks>
    /// For <see cref="NodeOpType.SerializeLocal"/>: snapshots the current wall ticks and
    /// scene identifier; computes the output file path but defers I/O to <see cref="Commit"/>.
    /// For other operations: returns <see langword="null"/> immediately (success, no pre-work).
    /// </remarks>
    public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
    {
        if (cmd.Operation == NodeOpType.SerializeLocal)
        {
            // Determine the exercise ID from the payload (if provided) or generate one.
            var exerciseId = ParseExerciseId(cmd.PayloadJson);
            var dir     = Path.Combine(
                LocalTempRoot,
                Fdp.Toolkit.Orchestration.OrchestrationConstants.ExercisesDirectoryName,
                exerciseId.ToString("N"));
            _pendingFilePath                  = Path.Combine(dir, "Orchestrator.json");
            _pendingSaveWallTicks             = DateTimeOffset.UtcNow.Ticks;   // wall-clock snapshot at prepare time
            _pendingSaveSceneId               = _scenarioId;
            _pendingSaveScenarioTimeSeconds   = ScenarioTimeSeconds;
        }
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// For <see cref="NodeOpType.SerializeLocal"/>: writes <c>Orchestrator.json</c> and
    /// returns the manifest entry path via a side-channel (callers read <see cref="CommitManifestEntry"/>).
    /// For <see cref="NodeOpType.CommitState"/> heading to <see cref="ClusterState.LoadingLive"/> or
    /// <see cref="ClusterState.LoadingEdit"/>: loads the pre-fetched <c>Orchestrator.json</c> and
    /// publishes <see cref="OrchestratorContextTopic"/>.
    /// </remarks>
    public void Commit(NodeOpCommand cmd, EntityRepository? repo)
    {
        if (cmd.Operation == NodeOpType.SerializeLocal)
        {
            CommitSerializeLocal();
        }
        else if (cmd.Operation == NodeOpType.CommitState)
        {
            var targetState = ParseTargetState(cmd.PayloadJson);
            if (targetState == ClusterState.LoadingLive || targetState == ClusterState.LoadingEdit)
                CommitLoad(cmd);
        }
    }

    /// <inheritdoc />
    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
    {
        // Reset pending state; no I/O was committed.
        _pendingFilePath                = null;
        _pendingSaveWallTicks           = 0;
        _pendingSaveSceneId             = null;
        _pendingSaveScenarioTimeSeconds = 0;
    }

    // ── Manifest output (read by ClusterMaster after Commit) ───────────────────────

    /// <summary>
    /// Set by <see cref="Commit"/> after a successful <see cref="NodeOpType.SerializeLocal"/>
    /// operation.  <see cref="Hrot.Orchestrator.ClusterMaster"/> reads this to build the
    /// global manifest entry for the storage gateway.
    /// </summary>
    public FileManifestEntry? CommitManifestEntry { get; private set; }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private void CommitSerializeLocal()
    {
        if (_pendingFilePath == null) return;

        try
        {
            var dir = Path.GetDirectoryName(_pendingFilePath)!;
            Directory.CreateDirectory(dir);

            var dto = new GlobalContextDto
            {
                StartWallTicks        = _pendingSaveWallTicks,
                SceneId               = _pendingSaveSceneId ?? string.Empty,
                ScenarioId            = _scenarioId,
                ScenarioTimeSeconds   = _pendingSaveScenarioTimeSeconds,
                SchemaVersion         = 2,
            };

            var json = JsonSerializer.Serialize(dto,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pendingFilePath, json);
            var exerciseIdText = new DirectoryInfo(Path.GetDirectoryName(_pendingFilePath)!).Name;

            CommitManifestEntry = new FileManifestEntry
            {
                SourceUnc    = _pendingFilePath,
                RelativeDest = Path.Combine(
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.ExercisesDirectoryName,
                    exerciseIdText,
                    Path.GetFileName(_pendingFilePath)),
            };

            FdpLog<GlobalContextClusterOpHandler>.Info(
                "[Orchestrator] GlobalContext serialized → {0}", _pendingFilePath);
        }
        catch (Exception ex)
        {
            FdpLog<GlobalContextClusterOpHandler>.Error(
                "[Orchestrator] GlobalContext serialize failed: {0}", ex.Message);
            throw;
        }
        finally
        {
            _pendingFilePath     = null;
            _pendingSaveWallTicks = 0;
            _pendingSaveSceneId  = null;
        }
    }

    private void CommitLoad(NodeOpCommand cmd)
    {
        // Derive the local path from the payload ScenarioId or a known convention.
        var scenarioId = ParseScenarioId(cmd.PayloadJson);
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            // No ScenarioId in payload — context load is optional; blank world is acceptable.
            FdpLog<GlobalContextClusterOpHandler>.Info(
                "[Orchestrator] CommitLoad: no ScenarioId in payload — skipping context restore (blank world).");
            return;
        }

        var filePath = Path.Combine(
            LocalTempRoot,
            Fdp.Toolkit.Orchestration.OrchestrationConstants.ScenariosDirectoryName,
            scenarioId,
            "Orchestrator.json");
        if (!File.Exists(filePath))
        {
            // graceful fallback for Editor scenarios
            FdpLog<GlobalContextClusterOpHandler>.Info(
                "[Orchestrator] CommitLoad: Orchestrator.json not found at '{0}'. Assuming fresh Editor scenario.", filePath);

            LoadedStartWallTicks = 0;
            LoadedScenarioTimeSeconds = 0;
            LoadedSceneId = string.Empty;
            LoadedScenarioId = scenarioId;

            // Seed the time controller with 0 so the scenario starts at the beginning
            OnContextLoaded?.Invoke(0, 0.0);
            return;

            //throw new InvalidOperationException(
            //    $"[Orchestrator] CommitLoad: Orchestrator.json not found at '{filePath}'. " +
            //    "Ensure PrefetchScenario completed before the LoadingLive/LoadingEdit transition.");
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var dto  = JsonSerializer.Deserialize<GlobalContextDto>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null)
            {
                throw new InvalidOperationException(
                    $"[Orchestrator] CommitLoad: Orchestrator.json at '{filePath}' deserialized to null. " +
                    "The file may be empty or structurally invalid.");
            }

            LoadedStartWallTicks        = dto.StartWallTicks;
            LoadedSceneId               = dto.SceneId;
            LoadedScenarioTimeSeconds   = dto.ScenarioTimeSeconds;
            LoadedScenarioId            = dto.ScenarioId;

            // Publish restored context over DDS so all nodes receive the scene information.
            _contextWriter.Write(new OrchestratorContextTopic
            {
                ScenarioId = dto.SceneId,
            });

            // Notify the hosting application so it can seed the time controller.
            OnContextLoaded?.Invoke(LoadedStartWallTicks, LoadedScenarioTimeSeconds);

            FdpLog<GlobalContextClusterOpHandler>.Info(
                "[Orchestrator] GlobalContext loaded: SceneId={0}, ScenarioId={1}, "
                + "WallTicks={2}, ScenarioTimeSeconds={3:F1}",
                dto.SceneId, dto.ScenarioId, dto.StartWallTicks, dto.ScenarioTimeSeconds);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            FdpLog<GlobalContextClusterOpHandler>.Error(
                "[Orchestrator] GlobalContext load failed: {0}", ex.Message);
            throw;
        }
    }

    private static Guid ParseExerciseId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return Guid.NewGuid();
        try
        {
            var dto = JsonSerializer.Deserialize<ArchivePayloadDto>(payloadJson, OrchestrationJsonOptions.Default);
            if (dto != null && dto.ExerciseId != Guid.Empty) return dto.ExerciseId;
        }
        catch { }
        return Guid.NewGuid();
    }

    private static string? ParseScenarioId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<NodeTransitionPayloadDto>(payloadJson, OrchestrationJsonOptions.Default);
            return dto?.ScenarioId;
        }
        catch { }
        return null;
    }

    private static ClusterState ParseTargetState(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return ClusterState.Idle;
        if (int.TryParse(payloadJson, out var n)) return (ClusterState)n;
        try
        {
            var dto = JsonSerializer.Deserialize<NodeTransitionPayloadDto>(payloadJson, OrchestrationJsonOptions.Default);
            if (dto?.TargetState != null) return dto.TargetState.Value;
        }
        catch { }
        return ClusterState.Idle;
    }
}

/// <summary>
/// Serializable DTO for the Orchestrator's global scenario context.
/// Written to <c>Orchestrator.json</c> during scenario save.
/// </summary>
public sealed class GlobalContextDto
{
    /// <summary>Simulation wall ticks at the moment of save (Stopwatch ticks).</summary>
    [JsonPropertyName("startWallTicks")]
    public long StartWallTicks { get; set; }

    /// <summary>Scene or map identifier active at the time of save.</summary>
    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    /// <summary>
    /// Elapsed simulation time in seconds at the moment of save.
    /// Used by consumers (e.g. <c>MasterTimeController.SeedState</c>) to resume
    /// scenario time from the correct offset after a load / checkpoint restore.
    /// </summary>
    [JsonPropertyName("scenarioTimeSeconds")]
    public double ScenarioTimeSeconds { get; set; }

    /// <summary>
    /// Scenario identifier that was active at the time of save
    /// (separate from <see cref="SceneId"/> which is the map/terrain identifier).
    /// </summary>
    [JsonPropertyName("scenarioId")]
    public string ScenarioId { get; set; } = string.Empty;

    /// <summary>Schema version for forward-compatibility guards.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;
}
