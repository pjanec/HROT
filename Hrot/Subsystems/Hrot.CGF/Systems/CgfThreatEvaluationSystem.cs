using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Systems;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Brain-tier adapter that drives <see cref="ThreatEvaluationSystem"/> on the CGF node.
    /// Runs as a synchronous <see cref="ComponentSystem"/> in the simulation phase, immediately
    /// before <c>CognitiveRuntimeModule</c>, so B-Trees always evaluate against freshly
    /// decayed and boosted threat scores.
    ///
    /// <para>
    /// <see cref="ThreatEvaluationSystem"/> reads <c>ActiveSensorTracks</c> (written by
    /// <c>SensorTrackStateIngressTranslator</c>) to boost <c>TargetMemory</c>, and applies
    /// continuous score decay each frame.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public sealed class CgfThreatEvaluationSystem : ComponentSystem
    {
        private readonly ThreatEvaluationSystem _threatEvaluation = new ThreatEvaluationSystem();

        protected override void OnUpdate()
        {
            var view = (ISimulationView)World;
            _threatEvaluation.Execute(view, DeltaTime);
        }
    }
}
