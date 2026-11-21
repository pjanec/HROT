using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ EXPERIMENT — POC 3 of the threaded editor (CE-4442, C-1; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §14): walks a CONSTRUCTED object
    /// graph and reports every path from a root to a given object (a world). The forwarding-rail pattern applied to worlds: "no node system holds A,
    /// no presentation object holds S" is checked on the objects the host actually built, not on its source.
    /// <para>Follows instance fields (every visibility, base types included), arrays, <see cref="IEnumerable"/> collections (dictionary values
    /// included) and delegates (their target — a closure's captured variables are its fields). Does not descend into an <see cref="EntityRepository"/>
    /// (a world is a leaf), strings, <see cref="Type"/>, reflection objects or value-type primitives. Bounded by depth and by a node budget.</para>
    /// ⛔ An instrument: it allocates, it uses reflection, it runs once at start-up behind the experiment's switch.
    /// </summary>
    public static class WorldReferenceWalker
    {
        public sealed record Hit(string Path, string HolderType, string Field);

        public static List<Hit> Find(object root, string rootName, object target, int maxDepth = 10, int maxNodes = 2_000_000, object[]? stopAt = null)
        {
            var hits = new List<Hit>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (stopAt != null) foreach (var o in stopAt) if (o != null) seen.Add(o);   // the composition root holds both worlds by design: a leaf
            var stack = new Stack<(object Obj, string Path, int Depth)>();
            stack.Push((root, rootName, 0));
            int nodes = 0;
            while (stack.Count > 0 && nodes < maxNodes)
            {
                var (obj, path, depth) = stack.Pop();
                if (!seen.Add(obj)) continue;
                nodes++;
                if (obj is EntityRepository) continue;   // a world is a leaf
                if (depth >= maxDepth) continue;
                var t = obj.GetType();
                if (Skip(t)) continue;

                if (obj is Delegate d)
                {
                    foreach (var inner in d.GetInvocationList())
                        if (inner.Target is { } target2) Visit(target2, path + "→" + inner.Method.Name + "()", depth + 1, "(closure)", t);
                    continue;
                }
                if (obj is Array arr)
                {
                    if (arr.Rank != 1 || Skip(t.GetElementType()!)) continue;
                    int i = 0;
                    foreach (var e in arr) { if (e != null) Visit(e, path + "[" + i + "]", depth + 1, "[]", t); if (++i > 4096) break; }
                    continue;
                }
                if (obj is IDictionary dict)
                {
                    int i = 0;
                    foreach (DictionaryEntry e in dict) { if (e.Value != null) Visit(e.Value, path + "[" + Short(e.Key) + "]", depth + 1, "[value]", t); if (++i > 4096) break; }
                    continue;
                }
                if (obj is ICollection coll && obj is not IDictionary && IsBclCollection(t))
                {
                    int i = 0;
                    foreach (var e in coll) { if (e != null) Visit(e, path + "[" + i + "]", depth + 1, "[]", t); if (++i > 4096) break; }
                    continue;
                }
                for (var ty = t; ty != null && ty != typeof(object); ty = ty.BaseType)
                {
                    foreach (var f in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType.IsPointer || f.FieldType.IsByRefLike) continue;
                        object? v;
                        try { v = f.GetValue(obj); } catch { continue; }
                        if (v == null) continue;
                        Visit(v, path + "." + Clean(f.Name), depth + 1, Clean(f.Name), ty);
                    }
                }
            }
            return hits;

            void Visit(object v, string p, int dep, string field, Type holder)
            {
                if (ReferenceEquals(v, target)) { hits.Add(new Hit(p, Name(holder), field)); return; }
                if (v.GetType().IsValueType && !v.GetType().IsPrimitive && v is not IEnumerable && !HasRefFields(v.GetType())) return;
                stack.Push((v, p, dep));
            }
        }

        /// <summary>A short report: the distinct (holder type · field) pairs, each with one example path.</summary>
        public static string Report(string title, List<Hit> hits, int maxLines = 200)
        {
            var sb = new StringBuilder();
            var byHolder = new Dictionary<string, (int Count, string Example)>();
            foreach (var h in hits)
            {
                string k = h.HolderType + " · " + h.Field;
                byHolder[k] = byHolder.TryGetValue(k, out var x) ? (x.Count + 1, x.Example) : (1, h.Path);
            }
            sb.Append(title).Append(": ").Append(hits.Count).Append(" paths, ").Append(byHolder.Count).Append(" distinct holders\n");
            int n = 0;
            foreach (var kv in byHolder)
            {
                sb.Append("  ").Append(kv.Key).Append("  ×").Append(kv.Value.Count).Append("  e.g. ").Append(kv.Value.Example).Append('\n');
                if (++n >= maxLines) { sb.Append("  …\n"); break; }
            }
            return sb.ToString();
        }

        private static readonly Dictionary<Type, bool> s_refFields = new();
        private static bool HasRefFields(Type t)
        {
            lock (s_refFields)
            {
                if (s_refFields.TryGetValue(t, out var b)) return b;
                b = false;
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!f.FieldType.IsPrimitive && !f.FieldType.IsEnum && !f.FieldType.IsPointer) { b = true; break; }
                s_refFields[t] = b;
                return b;
            }
        }

        private static bool Skip(Type t) =>
            t.IsPrimitive || t.IsEnum || t.IsPointer || t == typeof(string) || typeof(Type).IsAssignableFrom(t) || typeof(MemberInfo).IsAssignableFrom(t)
            || typeof(Assembly).IsAssignableFrom(t) || t.Namespace?.StartsWith("System.Reflection", StringComparison.Ordinal) == true
            || t.Namespace?.StartsWith("System.Threading", StringComparison.Ordinal) == true
            || t.Namespace?.StartsWith("System.Runtime", StringComparison.Ordinal) == true
            || t.FullName?.StartsWith("System.Text", StringComparison.Ordinal) == true;

        private static bool IsBclCollection(Type t) => t.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true;

        private static string Clean(string field) => field.StartsWith("<", StringComparison.Ordinal) && field.Contains(">k__BackingField") ? field[1..field.IndexOf('>')] : field;
        private static string Short(object? key) { var s = key?.ToString() ?? "null"; return s.Length > 24 ? s[..24] : s; }
        private static string Name(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(",", Array.ConvertAll(t.GetGenericArguments(), a => a.Name)) + ">" : t.Name;
    }
}
