using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Ir;

public abstract record IrTerminator
{
    public IrDebugAnnotation Debug { get; init; } = null!;
}

public sealed record IrTerm_Goto(IrBlockId Target) : IrTerminator;
public sealed record IrTerm_Branch(IrValue Condition, IrBlockId IfTrue, IrBlockId IfFalse) : IrTerminator;
public sealed record IrTerm_Return(IrValue? Value) : IrTerminator;
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
