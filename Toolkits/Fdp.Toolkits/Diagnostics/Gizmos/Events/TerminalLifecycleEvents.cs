namespace Fdp.Toolkit.Diagnostics.Gizmos.Events
{
    // Published on FdpEventBus when a debug terminal (local Raylib window or remote DDS)
    // begins participation in the gizmo pipeline. Used as an informational notification;
    // the actual execution gate is driven by GizmoExecutionController directly.
    public sealed class TerminalConnectedEvent
    {
        public long TerminalId { get; init; }
    }

    // Published on FdpEventBus when a debug terminal leaves the gizmo pipeline.
    // Used as an informational notification; teardown is driven synchronously by
    // GizmoExecutionController.RemoveListener() without relying on this event.
    public sealed class TerminalDisconnectedEvent
    {
        public long TerminalId { get; init; }
    }
}
