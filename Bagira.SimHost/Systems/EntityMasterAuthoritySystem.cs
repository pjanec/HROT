using Bagira.BDC.SSTD;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Systems;

/// <summary>
/// Ensures locally-owned entities mark authority for EntityMaster so egress
/// translators can publish the descriptor.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class EntityMasterAuthoritySystem : IModuleSystem
{
    private readonly int _localNodeId;

    public EntityMasterAuthoritySystem(int localNodeId)
        => _localNodeId = localNodeId;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            return;

        var query = repo.Query()
            .With<EntityMaster>()
            .With<NetworkAuthority>()
            .Build();

        foreach (var entity in query)
        {
            ref readonly var auth = ref repo.GetComponentRO<NetworkAuthority>(entity);
            if (auth.PrimaryOwnerId != _localNodeId)
                continue;

            if (!repo.HasAuthority<EntityMaster>(entity))
                repo.SetAuthority<EntityMaster>(entity, hasAuthority: true);
        }
    }
}
