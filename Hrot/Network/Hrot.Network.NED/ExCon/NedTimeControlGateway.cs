using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.NED.ExCon;

/// <summary>
/// Implements <see cref="ITimeControlGateway"/> by publishing
/// <see cref="ClusterOpRequest"/> messages over DDS.
/// </summary>
public sealed class NedTimeControlGateway : ITimeControlGateway
{
    private readonly DdsWriter<ClusterOpRequest> _writer;

    public NedTimeControlGateway(DdsParticipant participant)
    {
        if (participant == null) throw new ArgumentNullException(nameof(participant));
        _writer = new DdsWriter<ClusterOpRequest>(participant, "ClusterOpRequest");
    }

    /// <inheritdoc/>
    public void RequestPause()
        => _writer.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.PauseTime,
            PayloadJson   = "{}",
        });

    /// <inheritdoc/>
    public void RequestResume()
        => _writer.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ResumeTime,
            PayloadJson   = "{}",
        });

    /// <inheritdoc/>
    public void RequestStep()
        => _writer.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.StepTime,
            PayloadJson   = "{}",
        });

    /// <inheritdoc/>
    public void SetTimeScale(float scale)
        => _writer.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.SetTimeScale,
            PayloadJson   = SetTimeScalePayload(scale),
        });

    /// <summary>
    /// ⭐ <c>CE-3191</c> — the <c>SetTimeScale</c> payload: the object the master reads (<c>SetTimeScalePayloadDto</c>,
    /// <c>ClusterMaster</c>). ⛔ It used to be the bare number, which failed to bind there; the error was swallowed and the scale
    /// fell back to 1.0, so the ExCon time-scale control never changed the scale.
    /// </summary>
    public static string SetTimeScalePayload(float scale)
        => JsonSerializer.Serialize(new Hrot.Network.Orchestration.SetTimeScalePayloadDto(scale),
                                    Hrot.Network.Orchestration.OrchestrationJsonOptions.Default);

    public void Dispose() => _writer.Dispose();
}
