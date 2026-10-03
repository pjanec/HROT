using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// ⭐ Per-posture EYE heights of an entity's sensor (metres above its Z), projected from the TKB
    /// <see cref="SensorCapabilitiesDto"/> by <c>PerceptionTkbTranslator</c>. The 3-D sight line starts here
    /// (🔒 R-182 <i>"sensor height must follow posture"</i>). 📄 docs/DESIGN_Terrain_World.md §7.1 W5.
    /// </summary>
    [ComponentId(GlobalComponentIds.SensorMount)]
    public struct SensorMount
    {
        public float Standing;
        public float Crouched;
        public float Prone;

        /// <summary>The eye height for <paramref name="stance"/>.</summary>
        public readonly float For(StanceId stance) => stance switch
        {
            StanceId.Crouched => Crouched,
            StanceId.Prone    => Prone,
            _                 => Standing,
        };
    }
}
