using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Tkb.Parameters;
using Fdp.Toolkit.World;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2260</c> — which grenade goes into a room before the entry.</summary>
    public enum RoomGrenadeKind : byte
    {
        None = 0,
        Stun = 1,
        Fragmentation = 2,
    }

    /// <summary>⭐ <c>CE-2260</c> / <c>CE-2261</c> (CB-66) — what the squad knows about a room before it goes in.</summary>
    /// <param name="HostileKnown">⭐ CE-2261 (CB-72, as built) — a hostile the squad's picture still holds in the room, alive and in the fight,
    /// seen lately or not: the contact is not over while he is there.</param>
    public readonly record struct RoomKnowledge(bool Suspected, bool CiviliansKnown, bool HostileSeen, bool HostileKnown = false);

    /// <summary>
    /// ⭐⭐ <c>CE-2260</c> / <c>CE-2261</c> (CB-66) — pure over the squad's pooled picture: is the room SUSPECTED (the order says so, a man of the
    /// stack was fired on at it, or a contact — seen or HEARD — lies inside its quad at its storey), are CIVILIANS known in it (a pooled
    /// neutral), and is a hostile SEEN in it now (identified, still in the fight). 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.3.
    /// </summary>
    public static class RoomSuspicion
    {
        /// <summary>"Seen now": an identified contact refreshed within this many ticks (1.5 s at 60 Hz).</summary>
        public const uint SeenTicks = 90;

        public static unsafe RoomKnowledge Of(EntityRepository world, Entity commander, ReadOnlySpan<float> corners, float floorZ, bool ordered, bool firedOn)
        {
            bool suspected = ordered || firedOn, civilians = false, seen = false, known = false;
            if (!world.HasComponent<SquadCognitiveState>(commander)) return new RoomKnowledge(suspected, false, false);
            ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(commander);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(world);   // the clock LastSeenTick is stamped in (CE-3292)
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                if (pool[c].PositionZ < floorZ - 1f || pool[c].PositionZ > floorZ + 2.8f) continue;
                if (!RoomEntryPlan.Inside(corners, new Vector2(pool[c].PositionX, pool[c].PositionY))) continue;
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0) { suspected = true; continue; }   // heard: somebody is in there
                var e = new Entity((ulong)pool[c].EntityId);
                if (!world.IsAlive(e) || ThreatDanger.IsOutOfTheFight(world, e)) continue;
                if (ThreatDanger.IsNeutral(world, e)) { civilians = true; continue; }
                suspected = true;
                known = true;
                if (pool[c].LastSeenTick <= now && now - pool[c].LastSeenTick <= SeenTicks) seen = true;
            }
            return new RoomKnowledge(suspected, civilians, seen, known);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2260</c> BC-4 (CB-67…CB-70) — the grenade before a room: which kind (CB-6 + CB-30, Eliminate), who carries it, and whether a
    /// friend would be hurt — predicted by the area effect's OWN laws (<see cref="AreaEffect.BlastFalloff"/>,
    /// <see cref="AreaEffect.ClosedBarrierTransmission"/> on a <see cref="IWorldQuery.Trace"/>), so the wall the stack stands behind counts.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.3.
    /// </summary>
    public static class GrenadeCheck
    {
        /// <summary>CB-30 — no throw when a friend would be stunned longer than this (s).</summary>
        public const float FriendStunSeconds = 0.5f;
        /// <summary>CB-70 — a fragmentation grenade's lethal radius, as a share of its fragment reach (an M67: 15 m reach, 5 m lethal).</summary>
        public const float LethalShareOfReach = 1f / 3f;
        /// <summary>CB-70 — fragments still getting through a wall above this share count as reaching the man (the same transmission the
        /// area effect applies, <see cref="AreaEffect.FragmentTransmission"/>).</summary>
        public const float FragmentsThrough = 0.05f;
        /// <summary>A man's body point the prediction is made at: his chest (m above his feet).</summary>
        public const float ChestHeight = 1.2f;

        /// <summary>CB-67 — the grenade the room gets: not suspected ⇒ none; Precision ⇒ stun; HighIntensity ⇒ fragmentation, a stun when
        /// civilians are known.</summary>
        public static RoomGrenadeKind Choose(ClearIntensity intensity, in RoomKnowledge room)
        {
            if (!room.Suspected) return RoomGrenadeKind.None;
            if (intensity == ClearIntensity.HighIntensity && !room.CiviliansKnown) return RoomGrenadeKind.Fragmentation;
            return RoomGrenadeKind.Stun;
        }

        /// <summary>The kind <paramref name="warhead"/> is: a stun warhead, a fragmenting one, or neither.</summary>
        public static RoomGrenadeKind KindOf(WarheadDto? warhead)
            => warhead == null ? RoomGrenadeKind.None
             : warhead.Kind == WarheadKind.Stun ? RoomGrenadeKind.Stun
             : warhead.FragmentRadiusM > 0f ? RoomGrenadeKind.Fragmentation
             : RoomGrenadeKind.None;

        /// <summary>
        /// The grenade mount of <paramref name="man"/> holding a round of <paramref name="kind"/> (his TKB weapon suite, a <c>Grenade</c>
        /// mount with rounds left): its index, its ammo and its warhead. False when he carries none.
        /// </summary>
        public static bool Carried(EntityRepository world, Entity man, RoomGrenadeKind kind, out int mount, out long ammo, out WarheadDto warhead)
        {
            mount = -1; ammo = 0; warhead = null!;
            if (kind == RoomGrenadeKind.None || !CombatTkb.TryGetTemplate(world, man, out var template)) return false;
            var suite = template.GetDescriptor<WeaponSuiteDto>();
            if (suite == null) return false;
            var db = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
            for (int i = 0; i < suite.Mounts.Count; i++)
            {
                var m = suite.Mounts[i];
                if (m.Category != WeaponCategory.Grenade || PersonHands.IsHands(m)) continue;
                long a = unchecked((long)m.AmmoGuid);
                var (w, _, _) = ParameterResolver.Warhead(db, a);
                if (w == null || KindOf(w) != kind) continue;
                if (Rounds(world, man, i) <= 0) continue;
                mount = i; ammo = a; warhead = w;
                return true;
            }
            return false;
        }

        /// <summary>The rounds left on <paramref name="man"/>'s mount <paramref name="mount"/> (−1 when it has no weapon state).</summary>
        public static int Rounds(EntityRepository world, Entity man, int mount)
        {
            var e = PersonHands.MountEntity(world, man, mount);
            return !e.IsNull && world.HasComponent<WeaponState>(e) ? world.GetComponentRO<WeaponState>(e).Ammo : -1;
        }

        /// <summary>
        /// ⭐ CB-70 — would a friend standing at <paramref name="friend"/> (his feet) be hurt by <paramref name="warhead"/> bursting at
        /// <paramref name="burst"/>? A stun: the predicted seconds (EffectSeconds × blast fall-off × the closed barrier between) above
        /// <see cref="FriendStunSeconds"/>. Fragmentation: within its lethal radius and fragments getting through what lies between. ⚠ The terrain shadow is
        /// left out — it only ever shrinks the effect, so leaving it out errs safe. No world query ⇒ no barrier (errs safe too).
        /// </summary>
        public static bool Hurts(IWorldQuery? query, WarheadDto warhead, Vector3 burst, Vector3 friend, List<TraceCrossing> scratch)
        {
            var chest = friend + new Vector3(0f, 0f, ChestHeight);
            float r = Vector3.Distance(burst, chest);
            float reach = MathF.Max(warhead.BlastInjuryRadiusM, warhead.FragmentRadiusM);
            if (r > reach) return false;
            scratch.Clear();
            query?.Trace(burst, chest, TracePurpose.Fire, scratch);
            float barrier = query == null ? 1f : AreaEffect.ClosedBarrierTransmission(scratch);
            if (warhead.Kind == WarheadKind.Stun)
                return warhead.EffectSeconds * AreaEffect.BlastFalloff(r, warhead.BlastLethalRadiusM, warhead.BlastInjuryRadiusM) * barrier > FriendStunSeconds;
            float lethal = MathF.Max(warhead.BlastLethalRadiusM, warhead.FragmentRadiusM * LethalShareOfReach);
            float through = query == null ? 1f : AreaEffect.FragmentTransmission(scratch, warhead.FragmentPenetrationMm);
            return r <= lethal && through > FragmentsThrough;
        }

        /// <summary>CB-70 — is <paramref name="burst"/> safe for every man at <paramref name="men"/> — where each will stand at the BANG (the
        /// stack at its stack points behind the wall, the thrower back at his), never where he stands while the grenade is thrown.</summary>
        public static bool SafeFor(IWorldQuery? query, WarheadDto warhead, Vector3 burst, ReadOnlySpan<Vector3> men, List<TraceCrossing> scratch)
        {
            foreach (var at in men)
                if (Hurts(query, warhead, burst, at, scratch)) return false;
            return true;
        }
    }
}
