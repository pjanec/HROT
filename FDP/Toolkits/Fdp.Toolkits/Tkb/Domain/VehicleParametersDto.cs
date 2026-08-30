using CarKinem.Core;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Physical vehicle parameters used by the dynamics subsystem.
    /// </summary>
    /// <remarks>
    /// Consumed by <c>VehicleKinematicsTkbTranslator</c>, which resolves a
    /// <see cref="CarKinem.Core.VehiclePresets"/> baseline from <see cref="VehicleClass"/>
    /// and then overrides it with whichever fields here are set.
    /// <para>
    /// <b>Absent-safe by construction.</b>  A float left at <c>0</c> means "not authored"
    /// and keeps the preset's value; the translator guards every override with
    /// <c>&gt; 0f</c>.  <see cref="VehicleClass"/> is the one field that cannot use that
    /// convention -- its <c>0</c> is <c>PersonalCar</c>, a legitimate value -- so it is
    /// nullable and absence is explicit.
    /// </para>
    /// <para>
    /// This matters because TKB templates reach this record from <b>two independent
    /// producers</b>: <c>NedTkbBuilder.WithPhysics</c> in code, and
    /// <c>TkbDeserializer</c> deserializing a <c>Gen.VehicleParameters</c> block from a
    /// staged TKB zip.  JSON authored against an older, narrower version of this record
    /// simply lacks the newer properties, and System.Text.Json leaves them at their
    /// defaults without complaint -- so every field added here must be safe when absent.
    /// </para>
    /// <para>
    /// Vehicle <i>height</i> deliberately does not live here: nothing on the kinematics
    /// path consumes it (<c>VehicleParams</c> has no height field and
    /// <c>PhysicsCollider</c> carries only <c>Radius</c>).  Its real home is the render /
    /// collider descriptor -- <c>StrideRenderModelDefDto.ShapeHeight</c>.
    /// </para>
    /// </remarks>
    [TkbDescriptor("Gen.VehicleParameters")]
    public record VehicleParametersDto
    {
        /// <summary>Total vehicle mass.</summary>
        [EditUnit("kg")]
        public float Mass { get; init; }

        /// <summary>Vehicle length.</summary>
        [EditUnit("m")]
        public float Length { get; init; }

        /// <summary>Vehicle width.</summary>
        [EditUnit("m")]
        public float Width { get; init; }

        /// <summary>Maximum forward speed.</summary>
        [EditUnit("m/s")]
        public float MaxSpeedFwd { get; init; }

        /// <summary>Maximum reverse speed.</summary>
        [EditUnit("m/s")]
        public float MaxSpeedRev { get; init; }

        /// <summary>Maximum linear acceleration.</summary>
        [EditUnit("m/s^2")]
        public float MaxAccel { get; init; }

        /// <summary>
        /// Maximum turn rate in degrees per second.  The translator converts this to
        /// <c>VehicleParams.MaxSteerRate</c> in radians.  <c>0</c> means "not authored"
        /// and keeps the preset's steer rate.
        /// </summary>
        [EditUnit("deg/s")]
        public float TurnRate { get; init; }

        /// <summary>
        /// Kinematic vehicle class, selecting the <see cref="VehiclePresets"/> baseline
        /// (steer limits, accel gain, lookahead, avoidance radius).  <c>null</c> means the
        /// template did not author a class, and the translator falls back to
        /// <see cref="CarKinem.Core.VehicleClass.PersonalCar"/>.
        /// <para>
        /// Nullable on purpose: <c>PersonalCar</c> is <c>0</c>, so a non-nullable enum
        /// could not distinguish "authored as PersonalCar" from "absent" -- and getting
        /// that wrong is what made a tank derive a car's steering.
        /// </para>
        /// </summary>
        public VehicleClass? VehicleClass { get; init; }
    }
}
