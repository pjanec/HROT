using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.Testing;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐⭐⭐ CE-4586 (docs/DESIGN_Debug_Api_Http_Only.md §6) — <b>the environment variables have ONE table, and it is complete.</b> 📌 Before it, 141
/// <c>HROT_*</c> + 12 <c>FDP_*</c> names were read raw at 96 production sites and listed nowhere, so an agent could not learn which exist — and the
/// performance switches can only be chosen at process start. ⭐ This rail is the completeness guarantee: <see cref="EnvVars"/> renders <c>/help</c>
/// and <c>/performance</c>, and it can only do that truthfully while (a) no production code reads an environment variable around it and (b) every
/// <c>HROT_*</c> / <c>FDP_*</c> name anywhere in the sources (tests and scripts' C# included) is declared in it.
/// <para>⚠ A SOURCE scan (like <c>EveryRouteReadsItsParamsWhereDerivedTests</c>): it sees every file without running anything. The allow-list is the
/// projects that cannot reference <c>Fdp.Core</c> (vendored libraries, netstandard generators / analyzers, tools outside the product) — they may read
/// raw, but their names are still declared, so <c>/help</c> lists them.</para>
/// </summary>
public sealed class EveryEnvReadIsRegisteredTests
{
    private static readonly string[] Roots = { "FDP", "Hrot", "Stride" };

    /// <summary>Path fragments (repo-relative, '/'-separated) whose code cannot reference <c>Fdp.Core</c> — raw reads are allowed there.</summary>
    internal static readonly string[] RawReadAllowed =
    {
        "FDP/ExtDeps/",          // vendored libraries (GizmoMap, SumoSharp, FastHSM …) — no Fdp.Core reference
        "Hrot/Engine/Hrot.Terrain.InputSet/",   // "Depends on nothing in HROT" (its csproj header: no ProjectReference at all) — the terrain-import format library shared with the tools
        "Hrot/Tools/",           // tools outside the product (design §6)
        "Generators/", "Analyzers/", "SourceGen/", ".Generators/", ".Analyzers/", ".SourceGen/",   // netstandard2.0 Roslyn components
    };

    /// <summary>Variables that are not ours (the OS / third-party tools set them) — a raw read of one is not a product knob and is not declared.</summary>
    private static readonly string[] ExternalNames = { "\"PATH\"", "\"DISPLAY\"", "\"SUMO_HOME\"", "\"CYCLONEDDS_URI\"" };

    private static readonly Regex RawRead = new(@"Environment\.GetEnvironmentVariable\(", RegexOptions.Compiled);
    private static readonly Regex Name = new(@"""((?:HROT|FDP|STRIDE)_[A-Z0-9_]+)""", RegexOptions.Compiled);

    private static IEnumerable<(string Rel, string Text)> Sources()
    {
        foreach (var root in Roots)
        {
            var dir = Path.Combine(TestRepo.Root, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                yield return (rel, File.ReadAllText(f));
            }
        }
    }

    internal static bool IsTest(string rel) => rel.Contains("Tests/") || rel.Contains(".Tests.") || rel.Contains("/TestShared/") || rel.Contains("Test.cs") || rel.Contains("Tests.cs");
    internal static bool AllowedRaw(string rel) => Array.Exists(RawReadAllowed, a => rel.Contains(a, StringComparison.Ordinal));

    /// <summary>🔴 No production code reads an environment variable except through <see cref="EnvVars"/> (bar the allow-list).</summary>
    [Fact]
    public void NoProductionCodeReadsAnEnvironmentVariable_ExceptThroughEnvVars()
    {
        var raw = Sources()
            .Where(s => !IsTest(s.Rel) && !AllowedRaw(s.Rel) && !s.Rel.EndsWith("Fdp.Core/EnvVars.cs", StringComparison.Ordinal))
            .SelectMany(s => s.Text.Split('\n').Select((line, i) => (s.Rel, Line: i + 1, Text: line)))
            .Where(l => RawRead.IsMatch(l.Text) && !Array.Exists(ExternalNames, e => l.Text.Contains(e, StringComparison.Ordinal)) && !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.Text.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Select(l => $"{l.Rel}:{l.Line}: {l.Text.Trim()}")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.True(raw.Length == 0,
            $"{raw.Length} raw environment read(s) outside Fdp.Core.EnvVars — declare the variable there and read it with EnvVars.Read / Int / IsSet "
          + "(DESIGN_Debug_Api_Http_Only.md §6):\n  " + string.Join("\n  ", raw));
    }

    /// <summary>🔴 Every <c>HROT_*</c> / <c>FDP_*</c> name in the sources (tests included) is declared — so <c>/help</c> lists every one.</summary>
    [Fact]
    public void EveryHrotAndFdpNameInTheSources_IsDeclared()
    {
        var declared = EnvVars.All.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var missing = Sources()
            .SelectMany(s => Name.Matches(s.Text).Select(m => (m.Groups[1].Value, s.Rel)))
            .Where(x => !declared.Contains(x.Item1))
            .GroupBy(x => x.Item1)
            .Select(g => $"{g.Key}  (e.g. {g.First().Rel})")
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            $"{missing.Length} environment variable name(s) used but not declared in Fdp.Core.EnvVars:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>⭐ Each declaration is usable: unique name, a purpose, a type, and its design section (what /help renders).</summary>
    [Fact]
    public void EveryDeclaration_IsUnique_AndSaysWhatItIsFor()
    {
        var dupes = EnvVars.All.GroupBy(v => v.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.Empty(dupes);
        var blank = EnvVars.All.Where(v => string.IsNullOrWhiteSpace(v.Purpose) || string.IsNullOrWhiteSpace(v.Type)
                                         || !Regex.IsMatch(v.Name, "^(HROT|FDP|STRIDE)_[A-Z0-9_]+$")).Select(v => v.Name).ToArray();
        Assert.Empty(blank);
    }

    /// <summary>⭐ The help section lists every declared name, and is the same under cs-CZ (R-4221).</summary>
    [Fact]
    public void TheHelpSection_ListsEveryVariable_LanguageNeutral()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        string text;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");
            text = EnvVars.RenderHelp();
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
        Assert.Equal(EnvVars.RenderHelp(), text);
        foreach (var v in EnvVars.All) Assert.Contains("`" + v.Name + "`", text);
    }

    /// <summary>🔴 The rail's own control: a raw read and an undeclared name are found.</summary>
    [Fact]
    public void TheScan_FindsARawRead_AndKnowsTheAllowList()
    {
        Assert.Matches(RawRead, "var x = Environment.GetEnvironmentVariable(\"HROT_X\");");
        Assert.True(AllowedRaw("FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/ImGuiBackend/ImGuiRenderer.cs"));
        Assert.False(AllowedRaw("FDP/Engine/Fdp.Core/WorldGuard.cs"));
        Assert.True(IsTest("Hrot/Engine/Hrot.Presentation.Tests/Map3D/Map3DFrameRail.cs"));
    }
}
