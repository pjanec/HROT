using System;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐ Q86 (R-215) — the editor runs the ONE orchestrator core the cluster runs, so a scenario load resets ITS
/// clock to 0 through the same load handler. ⛔ Before, the editor ran a hand-built copy of the orchestrator with no
/// load handler at all, and a load left the clock at whatever time the last world reached (CE-122).
/// 📄 docs/blueprints/Architect_Question_86_Editor_Runs_The_Orchestrator_Core.md §5 S5.
/// </summary>
public sealed class TheEditorRunsTheOrchestratorCoreTests
{
    [Fact(Timeout = 60_000)]
    public void Q86S5_AnEditorScenarioLoad_ResetsTheEditorsClockToZero()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var clock = editor.TimeControllerForTest!;
            var bus   = editor.OrchestrationBusForTest!;

            clock.SwitchToContinuous();
            editor.Update(0.016f);
            System.Threading.Thread.Sleep(200);
            editor.Update(0.016f);
            Assert.True(clock.GetCurrentState().TotalTime > 0.05, $"the clock must be running first ({clock.GetCurrentState().TotalTime})");

            bus.PublishManaged(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingLive,
                ScenarioId    = "q86s5_" + Guid.NewGuid().ToString("N"),   // no saved context ⇒ the fresh-scenario path: t = 0
                TimeMode      = "Deterministic",
            });
            // The load handler asks for the jump in frame 0; the editor updates its clock BEFORE it swaps the
            // orchestration bus (EditorSubsystem Update), so the request reaches the clock in frame 2 — the same
            // path, and the same latency, as the toolbar's own pause request. Measured: t = 0.000 from frame 2.
            var trace = new System.Text.StringBuilder();
            for (int frame = 0; frame < 3; frame++)
            {
                editor.Update(0.016f);
                trace.Append($" f{frame}:t={clock.GetCurrentState().TotalTime:F3}");
            }

            Assert.True(clock.GetCurrentState().TotalTime < 0.05,
                $"an editor load must reset the clock to 0 (was {clock.GetCurrentState().TotalTime}); trace:{trace}");
        }
        finally
        {
            editor.Shutdown();
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3330</c> — the editor has NO timeline of its own: on a real headless editor its live index IS its node context's
    /// (<c>Timeline</c>, E-1); frames of the editor (<c>Update</c>, the clock running) step it, so a unit that gains a network id in its world
    /// lands in that index (E-2, red-proof: the editor's <c>_node?.StepTimeline()</c> behind a runtime-false guard); and the access its
    /// <c>/timeline</c> attach serves (<c>TimelineOf(node)</c>) reads that same source (E-3: the attach's access, not the HTTP service — that exists only with HROT_DEBUG_API_PORT).
    /// </summary>
    [Fact(Timeout = 60_000)]
    public void CE3330_Node_TheEditorsLiveTimelineIsItsNodeContexts_SteppedByItsFrames_AndServedOnItsDebugSurface()
    {
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            var node = editor.NodeForTest!;
            Assert.NotNull(editor.LiveTimeline);                                              // E-1
            Assert.Same(node.Timeline, editor.LiveTimeline);

            var world = node.World;
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
                world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            editor.TimeControllerForTest!.SwitchToContinuous();
            editor.Update(0.016f);
            System.Threading.Thread.Sleep(50);
            editor.Update(0.016f);
            Assert.True(editor.LiveTimeline!.Frames > 0, "the editor's frames must be indexing before the event happens");

            var unit = world.CreateEntity();                                                  // E-2: an event between two editor frames
            world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(31337));
            var spawned = System.Array.Empty<Fdp.Toolkit.ReplayBrowser.Timeline.TimelineEvent>();
            for (int i = 0; i < 40 && spawned.Length == 0; i++)
            {
                System.Threading.Thread.Sleep(80);                                            // the snapshot publishes at most every quarter second
                editor.Update(0.016f);
                spawned = System.Linq.Enumerable.ToArray(editor.LiveTimeline.Index.Lane(Fdp.Toolkit.Replication.ReplicationTimelineLanes.Spawned.Id));
            }
            var hit = System.Linq.Enumerable.SingleOrDefault(spawned, e => e.Subject == 31337);
            Assert.True(hit.Subject == 31337, "the unit that gained a network id must reach the editor's live timeline index");

            // E-3: the editor's debug service exists only when HROT_DEBUG_API_PORT starts its HTTP host, so it is not constructed here; its
            //   /timeline attach is `_node is {} n ? EcsNodeDebugProvider.TimelineOf(n) : null` — the access asserted here is that very object.
            Assert.Same(editor.LiveTimeline, Hrot.Presentation.DebugApi.EcsNodeDebugProvider.TimelineOf(node).Source());
        }
        finally
        {
            editor.Shutdown();
        }
    }
}
