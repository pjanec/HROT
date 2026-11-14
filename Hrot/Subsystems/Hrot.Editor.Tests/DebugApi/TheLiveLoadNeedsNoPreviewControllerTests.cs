using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — <c>POST /scenario/load/live</c> on a host with NO preview controller (a <c>--mode all</c> cluster node):
/// it is never in a dry run, so the load goes out and stops nothing. 📌 behaviors, 2026-10-23
/// (<c>NOTE_Behaviors_To_Ui_LoadLive_Preview_Regression.md</c>): reading the preview directly answered
/// <c>NOT_SUPPORTED_HERE(preview.control)</c> for every live load there — the CE-1025 rails all had a preview controller.
/// 🔴 Red-proof: read <c>_preview.IsInPreviewMode</c> in <c>LeaveRunForLoad</c> ⇒ red.
/// </summary>
public sealed class TheLiveLoadNeedsNoPreviewControllerTests
{
    [Fact]
    public void A_live_load_on_a_host_without_a_preview_controller_goes_out_and_stops_no_run()
    {
        var master = new RecordingMasterControl();
        var transitions = master.Transitions;
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("Orchestrator", "Orchestrator", master: () => master),
            },
            currentPerspective: () => "Orchestrator",
            acksPending: null),
            clusterState: () => ClusterState.Idle);

        var answer = svc.LoadScenarioLive("df-lone-soldier-reflex");

        Assert.Null(answer["stoppedRun"]);
        var sent = Assert.Single(transitions);
        Assert.Equal(ClusterState.OperatingLive, sent.TargetState);
    }
}
