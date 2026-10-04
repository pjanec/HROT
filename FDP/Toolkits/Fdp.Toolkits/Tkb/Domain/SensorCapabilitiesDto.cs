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

        /// <summary>
        /// Sensor eye height above the entity's Z when STANDING (metres). ⭐ 0 = unset: the line-of-sight strategy
        /// then uses its default soldier mount (1.7 / 1.1 / 0.35). ⚠ Absent from older JSON ⇒ 0 ⇒ the default —
        /// safe by construction. 📄 docs/DESIGN_Terrain_World.md §7.1 W5 (🔒 R-182 "sensor height must follow posture").
        /// </summary>
        [EditUnit("m")]
        public float EyeHeightStanding { get; init; }

        /// <summary>Sensor eye height when CROUCHED (metres); 0 = unset.</summary>
        [EditUnit("m")]
        public float EyeHeightCrouched { get; init; }

        /// <summary>Sensor eye height when PRONE (metres); 0 = unset.</summary>
        [EditUnit("m")]
        public float EyeHeightProne { get; init; }

        /// <summary>
        /// ⭐ The unit's SENSORS — each becomes a sensor child on every node (docs/DESIGN_Sensors_And_Doctrine.md §4).
        /// Absent ⇒ empty. ⏳ Until visual perception moves onto the sensor form (S5), the vision fields above still
        /// drive the built-in perception pipeline and an empty list adds no sensor child.
        /// </summary>
        public System.Collections.Generic.List<SensorEntryDto> Sensors { get; init; } = new();
    }
}
