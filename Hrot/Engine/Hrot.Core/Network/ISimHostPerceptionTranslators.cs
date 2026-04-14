using System;
using Fdp.ModuleHost.Core;

namespace Hrot.Core.Network;

/// <summary>
/// Represents the set of perception DDS translators for SimHost nodes
/// returned by <see cref="INetworkFactory.CreateSimHostPerceptionTranslators"/>.
/// </summary>
public interface ISimHostPerceptionTranslators : IDisposable
{
    /// <summary>
    /// Registers all perception translator systems (ingress, egress, cleanup) on the given kernel.
    /// </summary>
    void RegisterOn(ModuleHostKernel kernel);
}
