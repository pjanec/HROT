using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class ParticleSystem : ComponentSystem
    {
        private EntityQuery _query = null!;
        private FdpEventBus _eventBus = null!;
        private LifecycleSystem _lifecycle = null!;

        public ParticleSystem(EntityRepository repo, FdpEventBus eventBus, LifecycleSystem lifecycle)
        {
            _eventBus = eventBus;
            _lifecycle = lifecycle;
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<Particle>()
                .With<Position>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime;
            
            var toDestroy = new System.Collections.Generic.List<Entity>();
            
            // Process death events - spawn explosions
            var deathEvents = _eventBus.Consume<DeathEvent>();
            foreach (ref readonly var deathEvt in deathEvents)
            {
                // Get death position
                if (World.IsAlive(deathEvt.Entity) && World.HasComponent<Position>(deathEvt.Entity))
                {
                    ref readonly var pos = ref World.GetComponentRO<Position>(deathEvt.Entity);
                    SpawnExplosion(pos.X, pos.Y, deathEvt.Type);
                }
            }
            
            // Update particles
            foreach (var entity in _query)
            {
                ref var particle = ref World.GetComponentRW<Particle>(entity);
                
                particle.LifeRemaining -= dt;
                
                if (particle.LifeRemaining <= 0)
                {
                    toDestroy.Add(entity);
                }
            }
            
            // Queue expired particles for cleanup at end of frame
            foreach (var entity in toDestroy)
            {
                _lifecycle.CommandBuffer.DestroyEntity(entity);
            }
        }
        
        private void SpawnExplosion(float x, float y, UnitType unitType)
        {
            var rand = new System.Random();
            int particleCount = rand.Next(8, 13); // 8-12 particles
            
            (byte r, byte g, byte b) = unitType switch
            {
                UnitType.Tank => ((byte)200, (byte)180, (byte)50),
                UnitType.Aircraft => ((byte)50, (byte)150, (byte)200),
                _ => ((byte)150, (byte)150, (byte)150)
            };
            
            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(rand.NextDouble() * System.Math.PI * 2);
                float speed = (float)(rand.NextDouble() * 8 + 4); // 4-12 units/sec
                
                var particle = World.CreateEntity();
                World.AddComponent(particle, new Position { X = x, Y = y });
                World.AddComponent(particle, new Velocity 
                { 
                    X = (float)System.Math.Cos(angle) * speed,
                    Y = (float)System.Math.Sin(angle) * speed
                });
                
                float lifetime = (float)(rand.NextDouble() * 0.5 + 0.5); // 0.5-1.0 seconds
                World.AddComponent(particle, new Particle 
                { 
                    LifeRemaining = lifetime,
                    MaxLife = lifetime,
                    R = r,
                    G = g,
                    B = b,
                    Size = 0.2f
                });
                
                World.AddComponent(particle, new RenderSymbol 
                { 
                    Shape = EntityShape.Circle,
                    R = r,
                    G = g,
                    B = b,
                    Size = 0.2f
                });
            }
        }
    }
}
