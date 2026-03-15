using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;

namespace Bagira.SimHost
{
    /// <summary>
    /// ECS component registry for combat and perception components.
    ///
    /// <para>Registers: faction alignment, perception receptors and target memory,
    /// weapon state, health buffers, ballistic projectiles, and physics colliders.</para>
    ///
    /// <para>
    /// Components not registered here (e.g. geographic, network-replication types)
    /// are owned by <c>BagiraSharedComponentRegistry</c> and must not be duplicated.
    /// Call <c>BagiraSharedComponentRegistry.RegisterAll</c> before this method.
    /// </para>
    /// </summary>
    public static class CombatComponentRegistry
    {
        /// <summary>
        /// Registers all combat and perception components into <paramref name="world"/>.
        /// </summary>
        public static void RegisterAll(EntityRepository world)
        {
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<HealthData>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<PhysicsCollider>();
        }
    }
}
