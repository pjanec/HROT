using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class SubEntityCleanupSystem : IModuleSystem
    {
        private EntityQuery? _partQuery;

        public void Execute(ISimulationView view, float dt)
        {
            // Main-thread PostSimulation: safe to cast.
            if (view is not EntityRepository repo) return;

            EnsureQueriesInitialized(repo);

            using var ecb = new EntityCommandBuffer();

            foreach (var entity in _partQuery!)
            {
                var meta = repo.GetComponent<PartMetadata>(entity);
                if (!repo.IsAlive(meta.ParentEntity))
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(repo);
        }

        private void EnsureQueriesInitialized(EntityRepository repo)
        {
            if (_partQuery != null) return;

            _partQuery = repo.Query()
                .With<PartMetadata>()
                .Build();
        }
    }
}
