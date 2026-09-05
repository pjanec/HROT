using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads the crowd-computed steering velocity for each <see cref="CrowdAgent"/>-tagged entity
    /// and applies it according to <b>who owns the entity's pose</b>.
    ///
    /// <para><b>Two authorities, chosen PER ENTITY (`S3`).</b> The Stride port's version of this
    /// system dropped the <see cref="SimVelocity"/>/<see cref="SimTransform"/> write entirely and
    /// wrote only <see cref="CrowdMotorIntent"/>. That is correct for a Stride node — under split
    /// physics authority the pose is written by <c>BulletReverseSyncSystem</c> after the physics
    /// step, and feeding <see cref="SimVelocity"/> back in as an input would be a feedback loop —
    /// but it is wrong for any node where FDP still owns the pose. So the branch is kept:</para>
    ///
    /// <list type="bullet">
    /// <item><description><b>Entity HAS <see cref="CrowdMotorIntent"/></b> ⇒ split authority. Write
    /// the intent and nothing else; the physics motor consumes it pre-step and the reverse-sync
    /// writes the resulting pose.</description></item>
    /// <item><description><b>Entity has NO <see cref="CrowdMotorIntent"/></b> ⇒ FDP authority, the
    /// pre-port behaviour, unchanged: write <see cref="SimVelocity"/> and integrate
    /// <see cref="SimTransform.Position"/> by velocity × dt.</description></item>
    /// </list>
    ///
    /// <para><b>Why per-entity and not per-node.</b> The component's presence IS the authority
    /// marker — it is added only by the host that also runs the motor and the reverse-sync. A
    /// node-level flag would be a second thing to keep in step with the first, and the failure
    /// would be silent: an agent that stopped moving with nothing to point at.</para>
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
    /// <c>.Without&lt;CrowdAgent&gt;()</c> to prevent double-integration on the FDP-authority path.
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

            bool splitAuthority = repo.IsComponentTypeRegistered<CrowdMotorIntent>();

            // The SimVelocity guard is retained for the FDP-authority path exactly as it was, so a
            // repo with no way to receive the output still short-circuits before the crowd tick.
            // A repo that registers CrowdMotorIntent has such a way, hence the alternative.
            if (!repo.IsComponentTypeRegistered<CrowdAgent>()
                || !repo.IsComponentTypeRegistered<NavigationStatus>()
                || !(repo.IsComponentTypeRegistered<SimVelocity>() || splitAuthority))
                return;

            // Advance crowd simulation once per tick.
            _dtCrowd.Update(deltaTime, view);

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<NavigationStatus>()
                .Build();

            foreach (var entity in query)
            {
                var status = repo.GetComponent<NavigationStatus>(entity);

                // Suppress steering during off-mesh traversal — animation owns locomotion.
                if (status.Phase == NavigationPhase.AwaitingTraversal)
                    continue;

                var velocity = _dtCrowd.GetAgentVelocity(entity);

                // ── Split authority: the physics motor owns the pose ──────────────
                if (splitAuthority && repo.HasComponent<CrowdMotorIntent>(entity))
                {
                    var intent = repo.GetComponent<CrowdMotorIntent>(entity);
                    intent.Velocity = velocity;
                    repo.SetComponent(entity, intent);
                    continue;
                }

                // ── FDP authority: unchanged from before the port ─────────────────
                if (!repo.HasComponent<SimVelocity>(entity))
                    continue;

                var simVel = repo.GetComponent<SimVelocity>(entity);
                simVel.Linear = velocity;
                repo.SetComponent(entity, simVel);

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
