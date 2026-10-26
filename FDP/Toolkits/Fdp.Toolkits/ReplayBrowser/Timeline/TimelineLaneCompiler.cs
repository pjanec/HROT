using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ One indexed lane, compiled: scans ONE played frame and appends its results. Stateful (the ring and structural diffs), so one per
    /// build. Made by <see cref="TimelineLaneCompiler"/> from the lane's predicate — never hand-written per lane (R-4079).
    /// </summary>
    public interface ILaneScan
    {
        void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results);
    }

    /// <summary>
    /// ⭐ <c>R-4079</c> (docs/DESIGN_Director_Timeline_Editor.md §9c ②) — compiles an <see cref="IndexedLaneDefinition"/>'s predicate with the
    /// model's own compilers (R-4077: one condition model): a transient event through <see cref="IEventScannerCompiler"/> (projected), a
    /// structural change through <see cref="StructuralScanner"/>, a ring's new entries through <see cref="RingEntryScanner"/>, and any other
    /// component condition through <see cref="IPredicateCompiler"/> — one event when an entity STARTS matching.
    /// </summary>
    public sealed class TimelineLaneCompiler
    {
        private readonly IEventScannerCompiler _events;
        private readonly IPredicateCompiler? _components;

        public TimelineLaneCompiler(IEventScannerCompiler events, IPredicateCompiler? components = null)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _components = components;
        }

        public ILaneScan Compile(IndexedLaneDefinition lane) => lane.When switch
        {
            TransientEventPredicateDto e => new EventLane(_events.CompileScanner(e, lane.Payload)),
            StructuralPredicateDto s => new StructuralLane(new StructuralScanner(s, lane.Payload)),
            RingEntryPredicateDto r => new RingLane(RingEntryScanner.Compile(r, lane.Payload)),
            _ when _components != null => new RisingEdgeLane(_components.CompileComponentPredicate(lane.When)),
            _ => throw new NotSupportedException($"lane '{lane.Lane.Id}': a {lane.When.GetType().Name} needs a component predicate compiler"),
        };

        private sealed class EventLane : ILaneScan
        {
            private readonly EventScannerDelegate _scan;
            public EventLane(EventScannerDelegate scan) => _scan = scan;
            public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results) => _scan(repo, frame, ticks, results, null);
        }

        private sealed class StructuralLane : ILaneScan
        {
            private readonly StructuralScanner _scanner;
            public StructuralLane(StructuralScanner scanner) => _scanner = scanner;
            public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results) => _scanner.Scan(repo, frame, ticks, results);
        }

        private sealed class RingLane : ILaneScan
        {
            private readonly RingEntryScanner _scanner;
            public RingLane(RingEntryScanner scanner) => _scanner = scanner;
            public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results) => _scanner.Scan(repo, frame, ticks, results);
        }

        /// <summary>A plain component condition: one event per entity when it starts matching (every frame it matches would drown the lane).</summary>
        private sealed class RisingEdgeLane : ILaneScan
        {
            private readonly Func<EntityRepository, Entity, bool> _matches;
            private HashSet<Entity> _was = new(), _now = new();
            public RisingEdgeLane(Func<EntityRepository, Entity, bool> matches) => _matches = matches;

            public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results)
            {
                _now.Clear();
                int maxIdx = repo.MaxEntityIndex;
                for (int i = 0; i <= maxIdx; i++)
                {
                    Entity e = repo.GetEntityByIndex(i);
                    if (e.IsNull || !_matches(repo, e)) continue;
                    _now.Add(e);
                    if (!_was.Contains(e)) results.Add(new SearchResultDto(frame, ticks, e, "matched"));
                }
                (_was, _now) = (_now, _was);
            }
        }
    }
}
