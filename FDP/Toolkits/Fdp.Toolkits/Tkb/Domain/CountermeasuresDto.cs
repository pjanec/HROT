using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (DESIGN_Missiles_And_Ammo §3c, Air Ops §7d AD3) — an aircraft type's dispensers: flares (decoy an IR seeker)
    /// and chaff (decoy a radar seeker), how many decoys one salvo throws, the least time between salvoes and how long a decoy lives.
    /// The BRAIN decides when to dispense (<c>DispenseCountermeasureIntent</c>, behaviors' CE-2191); <c>CountermeasureSystem</c> on the
    /// SimHost spawns the decoys and lets each homing round decide whether it is fooled.
    /// </summary>
    [TkbDescriptor("Combat.Countermeasures")]
    public record CountermeasuresDto
    {
        [Description("Flares carried (IR decoys).")]
        public int Flares { get; init; }

        [Description("Chaff bundles carried (radar decoys).")]
        public int Chaff { get; init; }

        [Description("Decoys thrown per salvo.")]
        public int Salvo { get; init; } = 2;

        [EditUnit("s")]
        [Description("Least time between two salvoes.")]
        public float IntervalS { get; init; } = 1f;

        [EditUnit("s")]
        [Description("How long a decoy burns / hangs before it is gone.")]
        public float DecoySeconds { get; init; } = 5f;
    }
}
