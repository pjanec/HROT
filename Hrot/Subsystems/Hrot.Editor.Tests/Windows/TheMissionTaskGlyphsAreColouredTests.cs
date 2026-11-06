using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Mission;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using NodeEditor.Core.Widgets;
using Raylib_cs;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ U-3 (docs/UX/DESIGN_Colour_Icons.md §3) — the mission panel's task-state glyph through the ONE icon map: running blue play · done green check · failed red cross · skipped and stopped grey. The REAL
/// <see cref="MissionPanel.DrawContent"/> draws a five-task plan in a real frame (real fonts, Xvfb) and the glyph column is read. 🔴 Red-proof (compiles): <c>MissionPanel.GetTaskIconKey</c> answering "task/stopped"
/// for a done task ⇒ the green check is missing ⇒ red. Render (never in git): <c>HROT_FRAME_SCREENSHOTS</c> ⇒ <c>colour-icons-mission.png</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheMissionTaskGlyphsAreColouredTests
{
    private sealed class Service : IMissionEditorService
    {
        public MissionPlan Plan = new();
        public IReadOnlyList<string> GetAvailableBehaviors(int entityId) => Array.Empty<string>();
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(int entityId) => (Plan, 1);
        public Task<MissionCommitResult> CommitMissionAsync(int entityId, MissionPlan plan, long baseVersion) => Task.FromResult(new MissionCommitResult(true, 1, null));
        public Task<MissionCommitResult> SendControlCommandAsync(int entityId, eMissionCommandType type, Guid taskId) => Task.FromResult(new MissionCommitResult(true, 1, null));
    }

    private sealed class Pick : IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => new TaskCompletionSource<GeoPoint>().Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => new TaskCompletionSource<int>().Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
    }

    private static MissionTask Task_(eTaskState state) => new() { TaskId = Guid.NewGuid(), BehaviorId = "Wait", State = state };

    [Fact]
    public void TheTaskIconKeys_AndTheirColours()
    {
        var plan = new MissionPlan();
        var running = Task_(eTaskState.TASK_PLANNED);
        Assert.Equal("task/running", MissionPanel.GetTaskIconKey(running, true));
        Assert.Equal("task/done", MissionPanel.GetTaskIconKey(Task_(eTaskState.TASK_DONE), false));
        Assert.Equal("task/failed", MissionPanel.GetTaskIconKey(Task_(eTaskState.TASK_FAILED), false));
        Assert.Equal("task/skipped", MissionPanel.GetTaskIconKey(Task_(eTaskState.TASK_SKIPPED), false));
        Assert.Equal("task/stopped", MissionPanel.GetTaskIconKey(Task_(eTaskState.TASK_PLANNED), false));
        Assert.Equal(IconPalette.Green, MissionPanel.GetTaskIcon(Task_(eTaskState.TASK_DONE), false).Color);
        Assert.Equal(IconPalette.Red, MissionPanel.GetTaskIcon(Task_(eTaskState.TASK_FAILED), false).Color);
        Assert.Equal(IconPalette.Blue, MissionPanel.GetTaskIcon(running, true).Color);
    }

    [SkippableFact]
    public void TheRealPanel_DrawsEachTasksGlyphInItsColour()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var service = new Service();
        var active = Task_(eTaskState.TASK_PLANNED);
        service.Plan.Tasks.AddRange(new[] { Task_(eTaskState.TASK_DONE), Task_(eTaskState.TASK_FAILED), Task_(eTaskState.TASK_SKIPPED), active, Task_(eTaskState.TASK_PLANNED) });
        service.Plan.ActiveTaskId = active.TaskId;
        var panel = new MissionPanel { SelectedEntityId = 7 };

        using var frame = UiFrameHarness.Begin(700, 900);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        for (int i = 0; i < 5; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always); ImGui.SetNextWindowSize(new Vector2(660, 880), ImGuiNET.ImGuiCond.Always);
                ImGui.Begin("Mission##u3");
                panel.DrawContent(service, new Pick());
                ImGui.End();
            });
        var dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-frames");
        string path = System.IO.Path.Combine(dir, "colour-icons-mission.png");
        frame.Screenshot(path);
        var image = Raylib.LoadImage(path);
        try
        {
            (int First, int Count) Find(Vector4 v, int tol, int yMin = 20)
            {
                int first = -1, n = 0;
                for (int y = yMin; y < 880; y++) for (int x = 14; x < 34; x++)       // the glyph column
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(c.R - v.X * 255f) <= tol && Math.Abs(c.G - v.Y * 255f) <= tol && Math.Abs(c.B - v.Z * 255f) <= tol) { if (first < 0) first = y; n++; }
                }
                return (first, n);
            }
            var done = Find(IconPalette.Green, 26); var failed = Find(IconPalette.Red, 26); var run = Find(IconPalette.Blue, 26); var grey = Find(IconPalette.Grey, 14, failed.First + 6);   // below the red cross: the anti-aliased edges of the heading text above are grey too
            Assert.True(done.Count >= 6 && failed.Count >= 6 && run.Count >= 6 && grey.Count >= 12, $"green {done.Count}, red {failed.Count}, blue {run.Count}, grey {grey.Count} px (two grey rows)");
            Assert.True(done.First < failed.First && failed.First < grey.First && grey.First < run.First, "down the list: done, failed, skipped, running — the task order");
        }
        finally { Raylib.UnloadImage(image); }
    }
}
