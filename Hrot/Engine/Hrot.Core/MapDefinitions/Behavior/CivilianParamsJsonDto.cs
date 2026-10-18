using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-3; docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — the authored contract of <c>CivilianHsm</c>: how his routine
    /// wanders and when he is alarmed. A civilian started by his TKB type's default carries none of it; every zero field keeps the default.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct CivilianParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CivilianHsm;

        /// <summary>How far from home (where he stood when it started) his routine takes him, m. Default 12.</summary>
        [JsonPropertyName("WanderRadius")]
        public float WanderRadius;

        /// <summary>His walking speed, m/s. Default 1.4.</summary>
        [JsonPropertyName("WalkSpeed")]
        public float WalkSpeed;

        /// <summary>How long he lingers at each spot, s — between the min and the max, at random. Defaults 3 and 10.</summary>
        [JsonPropertyName("PauseMinSeconds")]
        public float PauseMinSeconds;

        [JsonPropertyName("PauseMaxSeconds")]
        public float PauseMaxSeconds;

        /// <summary>The fear (0…1) at which he is alarmed. Default 0.3.</summary>
        [JsonPropertyName("AlarmAt")]
        public float AlarmAt;

        /// <summary>The fear below which an alarmed man goes back to his routine. Default 0.1.</summary>
        [JsonPropertyName("CalmBelow")]
        public float CalmBelow;

        /// <summary>⭐ CE-2236 — the fear (0…1) at which an alarmed man panics. Default 0.6.</summary>
        [JsonPropertyName("PanicAt")]
        public float PanicAt;

        /// <summary>⭐ CE-2236 — how fast he runs when he panics, m/s. Default 3.5.</summary>
        [JsonPropertyName("PanicSpeed")]
        public float PanicSpeed;

        /// <summary>⭐ CE-2236 — how far he looks for a building he can enter, m. Default 80.</summary>
        [JsonPropertyName("ShelterSearchRadius")]
        public float ShelterSearchRadius;

        /// <summary>⭐ CE-2236 (R-4035) — how long he hides inside before he is gone, s — between the min and the max, at random.
        /// Defaults 60 and 180.</summary>
        [JsonPropertyName("HideMinSeconds")]
        public float HideMinSeconds;

        [JsonPropertyName("HideMaxSeconds")]
        public float HideMaxSeconds;

        /// <summary>⭐ CE-2240 (CV-13) — he is a protester: he goes to <see cref="ProtestAt"/> and protests there (a ring round it,
        /// milling, fist raised or a sign held). Default false — a civilian who is not told to protest never does.</summary>
        [JsonPropertyName("Protest")]
        public bool Protest;

        /// <summary>⭐ CE-2240 — where he protests (a building's front, a square): the point the protesters ring.</summary>
        [JsonPropertyName("ProtestAt")]
        public System.Numerics.Vector3 ProtestAt;

        /// <summary>⭐ CE-2238 (§5i, CV-5) — he is an INSTIGATOR: his anger is 1 and he raises the anger of the protesters round him.
        /// Default false.</summary>
        [JsonPropertyName("Instigator")]
        public bool Instigator;

        /// <summary>⭐ CE-2238 — the anger (0…1) at which a protester riots; he calms back to the protest below half of it. Default 0.6.</summary>
        [JsonPropertyName("RiotAt")]
        public float RiotAt;

        /// <summary>⭐ CE-2241 ② (§5m PV-3, CV-4) — his hidden attitude, −1 (friendly) … +1 (hostile at heart); it lowers or raises HIS riot
        /// threshold (<c>RiotAt − 0.5 × Attitude</c>: −1 never riots). Default 0.</summary>
        [JsonPropertyName("Attitude")]
        public float Attitude;
    }
}
