namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Catalog of Blueprint node types wrapping the 5 squad coordination primitives (TASK-SQD-P6-02).
/// Provides display metadata for use by the Blueprint editor palette.
/// </summary>
public static class SquadPrimitiveNodeCatalog
{
    /// <summary>All squad primitive node entries.</summary>
    public static readonly SquadPrimitiveNodeEntry[] Entries = new SquadPrimitiveNodeEntry[]
    {
        new SquadPrimitiveNodeEntry(
            Kind:        "PartitionElements",
            DisplayName: "Partition Elements",
            Category:    "Squad/Primitives",
            Tooltip:     "Partition squad members into N elements with hysteresis."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AssignRoles",
            DisplayName: "Assign Roles",
            Category:    "Squad/Primitives",
            Tooltip:     "Assign roles to squad members via greedy score matrix."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AdvancePhase",
            DisplayName: "Advance Phase",
            Category:    "Squad/Primitives",
            Tooltip:     "Advance the squad phase sequencer on an event."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AcquireSlot",
            DisplayName: "Acquire Slot",
            Category:    "Squad/Primitives",
            Tooltip:     "Acquire the next available slot from the rotation ring."),
    };
}

/// <summary>Metadata entry for a squad primitive Blueprint node type.</summary>
public sealed record SquadPrimitiveNodeEntry(
    string Kind,
    string DisplayName,
    string Category,
    string Tooltip);
