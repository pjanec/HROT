using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt.Kernel;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — the MOVE order: go to <see cref="Location"/>. The params DTO of every behaviour that fulfils Move
    /// (<c>InfantrySquadMove</c>, …). Flat JSON, e.g. <c>{"Location":[52.52,13.41],"Speed":2}</c>. A zero field keeps the fulfilling
    /// behaviour's own default. ⭐ <see cref="Location"/> is a <see cref="PickableGeoPoint"/>, so the type alone makes the menu ask
    /// for a point (DESIGN_Map_Picking_Unification); the fulfilling behaviour converts it to local metres (its resolver or its node).
    /// </summary>
    [Order(nameof(OrderKind.Move), Gesture = OrderGesture.GroundClick)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveOrder
    {
        /// <summary>Where to go (geodetic). ⭐ <c>CE-2148</c> — required: a move without a point is refused.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public PickableGeoPoint Location;

        /// <summary>The asked speed (m/s); 0 = the behaviour's default.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>How close counts as arrived (m); 0 = the behaviour's default.</summary>
        [JsonPropertyName("ArrivalRadius")]
        public float ArrivalRadius;

        /// <summary>How much to prefer roads.</summary>
        [JsonPropertyName("RoadUse")]
        public RoadUse RoadUse;
    }

    /// <summary>
    /// ⭐ <c>CE-2140</c> — the ATTACK order: close in on <see cref="Target"/> and fight it until it is out of the fight. ⚠ ROE still
    /// applies. ⭐ <see cref="Target"/> is an <see cref="EntityRef"/>, so the type alone makes the menu ask for an entity.
    /// </summary>
    [Order(nameof(OrderKind.Attack), Gesture = OrderGesture.EnemyClick)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AttackOrder
    {
        /// <summary>The entity to attack. ⭐ <c>CE-2148</c> — required.</summary>
        [Required]
        [JsonPropertyName("Target")]
        public EntityRef Target;
    }

    /// <summary>
    /// ⭐ <c>CE-2140</c> — the HOLD order: stay where you are. Empty, but its OWN type, because the type identifies the order
    /// (<c>NoParams</c> means "this behaviour takes nothing", not "hold").
    /// </summary>
    [Order(nameof(OrderKind.Hold))]
    [StructLayout(LayoutKind.Sequential)]
    public struct HoldOrder
    {
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> (B4, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §7) — the STRIKE order: hit <see cref="Location"/>
    /// (an optional <see cref="Target"/> entity there is tracked). Every aircraft fulfils it through <c>AirPlatformStrike</c>; the attack
    /// pattern is chosen from the aircraft and its weapons, never by the sender. ⭐ No RTS gesture: a ground click is a Move — Strike is
    /// a menu / HTTP order.
    /// </summary>
    [Order(nameof(OrderKind.Strike))]
    [StructLayout(LayoutKind.Sequential)]
    public struct StrikeOrder
    {
        /// <summary>Where to strike (geodetic). Required.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public PickableGeoPoint Location;

        /// <summary>An entity at the place to track (optional).</summary>
        [JsonPropertyName("Target")]
        public EntityRef Target;
    }
}
