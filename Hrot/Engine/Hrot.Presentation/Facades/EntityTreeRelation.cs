namespace Hrot.UI.Common.Facades;

/// <summary>
/// ⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views HV-2) — which relation the ONE entity-tree panel shows.
/// <see cref="Scenario"/> (the macro origin tree) is later: it needs the macro entities (S3a), so the panel lists it disabled.
/// </summary>
public enum EntityTreeRelation
{
    /// <summary>Chain of command (<c>UnitSubordinate.Commander</c>) — today's ORBAT; drag assigns a commander / embarks.</summary>
    Command,
    /// <summary>What an entity is a PART of, else the vehicle it rides in — read-only.</summary>
    Structure,
    /// <summary>The macro an entity came from — not built yet.</summary>
    Scenario,
}
