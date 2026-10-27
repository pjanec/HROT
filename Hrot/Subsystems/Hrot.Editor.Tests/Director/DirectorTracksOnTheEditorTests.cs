using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Runner;
using Hrot.Editor.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10, T-L2 / T-L3) — the open take on the EDITOR: a headless <see cref="EditorSubsystem"/>
/// (precedent: <c>TheEditorRunsTheOrchestratorCoreTests</c>) holds ONE <see cref="DirectorTakeSession"/>; the Event Timeline panel's <c>Tracks</c> are that
/// take's Camera and Time tracks, and the panel's Ctrl+Z hook (<c>Undo</c>) is the editor's undo stack. The window's own buttons call the same session
/// methods; ImGui itself is not needed (the key chord is a frame rail in <c>Hrot.Presentation.Tests</c>: <c>CE1364_T3_CtrlZ_*</c>).
/// </summary>
public sealed class DirectorTracksOnTheEditorTests
{
    private const string Take = """{"segments":[{"from":0,"to":4}],"keys":[{"at":2,"lookAt":[1,2,3],"distance":60},{"at":5,"lookAt":[4,5,6],"distance":30}]}""";

    /// <summary>With a take open, the timeline panel's tracks ARE its Camera and Time tracks (none before, none after Close); an edit on a track is undone by the
    /// editor's undo (the panel's <c>Undo</c> hook), redone by <c>Redo</c>, and saved to the take's file as v2.
    /// 🔴 Mutation: the panel's <c>Undo</c> wired to nothing (or a second stack) ⇒ the undo assertion fails.</summary>
    [Fact(Timeout = 60_000)]
    public void CE1364_T3_ATakeOpenInTheDirector_ShowsItsTracksOnTheTimeline_AndTheEditorsUndoUndoesATrackEdit()
    {
        var scenario = "ce1364t3_" + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(EditorBootstrap.ScenariosRoot, scenario);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "takes"));
            var file = Path.Combine(dir, "takes", "main.take.json");
            File.WriteAllText(file, Take);

            var session = editor.DirectorTakesForTest;
            var panel = editor.EventTimelineForTest;
            Assert.NotNull(session);
            Assert.NotNull(panel.Tracks);
            Assert.Empty(panel.Tracks!());                         // no take open ⇒ no tracks: every other timeline host unchanged

            Assert.Empty(session!.Open(scenario, "main"));
            var tracks = panel.Tracks!();
            Assert.Equal(new[] { DirectorTracksIds.Camera, DirectorTracksIds.Time }, tracks.Select(t => t.Id));
            var camera = tracks.Single(t => t.Id == DirectorTracksIds.Camera);
            Assert.Equal(new[] { 2d, 5d }, camera.Items.Select(i => i.From));
            Assert.Single(tracks.Single(t => t.Id == DirectorTracksIds.Time).Items);

            camera.Move("key:0", 3.5, commit: true);               // a drag released on the panel
            Assert.Equal(3.5, camera.Items[0].From);
            Assert.True(session.UndoStack.CanUndo);

            panel.Undo!();                                          // what Ctrl+Z on the panel calls
            Assert.Equal(2d, camera.Items[0].From);
            panel.Redo!();
            Assert.Equal(3.5, camera.Items[0].From);

            Assert.Equal(file, session.Save());
            Assert.Equal(3.5, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["keys"]![0]!["at"]!.GetValue<double>());

            session.Close();
            Assert.Empty(panel.Tracks!());
            Assert.False(session.UndoStack.CanUndo);                // a closed take leaves no history behind
        }
        finally
        {
            editor.Shutdown();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    /// <summary>A second take cannot be opened over unsaved edits (they would be lost), and J / L step the ONE playback rate the Director window's slider shows.
    /// 🔴 Mutation: <c>Open</c> not checking <c>Dirty</c> ⇒ the refusal assertion fails.</summary>
    [Fact(Timeout = 60_000)]
    public void CE1364_T3_TheSession_RefusesASecondTakeOverUnsavedEdits_AndJLStepTheSharedRate()
    {
        var scenario = "ce1364t3_" + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(EditorBootstrap.ScenariosRoot, scenario);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "takes"));
            File.WriteAllText(Path.Combine(dir, "takes", "main.take.json"), Take);
            File.WriteAllText(Path.Combine(dir, "takes", "other.take.json"), Take);
            var session = editor.DirectorTakesForTest!;

            Assert.Empty(session.Open(scenario, "main"));
            Assert.Empty(session.Open(scenario, "other"));          // nothing edited yet: switching is free
            session.Editor!.Apply(new Hrot.UI.Common.Map3D.Director.TakeEdit.RemoveKey(0));
            var refused = session.Open(scenario, "main");
            Assert.Single(refused);
            Assert.Equal("other", session.TakeName);

            Assert.Equal(1f, session.Speed);
            session.StepRate(+1); Assert.Equal(1.5f, session.Speed);
            session.StepRate(+1); session.StepRate(+1); Assert.Equal(2f, session.Speed);   // the top stays
            session.StepRate(-1); session.StepRate(-1); session.StepRate(-1); session.StepRate(-1); session.StepRate(-1); session.StepRate(-1);
            Assert.Equal(0.25f, session.Speed);                      // and so does the bottom
        }
        finally
        {
            editor.Shutdown();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    /// <summary>CE-1364 — K on the Camera track stores the editor's map camera lens: the key's <c>Fov</c> is the 3-D camera's <c>FovyDegrees</c> (the one field of view the map
    /// has — the film default is only what a key WITHOUT a lens plays at). 🔴 Mutation: <c>PilotCameraSpec</c> not passing <c>cam.FovyDegrees</c> ⇒ the key's Fov is null.</summary>
    [Fact(Timeout = 60_000)]
    public void CE1364_K_OnTheEditorsTimeline_StoresTheMapCamerasFov()
    {
        var scenario = "ce1364fov_" + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(EditorBootstrap.ScenariosRoot, scenario);
        var editor = new EditorSubsystem();
        editor.Initialize(new SubsystemConfig { Headless = true });
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "takes"));
            File.WriteAllText(Path.Combine(dir, "takes", "main.take.json"), Take);
            editor.SetMapCameraForTest(new Fdp.Toolkit.Vis3D.MapCamera3D { FovyDegrees = 31f });
            var session = editor.DirectorTakesForTest!;
            Assert.Empty(session.Open(scenario, "main"));

            var camera = editor.EventTimelineForTest.Tracks!().Single(t => t.Id == DirectorTracksIds.Camera);
            Assert.True(camera.Key('K', 7d));

            var added = session.Editor!.Take.Keys[^1];
            Assert.Equal(7d, added.At.Offset);
            Assert.Equal(31f, added.Fov);
        }
        finally
        {
            editor.Shutdown();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

/// <summary>The track ids the editor rail names (the product's are <c>DirectorTracks.CameraId</c> / <c>TimeId</c>).</summary>
internal static class DirectorTracksIds
{
    public const string Camera = Hrot.UI.Common.Map3D.Director.DirectorTracks.CameraId;
    public const string Time = Hrot.UI.Common.Map3D.Director.DirectorTracks.TimeId;
}
