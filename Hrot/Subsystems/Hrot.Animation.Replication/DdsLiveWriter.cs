using CycloneDDS.Runtime;
using Fdp.Network.Cyclone;

namespace Hrot.Animation.Replication;

// =============================================================================
// DDS writer abstraction enabling unit-test doubles without a live DDS participant.
// =============================================================================

/// <summary>
/// Thin write-only abstraction over DDS so egress translators can be tested
/// without a live DDS participant (inject a CapturingWriter in tests).
/// </summary>
internal interface IAnimDdsWriter<T>
{
    void Write(T sample);
}

/// <summary>
/// Production implementation — wraps a real <see cref="DdsWriter{T}"/>.
/// When <paramref name="participant"/> is <c>null</c> (unit-test mode) no
/// <see cref="DdsWriter{T}"/> is created; <see cref="Write"/> becomes a no-op.
/// </summary>
internal sealed class DdsLiveWriter<T> : IAnimDdsWriter<T>
    where T : new()
{
    private readonly DdsWriter<T>? _inner;

    internal DdsLiveWriter(DdsParticipant? participant, string topicName)
        => _inner = participant is not null ? new DdsWriter<T>(participant, topicName) : null;

    public void Write(T sample) => _inner?.Write(sample);
}
