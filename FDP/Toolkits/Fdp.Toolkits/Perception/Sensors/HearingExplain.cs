using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>⭐ AQ106 L — why a sound was heard or not (<see cref="HearingVerdict.Reason"/>).</summary>
    public enum HearingReason : byte
    {
        Heard = 0,
        /// <summary>Farther than the sound carries (after the weather, for a loud one).</summary>
        BeyondItsCarry = 1,
        /// <summary>Farther than his ear reaches (a quiet sound; the weather shortens it).</summary>
        BeyondHisEar = 2,
        /// <summary>Within both, but the walls / slabs, or the corners and closed doors of the way round, took too much.</summary>
        WallsOrCorners = 3,
        /// <summary>His own sound, or his own side's — never heard (R-205).</summary>
        HisOwnSide = 4,
        /// <summary>The sound carries nowhere.</summary>
        Silent = 5,
    }

    /// <summary>⭐ AQ106 L — the hearing sensor's verdict on one sound (<see cref="AcousticSensorGenerator"/>'s <c>Judge</c>, the ONE place it is
    /// decided): the straight distance, the carry left after the walls or the way round, and the route it took.</summary>
    public readonly record struct HearingVerdict(HearingReason Reason, float Distance, float EffectiveCarry, SoundRoute Route);

    /// <summary>⭐ AQ106 L — one sound near a listener, explained.</summary>
    public sealed record HeardSoundExplanation(ulong Source, SoundKind Kind, byte Class, bool Voice, Vector3 At, float Carry, double Age,
                                               HearingVerdict Verdict, IReadOnlyList<Vector3> Waypoints);

    /// <summary>
    /// ⭐⭐ AQ106 L (R-3050; 🔒 user, 2026-10-25: <i>"how could we debug that sounds 'travel' properly? (what sounds could be heard and what could
    /// not by an entity)"</i>) — every sound near a listener in the last seconds, each with the verdict the hearing sensor reaches on it and the
    /// way it took: the ring's short sounds (shots, bursts, doors), and the bodies moving or shouting now. ⭐ A DRY RUN of the sensor — its own
    /// ears and its one <c>Judge</c> — so it cannot disagree with what was heard. It plants no cursor and writes nothing.
    /// </summary>
    public static class HearingExplain
    {
        /// <summary>How far from the listener the moving / shouting bodies are listed (× his ear range): beyond, nothing could be heard.</summary>
        public const float ListedRangeFactor = 2f;

        /// <summary>Explains <paramref name="unit"/>'s hearing over the last <paramref name="seconds"/>; false (and why) when he has no ears here.</summary>
        public static bool TryExplain(ISimulationView view, Entity unit, double seconds, List<HeardSoundExplanation> into, out string? why)
        {
            into.Clear();
            why = null;
            var sensor = AcousticSensorOf(view, unit);
            if (sensor.IsNull) { why = "no acoustic sensor of his on this node (hearing runs on the Perception node)"; return false; }
            if (!AcousticSensorGenerator.TryEars(view, sensor, unit, out var ears)) { why = "his acoustic sensor hears nothing (no range, or he has no position)"; return false; }
            byte ourForce = view.HasComponent<EntityInfo>(unit) ? (byte)view.GetComponentRO<EntityInfo>(unit).ForceId : (byte)0;
            double now = view is EntityRepository clock && clock.HasSingleton<GlobalTime>() ? clock.GetSingleton<GlobalTime>().TotalTime : 0d;

            // the ring: every short sound made in the window (the sensor's own reading, without its cursor)
            if (SoundEmissionRing.In(view) is { } ring)
            {
                Span<SoundEmission> sounds = stackalloc SoundEmission[AcousticSensorGenerator.MaxSoundsPerSolve];
                int k = ring.ReadAfter(0, sounds, out _, notBefore: now - Math.Max(0d, seconds));
                for (int i = 0; i < k; i++)
                {
                    var e = sounds[i];
                    bool own = e.Source == unit.PackedValue || e.Force == ourForce;
                    Add(into, in ears, e.Source, (SoundKind)e.Kind, e.Class, false, e.At, e.Carry, now - e.Time, own);
                }
            }

            // the bodies moving or shouting now, near him (the broadphase's own-side rule applied as a reason, not a filter)
            float listed = ears.Range * ListedRangeFactor;
            foreach (var body in view.Query().With<SimTransform>().With<EntityInfo>().Build())
            {
                if (body == unit) continue;
                var at = view.GetComponentRO<SimTransform>(body).Position;
                if (Vector3.Distance(at, ears.At) > Math.Max(listed, AcousticPerception.VoiceRange)) continue;
                bool own = (byte)view.GetComponentRO<EntityInfo>(body).ForceId == ourForce;
                if (Fdp.Toolkit.Civilians.Utterances.IsShouted(Fdp.Toolkit.Civilians.Utterances.Of(view, body, now, out _)))
                    Add(into, in ears, body.PackedValue, SoundKind.Movement, (byte)SoundSourceClass.Footsteps, true, at, AcousticPerception.VoiceRange, 0d, own);
                if (view.HasComponent<AcousticEmitter>(body) && view.GetComponentRO<AcousticEmitter>(body) is { CurrentMovingRange: > 0f } a)
                    Add(into, in ears, body.PackedValue, SoundKind.Movement, a.MovingClass, false, at, a.CurrentMovingRange, 0d, own);
            }
            return true;
        }

        private static void Add(List<HeardSoundExplanation> into, in AcousticSensorGenerator.Ears ears, ulong source, SoundKind kind, byte cls, bool voice,
                                Vector3 at, float carry, double age, bool own)
        {
            var waypoints = new List<Vector3>();
            var verdict = AcousticSensorGenerator.Judge(in ears, at, carry, cls, waypoints);
            if (own) verdict = verdict with { Reason = HearingReason.HisOwnSide };
            into.Add(new HeardSoundExplanation(source, kind, cls, voice, at, carry, age, verdict, waypoints));
        }

        /// <summary>The acoustic sensor that hears for <paramref name="unit"/>: a sensor child of his, or he himself (the legacy single sensor).</summary>
        public static Entity AcousticSensorOf(ISimulationView view, Entity unit)
        {
            if (!view.IsAlive(unit)) return Entity.Null;
            foreach (var e in view.Query().WithManaged<SensorCapability>().Build())
            {
                if (view.GetManagedComponentRO<SensorCapability>(e).Current.Acoustic is null) continue;
                if (e == unit) return e;
                if (view.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(e)
                    && view.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(e).ParentEntity == unit) return e;
            }
            return Entity.Null;
        }
    }
}
