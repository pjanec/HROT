using System.Collections.Concurrent;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class HitFlashSystem : ComponentSystem
    {
        private EntityQuery _query = null!;

        public HitFlashSystem(EntityRepository repo)
        {
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<HitFlash>()
                .With<RenderSymbol>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime;
            
            var toRemoveQueue = new ConcurrentQueue<Entity>();
            
            _query.ForEachParallel(entity =>
            {
                ref var flash = ref World.GetComponentRW<HitFlash>(entity);
                
                // Update flash timer
                flash.Remaining -= dt;
                
                if (flash.Remaining <= 0)
                {
                    toRemoveQueue.Enqueue(entity);
                }
            });
            
            // Remove expired flash components
            while (toRemoveQueue.TryDequeue(out var entity))
            {
                if (World.HasComponent<HitFlash>(entity))
                {
                    World.RemoveComponent<HitFlash>(entity);
                }
            }
        }
    }
}
