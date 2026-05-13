namespace Hrot.Common.Diagnostics.Gizmos;

/// <summary>
/// Exposes the <see cref="GizmoExecutionController"/> of a subsystem so that the
/// <c>PerspectiveCoordinatorSystem</c> can transfer the listener count when the
/// active perspective changes.
/// </summary>
public interface IGizmoControllable
{
    /// <summary>Returns the gizmo execution controller for this subsystem, or <c>null</c> if not applicable.</summary>
    Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? GizmoController { get; }
}
