using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ M7 (docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md §2) — the TKB descriptor <c>Gen.Flight</c>: how a flyer flies. A point mass:
    /// a top speed, a climb rate, a turn rate, a battery and the radius at which it meets what it flies into.
    /// </summary>
    [TkbDescriptor("Gen.Flight")]
    public record FlightParamsDto
    {
        [EditUnit("m/s")] [Description("Top speed.")]
        public float MaxSpeedMps { get; init; } = 30f;

        [EditUnit("m/s")] [Description("Climb and descent rate.")]
        public float ClimbMps { get; init; } = 8f;

        [EditUnit("deg/s")] [Description("Yaw rate.")]
        public float TurnRateDeg { get; init; } = 120f;

        [EditUnit("s")] [Description("The battery: how long it can fly (its behaviour keeps the clock).")]
        public float EnduranceSeconds { get; init; } = 600f;

        [EditUnit("m")] [Description("It meets a body or the ground within this distance.")]
        public float ContactRadiusM { get; init; } = 0.8f;

        [EditUnit("m")] [Description("Its own body (the collider others hit): radius and height.")]
        public float BodyRadiusM { get; init; } = 0.3f;

        /// <inheritdoc cref="BodyRadiusM"/>
        [EditUnit("m")]
        public float BodyHeightM { get; init; } = 0.3f;

        // ⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6) — a route's geometry for this flyer.
        [EditUnit("m")] [Description("The height above the ground a route flies at where a waypoint states none, and from which it lands.")]
        public float HoverHeightM { get; init; } = 30f;

        [EditUnit("m")] [Description("The radius a route's corners are rounded to; 0 = top speed / turn rate.")]
        public float TurnRadiusM { get; init; } = 0f;
    }
}
