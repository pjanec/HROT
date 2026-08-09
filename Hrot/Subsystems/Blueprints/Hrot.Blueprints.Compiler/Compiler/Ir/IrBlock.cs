using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Ir;

public abstract record IrTerminator
{
    public IrDebugAnnotation Debug { get; init; } = null!;
}

public sealed record IrTerm_Goto(IrBlockId Target) : IrTerminator;
public sealed record IrTerm_Branch(IrValue Condition, IrBlockId IfTrue, IrBlockId IfFalse) : IrTerminator;
/// <summary>
/// Returns from the generated method. <c>Value</c> set ⇒ <c>return __tN;</c>.
///
/// <para>
/// BP-117: <c>Value</c> null is ambiguous on its own — it means <c>return;</c> for a void method, but a
/// void <c>return;</c> in a method declared to return <c>T</c> (or a <c>ValueTuple</c>) is <b>CS0126</b>.
/// The emitter cannot tell the two apart because it does not know the method's declared return type at
/// this point, so the distinction is carried here: <c>ReturnsDefault</c> ⇒ <c>return default;</c>, which
/// is valid for a scalar and a tuple alike. Set only by
/// <c>Stage5_Schedule.SealFallThrough</c> for a Library graph that declares outputs and whose exec chain
/// ran off the end with no <c>Return</c> node — and always alongside <c>BP1657</c>, so the implicit
/// default is reported rather than silently returned.
/// </para>
///
/// <para>
/// ⚠ Deliberately a flag on this record rather than a new <c>IrTerm_ReturnDefault</c> type: the two
/// switches over <see cref="IrTerminator"/> (<c>TerminatorEmitter</c>, <c>IrPrinter</c>) both end in a
/// catch-all, so a new terminator kind could have been silently mis-emitted instead of failing loudly.
/// </para>
/// </summary>
public sealed record IrTerm_Return(IrValue? Value, bool ReturnsDefault = false) : IrTerminator;
public sealed record IrTerm_ReturnStatus(NodeStatus Status) : IrTerminator;
// FailureBlock (Q#13): when set, the WaitForChannel latent lowering routes a channel-Failure
// resume to this block (the wired OnFailure exec chain) instead of returning NodeStatus.Failure.
// Null for LatentDelay / WaitForEvent / WaitForChannel-with-unwired-OnFailure (unchanged behavior).
public sealed record IrTerm_Suspend(IrValue ResumePoint, IrValue? WaitUntilTime, IrBlockId ResumeBlock, IrBlockId? FailureBlock = null) : IrTerminator;
public sealed record IrTerm_FallThrough : IrTerminator;

public sealed record IrBlock
{
    public IrBlockId Id { get; init; }
    public string Label { get; init; } = "";
    public IReadOnlyList<IrStatement> Statements { get; init; } = Array.Empty<IrStatement>();
    public IrTerminator Terminator { get; init; } = null!;
    /// <summary>
    /// The authored exec node that owns this block. Set in Stage5 for blocks that
    /// directly represent an authored exec node (entry, pre-suspend, etc.).
    /// Infrastructure blocks (resume, dispatch) leave this null.
    /// </summary>
    public Guid? SourceNodeId { get; init; }
}
