using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Fdp.Toolkit.Squad.Command
{
    // ⭐⭐ CE-1319 (Brain View §12e BV-57, BV-58, BV-60, R-1040) — a commander's report and order HISTORIES as COMPONENTS, so the Brain
    //   tab's Reports and Orders expanders rebuild in the replay browser. Commanders only (provisioned with the squad state); NoScenario
    //   ⇒ recorded and snapshotted, never in the scenario file. ⭐ Each is a FIXED LIST (exactly `int Count` + one [InlineArray] —
    //   FixedListShape), newest FIRST, so the inspector dump, the JSON converter and the watch formatter read it with no new code.
    //   ⛔ SquadReportLog stays the node-wide HTTP record (256 shared per node); it is never the tab's source.
    //   📄 docs/DESIGN_Entity_Brain_View.md §12e.

    /// <summary>Which way a history entry went, seen from the commander that holds it.</summary>
    public enum CommandTraffic : byte
    {
        /// <summary>⚠ not <c>None</c>: the DDS IDL generator puts a file's enums in one scope.</summary>
        TrafficUnknown = 0,
        /// <summary>To this commander — a report from a subordinate, an order from his superior.</summary>
        Incoming,
        /// <summary>From this commander — a report to his superior, an order to a subordinate.</summary>
        Outgoing,
    }

    /// <summary>What the inbox did with a report (<see cref="SquadReportLogEntry.Verdict"/>, as a number).</summary>
    public enum ReportFate : byte
    {
        FateUnknown = 0,
        /// <summary>Taken into the addressee's picture.</summary>
        Folded,
        /// <summary>A late, older report — dropped (CB-32).</summary>
        Stale,
        /// <summary>The addressee's brain is not owned on this node.</summary>
        NotOwnedHere,
    }

    /// <summary>One report to or from the commander.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CommandReportEntry
    {
        /// <summary>When the inbox took it in (sim s).</summary>
        public float At;
        public CommandTraffic Traffic;
        public ReportFate Fate;
        /// <summary>The sender's network id.</summary>
        public long FromNetId;
        /// <summary>The addressee's network id.</summary>
        public long ToNetId;
        public CommandReport Report;
    }

    [InlineArray(CommandReportHistory.Capacity)]
    public struct CommandReportEntries { private CommandReportEntry _e0; }

    /// <summary>⭐ <c>CE-1319</c> (BV-57) — the last <see cref="Capacity"/> reports to and from this commander, newest first.</summary>
    [ComponentId(GlobalComponentIds.CommandReportHistory)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct CommandReportHistory
    {
        public const int Capacity = 32;
        public int Count;
        public CommandReportEntries Items;
    }

    /// <summary>One order the commander gave or received.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CommandOrderEntry
    {
        /// <summary>When it was (last) given (sim s).</summary>
        public float At;
        public CommandTraffic Traffic;
        /// <summary>⭐ The WHY, COPIED from the giver's brain journal (BV-60) so it survives the 24-entry journal's eviction: the newest
        /// change before the order — its kind (a <see cref="BrainJournalKind"/>), its cause byte and its A (the play, the behaviour…).</summary>
        public BrainJournalKind CauseKind;
        public byte Cause;
        /// <summary>How many times the same order was given again since <see cref="At"/> first (a runner's re-send is not news).</summary>
        public ushort Repeats;
        public int CauseA;
        /// <summary>The other side's network id — the member ordered, or the superior who ordered.</summary>
        public long CounterpartNetId;
        /// <summary>The behaviour (or abstract order) hash — <see cref="Fdp.Toolkit.Behavior.BehaviorHash.FromName"/>.</summary>
        public int Behaviour;
    }

    [InlineArray(CommandOrderHistory.Capacity)]
    public struct CommandOrderEntries { private CommandOrderEntry _e0; }

    /// <summary>⭐ <c>CE-1319</c> (BV-58) — the last <see cref="Capacity"/> orders this commander gave or received, newest first.</summary>
    [ComponentId(GlobalComponentIds.CommandOrderHistory)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct CommandOrderHistory
    {
        public const int Capacity = 32;
        public int Count;
        public CommandOrderEntries Items;
    }

    /// <summary>
    /// ⭐ The ONE write path into both histories — a no-op where the world or the unit has none (a soldier, a node without a brain), so
    /// only commanders pay. Newest first: an add shifts the list down by one (≤ 31 entries of ~70 B, only when a report or an order
    /// happens — never per tick).
    /// </summary>
    public static class CommandHistoryOf
    {
        /// <summary>Records <paramref name="report"/> on the addressee (incoming) and on the sender (outgoing), whichever is a commander here.</summary>
        public static void Report(EntityRepository world, Entity from, Entity to, in CommandReport report, float now, ReportFate fate)
        {
            if (!world.IsComponentTypeRegistered<CommandReportHistory>()) return;
            var entry = new CommandReportEntry
            {
                At = now, Fate = fate, Report = report,
                FromNetId = CommandPictureOps.NetIdOf(world, from), ToNetId = CommandPictureOps.NetIdOf(world, to),
            };
            if (!to.IsNull && world.IsAlive(to) && world.HasComponent<CommandReportHistory>(to))
            {
                entry.Traffic = CommandTraffic.Incoming;
                Push(ref world.GetComponentRW<CommandReportHistory>(to), entry);
            }
            if (!from.IsNull && !from.Equals(to) && world.IsAlive(from) && world.HasComponent<CommandReportHistory>(from))
            {
                entry.Traffic = CommandTraffic.Outgoing;
                Push(ref world.GetComponentRW<CommandReportHistory>(from), entry);
            }
        }

        /// <summary>Records an order <paramref name="giver"/> gave <paramref name="member"/>: outgoing on the giver, incoming on the member
        /// when the member is itself a commander. The cause is copied from the giver's journal.</summary>
        public static void Order(EntityRepository world, Entity giver, Entity member, int behaviourHash, float now)
        {
            if (!world.IsComponentTypeRegistered<CommandOrderHistory>()) return;
            var entry = new CommandOrderEntry { At = now, Behaviour = behaviourHash };
            CauseOf(world, giver, ref entry);
            if (world.IsAlive(giver) && world.HasComponent<CommandOrderHistory>(giver))
            {
                entry.Traffic = CommandTraffic.Outgoing;
                entry.CounterpartNetId = CommandPictureOps.NetIdOf(world, member);
                Push(ref world.GetComponentRW<CommandOrderHistory>(giver), entry);
            }
            if (!member.Equals(giver) && world.IsAlive(member) && world.HasComponent<CommandOrderHistory>(member))
            {
                entry.Traffic = CommandTraffic.Incoming;
                entry.CounterpartNetId = CommandPictureOps.NetIdOf(world, giver);
                Push(ref world.GetComponentRW<CommandOrderHistory>(member), entry);
            }
        }

        /// <summary>The newest journal change before the order that is not itself an order (the play phase, the task start, the decision
        /// switch that made the commander give it).</summary>
        private static void CauseOf(EntityRepository world, Entity giver, ref CommandOrderEntry entry)
        {
            if (!world.IsComponentTypeRegistered<BrainJournal>() || !world.IsAlive(giver) || !world.HasComponent<BrainJournal>(giver)) return;
            ref readonly var j = ref world.GetComponentRO<BrainJournal>(giver);
            for (int i = j.Count - 1; i >= 0; i--)
            {
                var e = j.At(i);
                if (e.Kind is BrainJournalKind.OrderIssued or BrainJournalKind.None) continue;
                entry.CauseKind = e.Kind;
                entry.Cause = e.Cause;
                entry.CauseA = e.A;
                return;
            }
        }

        internal static void Push(ref CommandReportHistory h, in CommandReportEntry e)
        {
            Span<CommandReportEntry> items = h.Items;
            int keep = Math.Min(h.Count, CommandReportHistory.Capacity - 1);
            items[..keep].CopyTo(items[1..]);
            items[0] = e;
            h.Count = keep + 1;
        }

        internal static void Push(ref CommandOrderHistory h, in CommandOrderEntry e)
        {
            Span<CommandOrderEntry> items = h.Items;
            if (h.Count > 0 && items[0].Traffic == e.Traffic && items[0].CounterpartNetId == e.CounterpartNetId
                && items[0].Behaviour == e.Behaviour && items[0].CauseKind == e.CauseKind && items[0].CauseA == e.CauseA)
            {
                if (items[0].Repeats < ushort.MaxValue) items[0].Repeats++;   // the same order again: not news — counted, not listed
                return;
            }
            int keep = Math.Min(h.Count, CommandOrderHistory.Capacity - 1);
            items[..keep].CopyTo(items[1..]);
            items[0] = e;
            h.Count = keep + 1;
        }
    }
}
