using System;
using System.Collections.Generic;
using ModuleHost.Core;

namespace Hrot.Core.Network;

/// <summary>
/// Represents the set of auxiliary SimHost DDS translators (time-sync, combat, mission-control)
/// returned by <see cref="INetworkFactory.CreateSimHostAuxiliaryTranslators"/>.
/// </summary>
public interface ISimHostAuxiliaryTranslators : IDisposable
{
    /// <summary>
    /// Registers all auxiliary translator systems (ingress, egress, cleanup) on the given kernel.
    /// </summary>
    void RegisterOn(ModuleHostKernel kernel);
}
