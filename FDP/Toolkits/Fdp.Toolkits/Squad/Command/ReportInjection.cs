using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>One report this node saw (<see cref="SquadReportLog"/>).</summary>
    public sealed class SquadReportLogEntry
    {
        /// <summary>Running number on this node (1-based).</summary>
        public long Serial;
        /// <summary>When this node took it in (sim s).</summary>
        public double SeenAt;
        public Entity FromUnit;
        public Entity ToUnit;
        public CommandReport Report;
        /// <summary>0 = raised here; else the node it arrived from (BR-3).</summary>
        public long OriginNodeId;
        /// <summary>What the inbox did: "folded", "stale" (a late older report dropped), or "not-owned-here".</summary>
        public string Verdict = "";
    }

    /// <summary>
    /// ⭐ <c>CE-2295</c> CC-E (PC-11, revising CC-12) — the last <see cref="Capacity"/> reports a brain node's inbox SAW (folded or dropped,
    /// addressed to a commander here or not), the record <c>GET /squad/reports</c> serves. ⛔ Not <c>GET /events</c>: that ring holds 500
    /// events of every type and a fight's shots evict a report within seconds (Building Clearing §5a). The <c>ShotLog</c> precedent: one
    /// per world, created on first use, network ids resolved at read time. ⚠ Kept where the inbox runs (a brain node).
    /// </summary>
    public sealed class SquadReportLog
    {
        public const int Capacity = 256;
        private static readonly ConditionalWeakTable<EntityRepository, SquadReportLog> s_logs = new();
        private readonly SquadReportLogEntry?[] _ring = new SquadReportLogEntry?[Capacity];
        private long _next;

        /// <summary>The log of <paramref name="world"/>, created on first use.</summary>
        public static SquadReportLog For(EntityRepository world) => s_logs.GetValue(world, _ => new SquadReportLog());

        /// <summary>The log only if <paramref name="world"/> already has one (readers never create it).</summary>
        public static SquadReportLog? Peek(EntityRepository world) => s_logs.TryGetValue(world, out var log) ? log : null;

        /// <summary>How many reports were ever recorded here.</summary>
        public long Count => _next;

        public void Add(SquadReportLogEntry e)
        {
            e.Serial = ++_next;
            _ring[(int)((e.Serial - 1) % Capacity)] = e;
        }

        /// <summary>The newest <paramref name="max"/> entries, newest first.</summary>
        public IEnumerable<SquadReportLogEntry> Recent(int max)
        {
            for (long s = _next, n = 0; s >= 1 && s > _next - Capacity && n < max; s--, n++)
                if (_ring[(int)((s - 1) % Capacity)] is { } e) yield return e;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2295</c> CC-E (CC-13…CC-15, R-2045) — faking a unit's reports and cutting its radio, for diagnostics and for exercising a
    /// superior's decisions. ONE service, used by the debug HTTP and by test rails alike.
    /// <list type="bullet">
    /// <item><see cref="Inject"/>: a report AS IF <c>unit</c>'s staff sent it — the same <see cref="SquadReportNotification"/> the staff
    /// publishes, so it enters BEFORE the wire (BR-3 carries it on), ages from its <c>ObservedAt</c>, and the superior's whole decision path
    /// runs as for a real one; marked <see cref="CommandReport.Synthetic"/> (CC-14: the inbox always takes it, the sender's next real report
    /// replaces it).</item>
    /// <item><see cref="Mute"/>: lost comms — the unit's staff sends nothing, CONTACT included (CC-15); its outbox keeps filling.</item>
    /// </list>
    /// </summary>
    public static class ReportInjection
    {
        /// <summary>Publish <paramref name="report"/> as <paramref name="unit"/>'s, to its direct superior; observed
        /// <paramref name="observedAgoSeconds"/> ago. <c>null</c> = sent; else why not.</summary>
        public static string? Inject(EntityRepository world, Entity unit, CommandReport report, float observedAgoSeconds = 0f)
        {
            if (!world.IsAlive(unit)) return "the unit does not exist";
            if (!world.IsComponentTypeRegistered<UnitSubordinate>() || !world.HasComponent<UnitSubordinate>(unit))
                return "the unit has no superior to report to (UnitSubordinate.Commander)";
            var superior = world.GetComponentRO<UnitSubordinate>(unit).Commander;
            if (superior.IsNull || !world.IsAlive(superior)) return "the unit's superior is gone";
            if (!world.Bus.IsRegisteredManaged<SquadReportNotification>()) return "this node carries no reports (SquadReportNotification unregistered)";
            float now = CommandPictureOps.Now(world);
            report.ObservedAt = now - System.Math.Max(0f, observedAgoSeconds);
            report.Synthetic = 1;
            report.Seq = 0;   // CC-14: the sender's counter lives on its brain node — a fake never guesses it
            world.Bus.PublishManaged(new SquadReportNotification { FromUnit = unit, ToUnit = superior, Report = report, SentAt = now });
            return null;
        }

        /// <summary>Cut <paramref name="unit"/>'s radio for <paramref name="seconds"/> (0 = restore it now). Written into its outbox — on the
        /// unit's BRAIN node. <c>null</c> = done; else why not.</summary>
        public static string? Mute(EntityRepository world, Entity unit, float seconds)
        {
            if (!world.IsAlive(unit)) return "the unit does not exist";
            if (!world.IsComponentTypeRegistered<ReportOutbox>()) return "this node keeps no outboxes (not a brain node)";
            if (!world.HasComponent<ReportOutbox>(unit)) world.AddComponent(unit, new ReportOutbox());
            world.GetComponentRW<ReportOutbox>(unit).MutedUntil = seconds > 0f ? CommandPictureOps.Now(world) + seconds : 0f;
            return null;
        }
    }
}
