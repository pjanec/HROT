using System.Numerics;
using Fdp.Kernel;

namespace FDP.Toolkit.Vis2D.Components;

/// <summary>
/// Defines parent-child relationships for hierarchical entities (ORGBAT).
/// Forms a linked-list tree structure.
/// </summary>
[ComponentId(GlobalComponentIds.VisHierarchyNode)]
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
public struct AggregateRoot { }

