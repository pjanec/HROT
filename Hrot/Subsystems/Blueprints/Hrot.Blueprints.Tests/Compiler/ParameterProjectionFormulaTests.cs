using System.Text.RegularExpressions;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>W13</c> / <c>BP-251</c> — ONE parameter-projection formula, repo-wide.</b>
///
/// <para>
/// 🔴 <b>There were two.</b> The BTree bridge's per-node adapter projects at a bin-packed byte offset
/// (<c>Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)48)</c>) that the packer budget-checks;
/// the blueprint's own standalone thunk projected at a <b>stride</b>,
/// <c>bb.BehaviorParameters[paramIndex * Unsafe.SizeOf&lt;Params&gt;()]</c>, which nothing bounded.
/// ⛔ <c>paramIndex</c> is the ordinal among <b>every distinct Action and Condition method name in the
/// tree</b> (<c>TreeCompiler:155</c>), so the multiplier grows with tree size — measured at
/// <b>bytes 200…240 of a 100-byte buffer</b> for <c>PlatoonHillAttack2</c>'s widest primitive.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Asserted as a FORMULA, not as the absence of a literal</b> — deliberately. <c>W3</c>'s lesson
/// was that naming the bad constant (<c>100</c>/<c>200</c>) *"would pass again the moment someone
/// reintroduced the mechanism at 300."* ⇒ this pins the shape every projection must have, so a third
/// formula fails no matter what numbers it uses.
/// </para>
/// </summary>
public sealed class ParameterProjectionFormulaTests
{
    /// <summary>⭐ The one permitted shape: a CONSTANT byte offset from the start of the region.</summary>
    private static readonly Regex OffsetForm = new(
        @"Unsafe\.AddByteOffset\(\s*ref bb\.BehaviorParameters\[0\],\s*\(nint\)\d+\s*\)",
        RegexOptions.Compiled);

    /// <summary>🔴 The retired shape: anything whose offset depends on the caller-supplied index.</summary>
    private static readonly Regex StrideForm = new(
        @"BehaviorParameters\[[^\]]*paramIndex[^\]]*\]",
        RegexOptions.Compiled);

    /// <remarks>
    /// ⚠ One asset cannot carry BOTH thunks — <c>BP1022</c> refuses an Action-intent primitive hosting
    /// <c>BTreeCondition</c> — so the action and condition halves are separate fixtures. Learned from the
    /// diagnostic, not assumed.
    /// </remarks>
    private static string EmitBTreeHostedPrimitive(AiPrimitiveIntent intent, AiPrimitiveHosting hosting)
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive($"ProjectionFormulaFixture{hosting}")
            .WithIntent(intent)
            .WithHostings(hosting)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Parameters.Add(new ParameterDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Speed",
            Type = new BlueprintTypeRef { TypeId = "float" },
        });

        var result = new Hrot.Blueprints.Core.Compiler.BlueprintCompiler()
            .Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return result.GeneratedSource!;
    }

    /// <summary>
    /// 🔴 <b>RED before <c>W13</c>:</b> both thunks emitted the stride. ⭐ Green means the standalone
    /// thunk projects the same way the bridge adapter does, so the <c>@0</c> in its registered key is
    /// true <b>by construction</b> rather than by convention.
    /// </summary>
    [Fact]
    public void TheStandaloneThunksProjectAtAConstantOffset_LikeTheBridgeAdapter()
    {
        var action    = EmitBTreeHostedPrimitive(AiPrimitiveIntent.Action,    AiPrimitiveHosting.BTreeAction);
        var condition = EmitBTreeHostedPrimitive(AiPrimitiveIntent.Condition, AiPrimitiveHosting.BTreeCondition);

        Assert.Single(OffsetForm.Matches(action));
        Assert.Single(OffsetForm.Matches(condition));
        Assert.Empty(StrideForm.Matches(action));
        Assert.Empty(StrideForm.Matches(condition));
    }

    /// <summary>
    /// ⭐⭐ <b>The whole shipped corpus, not just a fixture.</b> ⚠ A fixture proves the emitter changed;
    /// this proves nothing in the corpus still carries the retired shape — including assets whose
    /// hostings combination the fixture does not reproduce.
    /// </summary>
    [Fact]
    public void NoCorpusAssetEmitsAnIndexDependentProjection()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var offenders = new List<string>();

        foreach (var file in GoldenCorpus.EnumerateFiles())
        {
            var name = Path.GetFileName(file).Replace(".bp.json", "");
            var src  = GoldenCorpus.Compile(GoldenCorpus.Load(name)).Tier2;
            if (StrideForm.IsMatch(src)) offenders.Add(name);
        }

        Assert.True(offenders.Count == 0,
            "these assets still project parameters at a caller-supplied stride:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⭐ <b>The capability is preserved, and that is the point of routing rather than deleting.</b>
    /// <c>BTreeTick@0</c> is the architect-confirmed <i>blueprint-as-behavior</i> standalone hosting
    /// (<c>SLICE1-DESIGN §82</c>, <c>SLICE2-DESIGN:52</c>), opt-in per hosting. ⛔ Deleting it would have
    /// removed a capability rather than a mistake — so the registration must still be here.
    /// </summary>
    [Fact]
    public void TheStandaloneHostingIsStillRegistered()
    {
        Assert.Contains(".BTreeTick@0\"",
            EmitBTreeHostedPrimitive(AiPrimitiveIntent.Action, AiPrimitiveHosting.BTreeAction));
        Assert.Contains(".BTreeEvaluate@0\"",
            EmitBTreeHostedPrimitive(AiPrimitiveIntent.Condition, AiPrimitiveHosting.BTreeCondition));
    }
}
