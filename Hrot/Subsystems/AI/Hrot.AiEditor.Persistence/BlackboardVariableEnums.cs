namespace Hrot.AiEditor.Persistence;

/// <summary>
/// Authoring role of a blackboard variable (S3-1).
/// Default is Input (value 0) so omit-when-default serialization works correctly.
/// </summary>
public enum BlackboardVariableRole
{
    /// <summary>A parameter / input value. Default.</summary>
    Input  = 0,
    /// <summary>Mutable working state.</summary>
    State  = 1,
}

/// <summary>
/// Scope of a State-role blackboard variable (S3-1).
/// Determines how the slot key is computed and how the slot is provisioned.
/// Default is Node (value 0) so omit-when-default serialization works correctly.
/// Only meaningful when <see cref="BlackboardVariableRole"/> is <see cref="BlackboardVariableRole.State"/>.
/// </summary>
public enum WorkingStateScope
{
    /// <summary>
    /// ⛔⛔ <b><c>CE-435</c> — NOT AUTHORABLE since <c>2026-09-29</c>.</b> Per-node local slot, and the
    /// enum's DEFAULT.
    /// <para>🔴 A <b>standalone</b> <c>Role=State</c> variable at this scope is <b>silently skipped</b>
    /// by both bridge emitters — no slot, no diagnostic (<c>CE-423</c>) — because the Node key formula
    /// folds <c>assetId ++ nodeVisualId</c> and ignores the variable NAME, so a variable with no node
    /// has nothing to key off.</para>
    /// <para>⭐ <b>The VALUE stays and is still load-bearing:</b> it keys node-BOUND working state for
    /// hosted AiPrimitives, which is the common case and is not authored through the Variables panel.
    /// ⛔ What was removed is the author-facing CHOICE.</para>
    /// </summary>
    Node     = 0,

    /// <summary>
    /// ⭐⭐⭐ <b>THE ONLY AUTHORABLE SCOPE.</b> Shared across all nodes within one behavior assignment
    /// on an entity. <c>BehaviorTreeAsset</c>/<c>HsmAsset.UpdateVariableRole</c> force this whenever a
    /// variable's Role becomes <c>State</c>, so the panel offers no choice.
    /// </summary>
    Behavior = 1,

    // ⛔ HISTORY — Entity = 2 (shared across behaviours AND entities, name-only key, CE-422) was REMOVED by
    //   CE-441 slice 1 (Q76 §12.25): its last variable left with CE-440. A legacy file carrying "Entity" now
    //   fails to load. Do not reuse 2.
}
