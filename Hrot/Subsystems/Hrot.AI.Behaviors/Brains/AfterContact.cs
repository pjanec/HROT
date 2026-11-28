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
        /// <summary>R2 — a LONE man's pause after a broken contact (s): 0 — he reports and goes on; he has nobody to account for (doctrine's reorganisation is a squad act). 📐 ia-area-defend: 5 s still pushed the lone attacker's second approach into the next attacker's window.</summary>
        public const float LoneRallySeconds = 0f;

        /// <summary>How many of the squad's members are alive and in the fight.</summary>
        public static int LiveMembers(EntityRepository world, Entity commander)
        {
            if (!world.HasComponent<UnitRoster>(commander)) return 0;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(commander);
            int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (world.IsAlive(m) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, m)) n++;
            }
            return n;
        }

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

        /// <summary>
        /// ⭐⭐ <c>CE-2472</c> — <paramref name="man"/> is in an enemy's sight: by HIS OWN perception, he saw an identified enemy of the squad's pool with his eyes
        /// within <see cref="LookAt.InSightTicks"/> (<see cref="LookAt.InSightNow"/>) — sight goes both ways. ⛔ Never the terrain: the brain asks the world
        /// only through its sensors (user, 2026-11-21: "brain needs to ask about the environment using sensors and EQS and never directly - those might run on
        /// different nodes"). No allocation.
        /// </summary>
        public static bool InAnEnemysSight(EntityRepository world, Entity commander, Entity man)
        {
            var pool = Pool(world, commander, out int n);
            for (int i = 0; i < n; i++)
                if (Identified(world, in pool[i], out var enemy) && LookAt.InSightNow(world, man, enemy)) return true;
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
                var at = new Vector3(pool[i].PositionX, pool[i].PositionY, pool[i].PositionZ);
                UnderFireEvents.LearnDirect(world, side, at, CoveredMetres(world, commander, at), enemyStrength, now);
                learned++;
            }
            return learned;
        }

        /// <summary>⭐ <c>CE-2496</c> — the most an enemy position is ever taken to cover (m): small arms and a machine gun's effective reach.</summary>
        public const float MaxCoveredMetres = 800f;

        /// <summary>
        /// ⭐ <c>CE-2496</c> (R-2145) — how far the enemy at <paramref name="enemy"/> covers: at least <see cref="DirectRangeMetres"/>, and at least as far as he
        /// ENGAGED the squad — the farthest live member now, plus <see cref="RecordNearMetres"/> — capped at <see cref="MaxCoveredMetres"/>. 📐 of-squad-outmatched:
        /// two machine guns engaged from ~200 m on open ground; a fixed 150 m disc let the resumed move walk back into their sight after the rally.
        /// </summary>
        public static float CoveredMetres(EntityRepository world, Entity commander, Vector3 enemy)
        {
            float far = 0f;
            if (world.HasComponent<UnitRoster>(commander))
            {
                ref readonly var roster = ref world.GetComponentRO<UnitRoster>(commander);
                for (int i = 0; i < roster.Count; i++)
                {
                    var m = roster.SubordinateEntities[i];
                    if (!world.IsAlive(m) || !world.HasComponent<SimTransform>(m)) continue;
                    Vector3 p = world.GetComponentRO<SimTransform>(m).Position;
                    var d = p - enemy; d.Z = 0f;
                    far = MathF.Max(far, d.Length());
                }
            }
            return MathF.Min(MaxCoveredMetres, MathF.Max(DirectRangeMetres, far + RecordNearMetres));
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

        /// <summary>⭐ <c>CE-2467</c> — the side holds a fresh <c>Direct</c> record at <paramref name="point"/> (within <see cref="RecordNearMetres"/>): the ground that
        /// enemy covers is known, so routes and EQS path costs on the navmesh node are priced round it (Minefields §9b UF-5).</summary>
        public static bool Remembers(EntityRepository world, Entity commander, Vector3 point)
        {
            int side = SideOf(world, commander);
            if (side < 0 || !world.IsComponentTypeRegistered<UnderFireKnowledge>()) return false;
            return UnderFireTable.Of(world).NearestFresh(side, point, RecordNearMetres, out var r) && r.Cause == UnderFireCause.Direct;
        }

        /// <summary>⭐ R4 — the squad now has superiority over <paramref name="enemy"/> (its share ≤ <see cref="AssaultShare"/>).</summary>
        public static bool Superior(EntityRepository world, Entity commander, float enemy, float share = AssaultShare)
            => SquadInputs.EnemyShare(MathF.Max(enemy, SquadInputs.EnemyDangerOf(world, commander)), SquadInputs.OwnStrengthOf(world, commander)) <= share;

        /// <summary>⭐ <c>CE-2481</c> (Squad Tactics §7.10) — the enemy share up to which a RUNNING ordered attack keeps pressing (1.5 : 1): the
        /// culminating point, not the 3 : 1 that starts an assault (<see cref="AssaultShare"/>). One rule for both had no margin — 📐 uf-corner-assault,
        /// 6 v 2 is exactly 0.25: one man grazed (100 → 87.5 health) at 16.1 s made it 0.254 and the whole squad stopped to react.</summary>
        public const float PressShare = 0.4f;

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
