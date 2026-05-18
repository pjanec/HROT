using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Perception/sensor capabilities descriptor for a TKB entity.
    /// Drives projection of <c>PerceptionReceptor</c> and <c>TargetMemory</c>
    /// by <c>PerceptionTkbTranslator</c>.
    /// The <see cref="FieldOfViewDegrees"/> value is stored as degrees for human
    /// readability; the translator pre-computes the cosine at spawn time.
    /// </summary>
    [TkbDescriptor("Perception.SensorCapabilities")]
    public record SensorCapabilitiesDto
    {
        /// <summary>Maximum visual detection range in metres.</summary>
        [EditUnit("m")]
        public float VisionRange { get; init; }

        /// <summary>Maximum auditory detection range in metres.</summary>
        [EditUnit("m")]
        public float HearingRange { get; init; }

        /// <summary>
        /// Full field of view in degrees. 360 = omnidirectional.
        /// The translator converts this to FieldOfViewCos via cos(deg * 0.5 * PI/180).
        /// </summary>
        [EditRange(0, 360)]
        [EditUnit("deg")]
        public float FieldOfViewDegrees { get; init; } = 360f;
    }
}
