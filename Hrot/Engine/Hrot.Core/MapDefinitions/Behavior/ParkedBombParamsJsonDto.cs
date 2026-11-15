using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4621</c> M-5 (IM-2; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2 / §5) — the authored contract of <c>ParkedBomb</c>: the
    /// vehicle rides to <see cref="Spot"/>, parks, arms <see cref="Charge"/>'s timer to go off <see cref="Delay"/> later and puts its riders out.
    /// A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct ParkedBombParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ParkedBomb;

        /// <summary>The charge the vehicle carries (its cargo — it stays where the vehicle parks).</summary>
        [JsonPropertyName("Charge")]
        public EntityLink Charge;

        /// <summary>Where it parks.</summary>
        [JsonPropertyName("Spot")]
        public PointLink Spot;

        /// <summary>From parking to the burst; 0 = 30 s.</summary>
        [JsonPropertyName("Delay")]
        public SimDuration Delay;

        /// <summary>The ride in (m/s); 0 = 12.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;
    }
}
