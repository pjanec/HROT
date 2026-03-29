using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace Bagira.Orchestrator;

/// <summary>
/// DSM handler that serializes/deserializes the Orchestrator node's own global context
/// as part of a scenario save/load round.
///
/// <para>
/// <b>Save path</b> (<see cref="NodeOpType.SerializeLocal"/>): Writes
/// <c>GlobalContextDto</c> (simulation start wall ticks, scene identifier) to
/// <c>C:\FDP_Temp\&lt;DrillId&gt;\Orchestrator.json</c>.
/// Returns the file path as a single-entry <see cref="FileManifestEntry"/>
/// in <c>NodeOpStatus.ResultJson</c>.
/// </para>
///
/// <para>
/// <b>Load path</b> (<see cref="NodeOpType.CommitState"/> for
/// <see cref="DSMState.LoadingLive"/> or <see cref="DSMState.LoadingEdit"/>):
/// Parses the pre-fetched <c>Orchestrator.json</c> and populates
/// <see cref="LoadedStartWallTicks"/> / <see cref="LoadedSceneId"/> for
/// the hosting application to consume (e.g. <c>MasterTimeController.SeedState</c>).
/// Also publishes an updated <c>OrchestratorContextTopic</c> over DDS.
/// The hosting application is responsible for calling
/// <c>MasterTimeController.SeedState(LoadedStartWallTicks)</c> after this handler
/// completes.
/// </para>
/// </summary>
public sealed class GlobalContextDsmHandler : IDsmHandler
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
    /// Consumers (e.g. <c>DrillMaster</c> startup) should call
    /// <c>MasterTimeController.SeedState</c> after load.
    /// </summary>
    public long LoadedStartWallTicks { get; private set; }

    /// <summary>
    /// Scene identifier read from the most recently loaded <c>Orchestrator.json</c>.
    /// </summary>
    public string? LoadedSceneId { get; private set; }

    /// <summary>
    /// Pending save ticks — populated during <see cref="PrepareAsync"/> for the
    /// SerializeLocal command so <see cref="Commit"/> can write the file.
    /// </summary>
    private long _pendingSaveWallTicks;
    private string? _pendingSaveSceneId;
    private string? _pendingFilePath;

    /// <summary>
    /// Creates a <see cref="GlobalContextDsmHandler"/> for the given DDS participant
    /// and scenario identifier.
    /// </summary>
    /// <param name="participant">Participant used to publish <see cref="OrchestratorContextTopic"/>.</param>
    /// <param name="scenarioId">Scenario identifier stored in the global context file.</param>
    public GlobalContextDsmHandler(DdsParticipant participant, string scenarioId)
    {
        _contextWriter = new DdsWriter<OrchestratorContextTopic>(participant);
        _scenarioId    = scenarioId ?? string.Empty;
    }

    /// <summary>Test-only constructor that accepts a pre-built writer.</summary>
    internal GlobalContextDsmHandler(DdsWriter<OrchestratorContextTopic> contextWriter, string scenarioId)
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
            // Determine the drill ID from the payload (if provided) or generate one.
            var drillId = ParseDrillId(cmd.PayloadJson);
            var dir     = Path.Combine(LocalTempRoot, drillId.ToString("N"));
            _pendingFilePath   = Path.Combine(dir, "Orchestrator.json");
            _pendingSaveWallTicks = DateTimeOffset.UtcNow.Ticks;   // wall-clock snapshot at prepare time
            _pendingSaveSceneId   = _scenarioId;
        }
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// For <see cref="NodeOpType.SerializeLocal"/>: writes <c>Orchestrator.json</c> and
    /// returns the manifest entry path via a side-channel (callers read <see cref="CommitManifestEntry"/>).
    /// For <see cref="NodeOpType.CommitState"/> heading to <see cref="DSMState.LoadingLive"/> or
    /// <see cref="DSMState.LoadingEdit"/>: loads the pre-fetched <c>Orchestrator.json</c> and
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
            if (targetState == DSMState.LoadingLive || targetState == DSMState.LoadingEdit)
                CommitLoad(cmd);
        }
    }

    /// <inheritdoc />
    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
    {
        // Reset pending state; no I/O was committed.
        _pendingFilePath     = null;
        _pendingSaveWallTicks = 0;
        _pendingSaveSceneId  = null;
    }

    // ── Manifest output (read by DrillMaster after Commit) ───────────────────────

    /// <summary>
    /// Set by <see cref="Commit"/> after a successful <see cref="NodeOpType.SerializeLocal"/>
    /// operation.  <see cref="Bagira.Orchestrator.DrillMaster"/> reads this to build the
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
                StartWallTicks = _pendingSaveWallTicks,
                SceneId        = _pendingSaveSceneId ?? string.Empty,
                SchemaVersion  = 1,
            };

            var json = JsonSerializer.Serialize(dto,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pendingFilePath, json);

            CommitManifestEntry = new FileManifestEntry
            {
                SourceUnc    = _pendingFilePath,
                RelativeDest = Path.GetFileName(_pendingFilePath),
            };

            FdpLog<GlobalContextDsmHandler>.Info(
                "[Orchestrator] GlobalContext serialized → {0}", _pendingFilePath);
        }
        catch (Exception ex)
        {
            FdpLog<GlobalContextDsmHandler>.Error(
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
            FdpLog<GlobalContextDsmHandler>.Info(
                "[Orchestrator] CommitLoad: no ScenarioId in payload — skipping context restore (blank world).");
            return;
        }

        var filePath = Path.Combine(LocalTempRoot, scenarioId, "Orchestrator.json");
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException(
                $"[Orchestrator] CommitLoad: Orchestrator.json not found at '{filePath}'. " +
                "Ensure PrefetchScenario completed before the LoadingLive/LoadingEdit transition.");
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

            LoadedStartWallTicks = dto.StartWallTicks;
            LoadedSceneId        = dto.SceneId;

            // Publish restored context over DDS so all nodes receive the scene information.
            _contextWriter.Write(new OrchestratorContextTopic
            {
                ScenarioId = dto.SceneId,
            });

            FdpLog<GlobalContextDsmHandler>.Info(
                "[Orchestrator] GlobalContext loaded: SceneId={0}, WallTicks={1}",
                dto.SceneId, dto.StartWallTicks);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            FdpLog<GlobalContextDsmHandler>.Error(
                "[Orchestrator] GlobalContext load failed: {0}", ex.Message);
            throw;
        }
    }

    private static Guid ParseDrillId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return Guid.NewGuid();
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("DrillId", out var prop))
            {
                var raw = prop.GetString();
                if (Guid.TryParse(raw, out var g)) return g;
            }
        }
        catch { }
        return Guid.NewGuid();
    }

    private static string? ParseScenarioId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("ScenarioId", out var prop))
                return prop.GetString();
        }
        catch { }
        return null;
    }

    private static DSMState ParseTargetState(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return DSMState.Standby;
        if (int.TryParse(payloadJson, out var n)) return (DSMState)n;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("TargetState", out var prop))
                return (DSMState)prop.GetInt32();
        }
        catch { }
        return DSMState.Standby;
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

    /// <summary>Schema version for forward-compatibility guards.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
}
