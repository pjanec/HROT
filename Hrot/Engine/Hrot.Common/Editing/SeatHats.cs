using Hrot.Common.Infrastructure;

namespace Hrot.Common.Editing
{
    /// <summary>
    /// ⭐⭐ Seats and hats H6 (📄 docs/UX/DESIGN_Seats_And_Hats.md §2.2 "hats allowed", R-4119: a refusal is visible and says why) — which <see cref="Hat"/>s a
    /// <see cref="Seat"/> may wear. ONE table, read by the <c>HatArbiter</c> (a hat the seat does not allow falls to Observe), the action catalogue (an action that needs a hat is greyed
    /// with the reason), the HTTP session (<c>POST /session/hat</c>) and the hat menu — so a person's station, a menu row and an automated session all see the same refusal.
    /// </summary>
    public static class SeatHats
    {
        /// <summary>Whether <paramref name="seat"/> may wear <paramref name="hat"/>. Observe — the home hat — is allowed to every seat.</summary>
        public static bool Allows(Seat seat, Hat hat) => hat switch
        {
            Hat.Observe => true,
            Hat.Edit => seat == Seat.Author,
            Hat.Command => seat is Seat.Author or Seat.Instructor or Seat.SideController,
            Hat.Embodied => seat is Seat.Author or Seat.Instructor or Seat.Player,
            Hat.Direct => seat is Seat.Author or Seat.Reviewer,
            _ => false,
        };

        /// <summary>Why <paramref name="seat"/> may not wear <paramref name="hat"/> (the words the menu and the catalogue show), or null when it may. Constant strings: no allocation.</summary>
        public static string? Refusal(Seat seat, Hat hat) => Allows(seat, hat) ? null : seat switch
        {
            Seat.Author => "this seat cannot do that",
            Seat.Instructor => hat switch { Hat.Edit => "an Instructor seat cannot edit", _ => "an Instructor seat cannot direct" },
            Seat.SideController => hat switch { Hat.Edit => "a Side controller seat cannot edit", Hat.Embodied => "a Side controller seat cannot possess a unit", _ => "a Side controller seat cannot direct" },
            Seat.Player => hat switch { Hat.Edit => "a Player seat cannot edit", Hat.Command => "a Player seat cannot command", _ => "a Player seat cannot direct" },
            Seat.Observer => hat switch { Hat.Edit => "an Observer seat cannot edit", Hat.Command => "an Observer seat cannot command", Hat.Embodied => "an Observer seat cannot possess a unit", _ => "an Observer seat cannot direct" },
            _ => hat switch { Hat.Edit => "a Reviewer seat cannot edit", Hat.Command => "a Reviewer seat cannot command", _ => "a Reviewer seat cannot possess a unit" },
        };
    }
}
