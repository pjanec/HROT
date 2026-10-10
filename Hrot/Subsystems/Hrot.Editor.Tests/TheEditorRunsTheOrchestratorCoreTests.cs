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
}
