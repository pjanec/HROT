using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Vis2D.Components;

/// <summary>
/// Defines parent-child relationships for hierarchical entities (ORGBAT).
/// Forms a linked-list tree structure.
/// </summary>
[ComponentId(GlobalComponentIds.VisHierarchyNode)]
[DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
public struct VisHierarchyNode
{
    public Entity Parent;
    public Entity FirstChild;
    public Entity NextSibling;
}

/// <summary>
/// Component attached to Logical Nodes (Parent entities).
/// Updated automatically by the aggregation system.
/// </summary>
[ComponentId(GlobalComponentIds.AggregateState)]
[DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
public struct AggregateState
{
    public Vector2 Centroid;       // Average position of children
    public Vector2 BoundsMin;      // World AABB Min
    public Vector2 BoundsMax;      // World AABB Max
    public int ActiveChildCount;   // How many children are alive
    
    public bool IsValid => ActiveChildCount > 0;
    public Vector2 Size => BoundsMax - BoundsMin;
}

/// <summary>
/// Tag component to mark root entities in hierarchy.
/// </summary>
[ComponentId(GlobalComponentIds.AggregateRoot)]
[DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
public struct AggregateRoot { }

