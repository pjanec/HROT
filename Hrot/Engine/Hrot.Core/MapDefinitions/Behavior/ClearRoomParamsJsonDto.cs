using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2257</c> BC-1 (CB-48) — the authored contract of a squad commander's <c>ClearRoom</c>: the squad finds the room holding
    /// <see cref="Room"/> (one building survey), stacks on its door, opens or breaches it, enters (Battle Drill 6) and reports PROGRESS
    /// RoomClear. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct ClearRoomParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ClearRoom;

        /// <summary>A point inside the room (sim metres; the storey's floor height in Z).</summary>
        [JsonPropertyName("Room")]
        public Vector3 Room;

        /// <summary>The men's asked speed (m/s); 0 = 2.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-2260</c> BC-4 (CB-65) — how hard the room is gone into (the grenade before the entry).</summary>
        [JsonPropertyName("Intensity")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClearIntensity Intensity;

        /// <summary>⭐ <c>CE-2260</c> BC-4 (CB-65, CB-66) — the order says the room is held (intelligence): it is SUSPECTED before anything is seen.</summary>
        [JsonPropertyName("Suspected")]
        public bool Suspected;
    }

    /// <summary>⭐ <c>CE-2260</c> BC-4 (CB-65; docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.3) — how hard a squad goes into a room:
    /// <see cref="Precision"/> (the default — a stun grenade into a suspected room, never fragmentation) or <see cref="HighIntensity"/>
    /// (fragmentation into a suspected room with no civilian known, a stun when one is).</summary>
    public enum ClearIntensity : byte
    {
        Precision = 0,
        HighIntensity = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-2259</c> BC-3 (CB-57) — the authored contract of a squad commander's <c>ClearBuilding</c>: the squad surveys the building at
    /// <see cref="Building"/> once and clears it room by room — the lowest storey first, each room by the next portal from one already
    /// cleared — reporting each room, a Foothold and BuildingClear. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.2.
    /// </summary>
    [ParamsDto]
    public struct ClearBuildingParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ClearBuilding;

        /// <summary>A point on or in the building (sim metres).</summary>
        [JsonPropertyName("Building")]
        public Vector3 Building;

        /// <summary>The men's asked speed (m/s); 0 = 2.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-2260</c> BC-4 (CB-65) — how hard each room is gone into (the grenade before the entry).</summary>
        [JsonPropertyName("Intensity")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClearIntensity Intensity;
    }

    /// <summary>⭐ <c>CE-2257</c> BC-1 (CB-53) — the authored contract of <c>BreachDoor</c>: walk to the door, open it, and when it refuses
    /// (locked) breach it.</summary>
    [ParamsDto]
    public struct BreachDoorParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BreachDoor;

        /// <summary>The door, by terrain-object key.</summary>
        [JsonPropertyName("door")]
        public TerrainObjectRef Door;

        /// <summary>Walking speed (m/s); 0 ⇒ the nodes' default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
