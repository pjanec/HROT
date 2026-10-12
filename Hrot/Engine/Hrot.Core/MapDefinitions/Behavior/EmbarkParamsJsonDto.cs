using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the authored contract of the library <c>Embark</c>: walk to
    /// <see cref="Vehicle"/> and board it. Flat JSON (orders design §1 P2); the vehicle is an <see cref="EntityRef"/> (its network id),
    /// so the editor offers an entity pick and a scenario load remaps it.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]
    public struct EmbarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Embark;

        /// <summary>The vehicle to board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle { get; set; }

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed { get; set; }
    }

    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — the authored contract of the library <c>Disembark</c>: it takes nothing (the vehicle is the one the unit
    /// is aboard). ⚠ Its OWN empty DTO only because a curated non-order behaviour gets a params DTO through <see cref="BehaviorContractAttribute"/>
    /// today; it becomes <c>NoParams</c> when CE-2134 (B2) moves the contract onto the behaviour, with CE-2131's six empties.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]
    public struct DisembarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Disembark;
    }
}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a) — the authored contract of the squad
    /// <c>SquadDismount</c> (on the commander): every member gets out; done when nobody is aboard.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.Commander)]
    public struct SquadDismountParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadDismount;

        /// <summary>Failure when somebody is still aboard after this long (s); 0 ⇒ 10 s.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds { get; set; }

        /// <summary>The hold speed of a member already out (m/s); 0 ⇒ 3.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("speed")]
        public float Speed { get; set; }
    }
}
