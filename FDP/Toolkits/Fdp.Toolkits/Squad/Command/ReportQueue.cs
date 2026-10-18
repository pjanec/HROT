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
        /// <summary>Queue <paramref name="report"/> to go at <paramref name="dueAt"/> (sim s). <c>false</c> when the outbox is full or the
        /// world has none.</summary>
        public static bool Enqueue(EntityRepository world, Entity commander, CommandReport report, float dueAt)
        {
            if (!world.IsComponentTypeRegistered<ReportOutbox>() || !world.IsAlive(commander)) return false;
            if (!world.HasComponent<ReportOutbox>(commander)) world.AddComponent(commander, new ReportOutbox());
            ref var outbox = ref world.GetComponentRW<ReportOutbox>(commander);
            if (outbox.Queued.Count >= ReportOutbox.MaxQueued) return false;
            report.Seq = ++outbox.NextSeq;
            outbox.Queued.Items[outbox.Queued.Count++] = new QueuedReport { Report = report, DueAt = dueAt };
            return true;
        }
    }
}
