using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3062</c> — keeps every <see cref="AcousticEmitter"/>'s current sounds (🔒 user, 2026-10-05: "Moving entity also makes
    /// sound"): movement carries <c>MovingAudibleRange × min(1, speed / ReferenceSpeed)</c> while faster than
    /// <see cref="MinAudibleSpeed"/>; a shot and a detonation stay audible for <see cref="SoundLingerSeconds"/>, so a sensor the
    /// solver's budget defers by a tick still hears them. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 B–C.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class SoundEmissionSystem : IEcsModuleSystem
    {
        /// <summary>Below this speed (m/s) an entity's movement makes no sound.</summary>
        public const float MinAudibleSpeed = 0.5f;

        /// <summary>How long a shot or a detonation can still be heard (s) — covers a sensor deferred by the solver's budget.</summary>
        public const float SoundLingerSeconds = 0.5f;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AcousticEmitter>()) return;

            foreach (var e in repo.Query().With<AcousticEmitter>().With<SimTransform>().Build())
            {
                ref var a = ref repo.GetComponentRW<AcousticEmitter>(e);
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                float speed = 0f;
                if (a.HasLast && deltaTime > 0f)
                {
                    float dx = p.X - a.LastX, dy = p.Y - a.LastY, dz = p.Z - a.LastZ;
                    speed = MathF.Sqrt(dx * dx + dy * dy + dz * dz) / deltaTime;
                }
                else if (a.HasLast) speed = -1f;   // a zero-dt frame keeps last tick's movement sound
                a.LastX = p.X; a.LastY = p.Y; a.LastZ = p.Z; a.HasLast = true;

                if (speed >= 0f)
                {
                    float reference = a.ReferenceSpeed > 0f ? a.ReferenceSpeed : 5f;
                    a.CurrentMovingRange = speed > MinAudibleSpeed ? a.MovingAudibleRange * MathF.Min(1f, speed / reference) : 0f;
                }
                a.ShotTimeLeft       = MathF.Max(0f, a.ShotTimeLeft - deltaTime);
                a.DetonationTimeLeft = MathF.Max(0f, a.DetonationTimeLeft - deltaTime);
            }

            if (repo.Bus.IsRegistered<WeaponFireNotification>())
                foreach (ref readonly var shot in view.ReadEvents<WeaponFireNotification>())
                {
                    if (!repo.IsAlive(shot.Shooter) || !repo.HasComponent<AcousticEmitter>(shot.Shooter)
                        || !repo.HasComponent<SimTransform>(shot.Shooter)) continue;
                    ref var a = ref repo.GetComponentRW<AcousticEmitter>(shot.Shooter);
                    var p = repo.GetComponentRO<SimTransform>(shot.Shooter).Position;
                    a.ShotTimeLeft = SoundLingerSeconds;
                    a.ShotX = p.X; a.ShotY = p.Y; a.ShotZ = p.Z;
                }

            if (repo.Bus.IsRegistered<DetonationNotification>())
                foreach (ref readonly var det in view.ReadEvents<DetonationNotification>())
                {
                    if (!repo.IsAlive(det.Shooter) || !repo.HasComponent<AcousticEmitter>(det.Shooter)) continue;
                    ref var a = ref repo.GetComponentRW<AcousticEmitter>(det.Shooter);
                    a.DetonationTimeLeft = SoundLingerSeconds;
                    a.DetonationX = det.HitX; a.DetonationY = det.HitY; a.DetonationZ = det.HitZ;
                }
        }
    }
}
