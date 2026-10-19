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
