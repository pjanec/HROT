using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ Buildings Stage 5d (📄 docs/DESIGN_Building_Interiors.md §3j "5d") — "do <see cref="Verb"/> to this door", sent by a node
    /// that does NOT own the door to the one that does. The owner applies it with <c>DoorRules</c>; the result reaches every node as
    /// the door's <c>EntityDoorState</c> — there is no reply message.
    /// </summary>
    // Reliable + KeepAll: commands are events — a KeepLast reader drops one of two commands taken together (CE-3095).
    [DdsTopic("EntityDoorCommand")]
    [DdsIdlFile("hrot-sim-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct EntityDoorCommand
    {
        /// <summary>Network entity id of the door.</summary>
        public long DoorEntityId;

        /// <summary><c>DoorVerb</c>: 0 Open · 1 Close · 2 Lock · 3 Unlock · 4 Breach.</summary>
        public byte Verb;

        /// <summary>Network entity id of the actor, or 0 (an operator / unknown).</summary>
        public long ActorEntityId;
    }
}
