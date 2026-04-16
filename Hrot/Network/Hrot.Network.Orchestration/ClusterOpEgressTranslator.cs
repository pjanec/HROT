using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using NedClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpClusterState  = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Common.Orchestration;

/// <summary>
/// Anti-Corruption Layer egress translator for cluster-level commands.
///
/// <para>Consumes canonical typed intent events from the <see cref="FdpEventBus"/>
/// read buffer and writes the corresponding <see cref="ClusterOpRequest"/> DDS messages
/// to the Orchestrator.  Supported intents: <see cref="PauseTimeIntent"/>,
/// <see cref="ResumeTimeIntent"/>, <see cref="StepTimeIntent"/>,
/// <see cref="SetTimeScaleIntent"/>, <see cref="TransitionStateIntent"/>,
/// <see cref="ManageEpisodeIntent"/>, <see cref="ExecuteStorageOpIntent"/>,
/// <see cref="TakeCheckpointIntent"/>, <see cref="SeekReplayIntent"/>,
/// <see cref="CancelOperationIntent"/>.</para>
///
/// <para>This is the <b>only</b> class in the ExCon cluster-op egress stack that
/// is permitted to call <c>System.Text.Json.JsonSerializer</c>.</para>
///
/// <para>Call <see cref="Tick"/> once per frame after the bus <c>SwapBuffers</c>
/// so that intents published in the previous frame are dispatched in this frame.</para>
/// </summary>
public sealed class ClusterOpEgressTranslator : IDisposable
{
    private readonly FdpEventBus                  _bus;
    private readonly DdsWriter<ClusterOpRequest>  _writer;

    /// <summary>
    /// Creates a new translator that consumes from <paramref name="bus"/> and writes
    /// to a <see cref="DdsWriter{T}"/> created on <paramref name="participant"/>.
    /// </summary>
    public ClusterOpEgressTranslator(FdpEventBus bus, DdsParticipant participant)
    {
        _bus    = bus    ?? throw new ArgumentNullException(nameof(bus));
        _writer = new DdsWriter<ClusterOpRequest>(participant
            ?? throw new ArgumentNullException(nameof(participant)));
    }

    /// <summary>
    /// Drains all queued typed intent events from the bus and writes the corresponding
    /// <see cref="ClusterOpRequest"/> DDS messages.
    /// Call once per frame after the bus <c>SwapBuffers</c>.
    /// </summary>
    public void Tick()
    {
        foreach (var _ in _bus.ReadManaged<PauseTimeIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = NedClusterOpType.PauseTime,
                PayloadJson   = string.Empty,
            });

        foreach (var _ in _bus.ReadManaged<ResumeTimeIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = NedClusterOpType.ResumeTime,
                PayloadJson   = string.Empty,
            });

        foreach (var intent in _bus.ReadManaged<StepTimeIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = NedClusterOpType.StepTime,
                PayloadJson   = intent.DeltaSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        foreach (var intent in _bus.ReadManaged<SetTimeScaleIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = NedClusterOpType.SetTimeScale,
                PayloadJson   = intent.TimeScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.TransactionId,
                OperationType = NedClusterOpType.TransitionState,
                PayloadJson   = SerializeTransitionPayload(intent),
            });

        foreach (var intent in _bus.ReadManaged<ManageEpisodeIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.TransactionId,
                OperationType = NedClusterOpType.ManageEpisode,
                PayloadJson   = SerializeManageEpisodePayload(intent),
            });

        foreach (var intent in _bus.ReadManaged<ExecuteStorageOpIntent>())
        {
            NedClusterOpType opType = intent.Operation switch
            {
                StorageOpType.Export       => NedClusterOpType.ExportArchive,
                StorageOpType.Import       => NedClusterOpType.ImportArchive,
                StorageOpType.SaveScenario => NedClusterOpType.SaveScenario,
                _                          => NedClusterOpType.SaveScenario,
            };
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.RequestId,
                OperationType = opType,
                PayloadJson   = intent.ExerciseId ?? string.Empty,
            });
        }

        foreach (var intent in _bus.ReadManaged<TakeCheckpointIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.RequestId,
                OperationType = NedClusterOpType.TakeCheckpoint,
                PayloadJson   = string.Empty,
            });

        foreach (var intent in _bus.ReadManaged<SeekReplayIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.RequestId,
                OperationType = NedClusterOpType.ReplaySeek,
                PayloadJson   = $"{{\"TargetWallTicks\":{intent.TargetWallTicks}}}",
            });

        foreach (var intent in _bus.ReadManaged<CancelOperationIntent>())
            _writer.Write(new ClusterOpRequest
            {
                RequestId     = intent.TargetRequestId,
                OperationType = NedClusterOpType.CancelOperation,
                PayloadJson   = string.Empty,
            });
    }

    /// <inheritdoc/>
    public void Dispose() => _writer.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string SerializeTransitionPayload(TransitionStateIntent intent)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{{\"TargetState\":{(int)intent.TargetState}");
        if (intent.ScenarioId != null) sb.Append($",\"ScenarioId\":\"{intent.ScenarioId}\"");
        if (intent.ExerciseId != null) sb.Append($",\"ExerciseId\":\"{intent.ExerciseId}\"");
        if (intent.TimeMode   != null) sb.Append($",\"TimeMode\":\"{intent.TimeMode}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string SerializeManageEpisodePayload(ManageEpisodeIntent intent)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{{\"IsStart\":{(intent.IsStart ? "true" : "false")},\"EpisodeId\":\"{intent.EpisodeId}\"");
        if (intent.ScenarioId != null) sb.Append($",\"ScenarioId\":\"{intent.ScenarioId}\"");
        sb.Append('}');
        return sb.ToString();
    }
}
