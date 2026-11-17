using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4650</c> NA-4 (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §9) — the authored contract of <c>CircleRaid</c>, run by a COMMANDER whose roster is MOUNTS (horses with a rider in the saddle): each mount rides to its own
    /// point on a ring round <see cref="Objective"/>, then CIRCLES it at a gallop at 40–80 m (<see cref="RadiusMin"/>…<see cref="RadiusMax"/>) for <see cref="Circle"/> while its rider shoots <see cref="Target"/>; a rider who is hit
    /// BREAKS OFF to <see cref="Rally"/> for <see cref="Breakoff"/> and rejoins; at the end every mount rides to the rally point. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct CircleRaidParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CircleRaid;

        [JsonPropertyName("Objective")]
        public PointLink Objective;

        [JsonPropertyName("Rally")]
        public PointLink Rally;

        /// <summary>What the riders shoot (a guard of the camp); none = the riders' own weapon brains decide.</summary>
        [JsonPropertyName("Target")]
        public EntityLink Target;

        /// <summary>The nearest and farthest circle (m); 0 = 40 / 80.</summary>
        [JsonPropertyName("RadiusMin")]
        public float RadiusMin;

        [JsonPropertyName("RadiusMax")]
        public float RadiusMax;

        /// <summary>The gallop (m/s); 0 = 12.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How long they circle; 0 = 40 s.</summary>
        [JsonPropertyName("Circle")]
        public SimDuration Circle;

        /// <summary>How long a hit rider stays away before he rejoins; 0 = 12 s.</summary>
        [JsonPropertyName("Breakoff")]
        public SimDuration Breakoff;
    }
}
