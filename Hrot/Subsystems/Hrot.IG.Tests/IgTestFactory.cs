using CycloneDDS.Runtime;
using Fdp.Toolkit.Replication.Services;
using Fdp.Core;
using Hrot.Common;
using Hrot.Map.Common;
using Hrot.Network.NED.Factory;

namespace Hrot.IG.Tests;

/// <summary>
/// Test helper that creates a headless NedNetworkFactory for IG unit tests
/// that need replication without a live DDS domain pre-created.
/// </summary>
internal static class IgTestFactory
{
    /// <summary>
    /// Creates a NedNetworkFactory for headless test use.
    /// When <paramref name="domainId"/> is provided, a real <see cref="DdsParticipant"/>
    /// is created so that DDS ingress/egress works in integration tests.
    /// When omitted (null), the participant is null and the factory operates
    /// offline (structural wiring checks only; no DDS communication).
    /// </summary>
    internal static Hrot.Core.Network.INetworkFactory CreateHeadless(int? domainId = null)
        => new NedNetworkFactory(
            participant:  domainId.HasValue ? new DdsParticipant((uint)domainId.Value) : null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.ImageGenerator);
}
