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
        /// <summary>⭐ S7 P-3 — assists (another source downed a hostile the man had harmed within <see cref="MeasureEvidence.AssistSeconds"/> of his last harm), team kills (a friend his man downed — never a kill),
        /// deaths (his men going out of the fight, once each), captures (an <see cref="ObjectiveCapturedEvent"/> naming one of his men).</summary>
        public int Assists, TeamKills, Deaths, Captures;
        /// <summary>⭐ CE-3364 — hostiles a man of the subject harmed, with the sim time of his LAST harm: it feeds ONLY assists (a kill is a <c>ManDownedEvent</c> naming the man, never guessed from health).</summary>
        public Credits Helped;
        /// <summary>The men in the fight at the last tick (a man now out of the fight, still an entity, is a death).</summary>
        public Ents16 InFight; public int InFightCount;
    }

    /// <summary>
    /// ⭐ rules S4 E-2 / S7 P-3 (📄 docs/DESIGN_Scenario_Rules.md §00.11, §00.13) — the ONE reading of "what did the subject's men do" the <c>HitRatio</c> and <c>Count</c> measures share (⛔ two copies would drift):
    /// shots = <see cref="WeaponFireIntent"/> raised by a man of the subject (the intent is raised on the BRAIN node — the node the objectives run on — so it is visible here); a hit =
    /// a <see cref="DamageAssessedEvent"/> whose attributed source (CE-4155 <c>NoHarmBehavior.Sources</c>) is one of the men; the victim's side says what it was — a hostile (a hit), a Friend of the subject's side (fratricide), a Neutral (a civilian harmed). ⭐⭐ CE-3364 — kills, team kills and assists
    /// are NOT guessed from health: they read the ONE kill authority, the <see cref="ManDownedEvent"/> of the hit that took the man out of the fight (a kill: his man's hit downed a hostile; a team kill: a friend; an assist: another downed a hostile
    /// he had harmed within the window — a corpse hit again is nobody's kill). Deaths are his men going out of the fight; captures are <see cref="ObjectiveCapturedEvent"/>s naming them. The men are <c>ObjectiveCensus.MenOf</c>.
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
                    var credited = Credited(in e, attribute);
                    if (credited.IsNull) continue;
                    var victimSide = ObjectiveCensus.SideOf(world, e.HitEntity);
                    if (KillZone.Hostile(side, victimSide)) { t.Hits++; t.Helped.Put(e.HitEntity, now); }   // the man's last harm on it: an assist if another downs it soon
                    else if (victimSide == ForceId.Neutral) { if (e.HitEntity != credited) t.Civilians++; }
                    else if (victimSide == side && e.HitEntity != credited) t.Fratricide++;
                }
            }
            // ⭐⭐ CE-3364 — the ONE kill authority: a man going out of the fight by a hit is a ManDownedEvent (HealthApplicationSystem), carrying who did it. A kill / team kill / assist reads it; nothing is guessed from health.
            if (world.Bus.IsRegistered<ManDownedEvent>())
            {
                var downs = world.ReadEvents<ManDownedEvent>();
                for (int i = 0; i < downs.Length; i++)
                {
                    ref readonly var d = ref downs[i];
                    NoHarmBehavior.Sources(d.Source, d.OriginalSource, attribute, out var last, out var first);
                    var credited = !last.IsNull && s_men.Contains(last) ? last : !first.IsNull && s_men.Contains(first) ? first : Entity.Null;
                    if (!credited.IsNull)
                    {
                        if (KillZone.Hostile(side, d.VictimSide)) t.Kills++;
                        else if (d.VictimSide == side && d.Victim != credited) t.TeamKills++;
                    }
                    else if (KillZone.Hostile(side, d.VictimSide)) AssistIfHelped(ref t, d.Victim, now, assistSeconds > 0 ? assistSeconds : AssistSeconds);
                }
            }
            ExpireHelped(ref t, now, assistSeconds > 0 ? assistSeconds : AssistSeconds);
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

        /// <summary>The man of the subject a harm / downing is credited to by <paramref name="attribute"/> (Last = Source, Original = the first thrower, Both = either), else Null.</summary>
        private static Entity Credited(in DamageAssessedEvent e, HarmAttribute attribute)
        {
            NoHarmBehavior.Sources(in e, attribute, out var last, out var first);
            return !last.IsNull && s_men.Contains(last) ? last : !first.IsNull && s_men.Contains(first) ? first : Entity.Null;
        }

        /// <summary>⭐ S7 P-3 / CE-3364 — a hostile downed by another source within <paramref name="window"/> seconds of the subject's last harm on it is ONE assist.</summary>
        private static void AssistIfHelped(ref FireTally t, Entity victim, double now, double window)
        {
            int i = t.Helped.IndexOf(victim);
            if (i < 0) return;
            if (now - t.Helped.At[i] <= window) t.Assists++;   // ⭐ S4 ⑥ — the Count's authored assistSeconds, else AssistSeconds
            t.Helped.RemoveAt(i);
        }

        /// <summary>Harms older than the window can no longer make an assist: forgotten (the 16 slots stay free).</summary>
        private static void ExpireHelped(ref FireTally t, double now, double window)
        {
            for (int i = t.Helped.Count - 1; i >= 0; i--)
                if (now - t.Helped.At[i] > window) t.Helped.RemoveAt(i);
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
