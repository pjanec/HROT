using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the authored contract of the library <c>Embark</c>: walk to
    /// <see cref="Vehicle"/> and board it. Flat JSON (orders design §1 P2); the vehicle is an <see cref="EntityRef"/> (its network id),
    /// so the editor offers an entity pick and a scenario load remaps it. ⭐ Declared on the behaviour (CE-2134: <c>[Behavior(Params = …)]</c>).
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct EmbarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Embark;

        /// <summary>The vehicle to board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §4b, W-B) — the authored contract of the library <c>ManWeapon</c>:
    /// walk to <see cref="Weapon"/> (a crew weapon system — a mortar, a tripod HMG) and take its GUNNER seat, so the man controls it.
    /// </summary>
    [ParamsDto]
    public struct ManWeaponParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ManWeapon;

        /// <summary>The weapon system to man.</summary>
        [JsonPropertyName("weapon")]
        public EntityRef Weapon;

        /// <summary>Walking speed to it (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a) — the authored contract of the squad
    /// <c>SquadDismount</c> (on the commander): every member gets out; done when nobody is aboard.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct SquadDismountParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadDismount;

        /// <summary>Failure when somebody is still aboard after this long (s); 0 ⇒ 10 s.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;

        /// <summary>The hold speed of a member already out (m/s); 0 ⇒ 3.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("speed")]
        public float Speed;
    }
}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B10, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9) — the authored contract of the squad
    /// <c>SquadEmbark</c> (on the commander): every member boards <see cref="Vehicle"/>; done when all are aboard it.
    /// </summary>
    [ParamsDto]
    public struct SquadEmbarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadEmbark;

        /// <summary>The vehicle to board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Failure when somebody is still out after this long (s); 0 ⇒ 60.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the Embark node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 — the authored contract of the library <c>LoadCasualty</c>: walk to <see cref="Vehicle"/> with the man
    /// carried, seat him in it, then board it.
    /// </summary>
    [ParamsDto]
    public struct LoadCasualtyParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.LoadCasualty;

        /// <summary>The vehicle to load into and board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
