namespace Hrot.Blueprints.Core.Compiler.Ir;

public enum IrGraphKind
{
    Function,
    Event,
    AiPrimitiveMain,
    Construction,
}

public sealed record IrGraph
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public IrGraphKind Kind { get; init; }

    /// <summary>
    /// Q#14: for an Event graph, the event identity it handles (the <c>EventEntryNode.EventTypeId</c> — a
    /// typed event's FQN or a custom event's name). The emitter keys <c>BlueprintDefinition.EventHandlers</c>
    /// by this (NOT the graph name, which is a C# method-name suffix and can't hold an FQN) and reinterprets
    /// the dispatched payload as <c>global::{EventTypeFqn}</c>. Null for non-Event graphs.
    /// </summary>
    public string? EventTypeFqn { get; init; }

    /// <summary>Q#14 (3d): Self/Any recipient filter for an Event graph. When true, the emitted thunk
    /// early-returns unless the event's <see cref="TargetFieldName"/> equals <c>self</c>.</summary>
    public bool TargetFilterSelf { get; init; }

    /// <summary>Q#14 (3d): the event's <c>[EventTarget]</c> field name used by the Self filter comparison.</summary>
    public string? TargetFieldName { get; init; }

    public IReadOnlyList<IrField> Inputs { get; init; } = Array.Empty<IrField>();
    public IReadOnlyList<IrField> Outputs { get; init; } = Array.Empty<IrField>();
    /// <summary>
    /// BP-57 / Q27-A1 — this graph's function-local variables, in declaration order.
    ///
    /// <para>
    /// ⭐⭐ <b>A per-graph index space, and that is the load-bearing part.</b>
    /// <c>IrOp_ReadLocal</c>/<c>IrOp_WriteLocal</c> index into <b>this list</b>, never into
    /// <c>Stage5.FindVariableIndex</c>'s asset-level union of Variables/WorkingState/Parameters. That
    /// union is a priority-ordered space whose meaning <c>EmissionContext.VarFieldName</c> and
    /// <c>FindVariableIndex</c> already disagree about (see <c>FINDING_Variable_Index_Space.md</c>);
    /// putting locals into it would add a fourth list to a space that cannot express three.
    /// </para>
    ///
    /// <para>⚠ Locals are emitted as plain C# locals, so <c>State</c> does not grow.</para>
    /// </summary>
    public IReadOnlyList<IrField> Locals { get; init; } = Array.Empty<IrField>();

    public IReadOnlyList<IrBlock> Blocks { get; init; } = Array.Empty<IrBlock>();
    public IrBlockId Entry { get; init; }
    /// <summary>
    /// Maps every authored exec node to the probe id of its containing block
    /// (many-to-one: multiple exec nodes can share a block, all mapping to the
    /// block's SourceNodeId).  Data nodes are absent.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> BreakpointTargets { get; init; } = new Dictionary<Guid, Guid>();
}
