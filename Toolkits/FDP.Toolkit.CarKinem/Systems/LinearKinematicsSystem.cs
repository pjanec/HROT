using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Kernel;

namespace FDP.Toolkit.CarKinem.Systems
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
    /// <see cref="FDP.Toolkit.CarKinem.Modules.GroundKinematicsModule.RegisterSystems"/>
    /// (LinearKinematicsSystem is added last). Using explicit <c>[UpdateAfter]</c> or
    /// <c>[UpdateBefore]</c> attribute constraints within this group would create a
    /// cycle because <c>CarKinematicsSystem</c> carries
    /// <c>[UpdateAfter(typeof(SpatialHashSystem))]</c>.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
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
                ref var tf              = ref World.GetComponentRW<SimTransform>(entity);
                ref readonly var vel    = ref World.GetComponentRO<SimVelocity>(entity);
                tf.Position            += vel.Linear * dt;
            });
        }
    }
}
