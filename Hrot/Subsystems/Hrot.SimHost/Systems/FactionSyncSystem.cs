using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Hrot.IG.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Keeps Faction.FactionId synchronized with EntityInfo.ForceId.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class FactionSyncSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = view as EntityRepository;
            IEntityCommandBuffer? cmd = repo == null ? view.GetCommandBuffer() : null;

            var query = view.Query()
                .With<EntityInfo>()
                .With<Faction>()
                .Build();

            foreach (var entity in query)
            {
                ref readonly var info = ref view.GetComponentRO<EntityInfo>(entity);
                ref readonly var faction = ref view.GetComponentRO<Faction>(entity);

                byte expectedFactionId = info.ForceId switch
                {
                    ForceId.Friend => 1,
                    ForceId.Hostile => 2,
                    _ => 0
                };

                if (faction.FactionId != expectedFactionId)
                {
                    if (repo != null)
                    {
                        ref var rwFaction = ref repo.GetComponentRW<Faction>(entity);
                        rwFaction.FactionId = expectedFactionId;
                    }
                    else
                    {
                        cmd!.SetComponent(entity, new Faction { FactionId = expectedFactionId });
                    }
                }
            }
        }
    }
}
