using FDP.Toolkit.Perception.Events;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Systems
{
    /// <summary>
    /// Background-thread system that bridges <see cref="LosCheckRequestEvent"/>s from the
    /// <see cref="Modules.AutonomousPerceptionModule"/> to the physics raycast pipeline (or,
    /// in mock mode, directly confirms visibility for all requests).
    /// <para>
    /// Runs exclusively on the background thread inside
    /// <see cref="Modules.AutonomousPerceptionModule.Tick"/> — after
    /// <see cref="VisionBroadphaseSystem"/> emits requests and before
    /// <see cref="ThreatEvaluationSystem"/> processes the resulting visible-target events.
    /// </para>
    /// <para>
    /// <b>Mock mode:</b> Skips ray submission and immediately emits a
    /// <see cref="TargetVisibleEvent"/> for every incoming <see cref="LosCheckRequestEvent"/>.
    /// </para>
    /// </summary>
    public sealed class LosRequestBatchingSystem : IModuleSystem
    {
        /// <summary>
        /// When <c>true</c>, every <see cref="LosCheckRequestEvent"/> is immediately resolved
        /// as visible (no actual ray submission). Set to <c>true</c> for Phase 2 testing.
        /// </summary>
        private readonly bool _mockMode;

        /// <param name="mockMode">
        /// <c>true</c> to bypass ray submission and directly emit <see cref="TargetVisibleEvent"/>;
        /// <c>false</c> for production (requires physics raycast pipeline).
        /// </param>
        public LosRequestBatchingSystem(bool mockMode = false)
        {
            _mockMode = mockMode;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var requests = view.ConsumeEvents<LosCheckRequestEvent>();
            if (requests.IsEmpty) return;

            var cmds = view.GetCommandBuffer();
            if (_mockMode)
            {
                // Mock mode: treat broadphase visibility as confirmed LOS.
                // Full Entity handles are passed straight through — generation included.
                foreach (ref readonly var req in requests)
                    cmds.PublishEvent(new TargetVisibleEvent { Observer = req.Observer, Target = req.Target });
            }
            // Production mode: TODO — add RaycastBatchData writes via command buffer when available.
        }
    }
}
