using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, AW-8 / AW-9; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-1/PU-2) — a munition a HAND may pick up
    /// where it lies (a stone, a canister, a grenade). Its presence is the rule; on the MUNITION type, since a warhead may come from the
    /// reference library (the M67's) and carry nothing of ours.
    /// </summary>
    [TkbDescriptor("Munition.HandItem")]
    public record HandItemDto
    {
        /// <summary>How long an INERT round of this lies where it stopped before it is gone (s); 0 = it goes at once, as any round.
        /// A round that bursts (an area round) lies until its fuze, whatever this says.</summary>
        [EditUnit("s")] public float LiesSeconds { get; init; }
    }

    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, AW-8; §7.10 PU-7) — an AREA type whose inside is a source of an item a hand picks up without limit (the
    /// stone source): a man standing in it is topped up to <see cref="UpTo"/> rounds of <see cref="Ammo"/> (<c>ItemSources.Refill</c>).
    /// </summary>
    [TkbDescriptor("Area.ItemSource")]
    public record ItemSourceDto
    {
        /// <summary>The munition type a hand takes here.</summary>
        public long Ammo { get; init; }
        /// <summary>How many a man holds after a refill.</summary>
        public int UpTo { get; init; } = 5;
    }
}
