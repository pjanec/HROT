using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2331</c> (AQ98 A/B, R-2070) — the authored contract of <c>FightFromCover</c>, the soldier's cover reaction: it fights from cover
    /// (<c>PeekAndFire</c>, the library defaults) and ends once he is in cover with no hit or near miss on him for <see cref="QuietSeconds"/>.
    /// A zero field keeps its default. 📄 docs/blueprints/Architect_Question_98_Cover_Reaction_Fights_Back.md §3.
    /// </summary>
    [ParamsDto]
    public struct FightFromCoverParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.FightFromCover;

        /// <summary>How long nothing must land near him, once in cover, before the reaction ends (s); 0 = 8.</summary>
        [JsonPropertyName("QuietSeconds")]
        public float QuietSeconds;
    }
}
