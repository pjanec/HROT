using Fdp.Core;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐ <c>CE-2292</c> CC-B — putting a report into a commander's <see cref="ReportOutbox"/>: numbered here (<see cref="ReportOutbox.NextSeq"/>,
    /// CB-32: per sender, newest wins) and sent when due by the staff (CC-C). The ONE writer of the queue, shared by a play's
    /// <c>SendReport</c> and the staff's composer.
    /// </summary>
    public static class ReportQueue
    {
        /// <summary>Queue <paramref name="report"/> to go at <paramref name="dueAt"/> (sim s). <c>false</c> when the world has none. ⭐
        /// <c>CE-2304</c> — a full outbox drops its OLDEST report for the new one (newest wins, CB-32): an outbox the staff never empties
        /// (a unit with no superior, a Silent posture, a mute) kept the first eight and refused every later one — measured live, a defending
        /// squad's eight Displacing reports silently swallowed its PROGRESS Set. ⭐ <c>CE-2305</c> RQ-2 — <paramref name="subject"/> is what the report is about (a Casevac's casualty).</summary>
        public static bool Enqueue(EntityRepository world, Entity commander, CommandReport report, float dueAt, Entity subject = default)
        {
            if (!world.IsComponentTypeRegistered<ReportOutbox>() || !world.IsAlive(commander)) return false;
            if (!world.HasComponent<ReportOutbox>(commander)) world.AddComponent(commander, new ReportOutbox());
            ref var outbox = ref world.GetComponentRW<ReportOutbox>(commander);
            if (outbox.Queued.Count >= ReportOutbox.MaxQueued)
            {
                for (int i = 1; i < outbox.Queued.Count; i++) outbox.Queued.Items[i - 1] = outbox.Queued.Items[i];
                outbox.Queued.Count--;
            }
            report.Seq = ++outbox.NextSeq;
            outbox.Queued.Items[outbox.Queued.Count++] = new QueuedReport { Report = report, DueAt = dueAt, Subject = subject };
            return true;
        }
    }
}
