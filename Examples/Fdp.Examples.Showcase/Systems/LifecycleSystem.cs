using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    /// <summary>
    /// Lifecycle System - The "End Frame Barrier"
    /// Owns the shared EntityCommandBuffer that other systems write to.
    /// Runs LAST to apply all structural changes (destroy, create, add/remove components)
    /// after all logic systems have completed, ensuring a consistent world view.
    /// </summary>
    public class LifecycleSystem : ComponentSystem
    {
        // The shared buffer - other systems will access this
        public EntityCommandBuffer CommandBuffer { get; private set; }
        private EntityQuery _corpseQuery = null!;

        public LifecycleSystem(EntityRepository repo)
        {
            Create(repo);
            // Initialize with reasonable capacity to avoid resizing
            CommandBuffer = new EntityCommandBuffer(4096);
        }

        protected override void OnCreate()
        {
            _corpseQuery = World.Query()
                .With<Corpse>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime;
            
            // Process corpses - count down their timers
            var toRemove = new System.Collections.Generic.List<Entity>();
            foreach (var entity in _corpseQuery)
            {
                ref var corpse = ref World.GetComponentRW<Corpse>(entity);
                corpse.TimeRemaining -= dt;
                
                if (corpse.TimeRemaining <= 0)
                {
                    // Timer expired - queue for destruction
                    toRemove.Add(entity);
                }
            }
            
            // Queue expired corpses for destruction
            foreach (var entity in toRemove)
            {
                CommandBuffer.DestroyEntity(entity);
            }
            
            // Apply all queued commands from this frame
            // This is the ONLY place where structural changes happen
            if (!CommandBuffer.IsEmpty)
            {
                CommandBuffer.Playback(World);
                // Playback clears the buffer automatically in FDP Kernel
            }
        }
        
        // Cleanup
        protected override void OnDestroy()
        {
            CommandBuffer.Dispose();
        }
    }
}
