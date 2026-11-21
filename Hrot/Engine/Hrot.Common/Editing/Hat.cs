namespace Hrot.Common.Editing
{
    /// <summary>
    /// ⭐ Seats and hats H3 (📄 docs/UX/DESIGN_Seats_And_Hats.md §2.3, §3.3) — what the person at a station is doing RIGHT NOW: one hat at a time, owning the input
    /// and the camera. ⛔ Not a feature, not on the wire: local UI state, derived by the station's <c>HatArbiter</c> (R-4164: the hat follows the run mode).
    /// </summary>
    public enum Hat : byte
    {
        /// <summary>The home hat: the free / follow camera.</summary>
        Observe = 0,
        /// <summary>Authoring the scenario (Edit mode).</summary>
        Edit = 1,
        /// <summary>RTS command mode (Preview / Live).</summary>
        Command = 2,
        /// <summary>Driving a unit in first person.</summary>
        Embodied = 3,
        /// <summary>The director owns the camera.</summary>
        Direct = 4,
    }
}
