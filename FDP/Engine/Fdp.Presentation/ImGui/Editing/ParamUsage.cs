using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// ⭐⭐ CE-4210 P2 (MA-5) — every USE of a parameter name in an edit world, across the THREE binding homes: a reference leaf's Param form (<c>"^name"</c>, any depth —
    /// the S0 walker), a <see cref="BindableNode.Bind"/> entry (any depth), an entity's <see cref="ParamBindings"/> entry. ONE scan counts, lists and renames, so the count
    /// the Variables table shows and the rename can never disagree. ⛔ Not a second binder (SR-164): it finds names, it applies nothing.
    /// </summary>
    public static class ParamUsage
    {
        /// <summary>The walker's applier: counts the uses of <c>From</c>; when <c>To</c> is set, rewrites each to <c>To</c>.</summary>
        private sealed class Scan : LeafApplier
        {
            public readonly string From; public readonly string? To; public int Hits;
            public Scan(string from, string? to) { From = from; To = to; }
            private bool Mine(in RefSpec s) => s.Form == RefForm.Param && string.Equals(s.ParamName, From, StringComparison.Ordinal);
            public override EntityLink Link(EntityLink l) { if (!Mine(l.Spec)) return l; Hits++; return To is null ? l : l.BindToParam(To); }
            public override PointLink PointLink(PointLink p) { if (!Mine(p.Spec)) return p; Hits++; return To is null ? p : p.BindToParam(To); }
            public override AreaLink Area(AreaLink a) { if (!Mine(a.Spec)) return a; Hits++; return To is null ? a : a.BindToParam(To); }
            public override bool Node(BindableNode node)
            {
                if (node.Bind is not { Count: > 0 } map) return false;
                bool changed = false;
                foreach (var key in map.Where(kv => string.Equals(kv.Value, From, StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
                {
                    Hits++;
                    if (To is null) continue;
                    map[key] = To; changed = true;
                }
                return changed;
            }
        }

        /// <summary>Counts EVERY param name used, in one pass.</summary>
        private sealed class CountAllScan : LeafApplier
        {
            public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
            private void Hit(string n) { Counts.TryGetValue(n, out var c); Counts[n] = c + 1; }
            public override EntityLink Link(EntityLink l) { if (l.Spec.Form == RefForm.Param) Hit(l.Spec.ParamName); return l; }
            public override PointLink PointLink(PointLink p) { if (p.Spec.Form == RefForm.Param) Hit(p.Spec.ParamName); return p; }
            public override AreaLink Area(AreaLink a) { if (a.Spec.Form == RefForm.Param) Hit(a.Spec.ParamName); return a; }
            public override bool Node(BindableNode node) { if (node.Bind is { } m) foreach (var v in m.Values) Hit(v); return false; }
        }

        /// <summary>How often each param name is used in the world (all three homes) — the Variables table's reference count.</summary>
        public static IReadOnlyDictionary<string, int> CountAll(IInspectableSession s)
        {
            var scan = new CountAllScan();
            foreach (var (e, t) in Components(s))
            {
                var value = s.GetComponent(e, t);
                if (value is ParamBindings pb) { foreach (var p in pb.Bindings.Values) { scan.Counts.TryGetValue(p, out var c); scan.Counts[p] = c + 1; } continue; }
                EntityRefRemap.WalkObject(value, scan);
            }
            return scan.Counts;
        }

        private static bool CanHold(Type t) => t == typeof(ParamBindings) || typeof(BindableNode).IsAssignableFrom(t) || EntityRefRemap.HoldsLeaves(t);

        private static IEnumerable<(Entity E, Type T)> Components(IInspectableSession s)
        {
            var types = s.GetAllComponentTypes().Where(CanHold).ToList();
            foreach (var e in s.GetEntities().ToList())
                foreach (var t in types)
                    if (s.HasComponent(e, t)) yield return (e, t);
        }

        /// <summary>The number of uses of <paramref name="name"/> in the world, and a short line for each (<c>"EntityInfo.ForceId on [3]"</c>), the first <paramref name="max"/> of them.</summary>
        public static (int Count, IReadOnlyList<string> Where) Find(IInspectableSession s, string name, int max = 8)
        {
            var where = new List<string>(); int count = 0;
            foreach (var (e, t) in Components(s))
            {
                var value = s.GetComponent(e, t);
                if (value is ParamBindings pb)
                {
                    foreach (var (path, p) in pb.Bindings)
                        if (string.Equals(p, name, StringComparison.Ordinal)) { count++; if (where.Count < max) where.Add($"{path} on [{e.Index}]"); }
                    continue;
                }
                var scan = new Scan(name, null);
                EntityRefRemap.WalkObject(Clone(value), scan);
                if (scan.Hits > 0) { count += scan.Hits; if (where.Count < max) where.Add($"{t.Name} on [{e.Index}]" + (scan.Hits > 1 ? $" ×{scan.Hits}" : "")); }
            }
            return (count, where);
        }

        /// <summary>Rewrites every use of <paramref name="from"/> to <paramref name="to"/> in the world; returns the number of components written. Symmetric — <c>Rename(to, from)</c> undoes it.</summary>
        public static int Rename(IInspectableSession s, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal)) return 0;
            int written = 0;
            foreach (var (e, t) in Components(s).ToList())
            {
                var value = s.GetComponent(e, t);
                if (value is ParamBindings pb)
                {
                    if (!pb.Bindings.Values.Contains(from, StringComparer.Ordinal)) continue;
                    var next = pb.Bindings.ToDictionary(kv => kv.Key, kv => string.Equals(kv.Value, from, StringComparison.Ordinal) ? to : kv.Value, StringComparer.Ordinal);
                    s.SetComponent(e, t, new ParamBindings { Bindings = next });
                    written++;
                    continue;
                }
                var scan = new Scan(from, to);
                var copy = Clone(value);
                EntityRefRemap.WalkObject(copy, scan);
                if (scan.Hits == 0) continue;
                s.SetComponent(e, t, copy!);
                written++;
            }
            return written;
        }

        // Structs come back boxed (a copy). A managed component is the stored reference — a BindableNode tree is mutable DTO state by design (SR-160), so rewriting it in place IS the edit; SetComponent then re-stores it.
        private static object? Clone(object? v) => v;
    }
}
