using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-b (📄 docs/DESIGN_Entity_Interactions.md §7.4.10) — the authored contract of the library <c>CarryCasualty</c>: walk to
    /// <see cref="Casualty"/>, take him (dragged unless <see cref="Carry"/>), bring him to (<see cref="X"/>, <see cref="Y"/>) and put him down.
    /// Flat JSON (orders design §1 P2); the casualty is an <see cref="EntityRef"/> (its network id), so the editor offers an entity pick.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct CarryCasualtyParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CarryCasualty;

        /// <summary>The man who is down.</summary>
        [JsonPropertyName("casualty")]
        public EntityRef Casualty;

        /// <summary>Where to bring him (world X, m).</summary>
        [JsonPropertyName("x")]
        public float X;

        /// <summary>Where to bring him (world Y, m).</summary>
        [JsonPropertyName("y")]
        public float Y;

        /// <summary>Carry him on the shoulder instead of dragging (faster, higher).</summary>
        [JsonPropertyName("carry")]
        public bool Carry;

        /// <summary>Walking speed (m/s), to him and back; 0 ⇒ 3 m/s. The way back is further slowed by the load (C8).</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
