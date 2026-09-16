using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐ <b><c>E0</c> extended — BTree's 26 assets get a persisted-shape floor.</b>
///
/// <para>
/// ⚠⚠ <b>Half the hole, and the other half is named rather than papered over.</b> Batch 71 I measured
/// <i>"three delegates ⇒ BTree is a registration, not a rewrite"</i>; ⭐ the <b>shape</b> tier is
/// exactly that and lands here. 📐 The <b>emit</b> tier is not — see
/// <see cref="TheBTreeEmitTierNeedsARoslynCompilation"/>, which pins the reason.
/// </para>
/// </summary>
public sealed class BTreeGoldenCorpusTests
{
    private static readonly AiAssetKind Kind = AiAssetKind.BTree;

    private const string ShapeBaseline = "Golden/btree-persistence-shape.txt";

    private static string Sha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>⭐ Same format as the blueprint and HSM shape files, so the three read alike.</summary>
    [Fact]
    public void TheCanonicalJsonOfEveryCorpusAssetIsUnchanged()
    {
        var sb = new StringBuilder();
        foreach (var file in AiAssetCorpus.EnumerateFiles(Kind))
        {
            var canonical = Kind.Canonicalize(File.ReadAllText(file));
            sb.Append(Path.GetFileName(file))
              .Append("  ").Append(Sha256(canonical))
              .Append("  ").Append(canonical.Length)
              .Append('\n');
        }

        AiGoldenSnapshot.ReadOrRegenerate(ShapeBaseline, sb.ToString());
    }

    /// <summary>The canonical form is a FIXED POINT — same reasoning as the other two corpora.</summary>
    [Fact]
    public void RoundTripIsStable()
    {
        var unstable = new List<string>();
        foreach (var file in AiAssetCorpus.EnumerateFiles(Kind))
        {
            var once  = Kind.Canonicalize(File.ReadAllText(file));
            if (!string.Equals(once, Kind.Canonicalize(once), StringComparison.Ordinal))
                unstable.Add(Path.GetFileName(file));
        }

        Assert.True(unstable.Count == 0,
            "canonical serialization is not a fixed point for:\n  " + string.Join("\n  ", unstable));
    }

    /// <summary>⭐ The corpus is the generator's inputs — the glob, not a hardcoded list.</summary>
    [Fact]
    public void TheCsprojStillGlobsTheCorpus()
    {
        var csproj = File.ReadAllText(FindUp(Path.Combine(
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj")));
        Assert.Contains($@"<AdditionalFiles Include=""{AiAssetCorpus.GlobInProject(Kind)}"" />", csproj);
    }

    /// <summary>⭐ It really is 26 — the number the plan and the handoff both quote.</summary>
    [Fact]
    public void TheCorpusIsTheTwentySixShippedAssets()
        => Assert.Equal(26, AiAssetCorpus.EnumerateFiles(Kind).Count);

    /// <summary>
    /// 🔴 <b>The gate can FAIL</b> — a new green gate proves nothing, so this shows a mutation moves it.
    /// </summary>
    [Fact]
    public void TheShapeTierReddens_WhenAnAssetChanges()
    {
        var file = AiAssetCorpus.EnumerateFiles(Kind).First();
        var json = File.ReadAllText(file);

        var mutated = json.Replace("\"Name\"", "\"NameX\"");
        Assert.NotEqual(json, mutated);
        Assert.NotEqual(Sha256(Kind.Canonicalize(json)), Sha256(Kind.Canonicalize(mutated)));
    }

    /// <summary>
    /// ⚠⚠ <b>Why the EMIT tier is not registered, as a measurement rather than an omission — and this
    /// corrects my own Batch 71 claim.</b>
    ///
    /// <para>
    /// 📐 <c>BTreeJsonGenerator</c> builds a <c>structSizeResolver</c> from a Roslyn
    /// <c>Compilation</c> and calls <c>BTreeDeactivatorScanner.Scan(compilation, …)</c>, then passes
    /// both into every emit call. ⛔ The resolver-less overloads exist and compile, but their output is
    /// <b>not what ships</b> — baselining it would repeat the very mistake
    /// <c>GoldenCorpus.Options()</c> records about Debug-vs-Release: a baseline of output production
    /// never produces.
    /// </para>
    ///
    /// <para>
    /// ⭐ The HSM path has no such dependency, which is why it got both tiers. ⇒ BTree's emit tier
    /// needs a <c>CSharpGeneratorDriver</c> harness — a real item, not a leftover. ⭐ <b>Invert this
    /// when that lands.</b>
    /// </para>
    /// </summary>
    [Fact]
    public void TheBTreeEmitTierNeedsARoslynCompilation()
    {
        var generator = File.ReadAllText(FindUp(Path.Combine(
            "Hrot", "Subsystems", "AI", "Hrot.AiEditor.Generators", "BTreeJsonGenerator.cs")));

        // Production passes a compilation-derived resolver and a compilation scan into emission…
        Assert.Contains("structSizeResolver", generator);
        Assert.Contains("BTreeDeactivatorScanner.Scan(compilation", generator);

        // …so this kind registers NO emit parts, deliberately.
        Assert.Empty(Kind.Emit(File.ReadAllText(AiAssetCorpus.EnumerateFiles(Kind).First())));

        // ⭐ And the HSM kind does, which is the contrast that makes the limit specific rather than
        //   a general "we did not get to it".
        Assert.NotEmpty(AiAssetKind.Hsm.Emit(
            AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, "SampleGuard")));
    }

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
}
