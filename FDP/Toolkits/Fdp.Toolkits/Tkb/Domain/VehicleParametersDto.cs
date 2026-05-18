using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Physical vehicle parameters used by the dynamics subsystem.
    /// </summary>
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
    }
}
