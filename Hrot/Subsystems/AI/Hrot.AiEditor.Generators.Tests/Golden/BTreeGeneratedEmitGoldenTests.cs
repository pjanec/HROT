using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E0</c> — BTree's EMIT tier, over the REAL compilation.</b>
///
/// <para>
/// 🔴 <b>The measurement that specified this (Batch 73).</b> <c>BTreeJsonGenerator</c> takes two
/// inputs no synthesized compilation carries: a <c>structSizeResolver</c> built from the SEMANTIC
/// MODEL (<c>StructSizeResolver.MakeDelegate</c>) and <c>BTreeDeactivatorScanner.Scan(compilation,
/// …)</c> over real attributed methods. ⇒ ⛔ <b>a driver over stubs emits FALLBACK output — a baseline
/// of something production never produces</b>, which is worse than no tier. So the handoff's
/// instruction was: build it against the real compilation, or name the boundary and stop.
/// </para>
///
/// <para>
/// ⭐⭐ <b>What made it buildable: neither input needs SYNTAX.</b> <c>StructSizeResolver</c> resolves
/// through <c>Compilation.GetTypeByMetadataName</c>, and the scanner's own comment says it walks
/// <i>"all named types in the compilation (source <b>and referenced assemblies</b>)"</i>. ⇒ a
/// compilation with <b>no syntax trees at all</b> but the REAL assemblies as metadata references
/// gives the generator everything it reads. That is this harness.
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The acceptance test is <see cref="TheBaseline_DependsOnTheRealCompilation"/>:</b> the same
/// generator over a BARE compilation produces DIFFERENT output. ⛔ A green new tier proves nothing —
/// this is what shows the baseline is of production's output rather than of the fallback.
/// </para>
/// </summary>
public sealed class BTreeGeneratedEmitGoldenTests
{
    /// <summary>
    /// ⭐⭐ The generated sources for every corpus BTree asset, baselined as stored TEXT — the tier
    /// <c>E0</c> was missing on this host. Moves when a thunk key, a baked offset, a struct size or a
    /// deactivator registration changes.
    /// </summary>
    [Fact]
    public void TheGeneratedBTreeSourcesAreUnchanged()
    {
        var parts = RunOverRealCompilation();
        Assert.NotEmpty(parts);   // ⛔ an empty run would make the baseline vacuous

        foreach (var (hint, source) in parts)
            AiGoldenSnapshot.ReadOrRegenerate($"Golden/Generated/BTree/{hint}.txt", source);
    }

    /// <summary>
    /// 🔴🔴 <b>THE ACCEPTANCE TEST.</b> The identical generator, the identical assets, over a
    /// compilation carrying only <c>System.Private.CoreLib</c>: the output DIFFERS.
    ///
    /// <para>
    /// ⚠ <b>Why this is the right acceptance shape here.</b> The obvious one — mutate a struct and
    /// watch the baseline move — cannot be written against metadata references: the structs live in
    /// shipped assemblies. ⭐ But the property that matters is the same one: <b>the baseline is
    /// sensitive to what the compilation contains.</b> If it were not, the tier would be baselining
    /// fallback output and every future struct-size or deactivator change would slip through it.
    /// </para>
    ///
    /// <para>
    /// ⭐ The two differing halves are named rather than left as a whole-text inequality, so a future
    /// change that loses ONE of them still fails.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBaseline_DependsOnTheRealCompilation()
    {
        string real = Concat(RunOverRealCompilation());
        string bare = Concat(Run(BareCompilation()));

        Assert.NotEqual(real, bare);

        // ⭐ The size resolver: only the real compilation can size a struct-typed variable, so only it
        //   emits the projections keyed on those offsets.
        Assert.Contains("Unsafe.AddByteOffset", real);

        // ⭐ The deactivator scan: BTreeDeactivatorScanner finds nothing without the real assemblies.
        Assert.Contains("RegisterDeactivator", real);
        Assert.DoesNotContain("RegisterDeactivator", bare);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The validity rail: what this harness generates is byte-for-byte what the REAL BUILD
    /// generated.</b>
    ///
    /// <para>
    /// ⚠ <b>The failure mode this exists for.</b> A harness can be "over the real compilation" and
    /// still take a different arm than the build — a missing <c>*.bp.json</c>, a different assembly
    /// name, an unloaded reference — and then the baseline pins a HARNESS ARTEFACT. ⛔ That is the
    /// same trap as baselining fallback output, just harder to notice, because the tier looks green
    /// and reddens on nothing that matters.
    /// </para>
    ///
    /// <para>
    /// ⭐ So the comparison is against <c>obj/GeneratedFiles/…</c> — the files <c>csc</c> really wrote
    /// for <c>Hrot.AI.Behaviors</c>. 📐 Measured: identical apart from the UTF-8 BOM the build writes
    /// to disk and an in-memory <c>SourceText</c> does not carry.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHarnessReproducesTheRealBuildsOutput()
    {
        string generatedDir = Path.Combine(
            Path.GetDirectoryName(FindUp(Path.Combine(
                "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj")))!,
            "obj", "GeneratedFiles");

        Assert.True(Directory.Exists(generatedDir),
            $"the real build's generated output is missing at {generatedDir} — this tier compares "
            + "against it, so an absent directory is a broken gate, not a reason to skip");

        var onDisk = Directory.GetFiles(generatedDir, "*.g.cs", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.Ordinal);

        int compared = 0;
        foreach (var (hint, source) in RunOverRealCompilation())
        {
            if (!onDisk.TryGetValue(hint, out var path)) continue;   // asset not in that project
            Assert.Equal(
                File.ReadAllText(path).TrimStart('﻿'),          // the build writes a BOM
                source);
            compared++;
        }

        Assert.True(compared > 0, "no generated file was compared — the harness proved nothing");
    }

    /// <summary>⭐ Deterministic across runs — a baseline over non-deterministic output is a
    /// regeneration treadmill, not a gate (Batch 73's ordering finding).</summary>
    [Fact]
    public void TheGeneratorIsDeterministic()
    {
        var first  = RunOverRealCompilation();
        var second = RunOverRealCompilation();

        Assert.Equal(first.Select(p => p.HintName), second.Select(p => p.HintName));
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Source, second[i].Source);
    }

