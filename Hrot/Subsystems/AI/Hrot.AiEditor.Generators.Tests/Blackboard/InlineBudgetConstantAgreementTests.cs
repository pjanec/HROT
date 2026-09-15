using System.Reflection;
using Xunit;
using Fdp.Toolkit.Behavior;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.AiEditor.Generators.Tests.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b><c>W5</c> — the 100-byte inline budget is written down FOUR times, and nothing compared them.</b>
///
/// <para>
/// ⛔⛔ <b>The mirror is forced, so removing it is not the fix.</b>
/// <c>BehaviorParameterSizeAnalyzer:23-26</c> says so itself: <i>"Mirrors
/// BehaviorConstants.MaxBehaviorParamByteSize. Intentionally inlined here because this analyzer targets
/// netstandard2.0 and cannot reference the net8.0 Fdp.Toolkits runtime assembly."</i> ⭐ <b>The DRIFT is
/// the defect</b>, and a test is the only thing that can see both sides — tests are <c>net8.0</c> and may
/// reference the analyzer as an ordinary library.
/// </para>
///
/// <para>
/// 📐 <b>Measured while building this, and it is more than the handoff supposed: FOUR copies, not two.</b>
/// <list type="number">
///   <item><c>BehaviorConstants.MaxBehaviorParamByteSize</c> — the source of truth, and the one
///   <c>BrainBlackboard.BehaviorParameters[…]</c> is actually declared with.</item>
///   <item><c>BehaviorParameterSizeAnalyzer.MaxBehaviorParamByteSize</c> — <c>private const</c>,
///   netstandard2.0.</item>
///   <item><c>BlackboardBinPacker.MaxInlineBytes</c> — the editor-side packer.</item>
///   <item><c>BTreeBlackboardPackHelper.MaxInlineBytes</c> — the build-time packer inside the
///   generator, which is netstandard2.0 for the same reason as (2).</item>
/// </list>
/// ⚠ <b>And a fifth that this test cannot reach:</b> <c>BlueprintVariablesWindow:414</c> compares against
/// a bare <c>100</c> literal in an expression rather than a named constant. Filed, not fixed here.
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
        var field = type.GetField("MaxBehaviorParamByteSize",
                        BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(type.FullName,
                "MaxBehaviorParamByteSize — the analyzer's mirror of the inline budget was renamed or "
                + "removed; this test exists precisely to notice that.");
        return (int)field.GetRawConstantValue()!;
    }

    /// <summary>
    /// 🔴 <b>Proven red by editing ONE side:</b> changing any single copy to a different number fails
    /// here, naming which copy drifted. ⛔ Before this test, a change to
    /// <c>BehaviorConstants.MaxBehaviorParamByteSize</c> would have left the analyzer enforcing the old
    /// number — passing DTOs it should refuse, or refusing DTOs that now fit.
    /// </summary>
    [Fact]
    public void EveryCopyOfTheInlineBudgetAgreesWithBehaviorConstants()
    {
        int truth = BehaviorConstants.MaxBehaviorParamByteSize;

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
    /// ⭐⭐ <b>The constant is not free-floating: it is the declared length of the buffer it bounds.</b>
    /// ⚠ Without this, all four copies could agree on a number that no longer matches
    /// <c>BrainBlackboard.BehaviorParameters</c> — four mirrors of a wrong value, in perfect agreement.
    /// </summary>
    [Fact]
    public void TheBudgetIsTheDeclaredLengthOfTheBufferItBounds()
    {
        var field = typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)
            .GetField("BehaviorParameters", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(field);

        // A `fixed byte[N]` field is compiled to a nested FixedBuffer struct whose size IS N.
        var buffer = field!.FieldType;
        int declaredLength = System.Runtime.InteropServices.Marshal.SizeOf(buffer);

        Assert.Equal(BehaviorConstants.MaxBehaviorParamByteSize, declaredLength);
    }
}
