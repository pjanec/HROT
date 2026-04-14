using Fdp.Kernel;

namespace Fdp.Examples.Showcase.Components
{
    // Events for demonstration of Event Bus
    [EventId(100)]
    public struct CollisionEvent
    {
        public Entity EntityA;
        public Entity EntityB;
        public float ImpactForce;
    }
    
    [EventId(101)]
    public struct ProjectileFiredEvent
    {
        public Entity Shooter;
        public Entity Projectile;
        public UnitType ShooterType;
    }
    
    [EventId(102)]
    public struct ProjectileHitEvent
    {
        public Entity Projectile;
        public Entity Target;
        public float Damage;
    }
    
    [EventId(103)]
    public struct DamageEvent
    {
        public Entity Attacker;
        public Entity Target;
        public float Damage;
        public UnitType AttackerType;
    }
    
    [EventId(104)]
    public struct DeathEvent
    {
        public Entity Entity;
        public UnitType Type;
    }
}