    // ══ the harness ══════════════════════════════════════════════════════════

    private static IReadOnlyList<(string HintName, string Source)> RunOverRealCompilation()
        => Run(RealCompilation());

    private static IReadOnlyList<(string HintName, string Source)> Run(CSharpCompilation compilation)
    {
        var texts = CorpusAdditionalTexts();
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(texts.ToImmutableArrayCompat())
            .RunGenerators(compilation);

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .OrderBy(g => g.HintName, StringComparer.Ordinal)
            .Select(g => (g.HintName, g.SourceText.ToString()))
            .ToList();
    }

    /// <summary>
    /// ⭐ The corpus assets AND the <c>*.bp.json</c> blueprints beside them — the build feeds the
    /// generator both, so this feeds it both.
    ///
    /// <para>
    /// 📐 <b>Measured, not assumed:</b> withholding the blueprints changes NOTHING here. Their
    /// <c>Params</c> sizes come from <c>GetTypeByMetadataName</c> over the compiled
    /// <c>Hrot.AI.Behaviors.Generated.*_Bp.Params</c> types, so the <c>*.bp.json</c> fallback — which
    /// exists for the in-build case where a sibling generator's output is not yet visible — never
    /// fires against real assemblies. ⭐ They are passed anyway because matching the build's INPUTS is
    /// what keeps <see cref="TheHarnessReproducesTheRealBuildsOutput"/> honest as either arm changes.
    /// </para>
    /// </summary>
    private static AdditionalText[] CorpusAdditionalTexts()
    {
        var btreeDir = AiAssetCorpus.ResolveCorpusDir(AiAssetKind.BTree);
        var assetsRoot = Directory.GetParent(btreeDir)!.FullName;

        var files = Directory.GetFiles(btreeDir, "*.btree.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(assetsRoot, "*.bp.json", SearchOption.AllDirectories))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);
        return files
            .Select(f => (AdditionalText)new StringAdditionalText(f, File.ReadAllText(f)))
            .ToArray();
    }

    /// <summary>
    /// ⭐⭐ The REAL compilation: no syntax trees, every assembly this test process has loaded as a
    /// metadata reference. ⚠ Named <c>Hrot.AI.Behaviors</c> because generators branch on the assembly
    /// name, so a probe assembly name would take a different arm than production.
    /// </summary>
    private static CSharpCompilation RealCompilation()
    {
        // ⭐ Force-load the assembly that declares the corpus's node methods and DTO structs: without a
        //   touch it may not be loaded yet, and then the "real" compilation would quietly be a bare one.
        _ = typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
            if (seen.Add(a.Location))
                refs.Add(MetadataReference.CreateFromFile(a.Location));
        }

        return CSharpCompilation.Create(
            assemblyName: "Hrot.AI.Behaviors",
            syntaxTrees:  Array.Empty<SyntaxTree>(),
            references:   refs,
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    /// <summary>⚠ The control: what a synthesized compilation would have baselined.</summary>
    private static CSharpCompilation BareCompilation()
        => CSharpCompilation.Create(
            assemblyName: "Hrot.AI.Behaviors",
            syntaxTrees:  Array.Empty<SyntaxTree>(),
            references:   new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    private static string FindUp(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Not found on any ancestor: {relative}");
    }

    private static string Concat(IReadOnlyList<(string HintName, string Source)> parts)
        => string.Join("\n", parts.Select(p => p.HintName + "\n" + p.Source));
}
