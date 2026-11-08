using System;
using System.IO;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Runner;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Primitives;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177) — bookmarks and notes COMPOSED on the REAL editor (<c>EditorSubsystem.RegisterWindows</c>, before its headless guard): the Marks window and the mark popup are registered, <i>B</i> is a shell command bound in the ONE keymap (no
/// conflict with a key already there), greyed outside a run; and in Live — over the editor's OWN facade and ClusterView — the real Marks window lists what the master said and B takes the mark on the editor's bus. ⚠ The Event Timeline window itself
/// (and so its corner) is registered after the headless guard: a headless editor has none, so the corner is railed over the real adapter in <c>Hrot.Presentation.Tests/Marks/TheMarksTests</c> (the editor passes the same binding).
/// Renders <c>e7-marks-real-editor.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git).
/// 🔴 Mutations (each compiles): the <c>marks.mark</c> command not registered ⇒ the key rail reds; the controller built from a different facade than the timeline's ⇒ the Live rail reds.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheMarksOnTheRealEditorTests
{
    [Fact]
    public void E7_TheRealEditor_ComposesTheMarks_BIsOneShellCommandInTheKeymap_AndGreyedOutsideARun()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var wm = new WM(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(wm);

            Assert.NotNull(editor.MarksController);                                          // the host holds ONE controller
            Assert.True(wm.TryGetWindow("editor_marks", out _));
            Assert.True(wm.TryGetWindow("editor_mark_popup", out _));

            var cmd = wm.ShellCommands.Get("marks.mark");
            Assert.NotNull(cmd);
            Assert.Equal(new NodeEditor.Core.Action.KeyBinding(EditorKey.B, KeyModifiers.None), cmd!.DefaultKey);
            Assert.False(cmd.IsEnabled());                                                    // not in a run: greyed
            Assert.Equal("marks are taken in a run or added in its review", editor.MarksController!.MarkDisabledReason);
            wm.ShellCommands.Invoke("marks.mark");
            Assert.Null(editor.MarksController.Popup);                                        // nothing was marked

            // the one keymap: B is declared once and collides with nothing that applies at the same time
            Assert.DoesNotContain(Keymap.Process.Validate(), c => c.First.Chord.Key == EditorKey.B && c.First.Chord.Modifiers == KeyModifiers.None);
        }
        finally { editor.Shutdown(); }
    }

    /// <summary>
    /// ⭐ The REAL editor's marks, in Live, over its own facade and ClusterView (the master's broadcast "heard" on the editor's bus): <i>B</i> takes the mark on the editor's bus, the real Marks window lists the marks and notes it was handed,
    /// and the real popup draws over it — the mockup's ② and ③ from the host's own data, never a stand-in. Renders <c>e7-marks-real-editor.png</c>.
    /// </summary>
    [SkippableFact]
    public void E7_TheRealEditorsMarks_InLive_ListWhatTheMasterSaid_AndBTakesTheMarkOnItsBus()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        using var frame = UiFrameHarness.Begin(1240, 560);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);   // the editor's own fonts: the · and — of the labels
        try
        {
            var wm = new WM(new IconAtlas(IntPtr.Zero, 1240, 560));
            editor.RegisterWindows(wm);
            var facade = editor.TimeTransportForTest!;
            var bus = facade.BusForTest; var view = facade.ViewForTest!;
            var exercise = Guid.NewGuid();
            long wall = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc).Ticks;
            bus.PublishManaged(new Fdp.Toolkit.Orchestration.ClusterStateUpdateEvent { CurrentState = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, ExerciseId = (Fdp.Core.ExerciseId)exercise });
            bus.PublishManaged(new Fdp.Toolkit.Orchestration.ReplaySessionUpdateEvent { Session = new Fdp.Toolkit.Orchestration.ReplaySession
            {
                SessionId = Guid.NewGuid(), ExerciseId = (Fdp.Core.ExerciseId)exercise, Active = false,
                Checkpoints = new[]
                {
                    new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "Mark 1 · 3:18", TotalTime = 198, WallTicks = wall + 198 * TimeSpan.TicksPerSecond },
                    new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "Mark 2 · 7:43", TotalTime = 463, WallTicks = wall + 463 * TimeSpan.TicksPerSecond },
                },
            } });
            bus.PublishManaged(new Fdp.Toolkit.Orchestration.ExerciseNotesUpdateEvent { ExerciseId = (Fdp.Core.ExerciseId)exercise, Notes = new[]
            {
                new Fdp.Toolkit.Orchestration.ExerciseNote(Guid.NewGuid(), "Mark 2 · 7:43", 0, "Bridge crossed", "Alpha 1-2 crossed without cover — discuss", "Instructor-1", 7, "Alpha 1-2"),
                new Fdp.Toolkit.Orchestration.ExerciseNote(Guid.NewGuid(), "Mark 1 · 3:18", 198, "", "", "Instructor-1"),
            } });
            bus.SwapBuffers(); view.Update(); facade.Update(); bus.SwapBuffers();

            var marks = editor.MarksController!;
            Assert.Equal(Hrot.UI.Common.Facades.TransportMode.Live, facade.Mode);
            wm.TryGetWindow("editor_marks", out var window);
            window!.IsOpen = true;
            var atlas = new IconAtlas(IntPtr.Zero, 1240, 560);
            wm.TryGetWindow("editor_mark_popup", out var popup);
            void Draw() { ImGui.SetNextWindowPos(new System.Numerics.Vector2(20, 20)); window.Render("Scenario", atlas); popup!.Render("Scenario", atlas); }   // the editor's OWN window instances (a perspective switch would draw its other windows over them)
            frame.StepN(4, Draw);
            var vm = ((Hrot.Presentation.Windows.MarksWindow)window).SimulateDrawClientArea();
            Assert.Equal(2, vm.Rows.Count);
            Assert.Contains(vm.Rows, r => r.StartsWith("flag 7:43 | Bridge crossed | Alpha 1-2 crossed without cover — discuss | Alpha 1-2 | Instructor-1"));
            Assert.Contains(vm.Rows, r => r.StartsWith("flag 3:18 | Mark 1 · 3:18 | — | — | Instructor-1"));   // an unnamed mark still names who took it

            Assert.Null(marks.MarkDisabledReason);                                           // Live, recorded (the default), the Author seat
            wm.ShellCommands.Invoke("marks.mark");                                           // B
            bus.SwapBuffers();
            var taken = bus.ReadManaged<Fdp.Toolkit.Orchestration.TakeCheckpointIntent>().ToList();
            Assert.Single(taken);
            Assert.StartsWith("Mark 3 · ", taken[0].Label);                                  // numbered past the two marks the master listed
            Assert.NotNull(marks.Popup);
            frame.StepN(4, Draw);

            string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-marks");
            Directory.CreateDirectory(dir);
            frame.Screenshot(Path.Combine(dir, "e7-marks-real-editor.png"));
        }
        finally { editor.Shutdown(); }
    }
}
