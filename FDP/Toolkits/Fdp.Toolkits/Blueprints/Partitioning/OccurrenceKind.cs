namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// A3 / <c>D1′</c> (<c>DESIGN_Occurrence_Scoped_Storage</c> §13) — <b>what kind of thing occupies a
/// slot</b>, declared by whoever attached it rather than inferred from a registry miss.
///
/// <para>⭐⭐ <b>Why this exists.</b> Today <c>BlueprintTickSystem</c>'s walker skips non-blueprint
/// slots with <c>_registry.TryGetById(slot.BlueprintId, …) → continue</c>. 📐 That works <b>only</b>
/// because <c>BlueprintRegistry</c> happens not to know an FNV stateful key — it is an accident, not
/// a filter (<c>F7</c>/<c>F8</c>). ⛔ Once the walker runs on every ECS host (<c>O0</c>) it must
/// filter on a <b>declared</b> kind.</para>
///
/// <para>🔴 <b><see cref="Invalid"/> is 0 ON PURPOSE, and it is load-bearing.</b>
/// <c>BlueprintBlackboardPartitions.Initialize</c> zeroes the whole component, so <c>0</c> is what an
/// un-migrated, un-promoted or never-declared slot reads. ⛔ If <c>0</c> meant <c>Blueprint</c>, the
/// walker would resume filtering <b>by accident</b> — the very thing <c>D1′</c> exists to retire.
/// ⇒ ⛔ <b>never renumber a member onto 0.</b></para>
///
/// <para>⚠ <b>The width is 4 bits.</b> The kind is stored as a nibble per slot in
/// <c>BlueprintBlackboardHeader.Reserved</c> (8 B = 16 slots × 4 bits, an exact fit), so a member
/// value above <see cref="BlueprintBlackboardPartitions.MaxKind"/> cannot be stored —
/// <c>SetSlotKind</c> throws rather than truncating. ⭐ 12 of the 16 values are still free.</para>
///
/// <para>⚠ <b>Runtime-only, not an ABI.</b> Blackboard tier components are deliberately excluded from
/// the scenario save (<c>BlueprintBlackboardNoSaveTests</c>), so these numbers never reach a saved
/// scenario or a replay — unlike behavior ids, which <c>R-42</c> makes permanent. ⭐ They are still
/// worth keeping stable across a session because a live HTTP/inspector read reports them.</para>
/// </summary>
public enum OccurrenceKind : byte
{
    /// <summary>
    /// 🔴 <b>Not a kind — "nobody declared one".</b> What a zeroed, un-migrated or vacated slot reads.
    /// ⛔ Never assign a real kind to 0; see the type remarks.
    /// </summary>
    Invalid = 0,

    /// <summary>A blueprint Instance occurrence — payload <c>[BlueprintLatentCursor 16][Params][State]</c>.</summary>
    Blueprint = 1,

    /// <summary>A BTree occurrence — stateful node working state, and (from <c>O4</c>) tree state + params.</summary>
    BTree = 2,

    /// <summary>An HSM occurrence — per-region state (from <c>O7</c>) and stateful working state.</summary>
    Hsm = 3,
}
