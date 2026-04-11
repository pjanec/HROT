using System;

namespace Fdp.Kernel.Systems
{
    /// <summary>
    /// Validates 'Constructing' entities and promotes them to 'Active' when ready.
    /// Runs at the end of the frame.
    /// </summary>
    public class EntityValidationSystem : ComponentSystem
    {
        private EntityQuery _pendingEntities = null!;
        private int _frameCount;

        // Timeout in Seconds
        public const float MaxConstructionTime = 5.0f;

        protected override void OnCreate()
        {
            // Find entities that have LifecycleDescriptor but NO IsActiveTag
            _pendingEntities = World.Query()
                .With<LifecycleDescriptor>()
                .Without<IsActiveTag>() 
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();
        }

        protected override void OnUpdate()
        {
            _frameCount++;

            float dt = DeltaTime; // from ComponentSystem

            // 1. Validation Logic
            foreach (var entity in _pendingEntities)
            {
                ref var lifecycle = ref World.GetComponentRW<LifecycleDescriptor>(entity);
                
                // Check if all required modules have ACKed
                if ((lifecycle.RequiredModulesMask & lifecycle.AckedModulesMask) == lifecycle.RequiredModulesMask)
                {
                    // Transition to Active State
                    lifecycle.State = EntityState.Active;
                    
                    // Add the tag -> This makes the entity visible to Physics/GameLogic next frame
                    World.AddComponent(entity, new IsActiveTag());
                }
                else
                {
                    // Update timeout logic (Accumulate DeltaTime)
                    lifecycle.CreatedTime += dt;
                    
                    // 2. Timeout Logic
                    if (lifecycle.CreatedTime > MaxConstructionTime) 
                    {
                         // Zombie detected - Destroy!
                         World.DestroyEntity(entity);
                    }
                }
            }
        }
    }
}
