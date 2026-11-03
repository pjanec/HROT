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
    // ⭐ Ents16 (sixteen entities) lives beside Ids16 in Fdp.Toolkit.ScenarioRules (ObjectiveRules.cs) — S7's ObjectiveCapturedEvent carries one too.
    /// <summary>Eight entities (the hostiles an assist may still be credited for).</summary>
    [InlineArray(8)] public struct Ents8 { private Entity _e; }
    /// <summary>Sixteen sim times (s) — when each pending victim was last hit by the subject's men.</summary>
    [InlineArray(16)] public struct Secs16 { private float _t; }
    /// <summary>Eight sim times (s) — when each assist candidate was last hit by the subject's men.</summary>
    [InlineArray(8)] public struct Secs8 { private float _t; }

    /// <summary>⭐ rules S4 E-2 — what the subject's men have DONE, counted from the frame's events: the working state of <see cref="MeasureEvidence"/> (lives in a measure's blackboard).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireTally
    {
        public int Shots, Hits, Kills, Fratricide, Civilians, Assists;
        /// <summary>Hostiles a man of the subject hit and that are still in the fight (16 slots; beyond them a hit still counts, but its kill cannot be credited).</summary>
        public Ents16 Pending; public int PendingCount; public Secs16 PendingAt;
        /// <summary>⭐ rules S4 ⑥ — hostiles a man of the subject hit but whom somebody ELSE then hit (his credit superseded): an ASSIST if they go down within <c>assistSeconds</c> of the subject's last hit and nobody on the subject's side made the kill.</summary>
        public Ents8 Assisted; public int AssistedCount; public Secs8 AssistedAt;
        /// <summary>Hostiles already credited as killed (a corpse shot again is not a second kill; a revived one leaves the list and can be killed again).</summary>
        public Ents16 Killed; public int KilledCount;
        /// <summary>⭐ S7 P-3 (DESIGN_Scenario_Rules §00.13) — team kills (a friend he harmed went down — the kill rule on his own side, never a kill), deaths (his men going out of the
        /// fight, once each), captures (an <see cref="ObjectiveCapturedEvent"/> naming one of his men).</summary>
        public int TeamKills, Deaths, Captures;
        /// <summary>Friends a man of the subject harmed that are still in the fight / already counted as team-killed.</summary>
        public Ents16 Friends; public int FriendsCount; public Ents16 TeamKilled; public int TeamKilledCount;
        /// <summary>The men in the fight at the last tick (one now out of the fight, still an entity, is a death).</summary>
        public Ents16 InFight; public int InFightCount;
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

        /// <summary>The window (s) inside which a hit that was not the killing one still counts as an ASSIST (<c>Count { what: assists, assistSeconds }</c>).</summary>
        public const float DefaultAssistSeconds = 10f;

        /// <summary>Reads this frame's events into <paramref name="t"/> for <paramref name="subject"/>'s men (a missing subject has none). <paramref name="now"/> (sim s) times the hits for the assist window.</summary>
        public static void Update(EntityRepository world, Entity subject, HarmAttribute attribute, ref FireTally t, double now = 0d, float assistSeconds = DefaultAssistSeconds)
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
                    if (KillZone.Hostile(side, victimSide)) { t.Hits++; Remember(ref t, e.HitEntity, (float)now); }
                    else if (victimSide == ForceId.Neutral) { if (e.HitEntity != credited) t.Civilians++; }
                    else if (victimSide == side && e.HitEntity != credited) { t.Fratricide++; RememberFriend(ref t, e.HitEntity); }
                }
            }
            // a credited hostile that is out of the fight now (down, or gone) was KILLED by the subject's men — counted once
            for (int i = t.PendingCount - 1; i >= 0; i--)
            {
                var v = t.Pending[i];
                if (world.IsAlive(v) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v)) continue;
                t.Kills++;
                MarkScored(ref t, v);
                t.PendingAt[i] = t.PendingAt[t.PendingCount - 1];
                t.Pending[i] = t.Pending[--t.PendingCount];
            }
            // ⭐ rules S4 ⑥ — a hostile the subject's men hit, whom somebody else finished: an ASSIST, once, when he went down within the window of the subject's last hit
            for (int i = t.AssistedCount - 1; i >= 0; i--)
            {
                var v = t.Assisted[i];
                bool down = !world.IsAlive(v) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v);
                if (!down) continue;   // still fighting: the candidate waits (the window is judged when he goes down; the oldest candidate makes room for a new one)
                if (now - t.AssistedAt[i] <= assistSeconds && !WasScored(in t, v)) { t.Assists++; MarkScored(ref t, v); }
                t.Assisted[i] = t.Assisted[t.AssistedCount - 1]; t.AssistedAt[i] = t.AssistedAt[t.AssistedCount - 1]; t.AssistedCount--;   // scored, or too late
            }
            for (int i = t.KilledCount - 1; i >= 0; i--)   // revived: it can be killed (and credited) again
                if (world.IsAlive(t.Killed[i]) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, t.Killed[i])) t.Killed[i] = t.Killed[--t.KilledCount];
            TeamKills(world, ref t);
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

        /// <summary>⭐ S7 P-3 — a friend a man of the subject harmed (not himself): remembered until he goes out of the fight (a team kill) or another source harms him.</summary>
        private static void RememberFriend(ref FireTally t, Entity victim)
        {
            for (int i = 0; i < t.TeamKilledCount; i++) if (t.TeamKilled[i] == victim) return;
            for (int i = 0; i < t.FriendsCount; i++) if (t.Friends[i] == victim) return;
            if (t.FriendsCount < 16) t.Friends[t.FriendsCount++] = victim;
        }

        /// <summary>⭐ S7 P-3 — the kill rule on his own side: a friend he harmed now out of the fight is ONE team kill (never a kill); a revived one can be team-killed again.</summary>
        private static void TeamKills(EntityRepository world, ref FireTally t)
        {
            for (int i = t.FriendsCount - 1; i >= 0; i--)
            {
                var v = t.Friends[i];
                if (world.IsAlive(v) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, v)) continue;
                t.TeamKills++;
                if (t.TeamKilledCount == 16) { for (int k = 1; k < 16; k++) t.TeamKilled[k - 1] = t.TeamKilled[k]; t.TeamKilledCount = 15; }
                t.TeamKilled[t.TeamKilledCount++] = v;
                t.Friends[i] = t.Friends[--t.FriendsCount];
            }
            for (int i = t.TeamKilledCount - 1; i >= 0; i--)
                if (world.IsAlive(t.TeamKilled[i]) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, t.TeamKilled[i])) t.TeamKilled[i] = t.TeamKilled[--t.TeamKilledCount];
        }

        /// <summary>⭐ S7 P-3 — a man seen in the fight last tick who is out of it now while still an entity is one death (a revived man is seen again and can die again).</summary>
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

        /// <summary>A victim already counted (a kill or an assist) — a corpse shot again scores nothing.</summary>
        private static bool WasScored(in FireTally t, Entity victim)
        {
            for (int i = 0; i < t.KilledCount; i++) if (t.Killed[i] == victim) return true;
            return false;
        }

        private static void MarkScored(ref FireTally t, Entity v)
        {
            if (t.KilledCount == 16) { for (int k = 1; k < 16; k++) t.Killed[k - 1] = t.Killed[k]; t.KilledCount = 15; }   // the oldest forgotten
            t.Killed[t.KilledCount++] = v;
        }

        private static void Remember(ref FireTally t, Entity victim, float now)
        {
            if (WasScored(in t, victim)) return;
            for (int i = 0; i < t.AssistedCount; i++)   // the subject hit him again: the kill is his to take back
                if (t.Assisted[i] == victim) { t.Assisted[i] = t.Assisted[t.AssistedCount - 1]; t.AssistedAt[i] = t.AssistedAt[t.AssistedCount - 1]; t.AssistedCount--; break; }
            for (int i = 0; i < t.PendingCount; i++) if (t.Pending[i] == victim) { t.PendingAt[i] = now; return; }
            if (t.PendingCount < 16) { t.PendingAt[t.PendingCount] = now; t.Pending[t.PendingCount++] = victim; }
        }

        /// <summary>Somebody else's harm supersedes the subject's pending kill credit — and turns it into an assist candidate (kept with the time of the subject's last hit).</summary>
        private static void Forget(ref FireTally t, Entity victim)
        {
            for (int i = 0; i < t.FriendsCount; i++)   // S7: somebody else's harm also takes a pending team kill off him
                if (t.Friends[i] == victim) { t.Friends[i] = t.Friends[--t.FriendsCount]; break; }
            for (int i = 0; i < t.PendingCount; i++)
                if (t.Pending[i] == victim)
                {
                    float at = t.PendingAt[i];
                    t.PendingAt[i] = t.PendingAt[t.PendingCount - 1]; t.Pending[i] = t.Pending[--t.PendingCount];
                    if (t.AssistedCount == 8) { for (int k = 1; k < 8; k++) { t.Assisted[k - 1] = t.Assisted[k]; t.AssistedAt[k - 1] = t.AssistedAt[k]; } t.AssistedCount = 7; }   // the oldest forgotten
                    t.AssistedAt[t.AssistedCount] = at; t.Assisted[t.AssistedCount++] = victim;
                    return;
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
