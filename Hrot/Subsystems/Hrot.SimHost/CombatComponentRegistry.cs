using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.SimHost
{
    /// <summary>
    /// ECS component registry for combat and perception components.
    ///
    /// <para>Registers: perception receptors and target memory,
    /// weapon state, health buffers, ballistic projectiles, and physics colliders.</para>
    ///
    /// <para>
    /// Components not registered here (e.g. geographic, network-replication types)
    /// are owned by <c>HrotSharedComponentRegistry</c> and must not be duplicated.
    /// Call <c>HrotSharedComponentRegistry.RegisterAll</c> before this method.
    /// </para>
    /// </summary>
    public static class CombatComponentRegistry
    {
        /// <summary>
        /// Registers all combat and perception components into <paramref name="world"/>.
        /// </summary>
        public static void RegisterAll(EntityRepository world)
        {
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<SensorContactList>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<EntityInfo>();
            // Health is registered in HrotSharedComponentRegistry (shared across all nodes).
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<PhysicsCollider>();

            // ── Perception pipeline events ────────────────────────────────────
            world.RegisterEvent<AudioStimulusEvent>();
            world.RegisterEvent<LosCheckRequestEvent>();
            world.RegisterEvent<TargetVisibleEvent>();
            world.RegisterEvent<TargetHeardEvent>();
            world.RegisterEvent<SensorTrackStateEvent>(); 

            // Target seeding command (edit-1/EDIT1-E002)
            world.RegisterEvent<SeedTargetCommand>();
            world.RegisterEvent<WeaponFireIntent>();

            // ── Combat pipeline notifications ─────────────────────────────────
            // Published by FireProcessingSystem and HitResolutionSystem; consumed by
            // EventToEffectSystem (visual effects) and network egress translators.
            world.RegisterEvent<WeaponFireNotification>();
            world.RegisterEvent<DetonationNotification>();
        }
    }
}
