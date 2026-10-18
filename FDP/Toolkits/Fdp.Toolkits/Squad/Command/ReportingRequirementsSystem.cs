using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐ <c>CE-2291</c> CC-A (CC-2, R-2044) — a superior sets a subordinate commander's EXTRA reporting requirements: the
    /// <c>SetRoeEvent</c> twin. Sent with an order or on its own (<c>SetSubordinateReporting</c>, CC-B); applied by
    /// <see cref="ReportingRequirementsSystem"/>. ⭐ It REPLACES the whole set — all zero withdraws them (a play that set requirements
    /// withdraws them when it ends). ⚠ Applied on the node that raised it (the ROE pattern, <c>CE-2299</c> / BR-13 for cross-node).
    /// </summary>
    [EventId(BehaviorConstants.EventId_SetReporting)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SetReportingEvent
    {
        /// <summary>The subordinate commander whose reporting this sets.</summary>
        public Entity Entity;
        /// <summary>Who orders it; unmarked reads as Operator.</summary>
        public BehaviorOrigin Origin;
        public PirFlags Pir;
        public FfirFlags Ffir;
        public float SitrepEverySeconds;
        /// <summary>PC-3 — the area entity's network id for the area PIRs (0 = none).</summary>
        public long PirArea;
    }

    /// <summary>
    /// ⭐ <c>CE-2291</c> CC-A (CC-2) — applies <see cref="SetReportingEvent"/>: the ONE writer of <see cref="ReportingRequirements"/>,
    /// gated by origin exactly as <c>RoeSystem</c> is (a lower origin than the setter's is refused; the unset state yields to anyone).
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md</c> §3, §8 CC-A.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class ReportingRequirementsSystem : IEcsModuleSystem
    {
        /// <summary>Refused changes since this system was built (a test / diagnostics probe).</summary>
        public int RefusedCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<ReportingRequirements>() || !repo.Bus.IsRegistered<SetReportingEvent>()) return;

            foreach (var evt in repo.Bus.Read<SetReportingEvent>())
                if (!Apply(repo, evt)) RefusedCount++;
        }

        /// <summary>What a <see cref="SetReportingEvent"/> does, applied NOW. <c>false</c> = refused by the gate, or a dead entity.</summary>
        public static bool Apply(EntityRepository repo, in SetReportingEvent evt)
        {
            if (!repo.IsAlive(evt.Entity) || !repo.IsComponentTypeRegistered<ReportingRequirements>()) return false;
            if (!repo.HasComponent<ReportingRequirements>(evt.Entity)) repo.AddComponent(evt.Entity, new ReportingRequirements());

            ref var req = ref repo.GetComponentRW<ReportingRequirements>(evt.Entity);
            var origin = evt.Origin == BehaviorOrigin.Self ? req.SetBy : evt.Origin;
            if (req.SetBy != BehaviorOrigin.Unmarked
                && BehaviorOriginRank.Of(origin) < BehaviorOriginRank.Of(req.SetBy))
                return false;

            req.Pir = evt.Pir;
            req.Ffir = evt.Ffir;
            req.SitrepEverySeconds = evt.SitrepEverySeconds > 0f ? evt.SitrepEverySeconds : 0f;
            req.PirArea = evt.PirArea;
            bool withdrawn = evt.Pir == PirFlags.NoPir && evt.Ffir == FfirFlags.NoFfir && evt.SitrepEverySeconds <= 0f && evt.PirArea == 0;
            req.SetBy = withdrawn ? BehaviorOrigin.Unmarked                  // withdrawn: anyone may set them again
                      : origin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Operator : origin;
            return true;
        }
    }
}
