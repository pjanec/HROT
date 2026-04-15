using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Hrot.Common.Orchestration;
using Hrot.Core.Network;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// DDS-backed implementation of <see cref="IOrchestrationObserver"/> that wraps
/// <see cref="OrchestrationObserverTranslator"/>.
/// Created and returned by <see cref="NedNetworkFactory.CreateOrchestrationObserver"/>.
/// </summary>
internal sealed class NedOrchestrationObserver : IOrchestrationObserver
{
    private readonly OrchestrationObserverTranslator _inner;

    public NedOrchestrationObserver(DdsParticipant participant, FdpEventBus bus)
    {
        _inner = new OrchestrationObserverTranslator(
            participant ?? throw new ArgumentNullException(nameof(participant)),
            bus         ?? throw new ArgumentNullException(nameof(bus)));
    }

    /// <inheritdoc/>
    public void Tick() => _inner.Tick();

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();
}
