using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-b; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.5) — the authored contract of the library
    /// <c>Suppress</c>: hold and put rounds on <see cref="Target"/> — aimed when seen, blind at where you remember him when hidden, at
    /// (<see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>) when you never saw him. Flat JSON (orders design §1 P2); the target is an
    /// <see cref="EntityRef"/> (its network id), so the editor offers an entity pick.
    /// </summary>
    [ParamsDto]
    public struct SuppressParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Suppress;

        /// <summary>The contact to suppress.</summary>
        [JsonPropertyName("target")]
        public EntityRef Target;

        /// <summary>Where he was last reported (world X, m) — used when this soldier has no memory of him.</summary>
        [JsonPropertyName("x")]
        public float X;

        /// <summary>Where he was last reported (world Y, m).</summary>
        [JsonPropertyName("y")]
        public float Y;

        /// <summary>Where he was last reported (world Z, m).</summary>
        [JsonPropertyName("z")]
        public float Z;

        /// <summary>Seconds between aimed rounds; 0 ⇒ 1 s. Blind rounds go slower.</summary>
        [JsonPropertyName("cooldownSeconds")]
        public float CooldownSeconds;
    }
}
