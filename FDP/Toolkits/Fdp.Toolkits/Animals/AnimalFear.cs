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
            public Entity Entity;
        }

        // ── ⭐ AB-5 — the guard dog's ground, the ridden horse's bolt ─────────────────────────────────────────────────────────────────

        /// <summary>A territorial animal notices a person this far beyond its home's edge (m) — it barks at him from inside.</summary>
        public const float BarkBeyondHomeMetres = 15f;
        /// <summary>A territorial animal with no home guards this far round itself (m).</summary>
        public const float GuardMetres = 25f;
        /// <summary>A bolt lasts this long (s) — then the rider has the horse again (unless it is still past its bolt line).</summary>
        public const float BoltSeconds = 6f;

        /// <summary>
        /// The nearest PERSON (not a vehicle) a territorial animal guards against: inside its home plus <see cref="BarkBeyondHomeMetres"/>
        /// (or within <see cref="GuardMetres"/> of it when it has no home). Null = none.
        /// </summary>
        public static Entity FindIntruder(Vector3 here, bool hasHome, in AnimalHome home, ReadOnlySpan<Threat> threats)
        {
            Entity best = Entity.Null;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < threats.Length; i++)
            {
                ref readonly var th = ref threats[i];
                if (th.IsVehicle) continue;
                float reach;
                Vector3 centre;
                if (hasHome && home.Radius > 0f) { centre = home.Centre; reach = home.Radius + BarkBeyondHomeMetres; }
                else { centre = here; reach = GuardMetres; }
                float cx = th.Position.X - centre.X, cy = th.Position.Y - centre.Y;
                if (cx * cx + cy * cy > reach * reach) continue;
                float dx = th.Position.X - here.X, dy = th.Position.Y - here.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = th.Entity; }
            }
            return best;
        }

        /// <summary>
        /// ⭐ AB-5 (AR-10) — a BOLTING horse ignores its rider: full gallop, turning away from what frightened it (a reversal by the full
        /// steer, never backing — it has no reverse). Called by the manual drive (the kinematics) on its own entity; false when not bolting.
        /// </summary>
        public static bool BoltDrive(EntityRepository repo, Entity e, Vector2 pos, Vector2 fwd, ref Fdp.Toolkit.Navigation.ManualDrive drive)
        {
            if (!repo.IsComponentTypeRegistered<AnimalState>() || !repo.HasComponent<AnimalState>(e)) return false;
            if ((repo.GetComponentRO<AnimalState>(e).Flags & AnimalFlags.Bolting) == 0) return false;
            var f = repo.IsComponentTypeRegistered<Fear>() && repo.HasComponent<Fear>(e) ? repo.GetComponentRO<Fear>(e) : default;
            Vector2 away = f.HasFrightFrom != 0 ? pos - new Vector2(f.FrightFrom.X, f.FrightFrom.Y) : fwd;
            if (away.LengthSquared() < 1e-4f) away = fwd;
            away = Vector2.Normalize(away);
            float cross = fwd.X * away.Y - fwd.Y * away.X;   // + ⇒ away is to the left (ManualDrive.Steer + = left)
            float steer = Vector2.Dot(fwd, away) < 0f ? (cross >= 0f ? 1f : -1f) : Math.Clamp(cross * 3f, -1f, 1f);
            drive.Forward = 1f;
            drive.Steer = steer;
            drive.Lateral = 0f;
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-4623</c> AB-7 (AR-5) — the PANIC BREACH: a big animal of a species that breaks fences (<c>Breaks</c>), fleeing in PANIC (mode Flee,
        /// band 3), may break a SMALL barrier it runs into through the ONE contact rule (<c>ContactBreach</c>, its "panicked-animal" row) — never a wall.
        /// </summary>
        public static bool PanicBreaks(EntityRepository repo, Entity e)
        {
            if (!repo.IsComponentTypeRegistered<AnimalState>() || !repo.HasComponent<AnimalState>(e)) return false;
            var st = repo.GetComponentRO<AnimalState>(e);
            if (st.Mode != AnimalMode.Flee || st.FearBand < 3) return false;
            return TemperamentOf(repo, e) is { Breaks: true };
        }

        /// <summary>A horse that can bolt (<c>BoltFear</c> &gt; 0) bolts for <see cref="BoltSeconds"/> once its fear passes the line; true while it does.</summary>
        public static bool UpdateBolt(ref Fear f, AnimalTemperamentDto t, double now)
        {
            if (t.BoltFear <= 0f) { f.BoltUntil = 0; return false; }
            if (f.Value >= t.BoltFear && f.BoltUntil <= now) f.BoltUntil = now + BoltSeconds;
            return f.BoltUntil > now;
        }
    }
}
