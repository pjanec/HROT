using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt.Kernel;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2140</c> — the MOVE order: go to <see cref="Location"/>. The params DTO of every behaviour that fulfils Move
    /// (<c>InfantrySquadMove</c>, …). Flat JSON, e.g. <c>{"Location":[340,300,0],"Speed":2}</c>. A zero field keeps the fulfilling
    /// behaviour's own default. ⭐ <see cref="Location"/> is a <see cref="Fdp.Toolkit.Replication.PointLink"/> (S0 item 5): local metres
    /// <c>[x, y, z]</c>, <c>"@Key"</c> or both — the type alone makes the menu ask for a point (DESIGN_Map_Picking_Unification). ⛔ Not geodetic
    /// since S0 (it was a <c>PickableGeoPoint</c>; CE-2433).
    /// </summary>
    [Order(nameof(OrderKind.Move), Gesture = OrderGesture.GroundClick)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveOrder
    {
        /// <summary>Where to go (local metres, S0). ⭐ <c>CE-2148</c> — required: a move without a point is refused.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

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
        public Fdp.Toolkit.Replication.EntityLink Target;
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
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>An entity at the place to track (optional).</summary>
        [JsonPropertyName("Target")]
        public Fdp.Toolkit.Replication.EntityLink Target;

        // ⭐⭐ CE-2176 IF-2 (IF-D, R-2018; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §4) — the FIRE MISSION fields: a battery
        //   or a self-propelled gun reads them; an aircraft reads only Location / Target. A zero field keeps the RTS default
        //   (fire for effect, when ready, converged, 3 rounds).

        /// <summary>Fire for effect, or adjust first with one gun (IF-3; until then an Adjust fires for effect).</summary>
        [JsonPropertyName("Method")]
        public FireMissionMethod Method;

        /// <summary>Rounds per gun; 0 = 3.</summary>
        [JsonPropertyName("Rounds")]
        public int Rounds;

        /// <summary>Where each gun aims: all on the point, each at its own place in the battery, or spread over the width.</summary>
        [JsonPropertyName("Sheaf")]
        public FireMissionSheaf Sheaf;

        /// <summary>An open sheaf's width (m); 0 = guns × twice the burst's injury radius.</summary>
        [JsonPropertyName("SheafWidthM")]
        public float SheafWidthM;

        /// <summary>When the guns fire: as each is ready, or so every first round lands together.</summary>
        [JsonPropertyName("Control")]
        public FireMissionControl Control;

        /// <summary>Time on target: the impact this many seconds from now at the earliest; 0 = as soon as the slowest round can.</summary>
        [JsonPropertyName("ImpactInS")]
        public float ImpactInS;

        /// <summary>The observer who called it (IF-3).</summary>
        [JsonPropertyName("Observer")]
        public Fdp.Toolkit.Replication.EntityLink Observer;

        /// <summary>Fire although friends are within 600 m of the point (FM-5).</summary>
        [JsonPropertyName("DangerClose")]
        public bool DangerClose;

        /// <summary>⭐ <c>CE-2173</c> slice 6c (W1, R-2020) — what the point IS (why it is struck): the kind picks the weapon class — a
        /// building, a bunker, a bridge or a runway takes a heavy weapon, never a gun (Air Operations §9e). Unspecified = any.</summary>
        [JsonPropertyName("TargetKind")]
        public StrikeTargetKind TargetKind;

        /// <summary>⭐ <c>CE-7025</c> (City Battle §5, IF-5) — fire SMOKE (each gun loads its smoke round): a screen, no blast.</summary>
        [JsonPropertyName("Smoke")]
        public bool Smoke;
    }

    /// <summary>⭐ <c>CE-2173</c> slice 6c (W1, R-2020) — the reason a point is struck (Air Operations §7b, §9e).</summary>
    public enum StrikeTargetKind : byte
    {
        Unspecified = 0,
        Personnel = 1,
        Vehicles = 2,
        Building = 3,
        Fortification = 4,
        Bridge = 5,
        Runway = 6,
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> IF-3 (IF-E, FM-6; 📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.5, §3.8) — the OBSERVE FIRE order: a
    /// soldier who sees <see cref="Location"/> calls <see cref="Battery"/>'s fire on it, adjusts one gun's rounds onto it from what he sees
    /// burst, then calls fire for effect with the mission below. Every call is an ordinary <see cref="StrikeOrder"/> to the battery, so it
    /// crosses brain nodes the way any order does.
    /// </summary>
    [Order(nameof(OrderKind.ObserveFire))]
    [StructLayout(LayoutKind.Sequential)]
    public struct ObserveFireOrder
    {
        /// <summary>The target (geodetic). Required.</summary>
        [Required]
        [JsonPropertyName("Location")]
        public Fdp.Toolkit.Replication.PointLink Location;

        /// <summary>An entity at the place to track (optional).</summary>
        [JsonPropertyName("Target")]
        public Fdp.Toolkit.Replication.EntityLink Target;

        /// <summary>The battery (or self-propelled gun) to call. Required.</summary>
        [Required]
        [JsonPropertyName("Battery")]
        public Fdp.Toolkit.Replication.EntityLink Battery;

        /// <summary>How the observer located the target: the first round's error (map / eye ≈ 150 m, laser ≈ 10 m — IF-F).</summary>
        [JsonPropertyName("Locating")]
        public FireMissionLocating Locating;

        /// <summary>Fire for effect: rounds per gun; 0 = 3.</summary>
        [JsonPropertyName("Rounds")]
        public int Rounds;

        /// <summary>Fire for effect: the sheaf.</summary>
        [JsonPropertyName("Sheaf")]
        public FireMissionSheaf Sheaf;

        /// <summary>Fire for effect: an open sheaf's width (m).</summary>
        [JsonPropertyName("SheafWidthM")]
        public float SheafWidthM;

        /// <summary>Fire for effect: when the guns fire.</summary>
        [JsonPropertyName("Control")]
        public FireMissionControl Control;

        /// <summary>Fire although friends are within 600 m of the point (FM-5).</summary>
        [JsonPropertyName("DangerClose")]
        public bool DangerClose;
    }

    /// <summary>⭐ <c>CE-2176</c> IF-3 — how the observer located the target (IF-F).</summary>
    public enum FireMissionLocating : byte { Map = 0, Laser = 1 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — how a fire mission starts (IF-D).</summary>
    public enum FireMissionMethod : byte { FireForEffect = 0, Adjust = 1 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — where the guns aim (FM-3).</summary>
    public enum FireMissionSheaf : byte { Converged = 0, Parallel = 1, Open = 2 }

    /// <summary>⭐ <c>CE-2176</c> IF-2 — when the guns fire (FM-4).</summary>
    public enum FireMissionControl : byte { WhenReady = 0, TimeOnTarget = 1 }
}
