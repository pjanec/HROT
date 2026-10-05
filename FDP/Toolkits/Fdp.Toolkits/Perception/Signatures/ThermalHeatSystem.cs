using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — heat builds while an entity runs or fires and cools when it stops (🔒 user, 2026-10-05: "Hot when
    /// running or firing - pls implement some simple heat accumulation and cooldown"). docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.2.
    /// <code>
    /// heat += RunningHeatPerSecond × min(1, speed / ReferenceSpeed) × dt + FiringHeatPerShot × shots
    /// heat -= heat × CooldownPerSecond × dt;   clamp 0..1
    /// </code>
    /// Runs on the main loop where the entity moves and fires (the thermal sensor solves on a background snapshot of it).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class ThermalHeatSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ThermalState>()) return;

            // Shots fired this frame (FireProcessingSystem's notification, one per round).
            if (repo.Bus.IsRegistered<WeaponFireNotification>())
                foreach (ref readonly var shot in view.ReadEvents<WeaponFireNotification>())
                    if (repo.IsAlive(shot.Shooter) && repo.HasComponent<ThermalState>(shot.Shooter))
                    {
                        ref var t = ref repo.GetComponentRW<ThermalState>(shot.Shooter);
                        t.Heat = MathF.Min(1f, t.Heat + t.FiringHeatPerShot);
                    }

            foreach (var e in repo.Query().With<ThermalState>().With<SimTransform>().Build())
            {
                ref var t = ref repo.GetComponentRW<ThermalState>(e);
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                float speed = 0f;
                if (t.HasLast && deltaTime > 0f)
                {
                    float dx = p.X - t.LastX, dy = p.Y - t.LastY, dz = p.Z - t.LastZ;
                    speed = MathF.Sqrt(dx * dx + dy * dy + dz * dz) / deltaTime;
                }
                t.LastX = p.X; t.LastY = p.Y; t.LastZ = p.Z; t.HasLast = true;

                float reference = t.ReferenceSpeed > 0f ? t.ReferenceSpeed : 5f;
                float heat = t.Heat + t.RunningHeatPerSecond * MathF.Min(1f, speed / reference) * deltaTime;
                heat -= heat * t.CooldownPerSecond * deltaTime;
                t.Heat = Math.Clamp(heat, 0f, 1f);
            }
        }
    }
}
