using Fdp.Kernel;
using FDP.Toolkit.Perception.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Main-thread adapter for ThreatEvaluationSystem.
    /// </summary>
    public sealed class ThreatEvaluationAdapterSystem : ComponentSystem
    {
        private readonly ThreatEvaluationSystem _threatEvaluation = new ThreatEvaluationSystem();

        protected override void OnUpdate()
        {
            var view = (ISimulationView)World;
            _threatEvaluation.Execute(view, DeltaTime);
        }
    }
}
