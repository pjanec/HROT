using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>⭐ <c>CE-2360</c> (AQ102 I-3) — what a man does about a live grenade near him.</summary>
    public enum GrenadeResponse : byte
    {
        /// <summary>Run straight away from it, out of its lethal radius, and drop there.</summary>
        Run = 0,
        /// <summary>No time to get clear: a step away (his back to it) and flat on the ground.</summary>
        Prone = 1,
        /// <summary>Enough fuze left: pick it up and throw it back toward where it came from.</summary>
        ThrowBack = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-2360</c> (AQ102 I-3, R-2072) — the contract of <c>EvadeGrenade</c>, the reflex <c>GrenadeDangerSystem</c> starts as a
    /// reaction when a live grenade lies near a man: his answer, where it lies, how far it kills, the fuze it had left, and where to throw
    /// it back. 📄 docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md §3.3.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct EvadeGrenadeParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.EvadeGrenade;

        /// <summary>Run, Prone or ThrowBack.</summary>
        [JsonPropertyName("Response")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GrenadeResponse Response;

        /// <summary>Where the grenade lies (sim metres).</summary>
        [JsonPropertyName("At")]
        public Vector3 At;

        /// <summary>Its lethal radius (m); 0 = 5 (an M67's).</summary>
        [JsonPropertyName("LethalMetres")]
        public float LethalMetres;

        /// <summary>The fuze it had left when he reacted (s); 0 = not known.</summary>
        [JsonPropertyName("FuzeSeconds")]
        public float FuzeSeconds;

        /// <summary>Where a throw back goes (its thrower, or where it came from).</summary>
        [JsonPropertyName("ThrowAt")]
        public Vector3 ThrowAt;
    }
}
