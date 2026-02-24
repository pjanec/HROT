using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Kernel;

namespace FDP.Toolkit.Physics.Systems
{
    /// <summary>
    /// Advances the position of any entity that has <see cref="SimTransform"/> and
    /// <see cref="SimVelocity"/> but NOT <see cref="VehicleState"/>.
    /// Covers: bullets, pedestrians (future), projectiles, drift objects.
    /// Vehicles are handled by <c>CarKinematicsSystem</c>.
    ///
    /// Execution phase: PostSimulation, after BallisticsSystem (which snapshots
    /// PreviousPosition before movement), before SpatialHashSystem (which needs
    /// updated positions for the next frame's grid).
    ///
    /// <b>Ordering:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <c>[UpdateAfter(typeof(BallisticsSystem))]</c> cannot be declared here because
    ///     <c>FDP.Toolkit.Physics</c> does not reference <c>FDP.Toolkit.Combat</c> (that
    ///     would create a circular dependency). The ordering constraint is therefore
    ///     expressed from the other direction: <c>BallisticsSystem</c> carries
    ///     <c>[UpdateAfter(typeof(LinearKinematicsSystem))]</c>.
    ///   </item>
    /// </list>
    /// </summary>
    [UpdateInGroup(typeof(PostSimulationSystemGroup))]
    // [UpdateAfter(typeof(BallisticsSystem))]  ← cannot reference FDP.Toolkit.Combat from Physics
    //   (Combat already references Physics — circular). Enforced from BallisticsSystem side.
    [UpdateBefore(typeof(SpatialHashSystem))]
    public class LinearKinematicsSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            float dt = DeltaTime;

            var query = World.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Without<VehicleState>()
                .Build();

            // Parallel position integration: tf.Position += vel.Linear * dt.
            // Angular integration (tf.Rotation += ω·dt) is intentionally omitted:
            // bullets travel straight and rotation tracking is deferred to a later phase.
            query.ForEachParallel(entity =>
            {
                ref var tf  = ref World.GetComponentRW<SimTransform>(entity);
                ref readonly var vel = ref World.GetComponentRO<SimVelocity>(entity);
                tf.Position += vel.Linear * dt;
            });
        }
    }
}
