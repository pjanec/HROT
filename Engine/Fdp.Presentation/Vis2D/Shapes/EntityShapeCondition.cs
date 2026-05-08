using System;

namespace Fdp.Toolkit.Vis2D.Shapes;

/// <summary>
/// Bitmask of runtime conditions that can selectively show or hide individual
/// polyline elements within an <see cref="EntityShapeProfile"/>.
///
/// <para>
/// The flags are intentionally domain-agnostic: each hosting subsystem (CGF,
/// SimHost, Editor) maps its own ECS components to these flags via gizmo
/// rendering logic.
/// </para>
/// </summary>
[Flags]
public enum EntityShapeCondition : uint
{
    None      = 0,
    Damaged   = 1 << 0,
    Destroyed = 1 << 1,
    Immobile  = 1 << 2,
    Firing    = 1 << 3,
}
