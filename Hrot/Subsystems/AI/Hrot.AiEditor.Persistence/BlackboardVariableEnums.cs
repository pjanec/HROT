namespace Hrot.AiEditor.Persistence;

/// <summary>
/// The stateful slot-key FORMULA the generator picks (S3-1), mirrored by the runtime's <c>OccurrenceSlotScope</c>.
/// ⭐ <c>CE-3193</c> — no longer a property of a variable: a binding that names a working-state variable keys by that name
/// (<see cref="Behavior"/>, the block's <c>St</c> member); one that names none keys by its node (<see cref="Node"/>, a side slot).
/// ⛔ HISTORY: the authored <c>Scope</c> of a <c>Role=State</c> variable; <c>BlackboardVariableRole</c> was deleted with it.
/// </summary>
public enum WorkingStateScope
{
    /// <summary>Keyed by <c>assetId ++ nodeVisualId</c>: a stateful node that names no working-state variable gets its own
    /// side slot (hosted AiPrimitives, the legacy per-node key). The default.</summary>
    Node     = 0,

    /// <summary>Keyed by <c>assetId ++ variable name</c>: the working-state variable a binding names — shared by every node
    /// that names it, the block's <c>St</c> member when the block is emitted.</summary>
    Behavior = 1,

    // ⛔ HISTORY — Entity = 2 (shared across behaviours AND entities, name-only key, CE-422) was REMOVED by
    //   CE-441 slice 1 (Q76 §12.25): its last variable left with CE-440. A legacy file carrying "Entity" now
    //   fails to load. Do not reuse 2.
}
