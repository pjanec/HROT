using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2306</c> (DA-93…DA-95; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8e) — the authored contract of
    /// <c>TriggerIed</c>: the trigger man watches the placed <see cref="Charge"/> and sets it off by phone when he sees a hostile vehicle (or,
    /// with <see cref="AnyTarget"/>, anyone hostile) within <see cref="KillZoneMetres"/> of it. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct TriggerIedParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.TriggerIed;

        /// <summary>The charge he sets off (a placed IED, a VBIED's cargo — any charge not his own).</summary>
        [JsonPropertyName("Charge")]
        public EntityRef Charge;

        /// <summary>The kill zone: within this distance of the charge (flat, m); 0 = 10.</summary>
        [JsonPropertyName("KillZoneMetres")]
        public float KillZoneMetres;

        /// <summary>DA-94 — people too (a foot patrol), not only vehicles.</summary>
        [JsonPropertyName("AnyTarget")]
        public bool AnyTarget;

        /// <summary>He gives up after this long (s); 0 = never.</summary>
        [JsonPropertyName("TimeoutSeconds")]
        public float TimeoutSeconds;
    }
}
