using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class ProjectileSystem : ComponentSystem
    {
        private const float HitRadius = 1.5f;
        private EntityQuery _projectileQuery = null!;
        private FdpEventBus _eventBus = null!;
        private LifecycleSystem _lifecycle = null!;
        private SpatialSystem _spatial;
        private List<Entity> _nearbyCache = new List<Entity>(64);

        public ProjectileSystem(EntityRepository repo, FdpEventBus eventBus, LifecycleSystem lifecycle, SpatialSystem spatial)
        {
            _spatial = spatial;
            _eventBus = eventBus;
            _lifecycle = lifecycle;
            Create(repo);
        }

        protected override void OnCreate()
        {
            _projectileQuery = World.Query()
                .With<Position>()
                .With<Projectile>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime;
            
            var toDestroy = new System.Collections.Generic.List<Entity>();
            
            // Collect all projectiles
            var projectiles = new System.Collections.Generic.List<Entity>();
            foreach (var e in _projectileQuery) projectiles.Add(e);
            
            // Update all projectiles
            foreach (var proj in projectiles)
            {
                ref var projData = ref World.GetComponentRW<Projectile>(proj);
                ref readonly var projPos = ref World.GetComponentRO<Position>(proj);
                
                // Capture values needed in nested loop
                Entity owner = projData.Owner;
                float damage = projData.Damage;
                Position position = projPos;
                
                // Age the projectile
                projData.Lifetime -= dt;
                if (projData.Lifetime <= 0)
                {
                    toDestroy.Add(proj);
                    continue;
                }
                
                // Check for hits against units using Spatial Map
                _spatial.Map.Query(projPos, HitRadius, _nearbyCache);

                foreach (var unit in _nearbyCache)
                {
                    if (unit == owner) continue; // Can't hit self
                    
                    ref var unitStats = ref World.GetComponentRW<UnitStats>(unit);
                    if (unitStats.Health <= 0) continue; // Already dead
                    
                    ref readonly var unitPos = ref World.GetComponentRO<Position>(unit);
                    
                    float dx = position.X - unitPos.X;
                    float dy = position.Y - unitPos.Y;
                    float distSq = dx * dx + dy * dy;
                    
                    if (distSq < HitRadius * HitRadius)
                    {
                        // HIT!
                        float healthBefore = unitStats.Health;
                        unitStats.Health -= damage;
                        bool wasKilled = unitStats.Health <= 0 && healthBefore > 0;
                        if (wasKilled) unitStats.Health = 0;
                        
                        // Update managed CombatHistory if present (for testing managed components)
                        if (World.HasComponent<CombatHistory>(unit))
                        {
                            var history = World.GetComponentRW<CombatHistory>(unit);
                            var ownerType = World.HasComponent<UnitStats>(owner)
                                ? World.GetComponentRO<UnitStats>(owner).Type
                                : UnitType.Infantry;
                            history.RecordDamageTaken(damage, ownerType.ToString());
                        }
                        
                        if (World.HasComponent<CombatHistory>(owner))
                        {
                            var history = World.GetComponentRW<CombatHistory>(owner);
                            history.RecordDamageDealt(damage, unitStats.Type.ToString());
                        }
                        
                        // Add hit flash effect
                        World.AddComponent(unit, new HitFlash
                        {
                            Remaining = 0.3f
                        });
                        
                        // Fire unmanaged events (for backward compatibility)
                        _eventBus.Publish(new ProjectileHitEvent
                        {
                            Projectile = proj,
                            Target = unit,
                            Damage = damage
                        });
                        
                        _eventBus.Publish(new DamageEvent
                        {
                            Attacker = owner,
                            Target = unit,
                            Damage = damage,
                            AttackerType = World.HasComponent<UnitStats>(owner) 
                                ? World.GetComponentRO<UnitStats>(owner).Type 
                                : UnitType.Infantry
                        });
                        
                        // Fire MANAGED damage event (for testing managed event recording/playback)
                        var ownerTypeName = World.HasComponent<UnitStats>(owner) 
                            ? World.GetComponentRO<UnitStats>(owner).Type.ToString()
                            : "Unknown";
                        var targetTypeName = unitStats.Type.ToString();
                            
                        _eventBus.PublishManaged(new EntityDamagedEvent
                        {
                            AttackerIndex = owner.Index,
                            AttackerGeneration = owner.Generation,
                            TargetIndex = unit.Index,
                            TargetGeneration = unit.Generation,
                            DamageAmount = damage,
                            DamageType = "Projectile",
                            AttackerTypeName = ownerTypeName,
                            TargetTypeName = targetTypeName,
                            WasKillingBlow = wasKilled,
                            TargetHealthRemaining = unitStats.Health
                        });
                        
                        // Check for death
                        if (wasKilled)
                        {
                            // Update combat history for killer
                            if (World.HasComponent<CombatHistory>(owner))
                            {
                                var history = World.GetComponentRW<CombatHistory>(owner);
                                history.RecordKill(targetTypeName);
                            }
                            
                            // Fire unmanaged death event
                            _eventBus.Publish(new DeathEvent
                            {
                                Entity = unit,
                                Type = unitStats.Type
                            });
                            
                            // Fire MANAGED death event (for testing)
                            int totalDamage = 0;
                            int timesHit = 0;
                            if (World.HasComponent<CombatHistory>(unit))
                            {
                                var history = World.GetComponentRO<CombatHistory>(unit);
                                totalDamage = history.TotalDamageTaken;
                                timesHit = history.TimesHit;
                            }
                            
                            _eventBus.PublishManaged(new EntityDeathEvent
                            {
                                EntityIndex = unit.Index,
                                EntityGeneration = unit.Generation,
                                EntityTypeName = targetTypeName,
                                KillerIndex = owner.Index,
                                KillerGeneration = owner.Generation,
                                KillerTypeName = ownerTypeName,
                                TotalDamageTaken = totalDamage,
                                TimesHit = timesHit,
                                PositionX = unitPos.X,
                                PositionY = unitPos.Y
                            });
                            
                            // Stop the entity from moving (remove velocity)
                            if (World.HasComponent<Velocity>(unit))
                            {
                                World.RemoveComponent<Velocity>(unit);
                            }
                            
                            // Visual death indicator (darker color)
                            if (World.HasComponent<RenderSymbol>(unit))
                            {
                                ref var render = ref World.GetComponentRW<RenderSymbol>(unit);
                                render.R = (byte)(render.R / 2);
                                render.G = (byte)(render.G / 2);
                                render.B = (byte)(render.B / 2);
                                render.Shape = EntityShape.Cross;
                            }
                            
                            // Mark as corpse - will be removed after 1 second
                            // This gives time for particle effects to complete
                            World.AddComponent(unit, new Corpse { TimeRemaining = 1.0f });
                        }
                        
                        toDestroy.Add(proj);
                        break; // Projectile hit something, stop checking
                    }
                }
            }
            
            // Queue destroyed projectiles for cleanup at end of frame
            // Don't destroy immediately - let the barrier handle it
            foreach (var proj in toDestroy)
            {
                _lifecycle.CommandBuffer.DestroyEntity(proj);
            }
        }
    }
}
