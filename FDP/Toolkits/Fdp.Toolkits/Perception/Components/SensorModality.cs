using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// Bitmask enum identifying which sensor modalities observed a target.
    /// Stored per slot in <see cref="TargetMemory.Modalities"/>.
    /// </summary>
    [System.Flags]
    public enum SensorModality : byte
    {
        /// <summary>Optical / visual detection (cameras, human observers).</summary>
        Visual   = 1,

        /// <summary>Active radar detection.</summary>
        Radar    = 2,

        /// <summary>Infrared / thermal detection.</summary>
        Thermal  = 4,

        /// <summary>Passive acoustic detection.</summary>
        Acoustic = 8,

        /// <summary>
        /// ⭐ <c>CE-3072</c> (R-213) — the danger-area sensor: the areas along the unit's route where it would be exposed. NOT a
        /// detection modality (no contact is ever stamped with it) but a sensor KIND, like the four above — the enum is the
        /// kind (<c>SensorTag.Kind</c>, <c>UnitSensors.Of</c>). Its result family is <c>SensorResultFamily.Area</c>, not the
        /// ranked list (<c>SensorKindRegistry</c>). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.2, §10.6.
        /// </summary>
        DangerArea = 16,
    }
}
