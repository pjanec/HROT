using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Common.Orchestration;

/// <summary>
/// ⭐⭐⭐ <c>CE-1436</c> (<c>R-3066</c>) — <b>the ONE ingress of the cluster's FACTS</b>: reads <see cref="ClusterStateTopic"/>, <see cref="AssetInventoryTopic"/>,
/// <see cref="ReplaySessionTopic"/> and <see cref="ClusterOpStatus"/> and publishes the events the node's <see cref="ClusterView"/> folds
/// (<see cref="ClusterStateUpdateEvent"/>, <see cref="AssetInventoryUpdateEvent"/>, <see cref="ReplaySessionUpdateEvent"/>, <see cref="ClusterOpCompletedEvent"/>),
/// each through ONE decoder (<see cref="StateOf"/>, <see cref="InventoryOf"/>, <see cref="DecodeResult"/>, <see cref="OpCompletedOf"/>).
/// <para>⭐ Owned by BOTH <see cref="NodeOpSlaveTranslator"/> (every ECS node) and <see cref="OrchestrationObserverTranslator"/> (ExCon): a host cannot
/// receive some facts and not others, and a field added on the wire reaches every reader. ⛔ The MONITORING topics (heartbeats, capabilities, the node-op
/// mirror) are NOT here — on an acting node those events drive its own egress and execution (design V2).</para>
/// 📄 docs/DESIGN_Cluster_View.md §3 V1.
/// </summary>
public sealed class ClusterFactsIngress : IDisposable
{
    private readonly FdpEventBus                      _bus;
    private readonly DdsReader<ClusterStateTopic>?    _stateReader;
    private readonly DdsReader<AssetInventoryTopic>?  _inventoryReader;
    private readonly DdsReader<ReplaySessionTopic>?   _sessionReader;
    private readonly DdsReader<ClusterOpStatus>?      _opStatusReader;
    private readonly DdsReader<OrchestratorContextTopic>? _contextReader;   // ⭐ CE-3344 — the master's in-sync set (R-4006), carried by the context plane
    private string? _lastInSyncJson;   // the set last published, so the echo of the master's own write on a shared bus ends after one round

    /// <summary>An ingress over the given readers; ⭐ a <see langword="null"/> reader (the test / offline shape) reads nothing.</summary>
    public ClusterFactsIngress(
        FdpEventBus                      bus,
        DdsReader<ClusterStateTopic>?    stateReader     = null,
        DdsReader<AssetInventoryTopic>?  inventoryReader = null,
        DdsReader<ReplaySessionTopic>?   sessionReader   = null,
        DdsReader<ClusterOpStatus>?      opStatusReader  = null,
        DdsReader<OrchestratorContextTopic>? contextReader = null)
    {
        _contextReader   = contextReader;
        _bus             = bus ?? throw new ArgumentNullException(nameof(bus));
        _stateReader     = stateReader;
        _inventoryReader = inventoryReader;
        _sessionReader   = sessionReader;
        _opStatusReader  = opStatusReader;
    }

    /// <summary>An ingress with all five readers on <paramref name="participant"/>.</summary>
    public static ClusterFactsIngress For(DdsParticipant participant, FdpEventBus bus)
    {
        if (participant is null) throw new ArgumentNullException(nameof(participant));
        return new ClusterFactsIngress(bus,
            new DdsReader<ClusterStateTopic>(participant),
            new DdsReader<AssetInventoryTopic>(participant),
            new DdsReader<ReplaySessionTopic>(participant),
            new DdsReader<ClusterOpStatus>(participant),
            new DdsReader<OrchestratorContextTopic>(participant));
    }

