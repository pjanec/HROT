using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;

namespace Hrot.Core.Network;

/// <summary>
/// Provides IG-specific DDS ingress translators.
/// </summary>
public interface IIgTranslators
{
    /// <summary>
    /// Creates all IG DDS ingress translators for the given session.
    /// Returns an empty list in headless mode or when NED is not available.
    /// </summary>
    IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless);
}

/// <summary>No-op stub for <see cref="IIgTranslators"/> (headless/offline mode).</summary>
public sealed class NullIgTranslators : IIgTranslators
{
    public IReadOnlyList<IDescriptorTranslator> GetTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        FdpEventBus bus,
        GhostCreationSystem? ghostCreationSystem,
        long localNodeId,
        bool headless)
        => System.Array.Empty<IDescriptorTranslator>();
}
