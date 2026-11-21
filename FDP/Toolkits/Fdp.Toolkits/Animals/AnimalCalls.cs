using System;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-8 (docs/designs/animals/DESIGN_Animals.md AR-7, AN-19) — the ANIMALS' calls, sim side: what each call kind sounds like to
    /// an ear (its range and its class) and WHEN a calm animal calls — in CLOSED FORM, so the presentation can play the same calls from the same
    /// numbers. The call itself is an <see cref="Utterance"/> (the one voice path, replicated): the AI hears it through <c>AcousticPerception</c>
    /// exactly as the operator hears it.
    /// </summary>
    public static class AnimalCalls
    {
        /// <summary>How far a call at rest carries (m) when the species gives none.</summary>
        public const float CallRangeM = 300f;
        /// <summary>How far an alarm call carries (m).</summary>
        public const float AlarmRangeM = 600f;
        /// <summary>How far a bark carries (m) — a sentry hears a dog well before he sees it.</summary>
        public const float BarkRangeM = 800f;
        /// <summary>How long one call lasts (s).</summary>
        public const float CallSeconds = 1.5f;
        /// <summary>A herd calls about this many times per member's interval, whatever its size (the per-herd throttle, AR-7).</summary>
        public const int CallersPerHerd = 3;

        public static bool IsAnimalCall(UtteranceKind k) => k is UtteranceKind.AnimalCall or UtteranceKind.AnimalAlarm or UtteranceKind.Bark;

        /// <summary>The range and the sound class an ear hears <paramref name="kind"/> at: an animal call at its own range (a call is a quiet
        /// <see cref="SoundSourceClass.Footsteps"/>-class sound, an alarm and a bark <see cref="SoundSourceClass.Animal"/>); anything else is a man's
        /// shout (<paramref name="voiceRange"/>, footsteps class) as before.</summary>
        public static void Heard(UtteranceKind kind, float voiceRange, out float range, out byte cls)
        {
            switch (kind)
            {
                case UtteranceKind.AnimalCall: range = CallRangeM; cls = (byte)SoundSourceClass.Footsteps; return;
                case UtteranceKind.AnimalAlarm: range = AlarmRangeM; cls = (byte)SoundSourceClass.Animal; return;
                case UtteranceKind.Bark: range = BarkRangeM; cls = (byte)SoundSourceClass.Animal; return;
                default: range = voiceRange; cls = (byte)SoundSourceClass.Footsteps; return;
            }
        }

        /// <summary>
        /// The CLOSED-FORM call schedule: the k-th call of animal <paramref name="index"/> starts at <c>k·I + jitter(index, k)</c> with I its interval
        /// (stretched for a herd of <paramref name="herdSize"/> so the herd calls ~<see cref="CallersPerHerd"/> times per interval). Returns the call
        /// number due at <paramref name="now"/> (≥ 0), or −1 when none is sounding. Pure: any node computes the same.
        /// </summary>
        public static long CallDue(uint index, double now, float intervalSeconds, int herdSize)
        {
            if (intervalSeconds <= 0f || now < 0) return -1;
            double interval = intervalSeconds * Math.Max(1.0, herdSize / (double)CallersPerHerd);
            long k = (long)Math.Floor(now / interval);
            double start = k * interval + Jitter(index, k) * interval * 0.8;
            return now >= start && now < start + CallSeconds ? k : -1;
        }

        /// <summary>A deterministic 0..1 per animal and call number.</summary>
        public static double Jitter(uint index, long k)
        {
            unchecked
            {
                uint h = index * 2654435761u ^ (uint)k * 2246822519u ^ (uint)(k >> 32) * 3266489917u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