    /// <summary>Reads the four fact topics and publishes their events. Call once per frame, before the bus swap.</summary>
    public void Tick()
    {
        // ⭐ C2 (Q91 §4-I I5, R-4006) — ClusterStateTopic → ClusterStateUpdateEvent. A not-alive instance means the master's writer is gone.
        if (_stateReader != null)
        {
            using var l = _stateReader.Take();
            foreach (var s in l)
            {
                if (s.IsValid) _bus.PublishManaged(StateOf(s.Data));
                else if (s.Info.InstanceState != DdsInstanceState.Alive)
                    _bus.PublishManaged(new ClusterStateUpdateEvent { MasterGone = true });
            }
        }

        // ⭐ R-3055 — AssetInventoryTopic → AssetInventoryUpdateEvent
        if (_inventoryReader != null)
        {
            using var l = _inventoryReader.Take();
            foreach (var s in l)
                if (s.IsValid) _bus.PublishManaged(InventoryOf(s.Data));
        }

        // ⭐ CE-3329 rows 32, 45 — ReplaySessionTopic → ReplaySessionUpdateEvent (the session layer, §9e–§9f)
        if (_sessionReader != null)
        {
            using var l = _sessionReader.Take();
            foreach (var s in l)
                if (s.IsValid && Hrot.Network.Orchestration.ReplaySessionWire.FromTopic(s.Data) is { } session)
                    _bus.PublishManaged(new ReplaySessionUpdateEvent { Session = session });
        }

        // ⭐ CE-3344 (R-3069) — the master's InSync set (durable, one producer) → InSyncSetChangedEvent, so the node's ClusterView knows whether THIS node is in sync.
        //   An empty field = "not published yet" (the context topic is keyless and also carries the scenario).
        if (_contextReader != null)
        {
            using var l = _contextReader.Take();
            foreach (var s in l)
            {
                if (!s.IsValid || string.IsNullOrWhiteSpace(s.Data.RequiredNodeIdsJson) || s.Data.RequiredNodeIdsJson == _lastInSyncJson) continue;
                try
                {
                    var ids = JsonSerializer.Deserialize<int[]>(s.Data.RequiredNodeIdsJson, FdpJsonOptionsRegistry.DefaultRelaxed);
                    if (ids == null) continue;
                    _lastInSyncJson = s.Data.RequiredNodeIdsJson;
                    _bus.PublishManaged(new InSyncSetChangedEvent { NodeIds = ids });
                }
                catch (JsonException) { /* a malformed set says nothing */ }
            }
        }

        // ClusterOpStatus → ClusterOpCompletedEvent
        if (_opStatusReader != null)
        {
            using var l = _opStatusReader.Take();
            foreach (var s in l)
                if (s.IsValid) _bus.PublishManaged(OpCompletedOf(s.Data));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stateReader?.Dispose();
        _inventoryReader?.Dispose();
        _sessionReader?.Dispose();
        _opStatusReader?.Dispose();
        _contextReader?.Dispose();
    }

    // ── the ONE decoders ───────────────────────────────────────────────────

    /// <summary>The master's op status as the event its readers expect: the result JSON rehydrated by <see cref="DecodeResult"/>, kept as text when it is not JSON.</summary>
    internal static ClusterOpCompletedEvent OpCompletedOf(in ClusterOpStatus status)
    {
        object? payload = null;
        if (!string.IsNullOrWhiteSpace(status.ResultJson))
        {
            try { payload = DecodeResult(status.ResultJson); }
            catch { payload = status.ResultJson; }
        }
        return new ClusterOpCompletedEvent
        {
            RequestId     = status.RequestId,
            StatusCode    = (OrchestrationStatusCode)status.StatusCode,
            ResultPayload = payload,
        };
    }

    /// <summary>
    /// The master's op result as the typed payload its readers expect: ⭐ CE-4093 an <see cref="Fdp.Toolkit.Orchestration.UnsavedCheckResult"/> (recognised by its
    /// one distinctive property — ⛔ the replay result below deserialises "successfully" from ANY JSON object and would swallow it), else the replay
    /// result <c>ClusterUiCache</c> reads. Throws <see cref="JsonException"/> for text that is not JSON (a refusal's reason): the caller keeps the text.
    /// </summary>
    internal static object? DecodeResult(string resultJson)
    {
        using (var doc = JsonDocument.Parse(resultJson))
            if (Fdp.Toolkit.Orchestration.UnsavedCheckResult.LooksLike(doc.RootElement))
                return JsonSerializer.Deserialize<Fdp.Toolkit.Orchestration.UnsavedCheckResult>(resultJson, FdpJsonOptionsRegistry.DefaultRelaxed);
        // Rehydrate the specific DTO expected by ClusterUiCache
        return JsonSerializer.Deserialize<Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult>(resultJson, FdpJsonOptionsRegistry.DefaultRelaxed);
    }

    /// <summary>⭐ CE-3329 — the ONE decoding of the master's cluster-state broadcast: a field added on the wire reaches every reader.</summary>
    internal static ClusterStateUpdateEvent StateOf(in ClusterStateTopic topic) => new()
    {
        CurrentState = (FdpClusterState)(int)topic.CurrentState,
        ExerciseId   = topic.ExerciseId,
        SessionId    = topic.SessionId,   // ⭐ C2
        ReplayAxis   = (Fdp.Toolkit.Orchestration.ReplayAxis)topic.ReplayAxis,   // ⭐ CE-3329 R-3058
        RunNotRecorded = topic.RunNotRecorded != 0,   // ⭐ CE-4090 D14
        RecordLiveRunsOff = topic.RecordLiveRunsOff != 0,
        ArmedMode = ArmedModeOf(topic.ArmedMode),   // ⭐ CE-3341 R-3068
        LastLoadMode = ArmedModeOf(topic.LastLoadMode),   // ⭐ R-1043
        WaitingFor = topic.WaitingFor ?? string.Empty,   // ⭐ CE-3344 R-3069
        ErrorReason = topic.ErrorReason ?? string.Empty,   // ⭐ CE-3345 R-3073
        ClusterPlayReady = topic.ClusterPlayReady,         // ⭐ CE-3345 R-3070
        LoadProgress = topic.LoadProgress ?? string.Empty,
        AskToKeepRecordings = topic.AskToKeepRecordings != 0,   // ⭐ AQ108 D15 (CE-4100 ①)
    };

    /// <summary>⭐ CE-3341 — an armed-mode wire int; a value this build does not know (a newer peer) reads as the default, Live.</summary>
    private static Fdp.Toolkit.Orchestration.ClusterArmedMode ArmedModeOf(int wire)
        => wire is >= 0 and <= (int)Fdp.Toolkit.Orchestration.ClusterArmedMode.AAR
            ? (Fdp.Toolkit.Orchestration.ClusterArmedMode)wire : Fdp.Toolkit.Orchestration.ClusterArmedMode.Live;

    /// <summary>⭐ R-3055 — the ONE decoding of the master's inventory sample.</summary>
    internal static AssetInventoryUpdateEvent InventoryOf(in AssetInventoryTopic data) => new()
    {
        LocalScenarios           = DeserializeStringArray(data.LocalScenariosJson),
        LocalExercises           = DeserializeExerciseArray(data.LocalExercisesJson),
        ArchivedExercises        = DeserializeExerciseArray(data.ArchivedExercisesJson),
        UnarchivedLocalExercises = DeserializeExerciseArray(data.UnarchivedLocalExercisesJson),
    };

    internal static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json, FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static ExerciseInventoryItem[] DeserializeExerciseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ExerciseInventoryItem>();
        try { return JsonSerializer.Deserialize<ExerciseInventoryItem[]>(json, FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<ExerciseInventoryItem>(); }
        catch { return Array.Empty<ExerciseInventoryItem>(); }
    }
}
