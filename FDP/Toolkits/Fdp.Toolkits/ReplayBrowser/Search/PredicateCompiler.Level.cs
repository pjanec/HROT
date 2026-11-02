using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using StructEdit.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>⭐ CE-4164 — how <see cref="PredicateCompiler"/> compiles the four specialised loop predicates.</summary>
    internal enum PredicateMode
    {
        /// <summary>The recording search / breakpoints / timeline: those four pass through (their own scanners decide).</summary>
        Scan,
        /// <summary>A trigger's <c>when</c> (SR-158): they answer a STATE of this entity, every frame.</summary>
        State,
    }

    /// <summary>
    /// ⭐ <c>CE-4164</c> (📄 docs/DESIGN_Scenario_Rules.md §00.9 S2-b, SR-158) — the LEVEL arms and the new leaves of the ONE predicate compiler.
    /// Every other arm is shared with Search mode; only the four loop predicates differ.
    /// </summary>
    public sealed partial class PredicateCompiler
    {
        // ── spatial: the entity's position is inside Bounds ─────────────────────────────────────────────────────────────────────────────

        private delegate float FloatRead<T>(ref T component);

        /// <summary>
        /// Level mode: true when the entity's position is inside <see cref="SpatialBoundingPredicateDto.Bounds"/> (<c>AreaLink.Contains</c> — the one
        /// area test). The position is <c>SimTransform.Position</c> when <c>PositionComponentType</c> is null, else the X / Y paths of that component.
        /// ⚠ <c>TriggerEvent</c> (entry / exit) is IGNORED here: an edge is the caller's job (the trigger's rising-edge logic).
        /// </summary>
        private static Func<EntityRepository, Entity, bool> CompileSpatialLevel(SpatialBoundingPredicateDto dto)
        {
            var area = dto.Bounds;
            if (dto.PositionComponentType == null)
                return (repo, e) => repo.IsComponentTypeRegistered<SimTransform>() && repo.HasComponent<SimTransform>(e)
                                    && area.Contains(repo, repo.GetComponentRO<SimTransform>(e).Position);
            if (!dto.PositionComponentType.IsValueType) return static (_, _) => false;   // a managed position component: not read (as the live tracker's accessor)
            var build = typeof(PredicateCompiler).GetMethod(nameof(BuildSpatialReader), BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Func<EntityRepository, Entity, bool>)build.MakeGenericMethod(dto.PositionComponentType).Invoke(null, new object[] { dto })!;
        }

        private static Func<EntityRepository, Entity, bool> BuildSpatialReader<T>(SpatialBoundingPredicateDto dto) where T : unmanaged
        {
            var area = dto.Bounds;
            int typeId = ComponentTypeRegistry.GetId(typeof(T));
            FloatRead<T> Reader(string path)
            {
                var p = Expression.Parameter(typeof(T).MakeByRefType(), "c");
                Expression access = p;
                foreach (string seg in path.Split('.')) access = Expression.PropertyOrField(access, seg);
                return Expression.Lambda<FloatRead<T>>(Expression.Convert(access, typeof(float)), p).Compile();
            }
            var x = Reader(dto.PositionXPath); var y = Reader(dto.PositionYPath);
            return (repo, e) =>
            {
                if (typeId < 0 || !repo.HasComponentByTypeId(e, typeId)) return false;
                ref readonly T c = ref repo.GetComponentRO<T>(e);
                ref T rw = ref System.Runtime.CompilerServices.Unsafe.AsRef(in c);
                return area.Contains(repo, new System.Numerics.Vector3(x(ref rw), y(ref rw), 0f));
            };
        }

        // ── lifecycle: the entity it identifies is alive ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Level mode: true when the entity being asked about is alive AND is the one the predicate identifies (an ECS handle / index, a network
        /// id, or a name substring). ⭐ CE-4205: the breakpoint manager's lifecycle tracker evaluates through this. <c>NameSubstring</c> reads the name by reflection: not allocation-free.
        /// </summary>
        private static Func<EntityRepository, Entity, bool> CompileLifecycleLevel(LifecyclePredicateDto dto)
        {
            string target = dto.TargetValue ?? string.Empty;
            switch (dto.IdentifierType)
            {
                case EntityIdentifierType.EcsHandle:
                    bool isIndex = int.TryParse(target, out int index);
                    return (repo, e) => repo.IsAlive(e) && (isIndex ? e.Index == index : e.ToString() == target);
                case EntityIdentifierType.NetworkId:
                    return (repo, e) => repo.IsAlive(e) && LifecycleIdentity.IsNetworkId(repo, e, target);
                case EntityIdentifierType.NameSubstring:
                    return (repo, e) =>
                    {
                        if (!repo.IsAlive(e)) return false;
                        if (dto.NameComponentType == null) return e.ToString().Contains(target, StringComparison.OrdinalIgnoreCase);
                        return ReadName(repo, e, dto) is { } n && n.Contains(target, StringComparison.OrdinalIgnoreCase);
                    };
                default: return static (_, _) => false;
            }
        }

        private static string? ReadName(EntityRepository repo, Entity entity, LifecyclePredicateDto dto)
        {
            Type type = dto.NameComponentType!;
            int typeId = ComponentTypeRegistry.GetId(type);
            if (typeId < 0 || !repo.HasComponentByTypeId(entity, typeId)) return null;
            object? cur;
            if (type.IsValueType)
            {
                unsafe
                {
                    void* ptr = repo.GetComponentPointer(entity, typeId);
                    if (ptr == null) return null;
                    cur = Marshal.PtrToStructure(new IntPtr(ptr), type);
                }
            }
            else cur = repo.GetManagedComponentByTypeId(entity, typeId);
            foreach (string seg in (dto.NamePropertyPath ?? "Name").Split('.'))
            {
                if (cur == null) return null;
                var t = cur.GetType();
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                if (t.GetField(seg, F) is { } fi) cur = fi.GetValue(cur);
                else if (t.GetProperty(seg, F) is { } pi) cur = pi.GetValue(cur);
                else return null;
            }
            return cur?.ToString();
        }

        // ── structural: the entity has the component ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Level mode: true when the entity HAS <c>ComponentType</c> (<c>Added</c> / <c>AnyChange</c> ⇒ has; <c>Removed</c> ⇒ has not).
        /// ⭐ <c>CE-4205</c>: <c>AuthorityRequirement</c> is honoured through the entity's component + authority masks (the breakpoint tracker's rule, now this one's).
        /// </summary>
        private static Func<EntityRepository, Entity, bool> CompileStructuralLevel(StructuralPredicateDto dto)
        {
            Type? type = dto.ComponentType;
            if (type == null) return static (_, _) => false;
            int typeId = ComponentTypeRegistry.GetId(type);
            bool wantHas = dto.ModificationType != StructuralModification.Removed;
            bool managed = !type.IsValueType;
            var authority = dto.AuthorityRequirement;
            return (repo, e) =>
            {
                if (!repo.IsAlive(e)) return false;
                bool has = typeId >= 0 && (managed ? repo.GetManagedComponentByTypeId(e, typeId) != null : repo.HasComponentByTypeId(e, typeId));
                if (has && authority != AuthorityRequirement.AnyAuthority)
                {
                    bool owned = repo.GetMetadata(e.Index).AuthorityMask.IsSet(typeId);
                    has = authority == AuthorityRequirement.RequireAuthority ? owned : !owned;
                }
                return has == wantHas;
            };
        }

        // ── transient event: such an event occurred THIS frame ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Level mode: true when an event of <c>EventType</c> was published this frame. With <c>AnyOccurrence</c> (or no <c>PropertyPath</c>) any
        /// such event counts; otherwise the event must carry an <c>Entity</c>-typed field at <c>PropertyPath</c> that names THIS entity (the
        /// scanner's own "[index, vN]" rendering, <see cref="EventScannerCompiler.TryExtractEntity"/>).
        /// </summary>
        private Func<EntityRepository, Entity, bool> CompileEventLevel(TransientEventPredicateDto dto)
        {
            Type eventType = dto.EventType ?? throw new ArgumentException("EventType must not be null.", nameof(dto));
            if (dto.AnyOccurrence || string.IsNullOrEmpty(dto.PropertyPath))
                return (repo, _) => repo.HasEvents(eventType);

            // reuse the timeline lanes' scanner: a pure "names this entity" scan = the value path with an always-true match
            var scanner = new EventScannerCompiler(_editService).CompileScanner(new TransientEventPredicateDto
            {
                EventType = eventType, AnyOccurrence = false, PropertyPath = dto.PropertyPath, Operator = SearchOperator.Contains, TargetValue = string.Empty,
            });
            var results = new System.Collections.Generic.List<SearchResultDto>();
            var filter = new TargetEntityFilter();
            return (repo, e) =>
            {
                if (e.IsNull || !repo.HasEvents(eventType)) return false;
                filter.UseNetworkId = false; filter.Index = e.Index; filter.Generation = e.Generation;
                results.Clear();
                scanner(repo, 0, 0, results, filter);
                return results.Count > 0;
            };
        }

        // ── the new leaves ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private Func<EntityRepository, Entity, bool> CompileOnEntity(OnEntityPredicateDto dto, PredicateMode mode)
        {
            var inner = Compile(dto.Predicate ?? throw new ArgumentException("OnEntity needs a predicate.", nameof(dto)), mode);
            var reference = dto.Ref.Ref;
            return (repo, _) =>
            {
                var target = reference.Resolve(repo);
                return !target.IsNull && repo.IsAlive(target) && inner(repo, target);
            };
        }

        private static Func<EntityRepository, Entity, bool> CompileScenarioTime(ScenarioTimePredicateDto dto)
        {
            double from = dto.FromSeconds, to = dto.ToSeconds;
            return (repo, _) =>
            {
                double now = ScenarioNow(repo);
                return now >= from && now < to;
            };
        }

        /// <summary>The scenario time (s since the start) a <c>ScenarioTime</c> leaf compares — and the number the rules view shows for it (<see cref="CompileLeafMeasure"/>): ONE reader.</summary>
        public static double ScenarioNow(EntityRepository repo)
            => repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;

        /// <summary>⭐ The counting half of <c>CountMatching</c>, shared by the predicate and the rules view's number (<see cref="CompileLeafMeasure"/>): how many entities match.</summary>
        private sealed class MatchCounter
        {
            private readonly Func<EntityRepository, Entity, bool> _inner;
            private readonly int _withId;
            private readonly bool _withNone;
            private EntityQuery? _query;
            private EntityRepository? _queryRepo;

            public MatchCounter(Func<EntityRepository, Entity, bool> inner, Type? withComponent)
            {
                _inner = inner;
                _withId = withComponent != null ? ComponentTypeRegistry.GetId(withComponent) : -1;
                _withNone = withComponent != null && _withId < 0;
            }

            /// <summary>The entities matching, counted until it exceeds <paramref name="stopAbove"/> (then that is all it says: stopAbove + 1). An unregistered <c>WithComponent</c>: nobody has it, 0.</summary>
            public int Count(EntityRepository repo, int stopAbove)
            {
                if (_withNone) return 0;
                if (!ReferenceEquals(_queryRepo, repo))
                {
                    var qb = repo.Query();
                    _query = (_withId >= 0 ? qb.WithComponentId(_withId) : qb.With<SimTransform>()).Build();
                    _queryRepo = repo;
                }
                int n = 0;
                foreach (var e in _query!)
                {
                    if (!_inner(repo, e)) continue;
                    if (++n > stopAbove) return n;
                }
                return n;
            }
        }

        private Func<EntityRepository, Entity, bool> CompileCountMatching(CountMatchingPredicateDto dto, PredicateMode mode)
        {
            var counter = BuildCounter(dto, mode);
            int min = dto.Min, max = dto.Max;
            return (repo, _) => { int n = counter.Count(repo, max); return n >= min && n <= max; };
        }

        private MatchCounter BuildCounter(CountMatchingPredicateDto dto, PredicateMode mode)
            => new(Compile(dto.Predicate ?? throw new ArgumentException("CountMatching needs a predicate.", nameof(dto)), mode), dto.WithComponent);
    }
}
