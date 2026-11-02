using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad.Command;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>Sixteen entities (the victims a measure has credited and is waiting to see go down).</summary>
    [InlineArray(16)] public struct Ents16 { private Entity _e; }

    /// <summary>⭐ rules S4 E-2 — what the subject's men have DONE, counted from the frame's events: the working state of <see cref="MeasureEvidence"/> (lives in a measure's blackboard).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireTally
    {
        public int Shots, Hits, Kills, Fratricide, Civilians;
        /// <summary>Hostiles a man of the subject hit and that are still in the fight (16 slots; beyond them a hit still counts, but its kill cannot be credited).</summary>
        public Ents16 Pending; public int PendingCount;
        /// <summary>Hostiles already credited as killed (a corpse shot again is not a second kill; a revived one leaves the list and can be killed again).</summary>
        public Ents16 Killed; public int KilledCount;
    }

    /// <summary>
    /// ⭐ rules S4 E-2 (📄 docs/DESIGN_Scenario_Rules.md §00.11) — the ONE reading of "what did the subject's men do" the <c>HitRatio</c> and <c>Count</c> measures share (⛔ two copies would drift):
    /// shots = <see cref="WeaponFireIntent"/> raised by a man of the subject (the intent is raised on the BRAIN node — the node the objectives run on — so it is visible here); a hit =
    /// a <see cref="DamageAssessedEvent"/> whose attributed source (CE-4155 <see cref="NoHarmBehavior.Sources"/>) is one of the men; the victim's side says what it was — a hostile (a hit, and a kill
    /// once it is out of the fight), a Friend of the subject's side (fratricide), a Neutral (a civilian harmed). The men are <c>ObjectiveCensus.MenOf</c>.
    /// </summary>
    public static class MeasureEvidence
    {
        private static readonly List<Entity> s_men = new(), s_roster = new();

        /// <summary>Reads this frame's events into <paramref name="t"/> for <paramref name="subject"/>'s men (a missing subject has none).</summary>
        public static void Update(EntityRepository world, Entity subject, HarmAttribute attribute, ref FireTally t)
        {
            s_men.Clear();
            ObjectiveCensus.MenOf(world, subject, s_men);
            var side = ObjectiveCensus.SideOf(world, subject);

            if (s_men.Count > 0 && world.Bus.IsRegistered<WeaponFireIntent>())
            {
                var shots = world.ReadEvents<WeaponFireIntent>();
                for (int i = 0; i < shots.Length; i++)
                    if (!shots[i].IsRemote && s_men.Contains(shots[i].Shooter)) t.Shots++;
            }
            if (world.Bus.IsRegistered<DamageAssessedEvent>())
            {
                var events = world.ReadEvents<DamageAssessedEvent>();
                for (int i = 0; i < events.Length; i++)
                {
                    ref readonly var e = ref events[i];
                    if (e.TotalDamage <= 0f || e.HitEntity.IsNull) continue;
                    NoHarmBehavior.Sources(in e, attribute, out var last, out var first);
                    var credited = !last.IsNull && s_men.Contains(last) ? last : !first.IsNull && s_men.Contains(first) ? first : Entity.Null;
                    if (credited.IsNull) { Forget(ref t, e.HitEntity); continue; }   // somebody else's harm supersedes a pending kill credit
                    var victimSide = ObjectiveCensus.SideOf(world, e.HitEntity);
                    if (KillZone.Hostile(side, victimSide)) { t.Hits++; Remember(ref t, e.HitEntity); }
                    else if (victimSide == ForceId.Neutral) { if (e.HitEntity != credited) t.Civilians++; }
                    else if (victimSide == side && e.HitEntity != credited) t.Fratricide++;
                }
            }
            // a credited hostile that is out of the fight now (down, or gone) was KILLED by the subject's men — counted once
            for (int i = t.PendingCount - 1; i >= 0; i--)
            {
                var v = t.Pending[i];
                if (world.IsAlive(v) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v)) continue;
                t.Kills++;
                if (t.KilledCount == 16) { for (int k = 1; k < 16; k++) t.Killed[k - 1] = t.Killed[k]; t.KilledCount = 15; }   // the oldest forgotten
                t.Killed[t.KilledCount++] = v;
                t.Pending[i] = t.Pending[--t.PendingCount];
            }
            for (int i = t.KilledCount - 1; i >= 0; i--)   // revived: it can be killed (and credited) again
                if (world.IsAlive(t.Killed[i]) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, t.Killed[i])) t.Killed[i] = t.Killed[--t.KilledCount];
            s_men.Clear();
        }

        private static void Remember(ref FireTally t, Entity victim)
        {
            for (int i = 0; i < t.KilledCount; i++) if (t.Killed[i] == victim) return;
            for (int i = 0; i < t.PendingCount; i++) if (t.Pending[i] == victim) return;
            if (t.PendingCount < 16) t.Pending[t.PendingCount++] = victim;
        }

        private static void Forget(ref FireTally t, Entity victim)
        {
            for (int i = 0; i < t.PendingCount; i++)
                if (t.Pending[i] == victim) { t.Pending[i] = t.Pending[--t.PendingCount]; return; }
        }

        /// <summary>
        /// The <see cref="SquadReportNotification"/>s of this frame sent by the subject or anyone under it (<c>ObjectiveCensus.RosterOf</c>): how many, and the sum of
        /// <c>SentAt − ObservedAt</c> (s; never negative). ⚠ a report is read once per event, so each measure keeps its own totals.
        /// </summary>
        public static void Reports(EntityRepository world, Entity subject, out int count, out double latencySum)
        {
            count = 0; latencySum = 0;
            if (!world.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            s_roster.Clear();
            ObjectiveCensus.RosterOf(world, subject, s_roster);
            if (s_roster.Count > 0)
                foreach (var n in world.ReadManagedEvents<SquadReportNotification>())
                {
                    if (!s_roster.Contains(n.FromUnit)) continue;
                    count++;
                    latencySum += System.Math.Max(0d, n.SentAt - n.Report.ObservedAt);
                }
            s_roster.Clear();
        }
    }
}
