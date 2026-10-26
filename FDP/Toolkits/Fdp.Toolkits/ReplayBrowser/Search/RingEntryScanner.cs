using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ <c>R-4077</c> extension (docs/DESIGN_Director_Timeline_Editor.md §9c ③) — evaluates a <see cref="RingEntryPredicateDto"/> frame by
    /// frame: one <see cref="SearchResultDto"/> per NEW entry of each entity's ring (its key absent the frame before), the first frame an
    /// entity is seen being its baseline. Stateful — one instance per scan.
    /// </summary>
    public abstract class RingEntryScanner
    {
        public abstract void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results, TargetEntityFilter? entityFilter = null);

        /// <summary>Compiles the predicate (and an optional projection read off each new entry, or off the entity's component).</summary>
        public static RingEntryScanner Compile(RingEntryPredicateDto predicate, ResultProjectionDto? projection = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            Type comp = predicate.ComponentType ?? throw new ArgumentException("ComponentType must not be null.", nameof(predicate));
            FieldInfo items = comp.GetField(predicate.ItemsPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new ArgumentException($"'{predicate.ItemsPath}' is not a field of {comp.Name}.", nameof(predicate));
            Type arr = items.FieldType;
            var inline = arr.GetCustomAttribute<InlineArrayAttribute>()
                ?? throw new ArgumentException($"{comp.Name}.{predicate.ItemsPath} is not an [InlineArray].", nameof(predicate));
            FieldInfo[] elementFields = arr.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (elementFields.Length != 1) throw new ArgumentException($"{arr.Name} must hold exactly one element field.", nameof(predicate));
            Type elem = elementFields[0].FieldType;
            if (predicate.KeyPaths.Count == 0) throw new ArgumentException("KeyPaths must name at least one field.", nameof(predicate));

            var scanner = (RingEntryScanner)Activator.CreateInstance(
                typeof(RingEntryScanner<,,>).MakeGenericType(comp, arr, elem), predicate, inline.Length, projection)!;
            return scanner;
        }

        // ── key compilation: Func<TElem, long>, mixing each key field without boxing ──────────────────────────────────────────────────────

        internal static long Mix(long h, long v) => unchecked((h ^ v) * 1099511628211L + 1469598103934665603L);
        internal static long Bits(float f) => BitConverter.SingleToInt32Bits(f);
        internal static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
        internal static long Bits(Entity e) => unchecked((long)e.PackedValue);

        internal static Func<TElem, long> CompileKey<TElem>(IReadOnlyList<string> paths)
        {
            var e = Expression.Parameter(typeof(TElem), "e");
            Expression h = Expression.Constant(1469598103934665603L);
            foreach (string path in paths)
            {
                Expression access = e;
                foreach (string seg in path.Split('.'))
                    access = Expression.PropertyOrField(access, seg);
                Expression asLong = access.Type switch
                {
                    var t when t == typeof(float) => Expression.Call(typeof(RingEntryScanner).GetMethod(nameof(Bits), BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(float) })!, access),
                    var t when t == typeof(double) => Expression.Call(typeof(RingEntryScanner).GetMethod(nameof(Bits), BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(double) })!, access),
                    var t when t == typeof(Entity) => Expression.Call(typeof(RingEntryScanner).GetMethod(nameof(Bits), BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(Entity) })!, access),
                    var t when t == typeof(bool) => Expression.Condition(access, Expression.Constant(1L), Expression.Constant(0L)),
                    var t when t.IsEnum => Expression.Convert(Expression.Convert(access, Enum.GetUnderlyingType(t)), typeof(long)),
                    var t when t.IsPrimitive => Expression.Convert(access, typeof(long)),
                    var t => throw new ArgumentException($"Key field '{path}' is a {t.Name}; a key field must be a number, enum, bool or entity."),
                };
                h = Expression.Call(typeof(RingEntryScanner).GetMethod(nameof(Mix), BindingFlags.NonPublic | BindingFlags.Static)!, h, asLong);
            }
            return Expression.Lambda<Func<TElem, long>>(h, e).Compile();
        }
    }

    internal sealed class RingEntryScanner<TComp, TArr, TElem> : RingEntryScanner
        where TComp : unmanaged where TArr : struct where TElem : unmanaged
    {
        private readonly int _capacity;
        private readonly Func<TComp, TArr> _items;
        private readonly Func<TComp, int>? _count;
        private readonly Func<TElem, long> _key;
        private readonly Func<object, object?>? _filterGetter;
        private readonly Func<string, bool>? _filter;
        private readonly CompiledProjection? _projection;
        private readonly string _label;
        /// <summary>⭐ CE-3330 — what was seen of one entity's ring last frame: its key set, and an exact COPY of the entries it was built from
        /// (reused in place every changed frame — no allocation for an unchanged ring).</summary>
        private sealed class Seen
        {
            public int Generation;
            public HashSet<long> Keys = new();
            public TElem[] Copy = Array.Empty<TElem>();
            public int Count = -1;
        }
        private readonly Dictionary<int, Seen> _seen = new();
        private readonly HashSet<long> _now = new();
        private readonly List<int> _gone = new();
        private int _stamp;
        private readonly Dictionary<int, int> _lastFrameSeen = new();

        public RingEntryScanner(RingEntryPredicateDto p, int capacity, ResultProjectionDto? projection)
        {
            _capacity = capacity;
            var c = Expression.Parameter(typeof(TComp), "c");
            _items = Expression.Lambda<Func<TComp, TArr>>(Expression.Field(c, p.ItemsPath), c).Compile();
            if (!string.IsNullOrWhiteSpace(p.CountPath))
                _count = Expression.Lambda<Func<TComp, int>>(Expression.Convert(Expression.PropertyOrField(c, p.CountPath!), typeof(int)), c).Compile();
            _key = CompileKey<TElem>(p.KeyPaths);
            if (!string.IsNullOrWhiteSpace(p.FilterPath))
            {
                _filterGetter = PropertyEvaluator.BuildGetter(typeof(TElem), p.FilterPath!);
                _filter = EventScannerCompiler.CompileStringMatch(p.FilterOperator, p.FilterValue ?? string.Empty);
            }
            if (projection != null) _projection = new CompiledProjection(projection, typeof(TElem));
            _label = typeof(TComp).Name;
        }

        public override void Scan(EntityRepository repo, int frame, long ticks, List<SearchResultDto> results, TargetEntityFilter? entityFilter = null)
        {
            if (!repo.IsComponentTypeRegistered<TComp>()) return;
            _stamp++;
            foreach (var entity in repo.Query().With<TComp>().Build())
            {
                if (entityFilter != null && !entityFilter.Passes(repo, entity)) continue;
                TComp comp = repo.GetComponentRO<TComp>(entity);
                TArr arr = _items(comp);
                int n = Math.Clamp(_count?.Invoke(comp) ?? _capacity, 0, _capacity);
                ReadOnlySpan<TElem> entries = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TArr, TElem>(ref arr), _capacity)[..n];

                bool baseline = !_seen.TryGetValue(entity.Index, out var was) || was.Generation != entity.Generation;
                if (!baseline)
                {
                    // ⭐ CE-3330 — O(changes): a ring byte-identical to last frame's (same count, same entries — a memcmp, never a hash, so a
                    //   collision cannot drop an event) has the same key set and so reports nothing; skip the hashing and the union.
                    if (was!.Count == n && MemoryMarshal.AsBytes(entries).SequenceEqual(MemoryMarshal.AsBytes(was.Copy.AsSpan(0, n))))
                    {
                        _lastFrameSeen[entity.Index] = _stamp;
                        continue;
                    }
                }
                _now.Clear();
                for (int i = 0; i < entries.Length; i++)
                {
                    long key = _key(entries[i]);
                    if (!_now.Add(key) || baseline || was!.Keys.Contains(key)) continue;
                    if (_filter != null)
                    {
                        object boxed = entries[i];
                        if (!_filter(Convert.ToString(_filterGetter!(boxed), System.Globalization.CultureInfo.InvariantCulture) ?? "null")) continue;
                    }
                    if (_projection != null)
                    {
                        var payload = _projection.Apply(repo, entries[i], entity, out var subject);
                        results.Add(new SearchResultDto(frame, ticks, subject, _label, payload));
                    }
                    else results.Add(new SearchResultDto(frame, ticks, entity, _label));
                }
                if (baseline) { was = new Seen(); _seen[entity.Index] = was; }
                was!.Generation = entity.Generation;
                was.Keys.Clear();
                was.Keys.UnionWith(_now);
                if (was.Copy.Length < _capacity) was.Copy = new TElem[_capacity];
                entries.CopyTo(was.Copy);
                was.Count = n;
                _lastFrameSeen[entity.Index] = _stamp;
            }
            // an entity whose component went away is forgotten: if it comes back, that frame is its baseline again
            _gone.Clear();
            foreach (var (index, stamp) in _lastFrameSeen) if (stamp != _stamp) _gone.Add(index);
            foreach (int index in _gone) { _seen.Remove(index); _lastFrameSeen.Remove(index); }
        }
    }
}
