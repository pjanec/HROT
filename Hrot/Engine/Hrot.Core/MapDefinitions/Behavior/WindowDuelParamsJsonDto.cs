using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-8 (D10; 📄 docs/DESIGN_Peek_And_Fire.md §8.4) — the authored contract of the curated <c>WindowDuel</c> behaviour:
    /// <c>PeekAndFire</c> with one of the two duel shapes, the design's A / B columns, plus a few overrides (0 = the shape's value).
    /// A STRUCT of unmanaged fields, so a host can also bind it by bytes.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct WindowDuelParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.WindowDuel;

        /// <summary>0 = <c>window</c> (A: a window firing position, a stance peek, rotates windows) · 1 = <c>street</c> (B: cover in
        /// the street, a step peek, suppresses before it bounds to the next cover).</summary>
        [JsonPropertyName("shape")]
        public int Shape;

        /// <summary>How far the hide-point query looks (m); 0 = the shape's (window 12, street 25).</summary>
        [JsonPropertyName("searchRadius")]
        public float SearchRadius;

        /// <summary>Exposures from one position before moving on; 0 = the shape's (window 3, street 2).</summary>
        [JsonPropertyName("exposuresPerPosition")]
        public int ExposuresPerPosition;

        /// <summary>Aimed rounds per exposure; 0 = 3.</summary>
        [JsonPropertyName("roundsPerExposure")]
        public int RoundsPerExposure;

        /// <summary>Rounds of a blind (or suppressive) burst; 0 = the shape's (window 2, street 4).</summary>
        [JsonPropertyName("blindRounds")]
        public int BlindRounds;
    }
}
