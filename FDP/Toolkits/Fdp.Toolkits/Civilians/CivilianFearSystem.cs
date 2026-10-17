using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-2, CV-3; docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — how frightening one thing a civilian heard or felt
    /// is. A shot, a heavy weapon or a blast raises his fear by its class's impulse, less the farther away it seemed (half at the edge of
    /// hearing); a round that passes him or hits him raises it more. Fear falls back by <see cref="DecayPerSecond"/>.
    /// </summary>
    public static class CivilianFear
    {
        /// <summary>The distance at which a sound frightens half as much as one beside him — the civilians' hearing range (CV-2).</summary>
        public const float FalloffMetres = 100f;
        public const float SmallArmsImpulse = 0.45f;
        public const float HeavyWeaponImpulse = 0.6f;
        public const float ExplosionImpulse = 0.8f;
        public const float NearMissImpulse = 0.6f;
        public const float HitImpulse = 1f;
        /// <summary>How fast fear fades when nothing new frightens him (per second).</summary>
        public const float DecayPerSecond = 0.05f;

        /// <summary>The fear one heard sound adds: its class's impulse × (1 − ½·distance / <see cref="FalloffMetres"/>); 0 for a sound that
        /// is not violence (footsteps, engines, rotors).</summary>
        public static float Impulse(SoundSourceClass heard, float distanceMetres)
        {
            float impulse = heard switch
            {
                SoundSourceClass.SmallArms => SmallArmsImpulse,
                SoundSourceClass.HeavyWeapon => HeavyWeaponImpulse,
                SoundSourceClass.Explosion => ExplosionImpulse,
                _ => 0f,
            };
            float far = Math.Clamp(distanceMetres / FalloffMetres, 0f, 1f);
            return impulse * (1f - 0.5f * far);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2235</c> (§5d) — every civilian's fear, once a frame, on the node that runs his brain: the sounds he heard
    /// (<see cref="SoundContactEvent"/>, grouped by listener in ONE pass over the frame's events, so the cost is the events, not
    /// civilians × events), the rounds that passed or hit him (<see cref="SensorChangedEvent"/> NearMiss / Hit), then the fade. Only
    /// a unit that carries <see cref="CivilianState"/> (added by his <c>CivilianHsm</c>) is touched. The HSM reads the result.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class CivilianFearSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<CivilianState>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;

            if (repo.Bus.IsRegistered<SoundContactEvent>())
                foreach (ref readonly var heard in repo.Bus.Read<SoundContactEvent>())
                {
                    if (!repo.IsAlive(heard.Observer) || !repo.HasComponent<CivilianState>(heard.Observer)) continue;
                    float distance = 0f;
                    if (repo.HasComponent<SimTransform>(heard.Observer))
                    {
                        var me = repo.GetComponentRO<SimTransform>(heard.Observer).Position;
                        float dx = heard.X - me.X, dy = heard.Y - me.Y;
                        distance = MathF.Sqrt(dx * dx + dy * dy);
                    }
                    Frighten(repo, heard.Observer, CivilianFear.Impulse((SoundSourceClass)heard.SourceClass, distance), now);
                }

            if (repo.Bus.IsRegistered<SensorChangedEvent>())
                foreach (var change in repo.Bus.Read<SensorChangedEvent>())
                {
                    if (!repo.IsAlive(change.Unit) || !repo.HasComponent<CivilianState>(change.Unit)) continue;
                    if (change.What == SensorChange.NearMiss) Frighten(repo, change.Unit, CivilianFear.NearMissImpulse, now);
                    else if (change.What == SensorChange.Hit) Frighten(repo, change.Unit, CivilianFear.HitImpulse, now);
                }

            float fade = CivilianFear.DecayPerSecond * deltaTime;
            foreach (var e in repo.Query().With<CivilianState>().Build())
            {
                ref var mind = ref repo.GetComponentRW<CivilianState>(e);
                if (mind.LastFrightAt == now) continue;   // ⭐ a fright this frame is not faded in the same frame
                mind.Fear = MathF.Max(0f, mind.Fear - fade);
            }
        }

        private static void Frighten(EntityRepository repo, Entity unit, float impulse, double now)
        {
            if (impulse <= 0f) return;
            ref var mind = ref repo.GetComponentRW<CivilianState>(unit);
            mind.Fear = MathF.Min(1f, mind.Fear + impulse);
            mind.LastFrightAt = now;
        }
    }
}
