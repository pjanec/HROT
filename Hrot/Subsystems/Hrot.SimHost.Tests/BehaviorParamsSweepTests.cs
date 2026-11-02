using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fdp.Testing;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ <c>CE-2377</c> step 1 — the SWEEP: every <c>Behavior</c> and <c>Sop</c> slot of every checked-in <c>scenario.json</c>
/// (<c>scenarios/</c>, <c>recipes/</c>, <c>macros/</c>) through the ONE side-effect-free <see cref="BehaviorParamsValidation.Validate"/>
/// against the PRODUCTION registry. The full findings list is written to the test output; the count is asserted against
/// <see cref="BehaviorParamsSweepAllowList"/>, which may only shrink. The scenario-load refusal is step 2, switched on when this is clean.
/// <para>⚠ Not swept: mission-plan task params (a different carrier; the plan's own leg DTOs), and <c>test-data/scenario-corpus</c> (frozen old versions).</para>
/// </summary>
public sealed class BehaviorParamsSweepTests
{
    private readonly ITestOutputHelper _out;
    public BehaviorParamsSweepTests(ITestOutputHelper output) { _out = output; }

    private sealed record Finding(string Group, string Scenario, string Slot, string Entity, string Behaviour, string Problem);

    private static BehaviorRegistry LoadProduction()
    {
        var registry = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(registry);
        return registry;
    }

    /// <summary>The problem with its position suffix ("Path: … | LineNumber: …") removed, so one cause groups across scenarios.</summary>
    private static string Normalise(string problem)
        => Regex.Replace(problem, @"\s*Path: \S+( \| LineNumber: \d+)?( \| BytePositionInLine: \d+)?\.?", "").Trim();

    /// <summary>(behaviour, how its non-empty params were checked) → slots; informational, written to the output.</summary>
    private static readonly Dictionary<(string, string), int> CoverageTally = new();

    private static List<Finding> Sweep(BehaviorRegistry registry, out int slotsChecked)
    {
        string root = TestRepo.Root;
        var findings = new List<Finding>();
        CoverageTally.Clear();
        int checkedSlots = 0;
        foreach (var top in new[] { "scenarios", "recipes", "macros" })
        {
            string dir = Path.Combine(root, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in RepoSources.Files(dir, "scenario.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace('\\', '/');
                JsonNode? doc;
                try { doc = JsonNode.Parse(File.ReadAllText(file)); } catch (Exception) { continue; }
                foreach (var (slot, name, p, entity) in ScenarioBehaviorSlots.Enumerate(doc))
                {
                    checkedSlots++;
                    if (!string.IsNullOrWhiteSpace(p) && p != "{}")
                    {
                        string how = !registry.TryGetId(name, out var id) || !registry.TryGetDefinition(id, out var def) ? "unknown behaviour"
                                   : def.JsonParamsDtoType == null ? "NO params DTO (key check skipped)"
                                   : registry.TryGetValidator(name, out _) ? "strict DTO + validator" : "strict DTO";
                        CoverageTally[(name, how)] = CoverageTally.GetValueOrDefault((name, how)) + 1;
                    }
                    foreach (var problem in BehaviorParamsValidation.Validate(registry, name, p))
                        findings.Add(new Finding($"{name} :: {Normalise(problem)}", rel, slot, entity, name, problem));
                }
            }
        }
        slotsChecked = checkedSlots;
        return findings;
    }

    [Fact]
    public void CE2377_EveryScenarioBehaviourSlot_ValidatesExceptTheShrinkingAllowList()
    {
        var registry = LoadProduction();
        Assert.True(registry.GetRegisteredNames().Count > 20, "the production loader registered almost nothing — this sweep would pass vacuously");

        var findings = Sweep(registry, out int slots);
        Assert.True(slots > 100, $"the sweep saw only {slots} slots — it is not reading the scenarios");

        // the full findings list, grouped — the deliverable
        var observed = findings.GroupBy(f => f.Group).ToDictionary(g => g.Key, g => g.Select(f => f.Scenario).Distinct().Count());
        _out.WriteLine($"slots checked: {slots}; findings: {findings.Count}; groups: {observed.Count}");
        foreach (var g in findings.GroupBy(f => f.Group).OrderByDescending(g => g.Select(f => f.Scenario).Distinct().Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var scenarios = g.Select(f => f.Scenario).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            _out.WriteLine($"[{scenarios.Count} scenario(s), {g.Count()} slot(s)] {g.Key}");
            foreach (var s in scenarios) _out.WriteLine("    " + s);
        }

        _out.WriteLine("coverage (non-empty params slots by how they were checked):");
        foreach (var (k, n) in CoverageTally.OrderBy(kv => kv.Key.Item2, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item1, StringComparer.Ordinal))
            _out.WriteLine($"  {n,4}  {k.Item2,-36} {k.Item1}");

        var allowed = BehaviorParamsSweepAllowList.Groups;
        var worse = observed.Where(kv => !allowed.TryGetValue(kv.Key, out var n) || kv.Value > n)
                            .Select(kv => $"  {kv.Key}: {kv.Value} scenario(s) (allowed {(allowed.TryGetValue(kv.Key, out var n) ? n : 0)})").ToArray();
        Assert.True(worse.Length == 0, "scenario behaviour params break the contract — fix the scenario; never add to the allow-list:\n" + string.Join("\n", worse));

        var stale = allowed.Where(kv => !observed.TryGetValue(kv.Key, out var n) || n < kv.Value)
                           .Select(kv => $"  {kv.Key}: allowed {kv.Value}, now {(observed.TryGetValue(kv.Key, out var n) ? n : 0)}").ToArray();
        Assert.True(stale.Length == 0, "these allow-listed groups shrank or are gone — lower / remove them in BehaviorParamsSweepAllowList:\n" + string.Join("\n", stale));
    }
}
