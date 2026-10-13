using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Terrain;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ Buildings Stage 5d-2 (📄 docs/DESIGN_Building_Interiors.md §3j "5d-2 as built") — the authored contract of the curated
    /// <c>DoorLocksmith</c> behaviour: walk to <see cref="Door"/>, unlock it, open it, then walk to (<see cref="X"/>, <see cref="Y"/>)
    /// — through it. ⭐ A STRUCT of unmanaged fields (the door is a <see cref="TerrainObjectRef"/>, JSON its key string), so a host
    /// can also bind it by bytes.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct DoorLocksmithParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.DoorLocksmith;

        /// <summary>The door, by terrain-object key (<c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>).</summary>
        [JsonPropertyName("door")]
        public TerrainObjectRef Door;

        /// <summary>Where to go once through (local metres, east).</summary>
        [JsonPropertyName("x")]
        public float X;

        /// <summary>Where to go once through (local metres, north).</summary>
        [JsonPropertyName("y")]
        public float Y;

        /// <summary>Walking speed (m/s); 0 ⇒ the nodes' default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
