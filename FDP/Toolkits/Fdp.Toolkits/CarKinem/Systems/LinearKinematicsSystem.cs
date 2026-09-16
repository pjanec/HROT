using System;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.CarKinem.Systems
{
    /// <summary>
    /// Advances the position of any entity that has <see cref="SimTransform"/> and
    /// <see cref="SimVelocity"/> but NOT <see cref="VehicleState"/>.
    /// Covers: bullets, pedestrians (future), projectiles, drift objects.
    /// Vehicles are handled by <see cref="CarKinematicsSystem"/>.
    ///
    /// <para>
    /// <b>Execution phase:</b> runs inside the simulation group after
    /// <see cref="CarKinematicsSystem"/> (which updates vehicle positions) and
    /// before the next-frame <see cref="SpatialHashSystem"/> rebuild.
    /// </para>
    ///
    /// <para>
    /// <b>Circular-dependency resolution (CT-MOD1-F):</b>
    /// Previously hosted in <c>FDP.Toolkit.Physics</c>, which referenced
    /// <c>FDP.Toolkit.CarKinem</c> for <see cref="VehicleState"/>, making it
    /// impossible for <see cref="GroundKinematicsModule"/> (in CarKinem) to include it
    /// without introducing a cycle. Moving the system here breaks the cycle: CarKinem
    /// no longer needs to reference Physics, and Physics no longer carries a reverse dep
    /// through this system.
    /// </para>
    ///
    /// <para>
    /// <b>Ordering note:</b>
    /// The ordering relative to <see cref="CarKinematicsSystem"/> and
    /// <see cref="SpatialHashSystem"/> is maintained by registration order inside
    /// <see cref="Fdp.Toolkit.CarKinem.Modules.GroundKinematicsModule.RegisterSystems"/>
    /// (LinearKinematicsSystem is added last). Using explicit <c>[UpdateAfter]</c> or
    /// <c>[UpdateBefore]</c> attribute constraints within this group would create a
    /// cycle because <c>CarKinematicsSystem</c> carries
    /// <c>[UpdateAfter(typeof(SpatialHashSystem))]</c>.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class LinearKinematicsSystem : IEcsModuleSystem
    {
        private readonly int[] _requiredComponentIds;

        /// <summary>Default: integrate everything that is not a vehicle or a crowd agent.</summary>
        public LinearKinematicsSystem() : this(System.Array.Empty<int>()) { }

        /// <summary>
        /// ⭐⭐ <c>CE-238</c> — <b>host-supplied NARROWING, so a host whose engine already owns some
        /// entities can restrict this system to the ones it does not.</b>
        ///
        /// <para>📌 <b>The case that required it, measured.</b> On the Stride host Bullet owns any entity
        /// with a physics body. An infantry character with <c>VehicleState</c> stripped has neither
        /// <c>VehicleState</c> nor (yet) <c>CrowdAgent</c>, so it matched the default query while a Bullet
        /// <c>CharacterComponent</c> was also driving it. The ECS integrator advanced the pose,
        /// <c>BulletReverseSyncSystem</c> derived the character's <c>SimVelocity</c> from that inflated
        /// frame-to-frame delta, and that fed the integrator again — a self-sustaining loop measured at a
        /// constant <b>111 m/s</b>.</para>
        ///
        /// <para>⭐ <b>Why NARROWING and not an exclusion list.</b> The obvious marker for "Bullet owns
        /// this" is <c>PhysicsBodyReference</c> — but it is <b>not an ECS component at all</b>: its own
        /// doc says it is "stored in a parallel <c>Dictionary&lt;Entity, PhysicsBodyReference&gt;</c>", so
        /// it can never be a query filter. What the Stride host actually needs is the positive statement
        /// of <c>CE-234</c>'s purpose — <i>integrate the projectiles</i> — expressed as
        /// <c>GlobalComponentIds.BallisticProjectile</c>.</para>
        ///
        /// <para>⭐ <b>Why parameterised and not duplicated.</b> A Stride-local copy of this integrator
        /// would be a second implementation of one concept. Passing component IDs keeps ONE
        /// implementation and moves the policy to the host that knows it — reusing
        /// <c>QueryBuilder.WithComponentId</c>, which exists for exactly this cross-assembly case.</para>
        /// </summary>
        /// <param name="requiredComponentIds">
        /// Raw component-type IDs an entity must ALSO carry to be integrated.
        /// Empty reproduces the previous behaviour exactly.
        /// </param>
        public LinearKinematicsSystem(params int[] requiredComponentIds)
            => _requiredComponentIds = requiredComponentIds ?? System.Array.Empty<int>();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(LinearKinematicsSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            float dt = deltaTime;

            var builder = repo.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Without<VehicleState>()
                .Without<CrowdAgent>();

            // CE-238: a host may narrow this to the entities its own engine does NOT own.
            for (int i = 0; i < _requiredComponentIds.Length; i++)
                builder = builder.WithComponentId(_requiredComponentIds[i]);

            var query = builder.Build();

            // Parallel position integration: tf.Position += vel.Linear * dt.
            // Angular integration (tf.Rotation += omega*dt) is intentionally omitted:
            // bullets travel straight and rotation tracking is deferred to a later phase.
            query.ForEachParallel(entity =>
            {
                ref var tf              = ref repo.GetComponentRW<SimTransform>(entity);
                ref readonly var vel    = ref repo.GetComponentRO<SimVelocity>(entity);
                tf.Position            += vel.Linear * dt;
            });
        }
    }
}
