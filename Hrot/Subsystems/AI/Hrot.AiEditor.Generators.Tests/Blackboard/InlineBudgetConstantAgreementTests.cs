using System.Reflection;
using Xunit;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Shared;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.AiEditor.Generators.Tests.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b><c>W5</c> — the params ceiling is written down FOUR times, and nothing compared them.</b>
///
/// <para>
/// ⛔⛔ <b>The mirror is forced, so removing it is not the fix.</b>
/// <c>BehaviorParameterSizeAnalyzer</c> says so itself: <i>"Intentionally inlined here because this
/// analyzer targets netstandard2.0 and cannot reference the net8.0 Fdp.Toolkits runtime assembly."</i>
/// ⭐ <b>The DRIFT is the defect</b>, and a test is the only thing that can see both sides — tests are
/// <c>net8.0</c> and may reference the analyzer as an ordinary library.
/// </para>
///
/// <para>
/// 📐 <b>The four copies:</b>
/// <list type="number">
///   <item><c>BehaviorConstants.MaxRootParamsByteSize</c> — the source of truth.</item>
///   <item><c>BehaviorParameterSizeAnalyzer.MaxRootParamsByteSize</c> — <c>private const</c>,
///   netstandard2.0.</item>
///   <item><c>BlackboardBinPacker.MaxInlineBytes</c> — the editor-side packer.</item>
///   <item><c>BTreeBlackboardPackHelper.MaxInlineBytes</c> — the build-time packer inside the
///   generator, which is netstandard2.0 for the same reason as (2).</item>
/// </list>
/// ⭐⭐ <b><c>CE-314</c> (2026-09-22) restored the FOURTH row.</b> <c>CE-307</c> had to leave
/// <c>BlackboardBinPacker.MaxInlineBytes</c> at 100 — there the number was also the inline/heavy SPLIT
/// POINT — and pinned that exception with a dedicated test. ⭐ <c>CE-314</c> removed the split, raised
/// the copy, deleted that test and folded the mirror back into the agreement above, <b>which is exactly
/// the failure the exception-test was written to force.</b>
///
/// ⚠ <b>And a fifth that this test cannot reach:</b> <c>BlueprintVariablesWindow:414</c> compares against
/// a bare <c>100</c> literal in an expression rather than a named constant. Filed, not fixed here.
/// </para>
///
/// <para>
/// 🔴🔴 <b><c>CE-307</c> (2026-09-22) — WHAT THE NUMBER MEANS CHANGED, AND SO DID WHAT ANCHORS IT.</b>
/// The budget was <b>100</b>: the width of <c>BrainBlackboard.BehaviorParameters</c>, a <c>fixed byte[]</c>
/// with tail registers after it, so the ceiling was a <b>buffer-overrun guard</b>. ⛔ <c>O2</c> moved the
/// registers to <c>BrainInterrupts</c> and <c>P3-C</c> moved params into their own occurrence slot, sized
/// to the behaviour and promoted up the tier ladder — <b>nothing neighbours them</b>. ⇒ what survives is a
/// <b>capacity</b> bound: the payload of the largest tier, above which no tier can hold the region at all.
/// ⚠ <b>The second test below changed its anchor accordingly</b> — it used to assert the budget equalled
/// the declared length of <c>BehaviorParameters</c>; that buffer is no longer what the budget bounds, and
/// it is being deleted by <c>P4</c>.
/// </para>
/// </summary>
public sealed class InlineBudgetConstantAgreementTests
{
    /// <summary>
    /// ⭐ The analyzer's copy is <c>private</c> and must STAY private — it is an implementation detail of
    /// a netstandard2.0 assembly. ⇒ read it the only way a test can, and say why in one place.
    /// </summary>
    private static int AnalyzerConstant()
    {
        var type = typeof(Fdp.Toolkit.Behavior.Analyzers.BehaviorParameterSizeAnalyzer);
        var field = type.GetField("MaxRootParamsByteSize",
                        BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(type.FullName,
                "MaxRootParamsByteSize — the analyzer's mirror of the params ceiling was renamed or "
                + "removed; this test exists precisely to notice that.");
        return (int)field.GetRawConstantValue()!;
    }

    /// <summary>
    /// 🔴 <b>Proven red by editing ONE side:</b> changing any single copy to a different number fails
    /// here, naming which copy drifted. ⛔ Without this test, a change to
    /// <c>BehaviorConstants.MaxRootParamsByteSize</c> would leave the analyzer enforcing the old
    /// number — passing DTOs it should refuse, or refusing DTOs that now fit.
    /// </summary>
    [Fact]
    public void EveryCopyOfTheParamsCeilingAgreesWithBehaviorConstants()
    {
        int truth = BehaviorConstants.MaxRootParamsByteSize;

        // ⚠ The mirrors go through `Mirror()` rather than being named inline: xUnit2000 folds a
        //   `const` reference and demands it sit in the `expected` slot, which would read as "the
        //   mirror is the truth". BehaviorConstants is the truth; the others are the values under test.
        Assert.Equal(truth, AnalyzerConstant());
        Assert.Equal(truth, Mirror(BlackboardBinPacker.MaxInlineBytes));
        Assert.Equal(truth, Mirror(BTreeBlackboardPackHelper.MaxInlineBytes));
    }


    /// <summary>Identity — see the comment above; it only stops the constant being folded.</summary>
    private static int Mirror(int value) => value;

    /// <summary>
    /// ⭐⭐⭐ <b>The ceiling is not free-floating: it is the capacity of the storage it bounds.</b>
    ///
    /// <para>⚠ Without this, all four copies could agree on a number that corresponds to nothing — four
    /// mirrors of a wrong value, in perfect agreement. 📌 That is exactly the state <c>CE-307</c> found:
    /// they agreed on <b>100</b>, the width of a buffer params had already stopped living in.</para>
    ///
    /// <para>⛔ <b>The anchor is the LARGEST tier's payload</b>, because a root params region occupies
    /// ONE slot (§29.6 — the whole packed table, never scattered) and the allocator promotes up the
    /// ladder until it fits. ⇒ above this, no tier can hold it and the behaviour could never be
    /// assigned.</para>
    /// </summary>
    [Fact]
    public void TheCeilingIsTheCapacityOfTheLargestTier()
    {
        Assert.Equal(BlueprintTierLadder.Tier16384PayloadSize,
                     BehaviorConstants.MaxRootParamsByteSize);
    }

    /// <summary>
    /// ⛔⛔ <b>The ceiling is the ladder's TOP, not an arbitrary tier.</b> ⚠ Pinning only the equality
    /// above would still pass if someone added a bigger tier and forgot to repoint the ceiling — the
    /// mirrors would agree with each other and with a tier that is no longer the largest.
    /// </summary>
    [Fact]
    public void NoTierIsLargerThanTheCeiling()
    {
        foreach (var spec in Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Ascending)
            Assert.True(spec.PayloadSize <= BehaviorConstants.MaxRootParamsByteSize,
                $"Tier {spec.TotalSize} has a {spec.PayloadSize}-byte payload, which exceeds the " +
                $"declared params ceiling of {BehaviorConstants.MaxRootParamsByteSize}. A tier was " +
                "added without repointing BehaviorConstants.MaxRootParamsByteSize and its three mirrors.");
    }
}
