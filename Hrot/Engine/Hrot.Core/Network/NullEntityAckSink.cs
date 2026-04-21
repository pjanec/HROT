using System;

namespace Hrot.Core.Network;

/// <summary>
/// No-op <see cref="IEntityAckSink"/> used when the genesis pipeline runs in
/// offline mode (no live network transport available).
///
/// <para>Discards all ACK writes silently, satisfying the interface contract
/// without requiring a real DDS/NED participant.  The
/// <see cref="Hrot.CGF.CgfSubsystem"/> supplies an instance of this class when
/// <c>CreateCgfEntityLifecycleAdapters()</c> returns <c>null</c>, ensuring the
/// <see cref="Systems.CreateEntityRequestSystem"/> is registered unconditionally
/// in all environments (online and offline).</para>
/// </summary>
public sealed class NullEntityAckSink : IEntityAckSink
{
    /// <inheritdoc/>
    /// <remarks>No-op: offline mode has no recipient for ACK messages.</remarks>
    public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status) { }
}
