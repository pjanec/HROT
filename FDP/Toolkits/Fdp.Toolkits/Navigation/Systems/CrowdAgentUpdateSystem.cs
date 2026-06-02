using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads the crowd-computed steering velocity for each <see cref="CrowdAgent"/>-tagged
    /// entity and writes it to <see cref="CrowdMotorIntent.Velocity"/> only.
    ///
    /// <para>
    /// <b>P2-T4 refactor (STR-D12).</b>
    /// This system no longer integrates <see cref="SimTransform.Position"/> or writes
    /// <see cref="SimVelocity"/>. Under split physics authority (Stride Phase 2+),
    /// <see cref="SimTransform"/> and <see cref="SimVelocity"/> are written exclusively by
    /// <c>BulletReverseSyncSystem</c> after the physics step. The steering output is instead
    /// written to a dedicated <see cref="CrowdMotorIntent"/> component which
    /// <c>BulletCharacterMotor</c> consumes as a pre-physics intent (design §5.3, §6.2).
    /// </para>
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
                || !repo.IsComponentTypeRegistered<NavigationStatus>())
                return;

            // Advance crowd simulation once per tick.
            _dtCrowd.Update(deltaTime, view);

            // Only iterate entities that have a CrowdMotorIntent to write into.
            // Entities without the intent component are skipped (no intent registered for them).
            if (!repo.IsComponentTypeRegistered<CrowdMotorIntent>())
                return;

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<CrowdMotorIntent>()
                .With<NavigationStatus>()
                .Build();

            foreach (var entity in query)
            {
                var status = repo.GetComponent<NavigationStatus>(entity);

                // Suppress steering during off-mesh traversal — animation owns locomotion.
                if (status.Phase == NavigationPhase.AwaitingTraversal)
                    continue;

                var velocity = _dtCrowd.GetAgentVelocity(entity);

                // Write steering output to CrowdMotorIntent — the ONLY mutation this system
                // makes. SimTransform and SimVelocity are NOT touched (STR-D12 fix).
                var intent = repo.GetComponent<CrowdMotorIntent>(entity);
                intent.Velocity = velocity;
                repo.SetComponent(entity, intent);
            }
        }
    }
}
