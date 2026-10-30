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
            ValueChangePredicateDto v => new ValueChangeLane(v, lane.Payload),
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

        /// <summary>
        /// ⭐ <c>CE-4117</c> — a component's value changes (<see cref="ValueChangePredicateDto"/>): per alive entity, its state (the lane's
        /// projection of the component) against the previous frame's. Tracks entities by raw index + generation, as <see cref="StructuralScanner"/>
        /// does (an index reused with a new generation is a departure plus an arrival — the arrival's first state is a change); a departure
        /// is silent. States compare as payloads (all five fields), never by identity.
        /// </summary>
        private sealed class ValueChangeLane : ILaneScan
        {
            private readonly Type _component;
            private readonly CompiledProjection _projection;
            private readonly bool _presentIsOne;
            private int[] _gen = Array.Empty<int>();                      // generation tracked per index, −1 = no state held
            private SearchResultPayload[] _state = Array.Empty<SearchResultPayload>();
            private readonly string _changed = "changed", _removed = "removed";

            public ValueChangeLane(ValueChangePredicateDto predicate, ResultProjectionDto? payload)
            {
                _component = predicate.ComponentType ?? throw new ArgumentException("a ValueChange lane names its component");
                var projection = payload ?? new ResultProjectionDto();
                if (projection.SourceComponent != null && projection.SourceComponent != _component)
                    throw new ArgumentException($"a ValueChange lane reads {_component.Name}, not {projection.SourceComponent.Name}");
                _presentIsOne = string.IsNullOrWhiteSpace(projection.ValuePath);
                _projection = new CompiledProjection(new ResultProjectionDto
                {
                    SourceComponent = _component, SubjectPath = projection.SubjectPath, OtherPath = projection.OtherPath,
                    XPath = projection.XPath, YPath = projection.YPath, ValuePath = projection.ValuePath,
                }, _component);
            }

            public void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results)
            {
                int typeId = ComponentTypeRegistry.GetId(_component);
                if (typeId < 0) return;
                int maxIdx = repo.MaxEntityIndex;
                if (_gen.Length <= maxIdx)
                {
                    int old = _gen.Length, size = Math.Max(maxIdx + 1, old * 2);
                    Array.Resize(ref _gen, size); Array.Resize(ref _state, size);
                    for (int k = old; k < size; k++) _gen[k] = -1;
                }
                for (int i = 0; i <= maxIdx; i++)
                {
                    ref var meta = ref repo.GetMetadata(i);
                    bool active = meta.IsActive;
                    if (_gen[i] >= 0 && (!active || meta.Generation != _gen[i])) _gen[i] = -1;   // departed: silent
                    if (!active) continue;
                    bool present = repo.GetComponentMask(i).IsSet(typeId);
                    bool had = _gen[i] >= 0;
                    if (!present && !had) continue;
                    var entity = new Entity(i, meta.Generation);
                    if (!present)
                    {
                        _gen[i] = -1;                                        // removed while alive: one entry, then no state
                        results.Add(new SearchResultDto(frame, ticks, entity, _removed, new SearchResultPayload(ResultIds.Of(repo, entity), 0, 0, 0, 0)));
                        continue;
                    }
                    object? boxed = ComponentBox.Read(repo, entity, _component);
                    var p = _projection.Apply(repo, boxed, entity, out _);
                    if (_presentIsOne) p = p with { Value = 1f };
                    if (had && _state[i].Equals(p)) continue;
                    _gen[i] = meta.Generation; _state[i] = p;
                    results.Add(new SearchResultDto(frame, ticks, entity, _changed, p));
                }
            }
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
