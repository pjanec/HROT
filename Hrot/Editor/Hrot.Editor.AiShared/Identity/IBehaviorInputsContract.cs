namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐ <c>CE-439</c> (<c>Q76</c> §12.28) — a behaviour asset that publishes an Inputs struct, as seen by a HOST that binds it as a
/// hosted subtree. ⭐ One member, so the HSM editor can read a BTree child's contract through the catalogue without referencing
/// the BTree editor.
/// </summary>
public interface IBehaviorInputsContract
{
    /// <summary>The type id (no <c>global::</c>) of the generated Inputs struct, or <c>null</c> when the behaviour publishes
    /// none (no <c>Role=Input</c> variable, or an unmanaged blackboard).</summary>
    string? InputsTypeId { get; }
}
