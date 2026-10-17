using System;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Hrot.Core.Network;

/// <summary>
/// ⭐⭐⭐ <c>CE-3228</c> — the node's LOCAL request intake: every <see cref="EntityCreationRequest"/> published on this world's FDP bus this
/// frame. 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4d. It replaces the <c>ScenarioEntityCreationRequestSource</c> queue singleton:
/// every local author (a system, a UI tool, the debug API, scenario load) runs on the frame thread, so the queue's thread-safety served nobody
/// — 🔒 user: <i>"whoever wants to create an entity simply puts a request in the form of FDP bus event, and some system will create the entity
/// based on that."</i>
/// <para>⭐ It sits exactly where the queue sat — INSIDE <see cref="ForwardingEntityCreationRequestSource"/>, beside the wire source — so a
/// request that arrived from another node still never reaches the forwarder (§3.4b). ⚠ The bus is a broadcast (§3.4a): this is the ONE
/// reader of the request type, and <c>CreateEntityRequestSystem</c> drains it once per frame.</para>
/// </summary>
public sealed class BusEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly FdpEventBus _bus;

    public BusEntityCreationRequestSource(FdpEventBus bus) => _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    /// <inheritdoc/>
    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var requests = _bus.ReadManaged<EntityCreationRequest>();
        for (int i = 0; i < requests.Count; i++) handler(requests[i]);
    }
}
