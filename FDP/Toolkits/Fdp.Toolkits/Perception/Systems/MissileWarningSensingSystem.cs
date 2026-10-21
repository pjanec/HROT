using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2189</c> (<c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3c, Air Ops §7d AD4) — on the aircraft's BRAIN,
    /// a <see cref="MissileWarningEvent"/> (local on a one-world host, the DDS ingress on a split cluster — the same event either way)
    /// becomes the aircraft's <see cref="SensorChange.MissileWarning"/> edge and its <see cref="MissileWarning"/> component: where the
    /// missile came from, the bearing to it (0 = north, clockwise), its seeker and the time to impact. The near-miss pattern
    /// (<see cref="NearMissSensingSystem"/>), beside V8a's <c>AirThreat</c>.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MissileWarningSensingSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<MissileWarningEvent>()) return;
            bool sensors = repo.Bus.IsRegistered<SensorChangedEvent>();
            bool store = repo.IsComponentTypeRegistered<MissileWarning>() && repo.IsComponentTypeRegistered<SimTransform>();
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;
            var ecb = view.GetCommandBuffer();
            foreach (ref readonly var w in view.ReadEvents<MissileWarningEvent>())
            {
                if (!view.IsAlive(w.Unit)) continue;
                if (store && repo.HasComponent<SimTransform>(w.Unit))
                {
                    var at = repo.GetComponentRO<SimTransform>(w.Unit).Position;
                    float bearing = MathF.Atan2(w.FromX - at.X, w.FromY - at.Y) * (180f / MathF.PI);
                    var warning = new MissileWarning
                    {
                        At = now, From = new System.Numerics.Vector3(w.FromX, w.FromY, w.FromZ),
                        BearingDeg = bearing < 0f ? bearing + 360f : bearing,
                        Seeker = (Fdp.Toolkit.Tkb.Domain.GuidanceKind)w.Seeker, TimeToImpactS = w.TimeToImpactS,
                    };
                    if (repo.HasComponent<MissileWarning>(w.Unit)) repo.GetComponentRW<MissileWarning>(w.Unit) = warning;
                    else repo.AddComponent(w.Unit, warning);
                }
                if (sensors) ecb.PublishEvent(new SensorChangedEvent { Unit = w.Unit, What = SensorChange.MissileWarning });
            }
        }
    }
}
