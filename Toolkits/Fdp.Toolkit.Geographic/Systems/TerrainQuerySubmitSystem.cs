using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using ModuleHost.Core.Abstractions;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Runs immediately after <see cref="TerrainQueryInitializationSystem"/>
    /// (<see cref="SystemPhase.Input"/>).
    ///
    /// <para>
    /// For every entity that has both a <see cref="SimTransform"/> and an active
    /// <see cref="GroundClampingConfig"/> (i.e. <see cref="GroundClampingConfig.IsClampingActive"/>
    /// returns <c>true</c>), this system writes one <see cref="TerrainQueryRequest"/> into the
    /// <see cref="TerrainQueryBatchData"/> singleton.
    /// </para>
    ///
    /// <para>
    /// The query XY position is forward-predicted by one frame using the entity's
    /// current velocity so that <see cref="TerrainQuerySolverSystem"/> (which runs later
    /// in the <see cref="SystemPhase.Simulation"/> phase) returns a hit that aligns with
    /// where the entity will be when <see cref="TerrainQueryResolutionSystem"/> applies
    /// the result.
    /// </para>
    ///
    /// <para>
    /// Entities without <see cref="SimVelocity"/> are queried at their current position.
    /// When the batch is full (<c>Count == DefaultCapacity</c>) additional entities are
    /// silently skipped for that frame.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class TerrainQuerySubmitSystem : IModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = (EntityRepository)view;

            if (!world.HasSingleton<TerrainQueryBatchData>()) return;
            ref var batch = ref world.GetSingleton<TerrainQueryBatchData>();

            var query = view.Query()
                .With<SimTransform>()
                .With<GroundClampingConfig>()
                .Build();

            foreach (var entity in query)
            {
                if (batch.Count >= TerrainQueryBatchData.DefaultCapacity) break;

                ref readonly var config = ref view.GetComponentRO<GroundClampingConfig>(entity);
                if (!config.IsClampingActive) continue;

                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

                // Forward-predict one frame if velocity is available.
                float qx = tf.Position.X;
                float qy = tf.Position.Y;

                if (view.HasComponent<SimVelocity>(entity))
                {
                    ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);
                    qx += vel.Linear.X * deltaTime;
                    qy += vel.Linear.Y * deltaTime;
                }

                int slot = batch.Count++;
                batch.Requests[slot] = new TerrainQueryRequest
                {
                    Entity        = entity,
                    QueryX        = qx,
                    QueryY        = qy,
                    ReferenceSimZ = tf.Position.Z,
                };
            }
        }
    }
}
