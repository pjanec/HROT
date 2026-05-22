using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a single wire (link) connecting two pins.</summary>
public interface ILinkModel
{
    /// <summary>Stable id.</summary>
    LinkId Id { get; }

    /// <summary>Source pin (output side).</summary>
    PinId FromPin { get; }

    /// <summary>Target pin (input side).</summary>
    PinId ToPin { get; }

    /// <summary>Wire style (solid/dashed/etc).</summary>
    LinkStyle Style { get; }

    /// <summary>
    /// Reroute waypoint positions, in canvas coordinates, ordered along the
    /// wire from source to target. Empty if no reroutes.
    /// </summary>
    IReadOnlyList<Vector2> Waypoints { get; }
}

/// <summary>Wire rendering style.</summary>
public enum LinkStyle
{
    Solid,
    Dashed,
}
