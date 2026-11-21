using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Utility;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2466</c> (R-2140, 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.9) — what a squad does after a contact with an
    /// enemy it did not destroy: REMEMBER the ground each enemy covers (R1, the shared <c>UnderFire</c> knowledge of the squad's side —
    /// Minefields §9b), RALLY and report (R2), never assault frontally a force it broke contact from (R4), stay out when no longer fit to fight
    /// (R5). The ONE home of these reads for <see cref="ReactToContactBehavior"/>; no allocation (the pool is a fixed span).
    /// </summary>
    public static class AfterContact
    {
        /// <summary>How far an enemy position is taken to cover (m) — the urban fighting range the corner fight measured (contact at 30–35 m,
        /// kills at 31 m) with room for the length of a street block.</summary>
        public const float DirectRangeMetres = 150f;
        /// <summary>R4 — a contact this close (m) to a remembered position IS that position (the record's frozen point vs a man at a corner).</summary>
        public const float RecordNearMetres = 25f;
        /// <summary>R4 — superiority: the enemy's share of the strength in play at most this (3 : 1, the doctrine's assault ratio).</summary>
        public const float AssaultShare = 0.25f;
        /// <summary>R5 — fit to fight: the squad's health share at least this.</summary>
        public const float FitShare = 0.5f;
        /// <summary>R2 — the rally hold (s).</summary>
        public const float RallySeconds = 30f;

        /// <summary>The squad's side (<c>ForceId</c> as an int), −1 when it has none.</summary>
        public static int SideOf(EntityRepository world, Entity commander)
        {
            var f = Objectives.ObjectiveCensus.SideOf(world, commander);
            return f == Fdp.Core.ForceId.Neutral ? -1 : (int)f;
        }

        private static ReadOnlySpan<SquadContact> Pool(EntityRepository world, Entity commander, out int count)
        {
            count = 0;
            if (!world.HasComponent<SquadCognitiveState>(commander)) return default;
            ref readonly var state = ref world.GetComponentRO<SquadCognitiveState>(commander);
            count = state.Contacts.Count;
            return MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);
        }

        /// <summary>An identified (seen, not only heard) ENEMY of the pool — the one hostility rule (<see cref="SquadContacts.IsEnemy"/>: alive, in the
        /// fight, not a neutral — CE-2353).</summary>
        private static bool Identified(EntityRepository world, in SquadContact c, out Entity e)
        {
            e = new Entity((ulong)c.EntityId);
            return (c.Flags & SquadContact.AnonymousFlag) == 0 && c.EntityId > 0 && SquadContacts.IsEnemy(world, in c);
        }

        /// <summary>An identified enemy of the pool is still in the fight.</summary>
        public static bool EnemyAlive(EntityRepository world, Entity commander)
        {
            var pool = Pool(world, commander, out int n);
            for (int i = 0; i < n; i++) if (Identified(world, in pool[i], out _)) return true;
            return false;
        }

        /// <summary>⭐ R1 — one <c>Direct</c> record per identified, live enemy of the pool (two corners = two records); a learn lands next tick.
        /// <paramref name="enemyStrength"/> = the most the reaction saw (S6). Returns how many were learned.</summary>
        public static int Learn(EntityRepository world, Entity commander, float enemyStrength)
        {
            int side = SideOf(world, commander);
            if (side < 0) return 0;
            var pool = Pool(world, commander, out int n);
            SimInstant now = (SimInstant)CommandPictureOps.Now(world);
            int learned = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Identified(world, in pool[i], out _)) continue;
                UnderFireEvents.LearnDirect(world, side, new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ), DirectRangeMetres, enemyStrength, now);
                learned++;
            }
            return learned;
        }

        /// <summary>⭐ R4 — an identified enemy of the pool stands at a fresh <c>Direct</c> record of the side; <paramref name="remembered"/> its strength.</summary>
        public static bool FacesRemembered(EntityRepository world, Entity commander, out float remembered)
        {
            remembered = 0f;
            int side = SideOf(world, commander);
            if (side < 0) return false;
            var table = UnderFireTable.Of(world);
            var pool = Pool(world, commander, out int n);
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!Identified(world, in pool[i], out _)) continue;
                var at = new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ);
                if (!table.NearestFresh(side, at, RecordNearMetres, out var r) || r.Cause != UnderFireCause.Direct) continue;
                any = true;
                remembered = MathF.Max(remembered, r.EnemyStrength);
            }
            return any;
        }

        /// <summary>⭐ R4 — the squad now has superiority over <paramref name="enemy"/> (its share ≤ <see cref="AssaultShare"/>).</summary>
        public static bool Superior(EntityRepository world, Entity commander, float enemy)
            => SquadInputs.EnemyShare(MathF.Max(enemy, SquadInputs.EnemyDangerOf(world, commander)), SquadInputs.OwnStrengthOf(world, commander)) <= AssaultShare;

        /// <summary>⭐ R5 — still fit to fight: the squad's health share ≥ <see cref="FitShare"/>.</summary>
        public static bool Fit(EntityRepository world, Entity commander)
            => SquadInputs.SquadStrengthRatio(new UtilityInputCtx { Repo = world, Self = commander }) >= FitShare;

        /// <summary>⭐ R2 — the rally's two reports: LACE (ammunition, casualties) and a SPOT of each remembered enemy, up the chain.</summary>
        public static void ReportRally(EntityRepository world, Entity commander)
        {
            SimInstant now = (SimInstant)CommandPictureOps.Now(world);
            Vector3 at = world.HasComponent<SimTransform>(commander) ? (Vector3)world.GetComponentRO<SimTransform>(commander).Position : Vector3.Zero;
            ReportQueue.Enqueue(world, commander, new CommandReport
            {
                Kind = ReportKind.Lace, At = at, Ammo = StaffComposerSystem.AmmoOf(world, commander),
                Count = (byte)Math.Clamp(SquadCasualties.Count(world, commander), 0, 255), ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
            var pool = Pool(world, commander, out int n);
            for (int i = 0; i < n; i++)
            {
                if (!Identified(world, in pool[i], out _)) continue;
                ReportQueue.Enqueue(world, commander, new CommandReport
                {
                    Kind = ReportKind.Spot, At = new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ), Count = 1,
                    Size = ElementSize.Individual, Activity = ElementActivity.Firing, ObservedAt = now,
                }, now + CommandNodes.ReportDelaySeconds);
            }
        }

        /// <summary>⭐ R5 / R6 — out of it: a request up (<paramref name="what"/>) and PROGRESS TaskFailed.</summary>
        public static void ReportCannotGoOn(EntityRepository world, Entity commander, RequestKind what)
        {
            SimInstant now = (SimInstant)CommandPictureOps.Now(world);
            Vector3 at = world.HasComponent<SimTransform>(commander) ? (Vector3)world.GetComponentRO<SimTransform>(commander).Position : Vector3.Zero;
            ReportQueue.Enqueue(world, commander, new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)what, At = at, Radius = 30f, Count = 1, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
            CommandNodes.ReportProgress(world, commander, ProgressKind.TaskFailed, at);
        }
    }
}
