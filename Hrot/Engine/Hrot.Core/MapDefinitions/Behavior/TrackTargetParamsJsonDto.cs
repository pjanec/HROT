using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>WL-4</c> L-8 (📄 docs/DESIGN_Weapon_Layers.md §5f) — the authored contract of the curated <c>TrackTarget</c> behaviour:
    /// keep mount <see cref="Mount"/>'s turret on the unit's top threat without firing. A STRUCT of unmanaged fields.
    /// </summary>
    [BehaviorContract(BehaviorId, BehaviorCategory.AllMilitary)]
    public struct TrackTargetParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.TrackTarget;

        /// <summary>The mount whose turret follows the target (its index in the type's weapon suite); 0 = the primary.</summary>
        [JsonPropertyName("mount")]
        public int Mount { get; set; }
    }
}
