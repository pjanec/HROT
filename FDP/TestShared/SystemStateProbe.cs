using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐⭐ <c>R-3039</c> S4 — every field a system, module or network translator can carry between frames, classified. A checkpoint, a
    /// recording and the dry run copy the WORLD and nothing else, so state held in such a field is lost at a restore unless it is derived
    /// (A), protocol re-derived at the restore (B), or not state at all (K). Class C (simulation memory) and X (state in an engine outside
    /// the ECS) are allowed only as listed work items. The classification is the table in
    /// <c>docs/DESIGN_System_State_Inventory.md</c>, read by this probe — one source for the reader and the rail.
    /// <para>What counts as a field that can carry state: any collection or array; a mutable value-type or string field; a reference to a
    /// concrete class (classified once per TYPE, as <c>type:Name</c>). Interfaces, delegates, constants and readonly scalars are dependencies or
    /// configuration and are skipped.</para>
    /// </summary>
    public static class SystemStateProbe
    {
        public sealed record Hit(string Family, string Owner, string Key, string Kind, string TypeKey);

        public sealed record Row(string Key, string Class, string Reason);

        public sealed record Result(int Owners, IReadOnlyList<Hit> Hits, IReadOnlyList<Hit> Unclassified, IReadOnlyList<string> UnusedRows,
                                    IReadOnlyList<(Hit Hit, Row Row)> Pending);

        private static readonly string[] Families = { "system", "module", "translator" };

        /// <summary>Every hit in the production assemblies beside the test.</summary>
        public static IReadOnlyList<Hit> Walk(out int owners)
        {
            var hits = new List<Hit>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            owners = 0;
            foreach (var asm in ProductionAssemblies())
                foreach (var type in LoadableTypes(asm))
                {
                    if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) continue;
                    string? family = FamilyOf(type);
                    if (family == null) continue;
                    owners++;
                    for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            string? kind = KindOf(f);
                            if (kind == null) continue;
                            string key = Qualified(f.DeclaringType!) + "." + f.Name;
                            if (!seen.Add(key)) continue;   // a base-class field once, not per subclass
                            hits.Add(new Hit(family, Qualified(type), key, kind, kind == "object" ? "type:" + Pretty(f.FieldType) : ""));
                        }
                }
            return hits;
        }

        /// <summary>Matches every hit against the inventory rows.</summary>
        public static Result Check(IReadOnlyList<Row> rows)
        {
            var hits = Walk(out int owners);
            var exact = rows.Where(r => !r.Key.Contains('*')).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var globs = rows.Where(r => r.Key.Contains('*')).Select(r => (Row: r, Re: Glob(r.Key))).ToList();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var unclassified = new List<Hit>();
            var pending = new List<(Hit, Row)>();
            foreach (var h in hits)
            {
                Row? row = Match(h.Key, exact, globs, used);
                if (row == null && h.Kind == "object") row = Match(h.TypeKey, exact, globs, used);
                if (row == null) { unclassified.Add(h); continue; }
                if (row.Class is "C" or "X") pending.Add((h, row));
            }
            var unused = rows.Select(r => r.Key).Where(k => !used.Contains(k)).Distinct().ToList();
            return new Result(owners, hits, unclassified, unused, pending);
        }

        private static Row? Match(string key, Dictionary<string, Row> exact, List<(Row Row, Regex Re)> globs, HashSet<string> used)
        {
            if (exact.TryGetValue(key, out var r)) { used.Add(r.Key); return r; }
            foreach (var g in globs)
                if (g.Re.IsMatch(key)) { used.Add(g.Row.Key); return g.Row; }
            return null;
        }

        private static Regex Glob(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);

        /// <summary>Reads the inventory tables: every markdown row whose first cell is a back-quoted key and second cell a class letter.</summary>
        public static IReadOnlyList<Row> ReadInventory(string markdownPath)
        {
            var rows = new List<Row>();
            var cell = new Regex(@"^\|\s*`(?<key>[^`]+)`\s*\|\s*\*{0,2}(?<cls>[KABCSX])\*{0,2}\s*\|\s*(?<reason>[^|]*)\|", RegexOptions.CultureInvariant);
            foreach (var line in File.ReadLines(markdownPath))
            {
                var m = cell.Match(line);
                if (m.Success) rows.Add(new Row(m.Groups["key"].Value.Trim(), m.Groups["cls"].Value, m.Groups["reason"].Value.Trim()));
            }
            return rows;
        }

        /// <summary>The repository root (the directory holding <c>HROT.sln</c>), walking up from the test binaries.</summary>
        public static string RepoRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "HROT.sln"))) return d.FullName;
            throw new InvalidOperationException("HROT.sln not found above " + AppContext.BaseDirectory);
        }

        private static string? FamilyOf(Type type)
        {
            if (typeof(IEcsModuleSystem).IsAssignableFrom(type)) return "system";
            if (typeof(IEcsModule).IsAssignableFrom(type)) return "module";
            if (type.GetInterfaces().Any(i => i.FullName == "Fdp.Interfaces.INetworkTranslator")) return "translator";
            return null;
        }

        private static string? KindOf(FieldInfo f)
        {
            var t = f.FieldType;
            if (f.IsLiteral || typeof(Delegate).IsAssignableFrom(t)) return null;
            if (t == typeof(string)) return f.IsInitOnly ? null : "scalar";
            if (t.IsArray || typeof(IEnumerable).IsAssignableFrom(t)) return "collection";
            if (t.IsValueType) return f.IsInitOnly ? null : "scalar";
            if (t.IsInterface) return null;
            if (t.FullName is "Fdp.Core.EntityQuery" or "Fdp.Core.EntityRepository") return null;   // the world itself, and a view of it
            return "object";
        }

        /// <summary>A type's namespace-qualified name, nested types joined by '+', generic arguments by short name.</summary>
        public static string Qualified(Type t) =>
            t.DeclaringType != null ? Qualified(t.DeclaringType) + "+" + Pretty(t) : (t.Namespace is { } ns ? ns + "." : "") + Pretty(t);

        private static string Pretty(Type t) =>
            !t.IsGenericType ? t.Name : t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Pretty)) + ">";

        private static IEnumerable<Assembly> ProductionAssemblies()
        {
            foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                bool ours = name.StartsWith("Fdp.", StringComparison.Ordinal) || name.StartsWith("Hrot.", StringComparison.Ordinal)
                            || name.StartsWith("HrotStrideApp.", StringComparison.Ordinal);
                if (!ours || name.Contains("Test", StringComparison.Ordinal) || name.Contains("Example", StringComparison.Ordinal)) continue;
                Assembly? asm = null;
                try { asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name) ?? Assembly.LoadFrom(path); }
                catch (BadImageFormatException) { }
                if (asm != null) yield return asm;
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
        }
    }
}
