using FDP.Toolkit.Replication.Services;
using Fdp.Kernel;
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
    /// Creates a NedNetworkFactory with a null participant. When passed to
    /// <see cref="IgApplication.InitializeEmbedded"/>, the factory is reconfigured
    /// via <c>ConfigureForNode(_context)</c> so the replication module uses the
    /// context's entityMap and bus. A live participant is then created in
    /// InitializeNetwork (headless path) or picked from HrotNodeBuilder (non-headless).
    /// </summary>
    internal static Hrot.Core.Network.INetworkFactory CreateHeadless()
        => new NedNetworkFactory(
            participant:  null,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.ImageGenerator);
}
