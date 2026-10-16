using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/DESIGN_Entity_Interactions.md §7.3) — the authored contract of the library <c>Embark</c>: walk to
    /// <see cref="Vehicle"/> and board it. Flat JSON (orders design §1 P2); the vehicle is an <see cref="EntityRef"/> (its network id),
    /// so the editor offers an entity pick and a scenario load remaps it. ⭐ Declared on the behaviour (CE-2134: <c>[Behavior(Params = …)]</c>).
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct EmbarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Embark;

        /// <summary>The vehicle to board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> (📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §4b, W-B) — the authored contract of the library <c>ManWeapon</c>:
    /// walk to <see cref="Weapon"/> (a crew weapon system — a mortar, a tripod HMG) and take its GUNNER seat, so the man controls it.
    /// </summary>
    [ParamsDto]
    public struct ManWeaponParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ManWeapon;

        /// <summary>The weapon system to man.</summary>
        [JsonPropertyName("weapon")]
        public EntityRef Weapon;

        /// <summary>Walking speed to it (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d (📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a) — the authored contract of the squad
    /// <c>SquadDismount</c> (on the commander): every member gets out; done when nobody is aboard.
    /// </summary>
    [ParamsDto]   // CE-3185 (R-2000): a params DTO, listed by the one registry with its origin
    public struct SquadDismountParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadDismount;

        /// <summary>Failure when somebody is still aboard after this long (s); 0 ⇒ 10 s.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;

        /// <summary>The hold speed of a member already out (m/s); 0 ⇒ 3.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("speed")]
        public float Speed;
    }
}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 (B10, R-2015; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9) — the authored contract of the squad
    /// <c>SquadEmbark</c> (on the commander): every member boards <see cref="Vehicle"/>; done when all are aboard it.
    /// </summary>
    [ParamsDto]
    public struct SquadEmbarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadEmbark;

        /// <summary>The vehicle to board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Failure when somebody is still out after this long (s); 0 ⇒ 60.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the Embark node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }

    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3 — the authored contract of the library <c>LoadCasualty</c>: walk to <see cref="Vehicle"/> with the man
    /// carried, seat him in it, then board it.
    /// </summary>
    [ParamsDto]
    public struct LoadCasualtyParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.LoadCasualty;

        /// <summary>The vehicle to load into and board.</summary>
        [JsonPropertyName("vehicle")]
        public EntityRef Vehicle;

        /// <summary>Walking speed to the vehicle (m/s); 0 ⇒ the node's default.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ CE-1118 H-1 (📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.5 D-18) — the authored contract of the library
    /// <c>Disembark</c>: how the unit leaves its vehicle. All optional — <c>{}</c> steps out beside the hull as before.
    /// </summary>
    [ParamsDto]
    public struct DisembarkParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.Disembark;

        /// <summary>How he leaves (<c>Fdp.Toolkit.Motion.ExitKind</c>: 0 step out, 1 down a rope, 2 out of the door under a parachute).</summary>
        [JsonPropertyName("exit")]
        public byte Exit;

        /// <summary>Which rope or door (the carrier's <c>RopeAnchors</c> index; 0 = the first).</summary>
        [JsonPropertyName("anchorIndex")]
        public byte AnchorIndex;

        /// <summary>A jumper's landing point (zero = wherever he comes down).</summary>
        [JsonPropertyName("landing")]
        public System.Numerics.Vector3 Landing;
    }

    /// <summary>⭐ CE-1118 H-1 (§6) — the authored contract of the squad <c>SquadFastRope</c> (on the commander; the SOP gives it with <c>{}</c>).</summary>
    [ParamsDto]
    public struct SquadFastRopeParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadFastRope;

        /// <summary>Failure when the squad is not down and in its ring after this long (s); 0 ⇒ 60.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;

        /// <summary>The ring's radius round the rope point (m); 0 ⇒ 5 (shrunk to fit a roof).</summary>
        [JsonPropertyName("ringRadius")]
        public float RingRadius;
    }

    /// <summary>⭐ CE-1118 H-1 (§6) — the authored contract of the squad <c>SquadAirborneExit</c> (the jumpmaster; the SOP gives it with <c>{}</c>).</summary>
    [ParamsDto]
    public struct SquadAirborneExitParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadAirborneExit;

        /// <summary>Seconds between two jumpers; 0 ⇒ 1.</summary>
        [JsonPropertyName("intervalSeconds")]
        public float IntervalSeconds;

        /// <summary>The assembly ring's radius (m); 0 ⇒ 8.</summary>
        [JsonPropertyName("ringRadius")]
        public float RingRadius;

        /// <summary>Failure when the squad is not assembled after this long (s); 0 ⇒ 300.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;
    }

    /// <summary>⭐ CE-1118 H-1 (§6.5 D-22) — the authored contract of the squad <c>AssembleAfterDrop</c>: rally in a ring round a point.</summary>
    [ParamsDto]
    public struct AssembleAfterDropParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.AssembleAfterDrop;

        /// <summary>The rally point (zero ⇒ the mean of the squad where it stands).</summary>
        [JsonPropertyName("point")]
        public System.Numerics.Vector3 Point;

        /// <summary>The ring's radius (m); 0 ⇒ 8.</summary>
        [JsonPropertyName("ringRadius")]
        public float RingRadius;

        /// <summary>Failure when the squad is not assembled after this long (s); 0 ⇒ 300.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public float TimeoutSeconds;
    }

    /// <summary>⭐ CE-1118 H-3 (§6.5 D-25) — the authored contract of <c>EvadeAndSignal</c>.</summary>
    [ParamsDto]
    public struct EvadeAndSignalParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.EvadeAndSignal;

        /// <summary>How far from the nearest known enemy he gets (m); 0 ⇒ 120.</summary>
        [JsonPropertyName("awayMetres")]
        public float AwayMetres;

        /// <summary>His speed (m/s); 0 ⇒ 3.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
