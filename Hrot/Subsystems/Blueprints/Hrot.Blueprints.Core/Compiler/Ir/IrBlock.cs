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
public sealed record IrTerm_Suspend(IrValue ResumePoint, IrValue? WaitUntilTime, IrBlockId ResumeBlock) : IrTerminator;
public sealed record IrTerm_FallThrough : IrTerminator;

public sealed record IrBlock
{
    public IrBlockId Id { get; init; }
    public string Label { get; init; } = "";
    public IReadOnlyList<IrStatement> Statements { get; init; } = Array.Empty<IrStatement>();
    public IrTerminator Terminator { get; init; } = null!;
}
