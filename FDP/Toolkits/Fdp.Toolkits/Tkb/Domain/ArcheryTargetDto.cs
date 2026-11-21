using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-4649</c> (bows BW-11, docs/designs/weapons/DESIGN_Bows_And_Crossbows.md) — a prop type that is an ARCHERY TARGET: a straw boss on a stand with a scoring face. An arrow that
    /// strikes it scores the RING its point lands in: the distance of the hit from the face's centre (<see cref="FaceCentreZ"/> above the prop's ground point, in the
    /// target's own height) in rings <see cref="RingWidthM"/> wide, ring <see cref="Rings"/> at the centre down to 1 at the rim, 0 beyond. Read by <c>ArcheryScoring</c>.
    /// </summary>
    [TkbDescriptor("Body.ArcheryTarget")]
    public record ArcheryTargetDto
    {
        [EditUnit("m")]
        [Description("The scoring face's centre, metres above the prop's ground point.")]
        public float FaceCentreZ { get; init; } = 1.0f;

        [EditUnit("m")]
        [Description("How wide one ring is (m): the rings are concentric about the face's centre.")]
        public float RingWidthM { get; init; } = 0.06f;

        [Description("How many rings the face has; the centre ring scores this many.")]
        public int Rings { get; init; } = 10;
    }
}
