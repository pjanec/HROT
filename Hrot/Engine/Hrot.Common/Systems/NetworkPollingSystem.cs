using System;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Common.Systems;

/// <summary>
/// ⭐ Runs a network adapter's per-frame poll on the main thread — today the entity-lifecycle adapters'
/// <c>PollNetwork</c>, which keeps the cluster cache the ownership strategy reads up to date
/// (<c>CE-509</c>; <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S2).
/// <para>⚠ CGF and IG poll the same action from their application loops; SimHost and Stride have no such loop
/// hook, so they register this system instead. One per node.</para>
/// </summary>
[UpdateInPhase(SystemPhase.Input)]
public sealed class NetworkPollingSystem : IEcsModuleSystem
{
    private readonly Action _poll;

    public NetworkPollingSystem(Action poll) => _poll = poll ?? throw new ArgumentNullException(nameof(poll));

    public void Execute(ISimulationView view, float dt) => _poll();
}
