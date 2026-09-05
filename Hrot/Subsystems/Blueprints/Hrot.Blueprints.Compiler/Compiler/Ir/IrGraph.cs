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
    /// <para>
    /// ⚠ Locals are emitted as plain C# locals — <b>unless <see cref="LocalSlotPrefix"/> is set</b>,
    /// which is Q27-A3's second storage class for graphs that can suspend.
    /// </para>
    /// </summary>
    public IReadOnlyList<IrField> Locals { get; init; } = Array.Empty<IrField>();

    /// <summary>
    /// BP-57 / ⭐⭐ <b>Q27-A3</b> — non-null when this graph's locals are <b>blackboard slots</b> rather
    /// than C# locals, carrying the graph-qualifying prefix their emitted field names share
    /// (<c>__loc_{Graph}_</c>).
    ///
    /// <para>
    /// ⭐ <b>Set for exactly the graphs that can suspend</b> (<c>LocalStorage.CanSuspend</c>). A
    /// suspension is <c>return NodeStatus.Running</c>: the C# frame dies and a stack local with it, so
    /// a value written before a <c>Delay</c> would read back as its default after the resume. The slot
    /// lives in the same struct as <c>__phase</c>, for the same reason.
    /// </para>
    ///
    /// <para>
    /// ⛔ <see cref="Locals"/> keeps the DESIGNER's names either way; the prefix is applied at the two
    /// places that emit an identifier (<c>EmissionContext.LocalFieldName</c> and the slot list in
    /// <c>IrAsset.GraphLocalSlots</c>), both through <c>LocalStorage.SlotName</c>.
    /// </para>
    /// </summary>
    public string? LocalSlotPrefix { get; init; }

    public IReadOnlyList<IrBlock> Blocks { get; init; } = Array.Empty<IrBlock>();
    public IrBlockId Entry { get; init; }
    /// <summary>
    /// Maps every authored exec node to the probe id of its containing block
    /// (many-to-one: multiple exec nodes can share a block, all mapping to the
    /// block's SourceNodeId).  Data nodes are absent.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> BreakpointTargets { get; init; } = new Dictionary<Guid, Guid>();
}
