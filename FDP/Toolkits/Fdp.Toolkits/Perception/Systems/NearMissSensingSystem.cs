using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3064</c> (R-206) — on the unit's BRAIN, a <see cref="NearMissEvent"/> (local ballistics on a one-world host, or the
    /// DDS ingress on a split cluster — the same event either way) becomes the unit's <see cref="SensorChange.NearMiss"/> edge,
    /// beside Hit (<see cref="ThreatEvaluationSystem"/>): the reaction catalog, the HSM bridge and <c>RecentSenses</c> pick it up
    /// like any other sensing change, and ROE <c>ReturnFire</c> answers it. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 G.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class NearMissSensingSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<NearMissEvent>()
                || !repo.Bus.IsRegistered<SensorChangedEvent>()) return;
            var ecb = view.GetCommandBuffer();
            foreach (ref readonly var nm in view.ReadEvents<NearMissEvent>())
                if (view.IsAlive(nm.Unit))
                    ecb.PublishEvent(new SensorChangedEvent { Unit = nm.Unit, What = SensorChange.NearMiss });
        }
    }
}
