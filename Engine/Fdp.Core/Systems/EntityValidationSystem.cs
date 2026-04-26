using System;

namespace Fdp.Core.Systems
{
    /// <summary>
    /// Validates 'Constructing' entities and promotes them to 'Active' when ready.
    /// Runs at the end of the frame.
    /// </summary>
    public class EntityValidationSystem
    {
        private EntityQuery? _pendingEntities;
        private int _frameCount;

        // Timeout in Seconds
        public const float MaxConstructionTime = 5.0f;

        public void Execute(EntityRepository repo, float deltaTime)
        {
            // Build query on first execution (lazy init)
            _pendingEntities ??= repo.Query()
                .With<LifecycleDescriptor>()
                .Without<IsActiveTag>()
                .WithLifecycle(EntityLifecycle.Constructing)
                .Build();

            _frameCount++;

            // 1. Validation Logic
            foreach (var entity in _pendingEntities)
            {
                ref var lifecycle = ref repo.GetComponentRW<LifecycleDescriptor>(entity);

                // Check if all required modules have ACKed
                if ((lifecycle.RequiredModulesMask & lifecycle.AckedModulesMask) == lifecycle.RequiredModulesMask)
                {
                    // Transition to Active State
                    lifecycle.State = EntityState.Active;

                    // Add the tag -> This makes the entity visible to Physics/GameLogic next frame
                    repo.AddComponent(entity, new IsActiveTag());
                }
                else
                {
                    // Update timeout logic (Accumulate DeltaTime)
                    lifecycle.CreatedTime += deltaTime;

                    // 2. Timeout Logic
                    if (lifecycle.CreatedTime > MaxConstructionTime)
                    {
                         // Zombie detected - Destroy!
                         repo.DestroyEntity(entity);
                    }
                }
            }
        }
    }
}
