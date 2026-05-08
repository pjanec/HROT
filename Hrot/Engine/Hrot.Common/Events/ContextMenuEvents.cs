namespace Hrot.Common.Events
{
    /// <summary>
    /// Sent from ExCon to IG to update the context-menu definition available
    /// for a specific network entity.
    ///
    /// <para>The payload is a pre-serialised JSON string matching the
    /// <c>ContextMenuItemDto</c> array schema.  Consumers must <b>not</b> parse
    /// this string on the hot path — pass it directly to
    /// <c>ContextMenuAdapter.Schedule</c>.</para>
    /// </summary>
    public sealed class ContextActionsUpdate
    {
        /// <summary>Network identity of the entity whose menu is being updated.</summary>
        public int EntityNetworkId { get; init; }

        /// <summary>
        /// Pre-serialised JSON array of <c>ContextMenuItemDto</c> objects.
        /// Replaces any previously stored menu definition for this entity.
        /// </summary>
        public string MenuJson { get; init; } = string.Empty;
    }

    /// <summary>
    /// Published when the operator selects a context-menu action.
    ///
    /// <para>On the SimHost side this is raised by
    /// <see cref="Hrot.Network.NED.Gizmos.GizmoInteractionIngressSystem"/> when a
    /// <c>GizmoInteractionBatch.MenuAction</c> arrives from the IG terminal.
    /// On the IG side it is raised for non-local actions that must be routed to
    /// the authoritative domain.</para>
    /// </summary>
    public sealed class ContextActionTriggered
    {
        /// <summary>Network identity of the entity on which the action was triggered.</summary>
        public int EntityNetworkId { get; init; }

        /// <summary>Name of the triggered action (typically the integer action ID as a string).</summary>
        public string ActionName { get; init; } = string.Empty;
    }
}
