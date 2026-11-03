using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>
    /// ⭐ SR-103 / RV-4 (docs/DESIGN_Rules_View_UI.md) + rules S4 score history (handoff S4 Scores ④, SR-111 trend) — the scenario rules' timeline
    /// lanes, as DATA. All three are value-change lanes on the one recorded status (<see cref="ObjectiveStatus"/>), so the live timeline, a
    /// recording's index and the after-action review read ONE history (⛔ no new store — the recording already holds every frame):
    /// <list type="bullet">
    /// <item><c>objective.state</c> — an entry when an objective / measure / trigger changes state: Value = the <see cref="ObjectiveState"/> number;</item>
    /// <item><c>rules.fired</c> — an entry when a trigger's fire count moves: Value = <see cref="ObjectiveStatus.FireCount"/> (the first sight
    ///   carries the count it already has — a reader skips Value 0);</item>
    /// <item><c>objective.score</c> — the score history: an entry when <see cref="ObjectiveStatus.Score"/> changes: Value = Score, X = Target.</item>
    /// </list>
    /// <para>⚠ A value-change lane compares the PROJECTED fields only, so the status's other fields (timing, reason, links) moving add no entry.
    /// ⚠ Exact compare: a measure whose score moves every tick (a <c>Sample</c> mean) adds an entry each tick it moves.</para>
    /// </summary>
    public sealed class ScenarioRulesTimelineLanes : ITimelineLaneProvider
    {
        public const string Group = "Rules";
        public static readonly TimelineLaneDescriptor State = new("objective.state", "Objectives", Group: Group, Priority: 2, Color: 0xFF60C060);
        public static readonly TimelineLaneDescriptor Fired = new("rules.fired", "Triggers", Group: Group, Priority: 2, Color: 0xFF40A0FF);
        public static readonly TimelineLaneDescriptor Score = new("objective.score", "Score", Group: Group, Hidden: true);

        private static readonly IndexedLaneDefinition[] Definitions =
        {
            new(State, new ValueChangePredicateDto { ComponentType = typeof(ObjectiveStatus) },
                new ResultProjectionDto { SourceComponent = typeof(ObjectiveStatus), ValuePath = nameof(ObjectiveStatus.State) }),
            new(Fired, new ValueChangePredicateDto { ComponentType = typeof(ObjectiveStatus) },
                new ResultProjectionDto { SourceComponent = typeof(ObjectiveStatus), ValuePath = nameof(ObjectiveStatus.FireCount) }),
            new(Score, new ValueChangePredicateDto { ComponentType = typeof(ObjectiveStatus) },
                new ResultProjectionDto { SourceComponent = typeof(ObjectiveStatus), ValuePath = nameof(ObjectiveStatus.Score), XPath = nameof(ObjectiveStatus.Target) }),
        };

        public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new[] { State, Fired, Score };
        public IReadOnlyList<IndexedLaneDefinition> Indexed => Definitions;
        public bool Applies(TimelineLaneContext context) => context.Indexing;   // a recording's index or the live one (CE-3330)
    }
}
