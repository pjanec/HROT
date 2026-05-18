using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class CollisionSystem : ComponentSystem
    {
        private const float CollisionRadius = 2.0f;
        private EntityQuery _query = null!;
        private FdpEventBus _eventBus = null!;
        private SpatialSystem _spatial;
        private List<Entity> _nearbyCache = new List<Entity>(64);

        public CollisionSystem(EntityRepository repo, FdpEventBus eventBus, SpatialSystem spatial)
        {
            _spatial = spatial;
            _eventBus = eventBus;
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
        }

        protected override void OnUpdate()
        {
            // _spatial.Map has been updated by SpatialSystem just before this system runs

            foreach (var entityA in _query)
            {
                ref readonly var posA = ref World.GetComponentRO<Position>(entityA);
                
                // Use Spatial Map to get candidates
                _spatial.Map.Query(posA, CollisionRadius, _nearbyCache);

                foreach (var entityB in _nearbyCache)
                {
                    if (entityA == entityB) continue;

                    ref readonly var posB = ref World.GetComponentRO<Position>(entityB);

                    float dx = posA.X - posB.X;
                    float dy = posA.Y - posB.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < CollisionRadius * CollisionRadius)
                    {
                         // Collision detected! Fire event
                        _eventBus.Publish(new CollisionEvent 
                        { 
                            EntityA = entityA, 
                            EntityB = entityB, 
                            ImpactForce = (float)System.Math.Sqrt(distSq) 
                        });
                        
                        // Simple elastic collision response
                        if (World.HasComponent<Velocity>(entityA) && World.HasComponent<Velocity>(entityB))
                        {
                            ref var velA = ref World.GetComponentRW<Velocity>(entityA);
                            ref var velB = ref World.GetComponentRW<Velocity>(entityB);
                            
                            float tempX = velA.X;
                            float tempY = velA.Y;
                            velA.X = velB.X;
                            velA.Y = velB.Y;
                            velB.X = tempX;
                            velB.Y = tempY;
                        }
                    }
                }
            }
        }
    }
}
