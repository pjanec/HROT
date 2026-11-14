namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐ <c>CE-4582</c> (docs/DESIGN_Map_3D_Mode.md §6am D-1, D-4) — what the card renderer asks while it draws: hide each card behind
    /// whatever stands in front of its ENTITY. The renderer knows screens, not worlds — the host that knows the 3-D camera implements it
    /// (<c>Fdp.Toolkit.Vis3D.CardOcclusion</c>); in 2-D there is none and cards are drawn as before.
    /// </summary>
    public interface ICardOcclusion
    {
        /// <summary>True while a depth-tested scope is open (the host's toggle on, a 3-D camera).</summary>
        bool Active { get; }

        /// <summary>Everything drawn until <see cref="Pop"/> is tested at the depth of entity <paramref name="networkId"/>'s anchor.
        /// False when its anchor is unknown: the card is then drawn untested (on top). Always pair with <see cref="Pop"/>.</summary>
        bool Push(long networkId);

        /// <summary>Ends the depth of the last <see cref="Push"/>.</summary>
        void Pop();

        /// <summary>Closes the scope (depth test off). Idempotent — what is drawn after it is on top.</summary>
        void End();
    }
}
