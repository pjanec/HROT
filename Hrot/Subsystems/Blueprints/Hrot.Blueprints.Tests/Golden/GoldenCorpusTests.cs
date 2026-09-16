using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// <b>U-1 — the golden-corpus gate.</b> ⭐ <b>No product change; this is the instrument.</b>
///
/// <para>
/// ⛔ <b>A harness that has never failed is not a harness</b> — it is 42 green checkmarks that would
/// stay green through the whole unification. The <c>Bite_*</c> tests below are therefore as much of
/// the deliverable as the sweep: each mutates a corpus asset in memory and asserts <b>which tier</b>
/// notices.
/// </para>
/// </summary>
public sealed class GoldenCorpusTests
{
    // ────────────────────────────────────────────────────────────────────────
    // 🔴 The corpus definition — the glob, not the count
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The corpus is defined by the generator's own <c>AdditionalFiles</c> glob</b>, so this
    /// asserts the <c>.csproj</c> still says what the harness assumes. ⚠ If the glob moves and this
    /// test does not fail, every later <c>U-</c> task is measuring a corpus production does not
    /// compile.
    /// </summary>
    [Fact]
    public void TheCorpusIsTheGeneratorsOwnGlob()
    {
        var csproj = FindBehaviorsCsproj();
        var text   = File.ReadAllText(csproj);

        Assert.Contains($"<AdditionalFiles Include=\"{GoldenCorpus.CorpusGlobInProject}\"", text);
        // ⛔ Recipes/Blueprints is Content, never compiled by production — and globbing both roots
        // throws, because assets exist in each sharing an AssetId.
        Assert.DoesNotContain(@"<AdditionalFiles Include=""Recipes\", text);
    }

    /// <summary>
    /// ⭐ <b>The whole corpus compiles.</b> ⚠ Three <c>HillAssault2I_*</c> assets fail <c>BP1602</c>
    /// without the assembly preload — a null resolver makes Stage 0 reflect over <b>loaded</b>
    /// assemblies — so this is also the test that the preload is doing its job.
    /// </summary>
    [Fact]
    public void EveryCorpusAssetCompilesWithoutErrors()
    {
        var failures = new List<string>();
        foreach (var name in GoldenCorpus.EnumerateFiles()
                     .Select(f => Path.GetFileName(f).Replace(".bp.json", "")))
        {
            var rec = GoldenCorpus.Compile(GoldenCorpus.Load(name));
            var errors = rec.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().ToList();
            if (errors.Count > 0) failures.Add($"{name}: {string.Join(",", errors)}");
        }

        Assert.True(failures.Count == 0,
            "Corpus assets failed to compile:\n  " + string.Join("\n  ", failures));
        // 📌 today's count, informational. ⭐ 42 → 43 in Batch 60: `LayoutAlignmentWitness` (PA-14) is
        //    the constructed witness for the runtime layout gate — no shipped asset declares a type
        //    whose CLR alignment `FieldLayout.TypeAlignment` mispredicts, so the corpus could not
        //    witness `W2` at all. See EmittedStateLayoutTests.
        Assert.Equal(43, GoldenCorpus.EnumerateFiles().Count);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 🔴 The sweep — the baseline itself
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Tier 1 — never moves undeclared.</b> <c>StructureHash</c> · every emitted struct field
    /// (name · type · offset · size) · the diagnostic multiset. ⛔ A change here is a <b>failure</b>:
    /// offsets are what the running blackboard is addressed by, and the emitted tick re-initialises
    /// that memory on a hash mismatch.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Tier1_StructureAndDiagnostics_MatchBaseline(string assetName)
    {
        var rec = GoldenCorpus.Compile(GoldenCorpus.Load(assetName));
        TestData.ReadOrRegenerateSnapshot($"Golden/Tier1/{assetName}.txt", rec.Tier1);
    }

    /// <summary>
    /// ⭐ <b>Tier 2 — moves with a regenerated baseline.</b> The full generated source, stored as a
    /// FILE and ⛔ not hashed: <i>"a hash names the asset; a stored file names the LINE."</i>
    /// Regenerating is a reviewable diff, which is the point.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void Tier2_GeneratedSource_MatchesBaseline(string assetName)
    {
        var rec = GoldenCorpus.Compile(GoldenCorpus.Load(assetName));
        TestData.ReadOrRegenerateSnapshot($"Golden/Emit/{assetName}.cs.txt", rec.Tier2);
    }

    /// <summary>
    /// ⭐⭐ <b>The harness's stage sequence IS the compiler's.</b>
    ///
    /// <para>
    /// <see cref="GoldenCorpus.Compile"/> mirrors <see cref="BlueprintCompiler.Compile"/> rather than
    /// calling it, because Tier 1 needs the laid-out <c>IrAsset</c> that <c>CompileResult</c> does not
    /// expose. ⚠ <b>That is a denormalised copy</b> — the defect shape this programme keeps filing —
    /// so it is pinned here instead of trusted: if the real pipeline gains, loses or reorders a stage,
    /// this reddens on every asset rather than the baseline quietly measuring the wrong thing.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void HarnessPipelineMatchesTheRealCompiler(string assetName)
    {
        var harness = GoldenCorpus.Compile(GoldenCorpus.Load(assetName));
        var real    = new BlueprintCompiler().Compile(GoldenCorpus.Load(assetName), GoldenCorpus.Options());

        Assert.True(real.Succeeded, $"'{assetName}' failed the real compiler.");
        Assert.Equal(real.GeneratedSource, harness.Tier2);
        Assert.Equal(real.StructureHash, harness.Lowered!.StructureHash);
        Assert.Equal(
            real.Diagnostics.Select(d => d.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray(),
            harness.Diagnostics.Select(d => d.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    public static IEnumerable<object[]> Corpus() => GoldenCorpus.AssetNames();

    // ────────────────────────────────────────────────────────────────────────
    // 🔴🔴 Prove it BITES — and prove the two tiers are actually two
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Reordering one field reddens BOTH tiers</b>, and Tier 1 names the field and its new
    /// offset. ⚠ This is the mutation the whole two-tier design exists for: a reorder is a silent,
    /// correct-looking edit whose consequence is that every deployed entity's blackboard is
    /// re-initialised (<c>BP-234</c>).
    /// </summary>
    [Fact]
    public void Bite_ReorderingAField_ReddensTier1AndTier2()
    {
        const string Asset = "ManagedCollectionDemo";   // 6 variables — the widest struct in the corpus

        var baseline = GoldenCorpus.Compile(GoldenCorpus.Load(Asset));

        var mutated = GoldenCorpus.Load(Asset);
        Assert.True(mutated.Variables.Count >= 2, "fixture needs at least two variables to swap");
        (mutated.Variables[0], mutated.Variables[1]) = (mutated.Variables[1], mutated.Variables[0]);
        var after = GoldenCorpus.Compile(mutated);

        Assert.NotEqual(baseline.Tier1, after.Tier1);
        Assert.NotEqual(baseline.Tier2, after.Tier2);
        Assert.NotEqual(baseline.Lowered!.StructureHash, after.Lowered!.StructureHash);

        // ⭐ The report names the field, not just "something moved".
        var moved = mutated.Variables[0].Name;
        Assert.Contains(moved, after.Tier1);

        AssertComparisonRejects($"Golden/Tier1/{Asset}.txt", after.Tier1, Asset);
    }

    /// <summary>
    /// Asserts the snapshot comparison <b>rejects</b> <paramref name="mutatedText"/>, and that its
    /// message is readable.
    ///
    /// <para>
    /// ⚠⚠ <b>Two hazards this exists to avoid, one of which bit during the first regeneration run.</b>
    /// <list type="number">
    ///   <item>⛔ <b>Never point a bite test at a committed baseline path while regenerating.</b>
    ///   Under <c>BLUEPRINT_REGENERATE_SNAPSHOTS=1</c> the helper <i>writes</i> — so the first run of
    ///   this suite overwrote <c>ManagedCollectionDemo</c>'s Tier 1 with the <b>mutated</b> layout,
    ///   silently corrupting the very baseline the batch exists to record. The comparison is now run
    ///   against a scratch copy instead.</item>
    ///   <item>The throw is also suppressed in regenerate mode by design, so the assertion is skipped
    ///   there rather than failing for the wrong reason.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static void AssertComparisonRejects(string baselinePath, string mutatedText, string assetName)
    {
        var snapshots = TestData.ResolveSnapshotsDir();
        var scratch   = Path.Combine("Golden", "_scratch", $"{assetName}.txt");
        var full      = Path.Combine(snapshots, scratch);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(Path.Combine(snapshots, baselinePath.Replace('/', Path.DirectorySeparatorChar)),
                  full, overwrite: true);
        try
        {
            if (Environment.GetEnvironmentVariable("BLUEPRINT_REGENERATE_SNAPSHOTS") == "1")
                return;   // the helper writes rather than throws; nothing to assert

            var ex = Assert.Throws<Exception>(
                () => TestData.ReadOrRegenerateSnapshot(scratch, mutatedText));
            Assert.Contains(assetName, ex.Message);
            Assert.Contains("First difference at line", ex.Message);
        }
        finally
        {
            File.Delete(full);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Tier 2 ONLY — and this is what justifies keeping two tiers.</b>
    ///
    /// <para>
    /// Renaming the asset rewrites the emitted class name and every reference to it, so the generated
    /// source moves substantially — ⛔ <b>and not one struct field does.</b> ⇒ Tier 2 reddens
    /// (correctly: the output changed) while Tier 1 stays put, so a routine emit change is a
    /// reviewable rebase rather than a blackboard-layout alarm.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Provable, not hopeful:</b> <c>StructureHashComputation</c> hashes <c>Dispatch</c> plus
    /// <c>name|type|offset|size</c> per field and <b>nothing else</b> — the asset's own name is not an
    /// input. ⚠ An earlier draft mutated a node id instead; that also breaks the links referencing it,
    /// which changes the diagnostic multiset and reddens Tier 1 too — a worse test that would have
    /// "passed" for the wrong reason had the assertion been one-sided.
    /// </para>
    /// </summary>
    [Fact]
    public void Bite_ChangingEmittedTextWithoutMovingAField_ReddensTier2Only()
    {
        const string Asset = "Count4";

        var baseline = GoldenCorpus.Compile(GoldenCorpus.Load(Asset));

        var mutated = GoldenCorpus.Load(Asset);
        mutated.Name = mutated.Name + "Renamed";
        var after = GoldenCorpus.Compile(mutated);

        Assert.NotEqual(baseline.Tier2, after.Tier2);          // ⭐ the emitted text moved
        Assert.Equal(baseline.Tier1, after.Tier1);             // ⛔ and no field did
        Assert.Equal(baseline.Lowered!.StructureHash, after.Lowered!.StructureHash);
    }

    /// <summary>
    /// ⭐ <b>One extra diagnostic reddens Tier 1</b> via the multiset, even though no field moved.
    /// An orphan node is <c>BP3010</c> — a warning, so the asset still compiles and Tier 2 is
    /// unaffected in kind; the count is what changes.
    /// </summary>
    [Fact]
    public void Bite_AnExtraDiagnostic_ReddensTier1()
    {
        const string Asset = "Count4";

        var baseline = GoldenCorpus.Compile(GoldenCorpus.Load(Asset));

        var mutated = GoldenCorpus.Load(Asset);
        var graph = mutated.Graphs.First(g => g.Nodes.Count > 0);
        graph.Nodes.Add(new SequenceNode { Id = Guid.NewGuid() });   // unreachable ⇒ orphan
        var after = GoldenCorpus.Compile(mutated);

        Assert.NotEqual(baseline.Tier1, after.Tier1);
        Assert.Contains("Diagnostics:", after.Tier1);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static string FindBehaviorsCsproj()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Hrot.AI.Behaviors.csproj not found above the test output dir.");
    }
}
