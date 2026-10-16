using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Unmanaged command event requesting that a passenger entity exits their current vehicle.
    /// Published by mission-layer systems and consumed by <c>EmbarkationSystem</c>
    /// to execute the actual component-level disembarkation.
    /// </summary>
    [EventId(BehaviorConstants.EventId_DisembarkEntity)]
    public struct DisembarkEntityCommand
    {
        /// <summary>The entity that should exit its current vehicle.</summary>
        public Entity Passenger;

        /// <summary>⭐ <c>CE-3110</c> I-2a — the vehicle it leaves (the interaction's TARGET, carried to that vehicle's owner). Null ⇒
        /// the one the passenger's <c>IsEmbarkedTag</c> names.</summary>
        public Entity Vehicle;

        /// <summary>⭐ <c>CE-3110</c> I-2a (R-221) — set by the interaction ingress: this command came off the network.</summary>
        public bool IsRemote;

        /// <summary>⭐ Airborne insertion B-4 (D-8) — how he leaves: <see cref="Fdp.Toolkit.Motion.ExitKind.StepOut"/> (beside the hull,
        /// the default), down a rope, or out of the door under a parachute. APPENDED.</summary>
        public Fdp.Toolkit.Motion.ExitKind Exit;

        /// <summary>⭐ B-4 — which rope or door (the carrier's <c>RopeAnchors</c> index; 0 = the first). APPENDED.</summary>
        public byte AnchorIndex;

        /// <summary>⭐ Airborne insertion H-1 (design §6.5 D-18) — a jumper's landing point (zero = wherever he comes down): written into his
        /// <c>Descent.Target</c> by the node owning his position. The jumpmaster's decision; ignored for a step-out or a rope. APPENDED.</summary>
        public System.Numerics.Vector3 Target;
    }
}
