using System;
using System.Threading;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using ModuleHost.Network.Cyclone.Services;

namespace Hrot.ClusterRunner.Infrastructure;

/// <summary>
/// Shared helper for ensuring the DDS ID allocator has an active publication match
/// from a running <c>Hrot.Orchestrator</c> server before the node proceeds.
/// </summary>
public static class DdsIdAllocatorHelper
{
    /// <summary>
    /// Waits up to 30 s for the remote DDS ID allocator server (hosted by <c>Hrot.Orchestrator</c>)
    /// to announce publication.  Throws <see cref="InvalidOperationException"/> if the server is not
    /// found within the timeout — the node must not start without a working allocator.
    /// </summary>
    /// <param name="participant">Live DDS participant (used only for diagnostics context).</param>
    /// <param name="idAllocator">The allocator whose publication match is awaited.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no publication match is established within 30 seconds.
    /// </exception>
    public static void EnsureRouting(DdsParticipant participant, DdsIdAllocator idAllocator)
    {
        if (idAllocator == null) return;
        const int MaxWaitSeconds = 30;
        const int WarnAtSeconds  = 5;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(MaxWaitSeconds);
        var warnAt   = DateTime.UtcNow + TimeSpan.FromSeconds(WarnAtSeconds);
        bool warned  = false;
        while (DateTime.UtcNow < deadline)
        {
            if (idAllocator.HasPublicationMatch)
                return;
            if (!warned && DateTime.UtcNow >= warnAt)
            {
                FdpLog<DdsIdAllocator>.Warn(
                    "[DdsIdAllocator] No remote orchestrator server after {0} s — still waiting " +
                    "(up to {1:F0} s total). Verify that Hrot.Orchestrator is running.",
                    WarnAtSeconds, MaxWaitSeconds);
                warned = true;
            }
            Thread.Sleep(50);
        }

        if (idAllocator.HasPublicationMatch)
            return;

        throw new InvalidOperationException(
            $"[DdsIdAllocator] Publication match not established within {MaxWaitSeconds} s. " +
            "Hrot.Orchestrator must be running before this node starts.");
    }
}
