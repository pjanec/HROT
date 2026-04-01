namespace Hrot.Common;

/// <summary>
/// Published on the <c>FdpEventBus</c> when the active UI perspective changes.
/// Consumed by <c>PerspectiveCoordinatorSystem</c> (WM-S703) to synchronise
/// subsystem visibility and camera state with the new perspective.
/// </summary>
/// <param name="OldPerspective">The perspective that was active before the switch.</param>
/// <param name="NewPerspective">The perspective that is now active.</param>
public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);
