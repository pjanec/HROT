using System;
using Fdp.Toolkit.Perception.Components;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <b>One sensor a unit carries</b> — an entry of <see cref="SensorCapabilitiesDto.Sensors"/>
    /// (docs/DESIGN_Sensors_And_Doctrine.md §4, R-185 A / R-186 / R-187).
    /// <para>Every node that spawns the unit builds a sensor CHILD from it (part id <c>1000 + index</c>), so the Brain and
    /// the Muscle agree on the sensor without any configuration crossing the wire (R-185 K).</para>
    /// <para>⭐ Per-kind parameters are CONCRETE optional sub-records, one per kind — ⛔ not an interface: the TKB editor's
    /// StructEdit marks an interface-typed member Unsupported (measured, design §9 V1). The entry's <see cref="Kind"/>
    /// names which sub-record applies.</para>
    /// <para>⭐ The SAME record travels as JSON when a behaviour creates or overrides a sensor (R-186 M′).</para>
    /// </summary>
    public record SensorEntryDto
    {
        /// <summary>What the sensor senses with.</summary>
        public SensorModality Kind { get; init; }

        /// <summary>The EQS template the sensor runs (its asset id — the registry keys it by
        /// <c>EqsTemplateRegistry.BlueprintIdOf</c>). <see cref="Guid.Empty"/> = no sensing template yet: no child is built.</summary>
        public Guid Template { get; init; }

        /// <summary>⭐ R-187 — OFF at spawn. Absent = false = ON: a missing field can never blind a unit.</summary>
        public bool Disabled { get; init; }

        /// <summary>The query radius; 0 = the kind's own range.</summary>
        [EditUnit("m")]
        public float SearchRadius { get; init; }

        /// <summary>Parameters when <see cref="Kind"/> is <see cref="SensorModality.Visual"/>.</summary>
        public VisualSensorDto? Visual { get; init; }

        /// <summary>Parameters when <see cref="Kind"/> is <see cref="SensorModality.Thermal"/>.</summary>
        public ThermalSensorDto? Thermal { get; init; }

        /// <summary>Parameters when <see cref="Kind"/> is <see cref="SensorModality.Acoustic"/>.</summary>
        public AcousticSensorDto? Acoustic { get; init; }

        /// <summary>Parameters when <see cref="Kind"/> is <see cref="SensorModality.Radar"/>.</summary>
        public RadarSensorDto? Radar { get; init; }

        /// <summary>The range of the kind's own sub-record (0 when it is missing).</summary>
        public float Range => Kind switch
        {
            SensorModality.Visual   => Visual?.Range ?? 0f,
            SensorModality.Thermal  => Thermal?.Range ?? 0f,
            SensorModality.Acoustic => Acoustic?.Range ?? 0f,
            SensorModality.Radar    => Radar?.Range ?? 0f,
            _ => 0f,
        };

        /// <summary>True when the entry names exactly one kind and carries that kind's sub-record.</summary>
        public bool IsWellFormed => Kind switch
        {
            SensorModality.Visual   => Visual != null,
            SensorModality.Thermal  => Thermal != null,
            SensorModality.Acoustic => Acoustic != null,
            SensorModality.Radar    => Radar != null,
            _ => false,
        };
    }

    /// <summary>A daylight / optical sensor (eyes, sights, a camera).</summary>
    public record VisualSensorDto
    {
        [EditUnit("m")] public float Range { get; init; }
        [EditRange(0, 360)] [EditUnit("deg")] public float FieldOfViewDegrees { get; init; } = 360f;
    }

    /// <summary>A thermal imager.</summary>
    public record ThermalSensorDto
    {
        [EditUnit("m")] public float Range { get; init; }
        [EditRange(0, 360)] [EditUnit("deg")] public float FieldOfViewDegrees { get; init; } = 360f;
        /// <summary>The weakest heat signature it resolves (0 = any).</summary>
        public float MinSignature { get; init; }
    }

    /// <summary>A listening sensor (ears, microphones).</summary>
    public record AcousticSensorDto
    {
        [EditUnit("m")] public float Range { get; init; }
    }

    /// <summary>A radar.</summary>
    public record RadarSensorDto
    {
        [EditUnit("m")] public float Range { get; init; }
        [EditRange(0, 360)] [EditUnit("deg")] public float FieldOfViewDegrees { get; init; } = 360f;
    }
}
