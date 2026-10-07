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

    /// <summary>
    /// ⭐ It is 29. ⭐ <b>28 → 29 in <c>CE-2073</c>:</b> <c>Tactics/CombatPosture.btree.json</c> — the utility-driven posture
    /// (<c>Parallel(RequireOne)[ChooseOption, PostureSensors, ObserverSelector[…]]</c>), the first asset whose Parallel carries an
    /// authored policy (<c>DESIGN_Decision_Layer.md</c> §3.3b).
    /// ⭐ <b>26 → 28 in <c>CE-2092</c> / <c>CE-2093</c>:</b> <c>Tactics/TakeCover.btree.json</c> and
    /// <c>Tactics/FallBack.btree.json</c> — Root → one shared stateful action (<c>EqsTacticsNodes</c>), the first assets
    /// that take cover / fall back on the terrain EQS queries (<c>DESIGN_Eqs_Consuming_Behaviours.md</c> §2, R-204).
    /// ⭐ <b>25 → 26 in <c>CE-2080</c>:</b> <c>Sop/BasicInfantrySop.btree.json</c>, the shipped SOP — the first asset that
    /// carries SOP orders ("Do when idle" / "React", <c>DESIGN_Decision_Layer.md</c> §4.6–§4.7); compiled so a TKB template can
    /// name it, and published as the BTree recipe. (The method name keeps its old number — a rename is a Roslyn rename.)
    /// ⭐ <b>24 → 25 in <c>CE-417</c> slice 3b:</b> <c>BTreeCuratedBindingDemo.btree.json</c>, the first BTree
    /// asset that binds a C# <c>[SharedAiAction]</c> — twice, at two host offsets (F8; rail
    /// <c>BrainTickSystemBTreeArmTests.CE417_R4</c>). ⛔ <b>25 → 24 in <c>CE-440</c>:</b> <c>T37_SharedStateManifestProvisioning.btree.json</c>, the
    /// proof tree for Entity-scoped shared state, went with the GetShared/SetShared node pair (decision <c>A</c>,
    /// <c>Q76</c> §12.24). ⭐ <b>24 → 25 in <c>CE-428</c>:</b> <c>T40_BehaviorResolverAsset.btree.json</c>, the first
    /// behaviour that names a blueprint RESOLVER asset (<c>Q76</c> §12.20). ⚠ <b>Was 26 until <c>CE-436</c>,</b> which deleted
    /// <c>PlatoonHillAttack2.btree.json</c> and <c>HillAssault2I_Smoke.btree.json</c> — the
    /// blueprint-based hill attack and its smoke tree *(user, <c>2026-09-29</c>: "not needed")*.
    /// ⭐ <b>29 → 32 in <c>CE-2108</c> / <c>CE-3079</c> H2:</b> <c>Tactics/Flank</c>, <c>Tactics/FiringPosition</c> (the rest of the EQS
    /// tactics, <c>DESIGN_Eqs_Consuming_Behaviours.md</c> §9) and <c>Tactics/Sentry</c> (the hostile's self-ending watch, Utility demo §10.5).
    /// ⭐ <b>32 → 33 in <c>CE-3079</c> H5:</b> <c>Tactics/DangerCrossing</c> (the danger-area demo's tree, Utility demo §10.4).
    /// ⭐ The count is deliberately hard-coded rather than derived: it is the tripwire that makes an
    /// asset appearing or vanishing a DECISION someone states, not a diff someone skims.
    /// </summary>
    [Fact]
    public void TheCorpusIsTheTwentyFiveShippedAssets()
        => Assert.Equal(38, AiAssetCorpus.EnumerateFiles(Kind).Count);   // 33 → 37: CE-3083 G5 wrapper BTrees PostureAdvance/Hold/Sense/Suppress · 37 → 38: CE-3090 PostureHoldProne

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
