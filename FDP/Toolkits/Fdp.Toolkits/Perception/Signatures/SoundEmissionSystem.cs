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
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class SoundEmissionSystem : IEcsModuleSystem
    {
        /// <summary>Below this speed (m/s) an entity's movement makes no sound.</summary>
        public const float MinAudibleSpeed = 0.5f;

        /// <summary>How long a shot or a detonation can still be heard (s) — covers a sensor deferred by the solver's budget.</summary>
        public const float SoundLingerSeconds = 0.5f;

        // ⭐ CE-3315 — the world restores this system has numbered on from (WorldEpoch.RestoresOf)
        private int _restoresSeen;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AcousticEmitter>()) return;

            // ⭐ CE-3282 (Q95 C; Sensing §8.3) — the lossless ring HEARING reads (the emitter's 0.5 s linger below now only feeds the map's
            //   hearing gizmo); its clock moves every frame, so "too old" is measured on this node's time
            var ring = SoundEmissionRing.Of(repo);
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            // ⭐ CE-3315 (restore design §1k) — the world was put back: the ring numbers on where the restored world stood — the count the
            //   world carries (a checkpoint restore, here or in a fresh process), or, when that count is the abandoned future's (a dry run
            //   puts no singleton back), what this ring had made before the restored clock
            int restores = Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(view);
            //   ⭐ CE-3316 — and with it the newest sounds, so one a sensor had not read yet is still heard
            if (restores != _restoresSeen)
            {
                _restoresSeen = restores;
                Span<SoundEmission> tail = stackalloc SoundEmission[SoundTimeline.TailCapacity];
                long places;
                int n;
                if (repo.HasSingleton<SoundTimeline>() && repo.GetSingleton<SoundTimeline>().At <= now + 1e-6)
                {
                    ref readonly var carried = ref repo.GetSingleton<SoundTimeline>();
                    places = carried.Places;
                    n = carried.TailCount;
                    carried.TailRO().CopyTo(tail);
                }
                else n = ring.TailBefore(now, tail, out places);
                ring.Resume(places, tail[..n], now);
            }
            ring.Tick(now);

            bool flight = repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.FlightState>();
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
                // ⭐ M7 (DESIGN_FPV_Drone §2) — a flyer is loud while airborne at ANY speed: a hovering drone is not silent
                if (flight && repo.HasComponent<Fdp.Toolkit.Motion.FlightState>(e) && repo.GetComponentRO<Fdp.Toolkit.Motion.FlightState>(e).Airborne != 0)
                    a.CurrentMovingRange = a.MovingAudibleRange;
                a.ShotTimeLeft       = MathF.Max(0f, a.ShotTimeLeft - deltaTime);
                a.DetonationTimeLeft = MathF.Max(0f, a.DetonationTimeLeft - deltaTime);
            }

            if (repo.Bus.IsRegistered<WeaponFireNotification>())
                foreach (ref readonly var shot in view.ReadEvents<WeaponFireNotification>())
                {
                    if (!shot.Fired) continue;   // ⭐ Q94 A-2 (R-3018) — a REFUSED shot never flew: nothing to hear, see or fear
                    if (!repo.IsAlive(shot.Shooter) || !repo.HasComponent<AcousticEmitter>(shot.Shooter)
                        || !repo.HasComponent<SimTransform>(shot.Shooter)) continue;
                    // ⭐ CE-2188 (BS-1-DESIGN §5.4b F5) — a flare / chaff release is no gunshot: no report
                    if (Fdp.Toolkit.Combat.Munitions.IsExpendable(repo, shot.AmmoGuid)) continue;
                    // ⭐ CE-3248 (TC-6) — a stone or a grenade THROWN is no gunshot either: no report
                    if (Fdp.Toolkit.Combat.PersonHands.Threw(repo, in shot)) continue;
                    ref var a = ref repo.GetComponentRW<AcousticEmitter>(shot.Shooter);
                    var p = repo.GetComponentRO<SimTransform>(shot.Shooter).Position;
                    a.ShotTimeLeft = SoundLingerSeconds;
                    a.ShotX = p.X; a.ShotY = p.Y; a.ShotZ = p.Z;
                    if (a.FiringAudibleRange > 0f)
                        ring.Append(Emission(repo, shot.Shooter, SoundKind.Shot, a.FiringClass, p, a.FiringAudibleRange, ring.Now, TypeOf(repo, shot.Shooter), a.Family));
                }

            if (repo.Bus.IsRegistered<DetonationNotification>())
                foreach (ref readonly var det in view.ReadEvents<DetonationNotification>())
                {
                    if (!repo.IsAlive(det.Shooter) || !repo.HasComponent<AcousticEmitter>(det.Shooter)) continue;
                    ref var a = ref repo.GetComponentRW<AcousticEmitter>(det.Shooter);
                    a.DetonationTimeLeft = SoundLingerSeconds;
                    a.DetonationX = det.HitX; a.DetonationY = det.HitY; a.DetonationZ = det.HitZ;
                    if (a.DetonationAudibleRange > 0f)
                        ring.Append(Emission(repo, det.Shooter, SoundKind.Detonation, a.DetonationClass, new System.Numerics.Vector3(det.HitX, det.HitY, det.HitZ),
                                             a.DetonationAudibleRange, ring.Now, det.Ammo, a.Family));   // a burst's signature is its munition (Q95 A)
                }

            // ⭐ CE-3315/CE-3316 — the world's count of this timeline's sounds and the newest of them, kept beside the ring (a checkpoint
            //   carries them; the ring it cannot)
            long count = ring.Places;
            if (!repo.HasSingleton<SoundTimeline>() || repo.GetSingleton<SoundTimeline>().Places != count)
            {
                var timeline = new SoundTimeline { At = now };
                timeline.TailCount = ring.TailBefore(double.PositiveInfinity, timeline.TailSpan(), out timeline.Places);
                repo.SetSingletonUnmanaged(timeline);
            }
        }

        // ⭐ CE-3282 — one ring entry: the source (anonymous to the listener — only the error's seed and the own-force test read it).
        private static SoundEmission Emission(EntityRepository repo, Entity source, SoundKind kind, byte cls, System.Numerics.Vector3 at, float carry,
                                              double time, long signature, byte family)
            => new()
            {
                Source = source.PackedValue,
                Force  = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(source) ? (byte)repo.GetComponentRO<EntityInfo>(source).ForceId : (byte)0,
                Kind   = (byte)kind, Class = cls, At = at, Carry = carry, Time = time, Signature = signature, Family = family,
            };

        // ⭐ Q95 A — a shot's signature is the weapon's TKB type (a crewed weapon's round names the weapon, WS-3)
        private static long TypeOf(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>() && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e)
                ? repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType : 0L;
    }
}
