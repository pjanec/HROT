using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// 🔴🔴🔴 <b><c>UXI-23</c> <c>S3</c> — the map says so when it is RUNNING AND DRAWING NOTHING.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> ⭐ **§3.2e** (why this exists and why the
    /// declare-half does not replace it).
    ///
    /// <para>⛔⛔ <b>This is the half that would actually have caught <c>CE-123</c>.</b> §3.2a promised that
    /// declaring required systems and reporting unserviceable would have made SimHost's empty map <i>"a
    /// LOUD DIAGNOSTIC from the day GZH-003 landed"</i>. 📐 Re-measured: <b>false</b>. SimHost scheduled the
    /// group (<c>SimHostApp.cs:442</c>), all three systems were present, and the gate was open — a run-set
    /// check would have printed <i>"nothing unserviceable"</i> while the map drew <b>3</b> non-<c>Line</c>
    /// primitives for <b>8</b> entities. The failure was never a MISSING system; it was a present system
    /// told to draw nothing.</para>
    ///
    /// <para>⭐⭐ <b>What it checks instead — the observable signature of that bug.</b> The group is enabled,
    /// the world holds entities the map's own query matches (<c>SimTransform</c> + <c>NetworkIdentity</c>),
    /// and the frame contains <b>zero</b> <c>SemanticShape</c> primitives. That combination is exactly what
    /// SimHost looked like for weeks, and it is cheap to notice.</para>
    ///
    /// <para>⚠⚠ <b>It REPORTS; it never throws, and it never fails a boot.</b> A host can legitimately draw
    /// no shapes — with off-screen culling enabled and everything culled, which is live today as
    /// <c>CE-131</c>. So the message names culling among the causes, and the report LATCHES: once while
    /// broken, once more when it recovers. ⛔ A diagnostic that fires every frame is a diagnostic nobody
    /// reads.</para>
    ///
    /// <para>🔒 Contract copied verbatim from <c>ToolActivationDrainSystem</c>: an
    /// <c>Action&lt;string&gt;?</c> that defaults to the FDP log and carries <b>the name and the
    /// reason</b> — because <i>"nothing happened"</i> is indistinguishable from <i>"not implemented"</i> to
    /// the operator holding the mouse.</para>
    ///
    /// <para>⭐ Scheduled by the HOST like everything else the pack builds, as the LAST member of the gizmo
    /// group so it observes the frame the other three just wrote. When the group is disabled it does not
    /// run at all — which is correct: a headless node with no viewer is legitimately silent
    /// (<c>GZH-003</c>), and that is not what this looks for.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class MapSelfCheckSystem : IEcsModuleSystem
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly Func<bool> _isEnabled;
        private readonly Action<string>? _report;

        /// <summary>Latched so the message appears on the transition, not once per frame.</summary>
        private bool _reportedSilent;

        /// <summary>
        /// Frames to observe before reporting. ⭐ A load, a perspective switch or a world swap can leave a
        /// legitimately empty frame or two; a defect stays empty.
        /// </summary>
        internal const int GraceFrames = 120;

        private int _silentFrames;

        /// <param name="isEnabled">
        /// Whether the gizmo group is currently running. ⭐ A delegate rather than the group itself
        /// because the group is constructed WITH this system as its last member — its inner systems are
        /// constructor-only — so taking the group directly would be circular.
        /// </param>
        public MapSelfCheckSystem(
            DebugPrimitiveBuffer buffer,
            Func<bool> isEnabled,
            Action<string>? report = null)
        {
            _buffer    = buffer    ?? throw new ArgumentNullException(nameof(buffer));
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            _report    = report;
        }

        /// <summary>Exposed for rails: how many consecutive silent frames have been seen.</summary>
        internal int SilentFrames => _silentFrames;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;

            // A disabled group is legitimately silent — GZH-003 headless-first. Nothing to say.
            if (!_isEnabled())
            {
                _silentFrames = 0;
                return;
            }

            int eligible = CountEligibleEntities(repo);
            if (eligible == 0)
            {
                // No entities to draw is not a defect; it is an empty world.
                _silentFrames = 0;
                Recover();
                return;
            }

            if (CountEntityShapes() > 0)
            {
                _silentFrames = 0;
                Recover();
                return;
            }

            if (++_silentFrames < GraceFrames || _reportedSilent) return;

            _reportedSilent = true;
            Report(
                $"the map is RUNNING AND DRAWING NOTHING — the gizmo group is enabled and {eligible} "
              + "entit" + (eligible == 1 ? "y" : "ies") + " match the map's query (SimTransform + "
              + "NetworkIdentity), but the frame contains zero SemanticShape primitives after "
              + $"{_silentFrames} frames. Likely causes, in the order they have actually occurred: a "
              + "selection predicate reaching StatelessGizmoSystem, where it gates EVERY projector at once "
              + "(CE-123); off-screen culling marking every entity invisible (CE-131); or the entity "
              + "projector not registered at all.");
        }

        private void Recover()
        {
            if (!_reportedSilent) return;
            _reportedSilent = false;
            Report("the map is drawing entity shapes again.");
        }

        private void Report(string message)
        {
            if (_report != null) _report(message);
            else FdpLog<MapSelfCheckSystem>.Info("[Map] {0}", message);
        }

        /// <summary>
        /// Entities the shared entity projector would match. ⚠ Deliberately the SAME pair
        /// <c>EntityPresentationGizmo</c>'s <c>[GizmoProjector]</c> declares — if that query widens, this
        /// must widen with it or the check goes quietly vacuous.
        /// </summary>
        private static int CountEligibleEntities(EntityRepository repo)
        {
            int count = 0;
            var index = repo.GetEntityIndex();
            int maxIndex = index.MaxIssuedIndex;

            for (int i = 0; i <= maxIndex; i++)
            {
                ref readonly var meta = ref index.GetMetadata(i);
                if (!meta.IsActive) continue;

                var entity = new Entity(i, meta.Generation);
                if (repo.HasComponent<SimTransform>(entity) && repo.HasComponent<NetworkIdentity>(entity))
                    count++;
            }
            return count;
        }

        private int CountEntityShapes()
        {
            var frame = _buffer.GetFrame();
            int count = 0;
            for (int i = 0; i < frame.Length; i++)
                if (frame[i].Shape == DebugPrimitiveShape.SemanticShape) count++;
            return count;
        }
    }
}
