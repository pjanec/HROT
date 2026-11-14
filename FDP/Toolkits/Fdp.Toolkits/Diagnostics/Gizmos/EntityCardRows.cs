using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-4582</c> (docs/DESIGN_Map_3D_Mode.md §6am D-7) — the card rows of what used to be FREE text at a unit: every line ABOUT a
    /// unit is a row of ITS card (one card per unit), on its own layer. Rows stack top-down by number; the older rows are owned by their
    /// gizmos (frame 0, health 10, mind state 11–12, name 20, overhead mark 45, concealed 46, effect 50). One line = one row, so a gizmo
    /// that writes several lines owns a range.
    /// </summary>
    public static class EntityCardRows
    {
        /// <summary>Squad command: the commander's <c>SQUAD n phN</c> (<c>SquadGizmo</c>).</summary>
        public const byte SquadPhase = 60;
        /// <summary>Squad command: a member's element and role <c>E#R#</c>, on the MEMBER's card (<c>SquadGizmo</c>).</summary>
        public const byte MemberRole = 61;
        /// <summary>Squad command: a subordinate's last reported progress, on the SUBORDINATE's card (<c>CommandPictureGizmo</c>).</summary>
        public const byte MemberProgress = 62;
        /// <summary>Squad command: a selected commander whose picture has not reached this node (<c>CommandPictureAbsentGizmo</c>).</summary>
        public const byte PictureAbsent = 63;
        /// <summary>Actions: the first of the busy-channel lines (L, W, I → 70, 71, 72; <c>ActionStatusGizmo</c>).</summary>
        public const byte ActionsFirst = 70;
        /// <summary>Actions: the peek-and-fire phase (<c>PeekAndFireGizmo</c>).</summary>
        public const byte PeekPhase = 73;
        /// <summary>Utility: the first decision line; one row per logged decision, up to <see cref="UtilityLast"/> (<c>UtilityDecisionGizmo</c>).</summary>
        public const byte UtilityFirst = 74;
        public const byte UtilityLast = 79;
        /// <summary>Unit details: the stance (<c>StanceGizmo</c>).</summary>
        public const byte Stance = 80;
        /// <summary>Unit details: the heading in compass degrees (<c>EntityRotationGizmo</c>).</summary>
        public const byte Heading = 81;

        /// <summary>The id a card is keyed by: the entity's network id, or 0 (no card) when it has none or the view cannot say.</summary>
        public static int CardIdOf(ISimulationView view, Entity entity)
            => view is EntityRepository repo && repo.IsComponentTypeRegistered<NetworkIdentity>() && repo.IsAlive(entity)
               && repo.HasComponent<NetworkIdentity>(entity)
                ? repo.GetComponentRO<NetworkIdentity>(entity).Value
                : 0;
    }
}
