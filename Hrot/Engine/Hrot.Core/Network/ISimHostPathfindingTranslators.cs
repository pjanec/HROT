using System;
using Fdp.ModuleHost;

namespace Hrot.Core.Network;

/// <summary>
/// Represents the set of pathfinding DDS translators for SimHost nodes
/// returned by <see cref="INetworkFactory.CreateSimHostPathfindingTranslators"/>.
/// </summary>
public interface ISimHostPathfindingTranslators : IDisposable
{
    /// <summary>
    /// Registers all pathfinding translator systems (ingress, egress, cleanup) on the given kernel.
    /// </summary>
    void RegisterOn(ModuleHostKernel kernel);
}
