using Hrot.Common.Editing;

namespace Hrot.Common.Actions
{
    /// <summary>The verdict of <see cref="ActionAvailability.Evaluate"/>: the state, and the reason when it is <see cref="ActionOffer.Greyed"/>.</summary>
    public readonly record struct AvailabilityVerdict(ActionOffer State, string? Reason);

    /// <summary>
    /// ⭐⭐ B-11 A7 (docs/UX/DESIGN_Action_Catalogue.md §2.1 <c>Modes</c> + <c>Level</c>, §4 A7; Editing UX §3.2) — THE one availability declaration an
    /// action and a Details tab share: the <see cref="EditingMode"/>s it is offered in, the modes it is greyed in (with a reason), and the lowest
    /// station <see cref="EditingLevel"/> that may use it. <see cref="ActionDecl"/> declares the same <see cref="ActionModes"/> and
    /// <see cref="EditingLevel"/>; ⛔ no third mode vocabulary — the mode is the station's <see cref="StationMode"/> and the level its seat's.
    /// <para>A struct of plain values, evaluated allocation-free (the reasons are constants).</para>
    /// </summary>
    /// <param name="Offered">The modes it is offered in.</param>
    /// <param name="Greyed">The modes it is shown greyed in (with <paramref name="GreyReason"/>); a mode in neither set is hidden.</param>
    /// <param name="Level">The LEAST privileged station level that may use it; <see cref="EditingLevel.None"/> = anyone (a view).</param>
    /// <param name="GreyReason">Why it is greyed in a <paramref name="Greyed"/> mode.</param>
    public readonly record struct ActionAvailability(ActionModes Offered, ActionModes Greyed = 0, EditingLevel Level = EditingLevel.None, string? GreyReason = null)
    {
        /// <summary>Offered in every mode to every station.</summary>
        public static readonly ActionAvailability Always = new(ActionModes.Always);

        /// <summary>The verdict for a station in <paramref name="mode"/> at <paramref name="station"/>: the mode decides first (Hidden · Greyed with its
        /// reason), then the level greys an offered item with the seat's reason.</summary>
        public AvailabilityVerdict Evaluate(EditingMode mode, EditingLevel station)
        {
            var bit = ActionDecl.ModeBit(mode);
            if ((Offered & bit) == 0)
                return (Greyed & bit) != 0 ? new AvailabilityVerdict(ActionOffer.Greyed, GreyReason ?? "not available in this mode") : new AvailabilityVerdict(ActionOffer.Hidden, null);
            return LevelRefusal(Level, station) is { } why ? new AvailabilityVerdict(ActionOffer.Greyed, why) : new AvailabilityVerdict(ActionOffer.Offered, null);
        }

        /// <summary>⭐ The ONE level rule (D4): a station whose level is below <paramref name="need"/> is refused with the seat's reason; null = allowed.
        /// <see cref="ActionContext.SeatRefusal"/> and the Details tabs both call it.</summary>
        public static string? LevelRefusal(EditingLevel need, EditingLevel station)
            => (int)station > (int)need ? ActionContext.SeatText(station) : null;
    }
}
