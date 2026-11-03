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
    /// <summary>Sixteen sim times (s), parallel to an <see cref="Ents16"/>.</summary>
    [InlineArray(16)] public struct Times16 { private double _t; }

    /// <summary>Victims a measure has credited to the subject's men and is waiting to see go out of the fight: who, the sim time of the man's LAST harm, how many (16 slots; beyond them a hit still counts, but its kill cannot be credited).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Credits
    {
        public Ents16 Who; public Times16 At; public int Count;

        public readonly int IndexOf(Entity e) { for (int i = 0; i < Count; i++) if (Who[i] == e) return i; return -1; }
        public void RemoveAt(int i) { Who[i] = Who[Count - 1]; At[i] = At[Count - 1]; Count--; }
        public bool Remove(Entity e) { int i = IndexOf(e); if (i < 0) return false; RemoveAt(i); return true; }
        /// <summary>Adds <paramref name="e"/> (or refreshes its time); false when the 16 slots are full.</summary>
        public bool Put(Entity e, double at)
        {
            int i = IndexOf(e);
            if (i >= 0) { At[i] = at; return true; }
            if (Count >= 16) return false;
            Who[Count] = e; At[Count] = at; Count++;
            return true;
        }
    }

    /// <summary>⭐ rules S4 E-2 / S7 P-3 — what the subject's men have DONE, counted from the frame's events: the working state of <see cref="MeasureEvidence"/> (lives in a measure's blackboard).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireTally
    {
        public int Shots, Hits, Kills, Fratricide, Civilians;
        /// <summary>⭐ S7 P-3 — assists (a hostile the man harmed went down within <see cref="MeasureEvidence.AssistSeconds"/> of his last harm, another source having taken the credit), team kills (a friend he harmed went down — never a kill),
        /// deaths (his men going out of the fight, once each), captures (an <see cref="ObjectiveCapturedEvent"/> naming one of his men).</summary>
        public int Assists, TeamKills, Deaths, Captures;
        /// <summary>Hostiles a man of the subject hit and that are still in the fight.</summary>
        public Credits Foes;
        /// <summary>Hostiles already credited as killed (a corpse shot again is not a second kill; a revived one leaves the list and can be killed again).</summary>
        public Ents16 Killed; public int KilledCount;
        /// <summary>Friends a man of the subject harmed and that are still in the fight / already counted as team-killed (the kill rule on his own side).</summary>
        public Credits Friends; public Ents16 TeamKilled; public int TeamKilledCount;
        /// <summary>Hostiles the man harmed whose credit another source then took: an assist if they go down within the window of the man's last harm.</summary>
        public Credits Helped;
        /// <summary>The men in the fight at the last tick (a man now out of the fight, still an entity, is a death).</summary>
        public Ents16 InFight; public int InFightCount;
    }

    /// <summary>
    /// ⭐ rules S4 E-2 / S7 P-3 (📄 docs/DESIGN_Scenario_Rules.md §00.11, §00.13) — the ONE reading of "what did the subject's men do" the <c>HitRatio</c> and <c>Count</c> measures share (⛔ two copies would drift):
    /// shots = <see cref="WeaponFireIntent"/> raised by a man of the subject (the intent is raised on the BRAIN node — the node the objectives run on — so it is visible here); a hit =
    /// a <see cref="DamageAssessedEvent"/> whose attributed source (CE-4155 <see cref="NoHarmBehavior.Sources"/>) is one of the men; the victim's side says what it was — a hostile (a hit, and a kill
    /// once it is out of the fight; an assist when another source took the credit but it went down soon after his last harm), a Friend of the subject's side (fratricide, and a team kill once it is out of the fight),
    /// a Neutral (a civilian harmed). Deaths are his men going out of the fight; captures are <see cref="ObjectiveCapturedEvent"/>s naming them. The men are <c>ObjectiveCensus.MenOf</c>.
    /// </summary>
    public static class MeasureEvidence
    {
        /// <summary>⭐ S7 P-3 — a hostile that goes down within this many seconds of a man's last harm, another having taken the credit, is that man's assist.</summary>
        public const double AssistSeconds = 10.0;

        private static readonly List<Entity> s_men = new(), s_roster = new();

        /// <summary>Reads this frame's events into <paramref name="t"/> for <paramref name="subject"/>'s men (a missing subject has none). <paramref name="now"/> = the sim time (<c>ObjectiveHost.Frame.Now</c>).</summary>
        public static void Update(EntityRepository world, Entity subject, HarmAttribute attribute, double now, ref FireTally t, double assistSeconds = AssistSeconds)
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
                    if (credited.IsNull) { Forget(ref t, e.HitEntity, now); continue; }   // somebody else's harm supersedes a pending kill credit
                    var victimSide = ObjectiveCensus.SideOf(world, e.HitEntity);
                    if (KillZone.Hostile(side, victimSide))
                    {
                        t.Hits++;
                        if (Remember(ref t.Foes, in t.Killed, t.KilledCount, e.HitEntity, now)) t.Helped.Remove(e.HitEntity);   // he harmed it again: the kill, if it comes, is his
                    }
                    else if (victimSide == ForceId.Neutral) { if (e.HitEntity != credited) t.Civilians++; }
                    else if (victimSide == side && e.HitEntity != credited) { t.Fratricide++; Remember(ref t.Friends, in t.TeamKilled, t.TeamKilledCount, e.HitEntity, now); }
                }
            }
            // a credited hostile that is out of the fight now (down, or gone) was KILLED by the subject's men — counted once
            t.Kills += Settle(world, ref t.Foes, ref t.Killed, ref t.KilledCount, ref t.Helped);
            // ⭐ S7 P-3: a friend he harmed that is out of the fight now is a TEAM KILL — the same rule on his own side, counted once, never a kill
            t.TeamKills += Settle(world, ref t.Friends, ref t.TeamKilled, ref t.TeamKilledCount, ref t.Helped);
            Assists(world, ref t, now, assistSeconds > 0 ? assistSeconds : AssistSeconds);
            Deaths(world, ref t);
            if (world.Bus.IsRegistered<ObjectiveCapturedEvent>())
            {
                var taken = world.ReadEvents<ObjectiveCapturedEvent>();
                for (int i = 0; i < taken.Length; i++)
                    for (int k = 0; k < taken[i].MenCount && k < 16; k++)
                        if (s_men.Contains(taken[i].Men[k])) { t.Captures++; break; }   // one event = one capture, however many of his men stood there
            }
            s_men.Clear();
        }

        /// <summary>Puts <paramref name="victim"/> on the credit list unless already counted; true when it is (now) on the list.</summary>
        private static bool Remember(ref Credits credits, in Ents16 done, int doneCount, Entity victim, double now)
        {
            for (int i = 0; i < doneCount; i++) if (done[i] == victim) return false;
            return credits.Put(victim, now);
        }

        /// <summary>Another source harmed <paramref name="victim"/>: the credit is dropped — a hostile the man had harmed becomes a candidate for an ASSIST (with the time of his last harm).</summary>
        private static void Forget(ref FireTally t, Entity victim, double now)
        {
            int i = t.Foes.IndexOf(victim);
            if (i >= 0) { t.Helped.Put(victim, t.Foes.At[i]); t.Foes.RemoveAt(i); }
            t.Friends.Remove(victim);
        }

        /// <summary>Moves every credited victim that is out of the fight to <paramref name="done"/> and returns how many (a revived one leaves <paramref name="done"/> and can be counted again). One rule for kills and team kills.</summary>
        private static int Settle(EntityRepository world, ref Credits credits, ref Ents16 done, ref int doneCount, ref Credits helped)
        {
            int n = 0;
            for (int i = credits.Count - 1; i >= 0; i--)
            {
                var v = credits.Who[i];
                if (world.IsAlive(v) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v)) continue;
                n++;
                helped.Remove(v);
                if (doneCount == 16) { for (int k = 1; k < 16; k++) done[k - 1] = done[k]; doneCount = 15; }   // the oldest forgotten
                done[doneCount++] = v;
                credits.RemoveAt(i);
            }
            for (int i = doneCount - 1; i >= 0; i--)   // revived: it can be counted again
                if (world.IsAlive(done[i]) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, done[i])) done[i] = done[--doneCount];
            return n;
        }

        /// <summary>⭐ S7 P-3 — a hostile whose credit another source took, out of the fight within <see cref="AssistSeconds"/> of the man's last harm, is one assist; older entries expire.</summary>
        private static void Assists(EntityRepository world, ref FireTally t, double now, double window)
        {
            for (int i = t.Helped.Count - 1; i >= 0; i--)
            {
                var v = t.Helped.Who[i];
                bool within = now - t.Helped.At[i] <= window;   // ⭐ S4 ⑥ — the Count's authored assistSeconds, else AssistSeconds
                bool down = !world.IsAlive(v) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v);
                if (down && within) t.Assists++;
                if (down || !within) t.Helped.RemoveAt(i);
            }
        }

        /// <summary>⭐ S7 P-3 — a man seen in the fight last tick who is out of it now while still an entity is one death (once; a revived man is seen again and can die again).</summary>
        private static void Deaths(EntityRepository world, ref FireTally t)
        {
            for (int i = t.InFightCount - 1; i >= 0; i--)
            {
                var m = t.InFight[i];
                if (s_men.Contains(m)) continue;
                if (world.IsAlive(m) && !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, m)) t.Deaths++;
                t.InFight[i] = t.InFight[--t.InFightCount];   // dead, gone or no longer his: forgotten
            }
            foreach (var m in s_men)
            {
                bool known = false;
                for (int i = 0; i < t.InFightCount && !known; i++) known = t.InFight[i] == m;
                if (!known && t.InFightCount < 16) t.InFight[t.InFightCount++] = m;
            }
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
