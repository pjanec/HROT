namespace Hrot.Common.Events
{

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

        /// <summary>⭐ <c>CE-3178</c> — true when the click was made on ANOTHER node and relayed here over the gizmo topic
        /// (<c>GizmoInteractionIngressTranslator</c>); false for this node's own menu.</summary>
        public bool Relayed { get; init; }
    }
}
