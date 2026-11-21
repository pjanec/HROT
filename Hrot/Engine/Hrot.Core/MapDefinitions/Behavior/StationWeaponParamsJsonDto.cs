using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>Which of a person's carry stations (§7.13) a carried weapon goes to.</summary>
    public enum WeaponStation : byte
    {
        /// <summary>His hands — the weapon is drawn and can be fired.</summary>
        Hands = 1,
        /// <summary>His back — slung: carried and seen, but not fired.</summary>
        Back = 2,
        /// <summary>Hidden — concealed: nobody sees it and it arms nobody.</summary>
        Hidden = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-2238</c> part 4 (AW-11, AW-16; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.7 CO-2) — the authored contract of
    /// <c>StationWeapon</c>: he puts his carried weapon in <see cref="Station"/> (draw · sling · conceal), holds still <see cref="Seconds"/> and
    /// finishes. 0 seconds = he finishes as soon as it is there.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct StationWeaponParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.StationWeapon;

        /// <summary>Where the weapon goes: Hands (draw), Back (sling), Hidden (conceal). Unset = Hands.</summary>
        [JsonPropertyName("Station")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WeaponStation Station;

        /// <summary>How long he holds still once it is there (s); 0 = finish at once.</summary>
        [JsonPropertyName("Seconds")]
        public float Seconds;
    }
}
