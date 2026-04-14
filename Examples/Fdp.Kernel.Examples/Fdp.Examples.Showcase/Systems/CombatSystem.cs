using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class CombatSystem : ComponentSystem
    {
        private const float CombatRange = 15.0f; // Detection range
        private const float CombatCooldown = 1.5f; // Seconds between shots
        private EntityQuery _query = null!;
        private System.Collections.Generic.Dictionary<Entity, double> _lastAttackTime = new();
        private FdpEventBus _eventBus = null!;
        private SpatialSystem _spatial;
        private List<Entity> _nearbyCache = new List<Entity>(64);

        public CombatSystem(EntityRepository repo, FdpEventBus eventBus, SpatialSystem spatial)
        {
            _spatial = spatial;
            _eventBus = eventBus;
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<Position>()
                .With<UnitStats>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            var entities = new System.Collections.Generic.List<Entity>();
            foreach (var e in _query) entities.Add(e);

            for (int i = 0; i < entities.Count; i++)
            {
                Entity shooter = entities[i];
                ref var shooterStats = ref World.GetComponentRW<UnitStats>(shooter);
                
                // Skip dead units
                if (shooterStats.Health <= 0) continue;
                
                // Check cooldown
                if (_lastAttackTime.TryGetValue(shooter, out var lastTime))
                {
                    if (time.TotalTime - lastTime < CombatCooldown) continue;
                }
                
                ref readonly var shooterPos = ref World.GetComponentRO<Position>(shooter);
                
                // Find targets in range using Spatial Map
                _spatial.Map.Query(shooterPos, CombatRange, _nearbyCache);

                foreach (var target in _nearbyCache)
                {
                    if (shooter == target) continue;

                    ref var targetStats = ref World.GetComponentRW<UnitStats>(target);
                    
                    // Skip dead or same type
                    if (targetStats.Health <= 0) continue;
                    if (targetStats.Type == shooterStats.Type) continue; // Friendly fire off
                    
                    ref readonly var targetPos = ref World.GetComponentRO<Position>(target);
                    
                    float dx = targetPos.X - shooterPos.X;
                    float dy = targetPos.Y - shooterPos.Y;
                    float distSq = dx * dx + dy * dy;
                    
                    if (distSq < CombatRange * CombatRange && distSq > 0.01f)
                    {
                        // FIRE PROJECTILE!
                        float damage = CalculateDamage(shooterStats.Type, targetStats.Type);
                        float dist = (float)System.Math.Sqrt(distSq);
                        
                        // Spawn projectile
                        var projectile = World.CreateEntity();
                        World.AddComponent(projectile, new Position { X = shooterPos.X, Y = shooterPos.Y });
                        
                        // Calculate velocity towards target
                        float speed = 20f; // Units per second
                        World.AddComponent(projectile, new Velocity 
                        { 
                            X = (dx / dist) * speed, 
                            Y = (dy / dist) * speed 
                        });
                        
                        World.AddComponent(projectile, new Projectile
                        {
                            Owner = shooter,
                            Damage = damage,
                            Speed = speed,
                            Lifetime = 3.0f // 3 seconds max
                        });
                        
                        // Visual: small colored bullet
                        (byte r, byte g, byte b) = shooterStats.Type switch
                        {
                            UnitType.Tank => ((byte)255, (byte)255, (byte)100),
                            UnitType.Aircraft => ((byte)100, (byte)200, (byte)255),
                            _ => ((byte)200, (byte)200, (byte)200)
                        };
                        World.AddComponent(projectile, new RenderSymbol 
                        { 
                            Shape = EntityShape.Cross, 
                            R = r, 
                            G = g, 
                            B = b,
                            Size = 0.4f
                        });
                        
                        // Fire event
                        _eventBus.Publish(new ProjectileFiredEvent
                        {
                            Shooter = shooter,
                            Projectile = projectile,
                            ShooterType = shooterStats.Type
                        });
                        
                        _lastAttackTime[shooter] = time.TotalTime;
                        break; // One shot per frame
                    }
                }
            }
        }
        
        private float CalculateDamage(UnitType attacker, UnitType target)
        {
            // Simple rock-paper-scissors damage system
            return (attacker, target) switch
            {
                (UnitType.Tank, UnitType.Infantry) => 25f,      // Tanks strong vs Infantry
                (UnitType.Infantry, UnitType.Aircraft) => 15f,  // Infantry can shoot aircraft
                (UnitType.Aircraft, UnitType.Tank) => 30f,      // Aircraft strong vs Tanks
                (UnitType.Tank, UnitType.Aircraft) => 5f,       // Tanks weak vs Aircraft
                (UnitType.Aircraft, UnitType.Infantry) => 20f,  // Aircraft vs Infantry
                (UnitType.Infantry, UnitType.Tank) => 5f,       // Infantry weak vs Tanks
                _ => 10f
            };
        }
    }
}
