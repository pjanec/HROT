using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ Edit framework F7 (📄 docs/UX/DESIGN_Edit_Framework.md §2.5, R-4127) — WHICH edits the user is told about, read from the ONE journal
    /// (<see cref="EditJournal"/>): a refused, lost or conflicting edit is a STICKY notification (newest on top, until dismissed); an edit still
    /// pending after <see cref="SlowAfter"/> gets the busy mark; an applied one is silent. ⭐ The overlay that draws them (Hrot.Presentation) and
    /// every test read THIS — no second rule of what is shown. Allocates nothing: the caller passes its own reused list.
    /// </summary>
    public static class EditNotifications
    {
        /// <summary>How long an edit may be Pending before the busy mark shows (§2.5: "Pending longer than ~1 s").</summary>
        public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1);

        /// <summary>Is <paramref name="e"/> a notification (Refused, Lost or Conflict, not dismissed)?</summary>
        public static bool IsShown(EditJournalEntry e)
            => !e.Dismissed && e.Status is EditStatus.Refused or EditStatus.Lost or EditStatus.Conflict;

        /// <summary>Fills <paramref name="into"/> (cleared first) with the notifications, NEWEST FIRST, at most <paramref name="max"/>; returns how many
        /// are shown in all (the overlay says "+ N more" past <paramref name="max"/>).</summary>
        public static int Collect(EditJournal journal, List<EditJournalEntry> into, int max = int.MaxValue)
        {
            if (journal is null) throw new ArgumentNullException(nameof(journal));
            if (into is null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            int shown = 0;
            for (int i = journal.Count - 1; i >= 0; i--)
            {
                var e = journal[i];
                if (!IsShown(e)) continue;
                shown++;
                if (into.Count < max) into.Add(e);
            }
            return shown;
        }

        /// <summary>Is <paramref name="e"/> still waiting after <see cref="SlowAfter"/> (the busy mark)?</summary>
        public static bool IsSlow(EditJournalEntry e, DateTime utcNow)
            => e.Status is EditStatus.Pending or EditStatus.None && utcNow.Ticks - e.SubmittedUtcTicks >= SlowAfter.Ticks;

        /// <summary>The user closed it; it stays in the journal (the Messages history), never shown again.</summary>
        public static void Dismiss(EditJournalEntry e) => e.Dismissed = true;

        /// <summary>Closes every notification shown now.</summary>
        public static void DismissAll(EditJournal journal)
        {
            for (int i = 0; i < journal.Count; i++)
                if (IsShown(journal[i])) journal[i].Dismissed = true;
        }

        /// <summary>The first line of a notification: the label and the verdict — <i>"Delete refused"</i>, <i>"Embark got no answer"</i>, <i>"Commit mission plan conflicts"</i>.
        /// ⭐ The card's title (the reason is its body line); <see cref="Text"/> joins the two. Allocates — the overlay caches it per entry.</summary>
        public static string Title(EditJournalEntry e)
        {
            string what = e.Label.Length > 0 ? e.Label : e.Kind.ToString();
            string verdict = e.Status switch { EditStatus.Lost => "got no answer", EditStatus.Conflict => "conflicts", _ => "refused" };
            return $"{what} {verdict}";
        }

        /// <summary>The one line a notification says: <i>"Delete refused — Alpha 1-1 is a player's unit"</i>.</summary>
        public static string Text(EditJournalEntry e)
            => e.Reason.Length > 0 ? $"{Title(e)} — {e.Reason}" : Title(e);
    }
}
