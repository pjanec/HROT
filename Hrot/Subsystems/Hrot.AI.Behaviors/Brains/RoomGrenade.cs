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
        /// <summary>A member within this of a contact (m) is at its building's door — the stack outside counts as being at the building.</summary>
        public const float AtTheBuildingMetres = 12f;

        /// <summary>
        /// ⭐⭐ <c>CE-2453</c> (Building Clearing §7.3 CB-66 / CB-72 / CB-73; the <c>NotDefending</c> precedent, CE-2359) — FALSE when the squad is CLEARING
        /// (its order's kind is <c>ClearBuilding</c>, <see cref="Hrot.AI.Behaviors.Orders.CurrentOrders"/>) and EVERY contact in its pool is INSIDE a building one
        /// of its men is in or at the door of: the room drill answers that contact itself (a heard one suspects the room ⇒ the grenade before the entry; a
        /// seen one or a hit at the way in holds the entry). True otherwise — a contact in the STREET, or in another building, still pauses the clear
        /// (<c>ReactToContact</c>): between buildings the clear only walks. 📐 Before: <c>ia-area-defend</c>, a lone clearer at House A's door heard the
        /// guard upstairs and the contact row's reaction HELD him at the entry for 60 s+.
        /// </summary>
        [Fbt.Kernel.SharedAiCondition]
        public static unsafe bool NotClearingItsContact(Entity self, EntityRepository world)
        {
            if (Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, self).Kind != Hrot.AI.Behaviors.Orders.OrderKind.ClearBuilding) return true;
            if (!world.HasComponent<SquadCognitiveState>(self) || !world.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(self)) return true;
            ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(self);
            if (squad.Contacts.Count == 0) return true;
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            ref readonly var roster = ref world.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(self);
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                var at = new Vector3(pool[c].PositionX, pool[c].PositionY, pool[c].PositionZ);
                if (!GrenadeRules.Indoors(world, at, out int building)) return true;   // in the street: the SOP answers
                if (!AnyManAt(world, in roster, at, building)) return true;            // another building: the SOP answers
            }
            return false;
        }

        private static bool AnyManAt(EntityRepository world, in Fdp.Core.CommandHierarchy.UnitRoster roster, Vector3 contact, int building)
        {
            float r2 = AtTheBuildingMetres * AtTheBuildingMetres;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !world.IsAlive(m) || !world.HasComponent<SimTransform>(m) || !Injury.IsInTheFight(world, m)) continue;
                var p = world.GetComponentRO<SimTransform>(m).Position;
                if (GrenadeRules.Indoors(world, p, out int his) && his == building) return true;
                var d = new Vector2(p.X - contact.X, p.Y - contact.Y);
                if (d.LengthSquared() <= r2) return true;
            }
            return false;
        }

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
                if (Fdp.Toolkit.Animals.Animals.IsAnimal(world, e)) continue;   // ⭐ CE-4623 (AR-6): a cat in the room is no civilian (and no threat)
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
        /// <summary>CB-30 — no throw when a friend would be stunned longer than this (s) (⭐ CE-2452: the one rule, <see cref="GrenadeRules"/>).</summary>
        public const float FriendStunSeconds = GrenadeRules.FriendStunSeconds;
        /// <summary>CB-70 — a fragmentation grenade's lethal radius, as a share of its fragment reach (⭐ CE-2360: the one rule, <see cref="GrenadeRules"/>).</summary>
        public const float LethalShareOfReach = GrenadeRules.LethalShareOfReach;
        /// <summary>CB-70 — fragments still getting through a wall above this share count as reaching the man (<see cref="GrenadeRules"/>).</summary>
        public const float FragmentsThrough = GrenadeRules.FragmentsThrough;
        /// <summary>A man's body point the prediction is made at: his chest (m above his feet) (<see cref="GrenadeRules"/>).</summary>
        public const float ChestHeight = GrenadeRules.ChestHeight;

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
            => GrenadeRules.KindOf(warhead) switch   // ⭐ CE-2360 — the one rule the weapon brain's throw reads too
            {
                GrenadeKind.Stun => RoomGrenadeKind.Stun,
                GrenadeKind.Fragmentation => RoomGrenadeKind.Fragmentation,
                _ => RoomGrenadeKind.None,
            };

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
            => GrenadeRules.Hurts(query, warhead, burst, friend, scratch);   // ⭐ CE-2452 — the one law, in FDP Combat

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
