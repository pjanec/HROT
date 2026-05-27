using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads the crowd-computed velocity for each <see cref="CrowdAgent"/>-tagged entity and
    /// writes it to <see cref="SimVelocity"/>. Also integrates <see cref="SimTransform.Position"/>
    /// by the velocity * dt to match the crowd provider's internal integration.
    ///
    /// <para>
    /// Must run AFTER <see cref="OffMeshLinkDetectionSystem"/> (which sets
    /// <c>Phase = AwaitingTraversal</c> and removes the <c>CrowdAgent</c> tag via ECB)
    /// and BEFORE <see cref="NavigationExecutionSystem"/>.
    /// </para>
    ///
    /// <para>
    /// Entities in <see cref="NavigationPhase.AwaitingTraversal"/> are skipped — the animation
    /// system owns <see cref="SimTransform"/> during off-mesh traversal.
    /// </para>
    ///
    /// <para>
    /// <see cref="CarKinem.Systems.LinearKinematicsSystem"/> must carry
    /// <c>.Without&lt;CrowdAgent&gt;()</c> to prevent double-integration.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class CrowdAgentUpdateSystem : IEcsModuleSystem
    {
        private readonly IDtCrowdProvider _dtCrowd;

        /// <summary>
        /// Creates the system with access to the crowd steering provider.
        /// </summary>
        /// <param name="dtCrowd">The active crowd provider. Must not be null.</param>
        public CrowdAgentUpdateSystem(IDtCrowdProvider dtCrowd)
        {
            _dtCrowd = dtCrowd ?? throw new ArgumentNullException(nameof(dtCrowd));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(CrowdAgentUpdateSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            if (!repo.IsComponentTypeRegistered<CrowdAgent>()
                || !repo.IsComponentTypeRegistered<SimVelocity>()
                || !repo.IsComponentTypeRegistered<NavigationStatus>())
                return;

            // Advance crowd simulation once per tick.
            _dtCrowd.Update(deltaTime, view);

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<SimVelocity>()
                .With<NavigationStatus>()
                .Build();

            foreach (var entity in query)
            {
                var status = repo.GetComponent<NavigationStatus>(entity);

                // Suppress velocity during off-mesh traversal — animation owns locomotion.
                if (status.Phase == NavigationPhase.AwaitingTraversal)
                    continue;

                var velocity = _dtCrowd.GetAgentVelocity(entity);

                // Write crowd velocity to SimVelocity.
                if (repo.HasComponent<SimVelocity>(entity))
                {
                    var simVel = repo.GetComponent<SimVelocity>(entity);
                    simVel.Linear = velocity;
                    repo.SetComponent(entity, simVel);
                }

                // Integrate position: LinearKinematicsSystem is excluded for CrowdAgent
                // entities, so this system owns position integration for crowd-managed agents.
                if (repo.HasComponent<SimTransform>(entity))
                {
                    ref var tf = ref repo.GetComponentRW<SimTransform>(entity);
                    tf.Position += velocity * deltaTime;
                }
            }
        }
    }
}
