using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2436</c> AU-3 — the authored contract of a PLATOON's <c>PlatoonSupportByFire</c> (its squads each <c>SupportByFire</c>, spread across the
    /// line of fire); the fields as the squad's — <see cref="SupportByFireParamsJsonDto"/>.
    /// ⭐ <c>CE-2436</c> AU-1 (A-1) — the authored contract of a squad's <c>SupportByFire</c>: hold at <see cref="Position"/> and keep sustained
    /// fire on the reported posts <see cref="Post1"/>…<see cref="Post4"/> (an unset post is (0, 0, 0)), whether or not a man sees anyone there.
    /// The order's posts change when the base of fire SHIFTS (a new order). A zero field keeps its default.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Assault_Under_Fire.md §5 A-1, A-3.
    /// </summary>
    [ParamsDto]
    public struct PlatoonSupportByFireParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.PlatoonSupportByFire;

        /// <summary>Where the base of fire sets (world m).</summary>
        [JsonPropertyName("Position")]
        public Vector3 Position;

        /// <summary>The first reported post to keep under fire (world m).</summary>
        [JsonPropertyName("Post1")]
        public Vector3 Post1;

        /// <summary>The second reported post (unset = (0, 0, 0)).</summary>
        [JsonPropertyName("Post2")]
        public Vector3 Post2;

        /// <summary>The third reported post (unset = (0, 0, 0)).</summary>
        [JsonPropertyName("Post3")]
        public Vector3 Post3;

        /// <summary>The fourth reported post (unset = (0, 0, 0)).</summary>
        [JsonPropertyName("Post4")]
        public Vector3 Post4;

        /// <summary>Move speed to the support point (m/s); 0 = the squad's own.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>Seconds between a man's aimed rounds (blind rounds go slower); 0 = 1 s.</summary>
        [JsonPropertyName("CooldownSeconds")]
        public float CooldownSeconds;
    }
}
