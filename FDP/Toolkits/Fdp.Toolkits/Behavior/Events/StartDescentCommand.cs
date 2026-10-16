using Fdp.Core;
using Fdp.Toolkit.Motion;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// ⭐ Airborne insertion B-4 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §1, the CE-3059 resolution) — "<see cref="Passenger"/>
    /// has left <see cref="Anchor"/> by its rope or door: start his descent". Raised by <c>Embarkation.Disembark</c> on the CARRIER's owner
    /// (the passenger list's node, R-224) when it does not own the passenger's position, and carried by the interaction transport to the
    /// node that DOES (target = the passenger, actor = the carrier); <c>EmbarkationSystem</c> there adds the <see cref="Descent"/>, and the
    /// descent model puts him at the door. ⭐ The position's owner is the only node whose writes stick (<c>Embarkation.Leave</c>).
    /// </summary>
    [EventId(BehaviorConstants.EventId_StartDescent)]
    public struct StartDescentCommand
    {
        /// <summary>The man coming down (the interaction's TARGET).</summary>
        public Entity Passenger;
        /// <summary>The carrier he left (the ACTOR on the wire).</summary>
        public Entity Anchor;
        /// <summary>By its rope (<see cref="ExitKind.RopeDown"/>) or its door (<see cref="ExitKind.JumpOut"/>).</summary>
        public ExitKind Exit;
        /// <summary>Which rope or door (the carrier's <c>RopeAnchors</c> index).</summary>
        public byte AnchorIndex;
        /// <summary>⭐ H-1 (D-18) — his landing point (<see cref="DisembarkEntityCommand.Target"/>; zero = none). APPENDED.</summary>
        public System.Numerics.Vector3 Target;
        /// <summary>Set by the interaction ingress: this command came off the network.</summary>
        public bool IsRemote;
    }
}
