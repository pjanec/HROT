using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Xunit;

namespace Fdp.Toolkit.Tests.Replication
{
    /// <summary>
    /// ⭐ Edit framework F7 (docs/UX/DESIGN_Edit_Framework.md §2.5) — the ONE rule of what the user is told: refused / lost / conflict are sticky
    /// notifications, newest first, until dismissed; a pending edit is "slow" after 1 s; applied is silent; collecting allocates nothing.
    /// </summary>
    public sealed class EditNotificationsTests
    {
        private static EditJournalEntry Add(EditJournal j, string label, EditStatus status, string reason = "")
        {
            var e = j.Append(Guid.NewGuid(), EditOp.Delete(new Entity(1, 1), label), 7, "Edit");
            e.Status = status;
            e.Reason = reason;
            return e;
        }

        /// <summary>✅ Red-proof: IsShown without the Lost arm ⇒ the lost edit is missing ⇒ red.</summary>
        [Fact]
        public void F7_RefusedLostAndConflict_AreShownNewestFirst_AppliedAndPendingAreNot()
        {
            using var repo = new EntityRepository();
            var j = EditJournal.Of(repo);
            Add(j, "Rename", EditStatus.Applied);
            var refused = Add(j, "Delete", EditStatus.Refused, "Alpha 1-1 is a player's unit");
            Add(j, "Move", EditStatus.Pending);
            var lost = Add(j, "Embark", EditStatus.Lost);
            var conflict = Add(j, "Commit mission plan", EditStatus.Conflict, "the plan changed (version 4, yours 3)");

            var into = new List<EditJournalEntry>();
            Assert.Equal(3, EditNotifications.Collect(j, into));
            Assert.Equal(new[] { conflict, lost, refused }, into);
            Assert.Equal("Delete refused — Alpha 1-1 is a player's unit", EditNotifications.Text(refused));
            Assert.Equal("Embark got no answer", EditNotifications.Text(lost));

            Assert.Equal(3, EditNotifications.Collect(j, into, max: 1));   // the count of all, the list capped
            Assert.Single(into);

            EditNotifications.Dismiss(conflict);
            Assert.Equal(2, EditNotifications.Collect(j, into));
            Assert.Same(j.Find(conflict.RequestId), conflict);               // dismissed, still in the journal (the history)
            EditNotifications.DismissAll(j);
            Assert.Equal(0, EditNotifications.Collect(j, into));
        }

        [Fact]
        public void F7_APendingEdit_IsSlowAfterOneSecond()
        {
            using var repo = new EntityRepository();
            var e = Add(EditJournal.Of(repo), "Move", EditStatus.Pending);
            var t0 = new DateTime(e.SubmittedUtcTicks, DateTimeKind.Utc);
            Assert.False(EditNotifications.IsSlow(e, t0.AddMilliseconds(900)));
            Assert.True(EditNotifications.IsSlow(e, t0.AddMilliseconds(1100)));
            e.Status = EditStatus.Applied;
            Assert.False(EditNotifications.IsSlow(e, t0.AddSeconds(5)));
        }

        /// <summary>⭐ The hot-path rule (CLAUDE.md): the overlay collects every frame — 0 B once the list has grown.</summary>
        [Fact]
        public void F7_Collecting_AllocatesNothingInTheSteadyState()
        {
            using var repo = new EntityRepository();
            var j = EditJournal.Of(repo);
            for (int i = 0; i < 20; i++) Add(j, "Delete", i % 2 == 0 ? EditStatus.Refused : EditStatus.Applied, "no");
            var into = new List<EditJournalEntry>(32);
            EditNotifications.Collect(j, into);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) EditNotifications.Collect(j, into);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
