namespace Hrot.Common.Editing
{
    /// <summary>
    /// ⭐ Action catalogue (📄 docs/UX/DESIGN_Action_Catalogue.md §2.1, §5 Q1, R-4125) — the station's run state as ONE vocabulary: authoring a scenario,
    /// previewing it, running it live, reviewing a recording. ⛔ Paused is not a mode (a paused Live is Live). Read by the action catalogue (B-11) and the
    /// edit gate (edit framework F1).
    /// </summary>
    public enum EditingMode : byte
    {
        Edit = 0,
        Preview = 1,
        Live = 2,
        /// <summary>A recording: read-only.</summary>
        Review = 3,
    }

    /// <summary>
    /// ⭐ Seats and hats (📄 docs/UX/DESIGN_Seats_And_Hats.md §2.1–§2.2, D4, R-4131) — how much a station may edit, the SEAT's editing level.
    /// <see cref="None"/> = the Player and Reviewer seats (no editing at all).
    /// </summary>
    public enum EditingLevel : byte
    {
        Full = 0,
        Instructor = 1,
        Controller = 2,
        Observer = 3,
        None = 4,
    }
}
