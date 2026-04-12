using System;
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
            PayloadJson   = $"{{\"scale\":{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
        });

    public void Dispose() => _writer.Dispose();
}
