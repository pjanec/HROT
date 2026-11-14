using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (BS-1-DESIGN §5.4c) — an EXPENDABLE munition (DIS kind 8: a flare 8.2.2, chaff 8.2.1): one decoy
    /// leaves the aircraft's dispenser per round and follows <c>DecoyFlight.At</c> for <see cref="LifeSeconds"/>. Which seeker it fools comes
    /// from its DIS type (<c>Munitions.DecoyOf</c>) — one source. The SimHost's decoy and every viewer's drawn flare read these same numbers.
    /// </summary>
    [TkbDescriptor("Combat.Expendable")]
    public record ExpendableDto
    {
        [EditUnit("s")]
        [Description("How long the decoy burns / hangs before it is gone.")]
        public float LifeSeconds { get; init; } = 5f;

        [EditUnit("m/s")]
        [Description("How fast it sinks (a burning flare falls; chaff hangs and drifts down).")]
        public float SinkMps { get; init; } = 8f;
    }
}
