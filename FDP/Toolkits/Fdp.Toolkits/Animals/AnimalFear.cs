using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-3 (docs/designs/animals/DESIGN_Animals.md AR-3, AR-4) — the ANIMAL half of the ONE fear system
    /// (<c>CivilianFearSystem</c> calls it; one system, one place fear is kept). Three ways an animal is frightened:
    /// <list type="number">
    /// <item>what it HEARS — the civilians' impulse table (<c>CivilianFear.Impulse</c>: shots, heavy weapons, blasts), from EVERY force
    /// (the hearing sensor lifts the own-side skip for an animal); ⛔ a continuous sound (an engine, a rotor, footsteps) adds nothing;</item>
    /// <item>what is DONE to it — a round passing or hitting it;</item>
    /// <item>⭐ the FLIGHT ZONE — what it SEES near: a person or a vehicle inside the species' distances (livestock handling practice),
    /// a ground-truth look ~5 times a second, no vision sensor per sheep. ⭐ A threat that APPROACHES frightens it (up to
    /// <see cref="ApproachCap"/>); one that only passes by (a car on the road) leaves it uneasy (<see cref="PassingCap"/>) — a busy road
    /// never panics it for good. Inside the PANIC zone it panics.</item>
    /// </list>
    /// Fear fades from 1 to 0 over the species' recovery time. ⭐ A FEARLESS species (the guard dog, the war horse) ignores all of it but a
    /// blast, which makes it flinch (<see cref="FearlessBlastFactor"/>). The band (calm · uneasy · frightened · panic) is written to the
    /// replicated <see cref="AnimalState.FearBand"/> only when it changes.
    /// </summary>
    public static class AnimalFear
    {
        /// <summary>How often an animal looks round its flight zone (sim seconds).</summary>
        public const float ZoneLookSeconds = 0.2f;
        /// <summary>A vehicle's flight zone is this much wider than a man's (bigger, louder, faster) — the panic zone is not.</summary>
        public const float VehicleZoneFactor = 1.5f;
        /// <summary>A threat closing faster than this (m/s) counts as approaching in full.</summary>
        public const float ApproachMps = 3f;
        /// <summary>Below this closing speed (m/s) a threat is passing by, not coming.</summary>
        public const float PassingMps = 0.5f;
        /// <summary>A threat is heading AT it when its track passes within this fraction of the flight zone.</summary>
        public const float HeadingAtMeFraction = 0.5f;
        /// <summary>Fear per second from an approaching threat in the flight zone (at <see cref="ApproachMps"/>; a slower one less).</summary>
        public const float ApproachRate = 0.9f;
        /// <summary>Fear per second from a threat in the flight zone that is NOT coming (standing, passing, leaving).</summary>
        public const float PassingRate = 0.15f;
        /// <summary>The most an approaching threat in the flight zone (not the panic zone) can raise fear to: frightened, moving off.</summary>
        public const float ApproachCap = 0.6f;
        /// <summary>The most a passing threat can raise fear to: uneasy, head up.</summary>
        public const float PassingCap = 0.3f;
        /// <summary>Inside the panic zone fear is at least this: it runs.</summary>
        public const float PanicFear = 0.85f;
        /// <summary>A fearless animal flinches at a blast at this fraction of the impulse; nothing else touches it.</summary>
        public const float FearlessBlastFactor = 0.4f;
        /// <summary>A species with no recovery time recovers in this (s).</summary>
        public const float MinRecoverySeconds = 5f;

        /// <summary>The band every node sees: 0 calm (&lt; 0.15) · 1 uneasy (&lt; 0.4) · 2 frightened (&lt; 0.75) · 3 panic.</summary>
        public static byte Band(float fear) => fear < 0.15f ? (byte)0 : fear < 0.4f ? (byte)1 : fear < 0.75f ? (byte)2 : (byte)3;

        /// <summary>The fear one heard sound adds to an animal of temperament <paramref name="t"/> (the civilians' table; a fearless one only
        /// flinches at a blast).</summary>
        public static float Impulse(AnimalTemperamentDto? t, SoundSourceClass heard, float distanceMetres)
        {
            float i = Fdp.Toolkit.Civilians.CivilianFear.Impulse(heard, distanceMetres);
            if (t != null && t.Fearless) return heard == SoundSourceClass.Explosion ? i * FearlessBlastFactor : 0f;
            return i;
        }

        /// <summary>The temperament of <paramref name="animal"/>'s type, or null (an untyped test animal: the default zones of none).</summary>
        public static AnimalTemperamentDto? TemperamentOf(EntityRepository repo, Entity animal)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, animal, out var t) ? t.GetDescriptor<AnimalTemperamentDto>() : null;

        /// <summary>Raise <paramref name="animal"/>'s fear by <paramref name="impulse"/> from <paramref name="from"/> (heard or felt).</summary>
        public static void Frighten(EntityRepository repo, Entity animal, float impulse, double now, Vector3? from)
        {
            if (impulse <= 0f || !repo.HasComponent<Fear>(animal)) return;
            ref var f = ref repo.GetComponentRW<Fear>(animal);
            f.Value = MathF.Min(1f, f.Value + impulse);
            f.LastFrightAt = now;
            f.Seen = 0;
            if (from is { } p) { f.FrightFrom = p; f.HasFrightFrom = 1; }
        }

        /// <summary>
        /// One flight-zone look for one animal against the frame's threats. Returns true when it was frightened.
        /// </summary>
        public static bool LookRound(ref Fear f, AnimalTemperamentDto t, Vector3 here, ReadOnlySpan<Threat> threats, double now)
        {
            if (t.Fearless || t.FlightZoneM <= 0f) return false;
            float best = 0f;      // the strongest effect this look
            float cap = 0f;
            Vector3 from = default;
            bool panic = false;
            for (int i = 0; i < threats.Length; i++)
            {
                ref readonly var th = ref threats[i];
                float dx = here.X - th.Position.X, dy = here.Y - th.Position.Y;
                float d2 = dx * dx + dy * dy;
                float zone = th.IsVehicle ? t.FlightZoneM * VehicleZoneFactor : t.FlightZoneM;
                if (d2 >= zone * zone) continue;
                float d = MathF.Sqrt(d2);
                if (d < t.PanicZoneM) { panic = true; from = th.Position; break; }
                // closing speed: its velocity toward me
                float closing = d > 1e-3f ? (th.Velocity.X * dx + th.Velocity.Y * dy) / d : 0f;
                // ⭐ COMING = closing AND heading at me: its track passes within half my flight zone (a car on the road beside the field
                //   closes too, but its track misses — it passes by)
                float speed = MathF.Sqrt(th.Velocity.X * th.Velocity.X + th.Velocity.Y * th.Velocity.Y);
                float miss = speed > 0.1f ? MathF.Abs(th.Velocity.X * dy - th.Velocity.Y * dx) / speed : d;
                bool coming = closing > PassingMps && miss < t.FlightZoneM * HeadingAtMeFraction;
                float rate = coming ? ApproachRate * MathF.Min(1f, closing / ApproachMps) : PassingRate;
                float c = coming ? ApproachCap : PassingCap;
                if (c > cap || (c == cap && rate > best)) { best = rate; cap = c; from = th.Position; }
            }
            if (panic)
            {
                f.Value = MathF.Max(f.Value, PanicFear);
                Mark(ref f, now, from);
                return true;
            }
            if (cap <= 0f) return false;
            if (f.Value < cap) f.Value = MathF.Min(cap, f.Value + best * ZoneLookSeconds);
            Mark(ref f, now, from);
            return true;
        }

        private static void Mark(ref Fear f, double now, Vector3 from)
        {
            f.LastFrightAt = now;
            f.FrightFrom = from;
            f.HasFrightFrom = 1;
            f.Seen = 1;
        }

        /// <summary>The fade: from 1 to 0 over the species' recovery time; ⛔ never in the frame of a fright.</summary>
        public static void Fade(ref Fear f, AnimalTemperamentDto? t, double now, float dt)
        {
            if (f.LastFrightAt == now || f.Value <= 0f) return;
            float recovery = t != null && t.RecoverySeconds > 0f ? MathF.Max(MinRecoverySeconds, t.RecoverySeconds) : MinRecoverySeconds;
            f.Value = MathF.Max(0f, f.Value - dt / recovery);
        }

        /// <summary>One thing an animal can see in its flight zone: a person or a vehicle, where it is and how it moves.</summary>
        public struct Threat
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public bool IsVehicle;
        }
    }
}
