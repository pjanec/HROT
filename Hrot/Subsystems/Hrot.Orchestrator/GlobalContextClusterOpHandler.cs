using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common.Orchestration;
using Hrot.Common.Scenario;
using Hrot.Network.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;

namespace Hrot.Orchestrator;

/// <summary>
/// Cluster handler that restores the Orchestrator node's own global context as part of a scenario
/// load round.
///
/// <para>
/// CE-278: the <b>save path</b> (<see cref="NodeOpType.SerializeLocal"/>, which wrote
/// <c>exercises/&lt;id&gt;/Orchestrator.json</c> for the retired SaveScenario=2 op) is removed. The
/// archived-exercise metadata sidecar is now written on the Export path by
/// <c>AssetInventoryProcessManager</c>. This handler serves only the load transition below.
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
    /// In production this resolves via <see cref="Fdp.Toolkit.Orchestration.OrchestrationConstants.ResolveStagingRoot"/>
    /// (NAS-mirror convention).
    /// </summary>
    public string LocalTempRoot { get; set; } = Fdp.Toolkit.Orchestration.OrchestrationConstants.ResolveStagingRoot();

    private readonly DdsWriter<OrchestratorContextTopic> _contextWriter;
    // CE-278: _scenarioId (the save-side scene id) retired with the SerializeLocal save arm; the load path
    // reads the scenario id from the command payload (ParseScenarioId), not from ctor state.
    private readonly ReadOnlyMigrationAdapter? _readOnlyAdapter;

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

    // CE-278: the save-side state (the ScenarioTimeSeconds input + the _pendingSave* fields) is retired
    // together with the SaveScenario=2 SerializeLocal arm. This handler now serves only the CommitState
    // load transition (CommitLoad); the exercise sidecar is written on the Export path by
    // AssetInventoryProcessManager (§5).

    /// <summary>
    /// Creates a <see cref="GlobalContextClusterOpHandler"/> for the given DDS participant
    /// and scenario identifier.
    /// </summary>
    /// <param name="participant">Participant used to publish <see cref="OrchestratorContextTopic"/>.</param>
    /// <param name="scenarioId">Scenario identifier stored in the global context file.</param>
    /// <param name="readOnlyAdapter">Optional migration adapter for reading Phase 2 enveloped JSON files.</param>
    public GlobalContextClusterOpHandler(DdsParticipant participant, string scenarioId, ReadOnlyMigrationAdapter? readOnlyAdapter = null)
    {
        _contextWriter  = new DdsWriter<OrchestratorContextTopic>(participant);
        _readOnlyAdapter = readOnlyAdapter;
    }

    /// <summary>Test-only constructor that accepts a pre-built writer.</summary>
    internal GlobalContextClusterOpHandler(DdsWriter<OrchestratorContextTopic> contextWriter, string scenarioId, ReadOnlyMigrationAdapter? readOnlyAdapter = null)
    {
        _contextWriter  = contextWriter;
        _readOnlyAdapter = readOnlyAdapter;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType op)
        => op == NodeOpType.CommitState;   // CE-278: SerializeLocal (SaveScenario=2 save) arm retired.

    /// <inheritdoc />
    /// <remarks>CE-278: no pre-work — the SerializeLocal (save) arm is retired. Always returns
    /// <see langword="null"/>.</remarks>
    public Task<string?> PrepareAsync(NodeOpCommand cmd, CancellationToken ct)
        => Task.FromResult<string?>(null);   // CE-278: SerializeLocal (save) arm retired; nothing to prepare.

    /// <inheritdoc />
    /// <remarks>
    /// For <see cref="NodeOpType.CommitState"/> heading to <see cref="ClusterState.LoadingLive"/> or
    /// <see cref="ClusterState.LoadingEdit"/>: loads the pre-fetched <c>Orchestrator.json</c> and
    /// publishes <see cref="OrchestratorContextTopic"/>. (CE-278: the SerializeLocal save arm is retired.)
    /// </remarks>
    public void Commit(NodeOpCommand cmd, EntityRepository? repo)
    {
        // CE-278: the SerializeLocal (SaveScenario=2) arm is retired; only the load transition remains.
        if (cmd.Operation == NodeOpType.CommitState)
        {
            var targetState = ParseTargetState(cmd.PayloadJson);
            if (targetState == ClusterState.LoadingLive || targetState == ClusterState.LoadingEdit)
                CommitLoad(cmd);
        }
    }

    /// <inheritdoc />
    public void Abort(NodeOpCommand cmd, EntityRepository? repo)
    {
        // CE-278: no pending save state to reset (the SerializeLocal save arm is retired).
    }

    // ── Private helpers ──────────────────────────────────────────────────────────
    // CE-278: CommitSerializeLocal + CommitManifestEntry (the SaveScenario=2 writer of
    // exercises/<id>/Orchestrator.json) are retired. The exercise metadata sidecar is now written on the
    // Export path by AssetInventoryProcessManager (§5); this handler keeps only the CommitLoad read path.

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
            string json;
            if (_readOnlyAdapter != null)
            {
                var outcome = _readOnlyAdapter.LoadAndMigrateAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
                json = outcome.AsJsonString();
            }
            else
            {
                json = File.ReadAllText(filePath);
            }
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

    // CE-278: ParseExerciseId retired with the SerializeLocal (SaveScenario=2) save arm.

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

}
