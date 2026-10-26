using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// ⭐ <c>R-4076</c> / <c>R-4079</c> (docs/DESIGN_Director_Timeline_Editor.md TE-22, §9c ②) — the behaviour toolkit's four lanes over the
    /// brain's RECORDED histories, as DATA: each a <see cref="RingEntryPredicateDto"/> (one event per NEW entry, keyed by the entry's own
    /// identity — TE-22 ②; a unit's first frame is its baseline).
    /// <list type="bullet">
    /// <item>Changes — a new <see cref="BrainJournal"/> entry (key: its <c>Seq</c>; value: its kind; X / Y: its point);</item>
    /// <item>Orders — a new <see cref="CommandOrderHistory"/> entry, or the same order REPEATED (the repeat count is part of the key);
    ///   the counterpart; value: in / out;</item>
    /// <item>Reports — a new <see cref="CommandReportHistory"/> entry; the sender; value: in / out;</item>
    /// <item>Decisions — a <see cref="UtilityDecisionLog"/> slot whose switch count rose (key: decision + count, only counts above 0);
    ///   the winner; value: the decision id.</item>
    /// </list>
    /// </summary>
    public sealed class BehaviourTimelineLanes : ITimelineLaneProvider
    {
        public const string Group = "Behaviour";
        public static readonly TimelineLaneDescriptor Changes = new("behaviour.changes", "Changes", Group: Group, Priority: 1, Color: 0xFFE0B070);
        public static readonly TimelineLaneDescriptor Orders = new("behaviour.orders", "Orders", Group: Group, Priority: 2, Color: 0xFFD070B0);
        public static readonly TimelineLaneDescriptor Reports = new("behaviour.reports", "Reports", Group: Group, Priority: 2, Color: 0xFFB0B040);
        public static readonly TimelineLaneDescriptor Decisions = new("behaviour.decisions", "Decisions", Group: Group, Priority: 1, Color: 0xFFE0E070);

        private static readonly IndexedLaneDefinition[] Definitions =
        {
            new(Changes,
                new RingEntryPredicateDto { ComponentType = typeof(BrainJournal), ItemsPath = nameof(BrainJournal.Entries), CountPath = nameof(BrainJournal.Count),
                    KeyPaths = { nameof(BrainJournalEntry.Seq) } },
                new ResultProjectionDto { XPath = "Point.X", YPath = "Point.Y", ValuePath = nameof(BrainJournalEntry.Kind) }),
            new(Orders,
                new RingEntryPredicateDto { ComponentType = typeof(CommandOrderHistory), ItemsPath = nameof(CommandOrderHistory.Items), CountPath = nameof(CommandOrderHistory.Count),
                    KeyPaths = { nameof(CommandOrderEntry.At), nameof(CommandOrderEntry.Traffic), nameof(CommandOrderEntry.CounterpartNetId),
                                 nameof(CommandOrderEntry.Behaviour), nameof(CommandOrderEntry.Repeats) } },
                new ResultProjectionDto { OtherPath = nameof(CommandOrderEntry.CounterpartNetId), ValuePath = nameof(CommandOrderEntry.Traffic) }),
            new(Reports,
                new RingEntryPredicateDto { ComponentType = typeof(CommandReportHistory), ItemsPath = nameof(CommandReportHistory.Items), CountPath = nameof(CommandReportHistory.Count),
                    KeyPaths = { nameof(CommandReportEntry.At), nameof(CommandReportEntry.Traffic), nameof(CommandReportEntry.FromNetId), nameof(CommandReportEntry.ToNetId) } },
                new ResultProjectionDto { OtherPath = nameof(CommandReportEntry.FromNetId), ValuePath = nameof(CommandReportEntry.Traffic) }),
            new(Decisions,
                new RingEntryPredicateDto { ComponentType = typeof(UtilityDecisionLog), ItemsPath = nameof(UtilityDecisionLog.Slots),
                    KeyPaths = { nameof(UtilityLogSlot.DecisionId), nameof(UtilityLogSlot.SwitchCount) },
                    FilterPath = nameof(UtilityLogSlot.SwitchCount), FilterOperator = SearchOperator.GreaterThan, FilterValue = "0" },
                new ResultProjectionDto { OtherPath = nameof(UtilityLogSlot.Winner), ValuePath = nameof(UtilityLogSlot.DecisionId) }),
        };

        public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new[] { Changes, Orders, Reports, Decisions };
        public IReadOnlyList<IndexedLaneDefinition> Indexed => Definitions;
        public bool Applies(TimelineLaneContext context) => context.Recording;
    }
}
