using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// ⭐⭐ CE-4628 follow-up (user, 2026-11-19: <i>"Shouldn't all component ids have a symbolic constant? Apart from test components. And all written at one single
    /// place?"</i> — "yes go ahead") — every PRODUCTION component's id is a constant of ONE catalogue, <see cref="GlobalComponentIds"/>:
    /// ① every production <c>[ComponentId(…)]</c> names a <c>GlobalComponentIds</c> constant (never a literal, never another class — the old side catalogues
    /// <c>HrotComponentIds</c>, <c>NavigationContractsComponentIds</c>, … are ALIASES of it now); ② no id is used by two production component types;
    /// ③ the free production ids are PRINTED (run with <c>--logger "console;verbosity=detailed"</c>) — the one answer to "is the space full?", never a hand count.
    /// Test, benchmark and example projects are exempt (a test struct may sit on a production-free id while its suite never registers the production type).
    /// 🔴 Red-proof: put a literal back (<c>[ComponentId(230)]</c> on <c>Velocity</c>) ⇒ ① names the file.
    /// </summary>
    public sealed class ComponentIdCatalogueTests
    {
        private readonly ITestOutputHelper _out;
        public ComponentIdCatalogueTests(ITestOutputHelper output) => _out = output;

        // the attribute however written: [ComponentId(…)], [Fdp.Core.ComponentId(…)], [ComponentIdAttribute(…)], or beside another in one bracket
        private static readonly Regex Attribute = new(@"^\s*\[[^\]]*?(?:Fdp\.Core\.)?ComponentId(?:Attribute)?\(\s*(?<arg>[^)]*?)\s*\)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex Named = new(@"^(Fdp\.Core\.)?GlobalComponentIds\.(?<name>\w+)$", RegexOptions.CultureInvariant);
        private static readonly Regex Exempt = new(@"(/[^/]*Tests?[^/]*/|Tests\.cs$|/[^/]*Bench[^/]*/|/Examples/|/TestShared/|/obj/|/bin/|/\.git/|/\.claude/)", RegexOptions.CultureInvariant);   // ⭐ ui (CE-4628 merge): /.claude/ holds agents' git WORKTREES — whole second copies of the tree; read as production they doubled every id

        private static string Root()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "HROT.sln"))) return d.FullName;
            throw new InvalidOperationException("the source tree (HROT.sln) was not found above " + AppContext.BaseDirectory);
        }

        private static Dictionary<string, int> Catalogue() => typeof(GlobalComponentIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);

        private static List<(string File, int Line, string Arg)> ProductionAttributes()
        {
            string root = Root();
            var found = new List<(string, int, string)>();
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = "/" + Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Exempt.IsMatch(rel)) continue;
                string text = File.ReadAllText(path);
                if (!text.Contains("ComponentId", StringComparison.Ordinal)) continue;   // the cheap filter: any spelling ([Fdp.Core.ComponentId(…)] too)
                foreach (Match m in Attribute.Matches(text))
                    found.Add((rel, text.Take(m.Index).Count(c => c == '\n') + 1, m.Groups["arg"].Value));
            }
            return found;
        }

        [Fact]
        public void EveryProductionComponentId_IsAGlobalComponentIdsConstant()
        {
            var catalogue = Catalogue();
            var bad = ProductionAttributes()
                .Where(a => Named.Match(a.Arg) is not { Success: true } m || !catalogue.ContainsKey(m.Groups["name"].Value))
                .Select(a => $"{a.File}:{a.Line} [ComponentId({a.Arg})]").ToList();
            Assert.True(bad.Count == 0, "a production component id outside GlobalComponentIds (add the constant THERE):\n  " + string.Join("\n  ", bad));
        }

        [Fact]
        public void NoIdIsUsedByTwoProductionComponents_AndTheFreeIdsArePrinted()
        {
            var catalogue = Catalogue();
            var used = new Dictionary<int, List<string>>();
            foreach (var a in ProductionAttributes())
                if (Named.Match(a.Arg) is { Success: true } m && catalogue.TryGetValue(m.Groups["name"].Value, out int id))
                    (used.TryGetValue(id, out var list) ? list : used[id] = new List<string>()).Add($"{a.File}:{a.Line}");
            var twice = used.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key}: {string.Join(" · ", kv.Value)}").ToList();
            var named = new HashSet<int>(catalogue.Values);
            var free = Enumerable.Range(0, 496).Where(i => !used.ContainsKey(i) && !named.Contains(i)).ToList();
            var parked = Enumerable.Range(0, 496).Where(i => !used.ContainsKey(i) && named.Contains(i)).ToList();
            _out.WriteLine($"production component ids: {used.Count} used · {free.Count} FREE in 0-495 (no component, no name) · {parked.Count} named but unused in 0-495 (retired or reserved — reuse only deliberately) · 496-511 held back (the 512-bit mask; 500-511 the test-only range)");
            _out.WriteLine("FREE: " + string.Join(" ", free));
            _out.WriteLine("named but unused: " + string.Join(" ", parked.Select(i => $"{i}({string.Join("/", catalogue.Where(kv => kv.Value == i).Select(kv => kv.Key))})")));
            Assert.True(twice.Count == 0, "an id used by two production components:\n  " + string.Join("\n  ", twice));
        }
    }
}
