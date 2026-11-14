using System.ComponentModel;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ Airborne insertion §4 — one fast rope of a carrier: where it hangs from (the carrier's frame).</summary>
    public record RopeAnchorDto
    {
        [EditUnit("m")] public float X { get; init; }
        [EditUnit("m")] public float Y { get; init; }
        [EditUnit("m")] public float Z { get; init; }
    }

    /// <summary>
    /// ⭐ Airborne insertion B-7…B-9 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §7d, D-16) — an aircraft whose crew can EJECT: the
    /// pilot's type (spawned at the seat, <c>EjectionSystem</c>), where the seat is (the aircraft's frame, from its reference point), and
    /// the health share at or below which he ejects on his own (0 = only when ordered).
    /// </summary>
    [TkbDescriptor("Gen.Ejection")]
    public record EjectionDto
    {
        [Description("The TKB type of the man who ejects (a Pilot).")]
        public long PilotType { get; init; }
        [EditUnit("m")] public float SeatX { get; init; }
        [EditUnit("m")] public float SeatY { get; init; }
        [EditUnit("m")] public float SeatZ { get; init; }
        [Description("He ejects on his own when the aircraft's health is at or below this share of its maximum (0 = only when ordered).")]
        public float AutoEjectBelowFraction { get; init; }
    }

    /// <summary>
    /// ⭐⭐ Airborne insertion B-5 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §2, §4) — a jumper's parachute: its canopy and the
    /// figures the descent model flies it by. A field left 0 takes the canopy's default (<c>ParachuteModel.Defaults</c> — public figures,
    /// rounded: round T-11 class, ram-air MC-4 class).
    /// </summary>
    [TkbDescriptor("Gen.Parachute")]
    public record ParachuteDto
    {
        [Description("Round (static line) or RamAir (free fall, steerable).")]
        public CanopyKind Canopy { get; init; } = CanopyKind.Round;
        [EditUnit("m/s")] public float SinkMps { get; init; }
        [EditUnit("m/s")] public float ForwardMps { get; init; }
        [EditUnit("deg/s")] public float TurnRateDeg { get; init; }
        [EditUnit("deg")] public float MaxBankDeg { get; init; }
        [Description("The sink at full brakes, × the sink (and the forward speed × 1.5 of it).")]
        public float BrakeSinkFactor { get; init; }
        [EditUnit("m")] public float FlareHeightM { get; init; }
        [EditUnit("s")] public float OpenSeconds { get; init; }
        [Description("Free fall: the height above the ground the canopy is pulled at.")]
        [EditUnit("m")] public float PullHeightM { get; init; }
        [Description("On the ground: out of the harness (he cannot move or fire).")]
        [EditUnit("s")] public float LandingSeconds { get; init; }
    }
}
