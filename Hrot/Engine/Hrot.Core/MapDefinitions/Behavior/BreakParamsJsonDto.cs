using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2272</c> (TS-6, R-2034) — the contract of <c>Break</c>, which <c>StressSystem</c> starts as a reaction when a man's stress
    /// reaches his nerve: where he runs to (a cover point away from the threat), or nowhere — then he goes to ground where he is.
    /// 📄 docs/DESIGN_Temperament_And_Stress.md §4 TS-6.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct BreakParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Break;

        /// <summary>Whether he has somewhere to run to.</summary>
        [JsonPropertyName("HasRetreat")]
        public bool HasRetreat;

        /// <summary>Where he runs to (when <see cref="HasRetreat"/>).</summary>
        [JsonPropertyName("Retreat")]
        public Vector3 Retreat;
    }
}
