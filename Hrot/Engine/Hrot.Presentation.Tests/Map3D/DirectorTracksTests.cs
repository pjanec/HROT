using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Fdp.Toolkit.Vis3D;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10) — the director's tracks: the <see cref="TakeEditor"/> (one edit path, undo on the
/// <see cref="GizmoUndoStack"/>, save through <see cref="TakeStore"/>) and the Camera / Time tracks over it. The editor-level rail (the open take on the
/// panel, undo through the editor's stack) is <c>Hrot.Editor.Tests</c>'s <c>CE1364_T3_*</c>.
/// </summary>
public sealed class DirectorTracksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "t3-" + Guid.NewGuid().ToString("N"));
    private readonly TakeStore _store;

    public DirectorTracksTests() => _store = new TakeStore(() => _root, () => null);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Write(string name, string json)
    {
        var path = _store.TakePath("sc", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private const string V2 = """
        {"anchors":{"fire":{"on":"shot","who":"Tank 1","n":1}},
         "baked":{"times":{"fire":10},"tickHz":60,"scenarioHash":"h","buildSha":"b"},
         "segments":[{"from":0,"to":4},{"from":8,"to":12}],
         "keys":[{"at":2,"lookAt":[1,2,3],"distance":60,"pitch":-30},
                 {"at":5,"follow":"Tank 1","distance":12},
                 {"at":"fire+3","lookAt":[9,9,0],"distance":30}]}
        """;

    private const string V1 = """
        {"shots":[{"at":1,"seconds":3,"lookAt":[1,2,3],"distance":40},
                  {"at":6,"seconds":2,"follow":"Tank 1","distance":10,"to":{"distance":20}}]}
        """;

    private (TakeEditor Editor, DirectorTracks Tracks, GizmoUndoStack Undo) Open(string name, Func<CameraSpec?>? pilot = null, Action<int>? rate = null)
    {
        var loaded = _store.Load("sc", name);
        Assert.True(loaded.Ok, string.Join("; ", loaded.Problems));
        var undo = new GizmoUndoStack();
        var editor = new TakeEditor(_store, loaded.Value!, undo);
        return (editor, new DirectorTracks(editor, pilot ?? (() => null), rate), undo);
    }

    private static CameraSpec Look(float x) => new() { LookAt = new Vector3(x, 0, 0), Distance = 50 };

    private static double? SavedAt(string path, string array, int index) =>
        JsonNode.Parse(File.ReadAllText(path))![array]![index]![array == "keys" ? "at" : "from"]!.GetValue<double?>();

    /// <summary>A committed Move changes the key's time in the TAKE, and Save writes it (v2: <c>keys[]</c>) — the file says the new time.
    /// 🔴 Mutation: <c>TakeEditor.Retimed</c> keeping the old time ⇒ both the take and the file assertions fail.</summary>
    [Fact]
    public void CE1364_T3_ACommittedMove_ChangesTheKeysTime_AndSaveWritesIt()
    {
        var path = Write("main", V2);
        var (editor, tracks, _) = Open("main");
        Assert.Equal(new[] { 2d, 5d, 13d }, tracks.Camera.Items.Select(i => i.From));   // the anchored key sits at fire (10) + 3

        tracks.Camera.Move("key:1", 7.5, commit: true);

        Assert.Equal(new AtExpr(7.5), editor.Take.Keys[1].At);
        Assert.True(editor.Dirty);
        Assert.Equal(path, editor.Save());
        Assert.Equal(7.5, SavedAt(path, "keys", 1));
        Assert.Equal(7.5, _store.Load("sc", "main").Value!.Keys[1].At.Offset);   // and a reader gets it back
        Assert.False(editor.Dirty);
    }

    /// <summary>A key made from an anchor keeps its anchor: moving it changes the OFFSET, so a re-scout still carries it with the beat.
    /// 🔴 Mutation: <c>Retimed</c> returning a fixed time ⇒ the anchor assertion fails.</summary>
    [Fact]
    public void CE1364_T3_MovingAnAnchoredKey_KeepsItsAnchor_AndChangesTheOffset()
    {
        Write("main", V2);
        var (editor, tracks, _) = Open("main");
        tracks.Camera.Move("key:2", 15, commit: true);   // fire is at 10
        Assert.Equal(new AtExpr(5, "fire"), editor.Take.Keys[2].At);
        Assert.Equal(15d, tracks.Camera.Items[2].From);
    }

    /// <summary>A preview Move (a drag in progress) shows the moved item and touches nothing: the take is the same object, nothing is dirty, nothing is on the
    /// undo stack. 🔴 Mutation: the preview branch calling <c>Apply</c> ⇒ the take / undo assertions fail.</summary>
    [Fact]
    public void CE1364_T3_APreviewMove_DoesNotChangeTheTake()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        var before = editor.Take;

        tracks.Camera.Move("key:0", 3.25, commit: false);

        Assert.Same(before, editor.Take);
        Assert.False(editor.Dirty);
        Assert.False(undo.CanUndo);
        Assert.Equal(3.25, tracks.Camera.Items[0].From);          // shown moved ...
        tracks.Camera.Move("key:0", 0, commit: false);
        editor.ClearPreview();
        Assert.Equal(2d, tracks.Camera.Items[0].From);            // ... and back where the take says when the drag is gone
    }

    /// <summary>Undo puts the previous time back, Redo re-applies it — through the <see cref="GizmoUndoStack"/> (T-L2).
    /// 🔴 Mutation: <c>TakeEditRecord.Undo</c> restoring the AFTER take ⇒ the undo assertion fails.</summary>
    [Fact]
    public void CE1364_T3_Undo_RestoresThePreviousTime_AndRedoReappliesIt()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");

        tracks.Camera.Move("key:0", 4, commit: true);
        Assert.Equal(4d, tracks.Camera.Items[0].From);
        Assert.Equal("Move camera key", undo.UndoDescription);

        undo.Undo(null!);
        Assert.Equal(2d, tracks.Camera.Items[0].From);
        Assert.False(editor.Dirty);   // back at the file's own take

        undo.Redo(null!);
        Assert.Equal(4d, tracks.Camera.Items[0].From);
        Assert.True(editor.Dirty);
    }

    /// <summary>K adds a CUT key at the playhead with the pilot camera, and selects it; J / L step the rate (and are not taken without a rate seam).
    /// 🔴 Mutation: K adding the key at 0 instead of the playhead ⇒ the time assertion fails.</summary>
    [Fact]
    public void CE1364_T3_K_AddsACutKeyAtThePlayhead_WithThePilotsCamera()
    {
        Write("main", V2);
        var steps = new List<int>();
        var (editor, tracks, undo) = Open("main", () => Look(77), steps.Add);

        Assert.True(tracks.Camera.Key('K', 6.5));

        var added = editor.Take.Keys[^1];
        Assert.Equal(new AtExpr(6.5), added.At);
        Assert.Equal(KeyTransition.Cut, added.In);
        Assert.Equal(new Vector3(77, 0, 0), added.Camera.LookAt);
        Assert.Equal(4, tracks.Camera.Items.Count);
        Assert.True(tracks.Camera.Items[^1].Selected);
        Assert.True(undo.CanUndo);

        Assert.True(tracks.Camera.Key('J', 1)); Assert.True(tracks.Camera.Key('L', 1));
        Assert.Equal(new[] { -1, 1 }, steps);
        Assert.False(tracks.Time.Key('K', 1));   // a segment never takes K
    }

    /// <summary>Without a pilot camera K is not taken (no key is invented); without a rate seam neither is J / L. 🔴 Mutation: K adding a default camera
    /// when the pilot is null ⇒ the count assertion fails.</summary>
    [Fact]
    public void CE1364_T3_WithoutAPilotOrARate_KJL_AreNotTaken()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        Assert.False(tracks.Camera.Key('K', 1));
        Assert.False(tracks.Camera.Key('J', 1));
        Assert.Equal(3, editor.Take.Keys.Count);
        Assert.False(undo.CanUndo);
    }

    /// <summary>With a unit selected the key is a FOLLOW camera on it from the SAME eye (TE-2): the pose's eye is reproduced by the follow camera's maths.
    /// 🔴 Mutation: <c>PilotCamera.Of</c> ignoring the unit ⇒ <c>Follow</c> is null and the assertion fails.</summary>
    [Fact]
    public void CE1364_T3_APilotCameraWithASelectedUnit_IsAFollowKeyFromTheSameEye()
    {
        var pose = new CameraPose(new Vector3(100, 50, 0), 80f, 0.7f, -0.5f);
        var unit = new Vector3(120, 60, 2);
        var spec = PilotCamera.Of(pose, "Tank 1", unit);

        Assert.Equal("Tank 1", spec.Follow);
        Assert.Null(spec.LookAt);
        const float rad = MathF.PI / 180f;
        var target = unit + new Vector3(0, 0, spec.Lift);
        var forward = new Vector3(MathF.Cos(spec.Pitch * rad) * MathF.Cos(spec.Yaw * rad), MathF.Cos(spec.Pitch * rad) * MathF.Sin(spec.Yaw * rad), MathF.Sin(spec.Pitch * rad));
        var eyeBefore = pose.LookAt - new Vector3(MathF.Cos(pose.Pitch) * MathF.Cos(pose.Yaw), MathF.Cos(pose.Pitch) * MathF.Sin(pose.Yaw), MathF.Sin(pose.Pitch)) * pose.Distance;
        var eyeAfter = target - forward * spec.Distance;   // what FilmStudio's follow camera computes
        Assert.True(Vector3.Distance(eyeBefore, eyeAfter) < 0.05f, $"{eyeBefore} vs {eyeAfter}");

        // no unit ⇒ a plain look-at key of the same pose
        var plain = PilotCamera.Of(pose);
        Assert.Equal(pose.LookAt, plain.LookAt);
        Assert.Equal(80f, plain.Distance);
        Assert.Null(plain.Follow);
    }

    /// <summary>"Delete key" (the key's right-click item) removes it, and Undo brings it back. 🔴 Mutation: the menu item removing index + 1 ⇒ the wrong key goes.</summary>
    [Fact]
    public void CE1364_T3_DeleteKey_RemovesTheKey_AndIsUndoable()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        Assert.Empty(tracks.Camera.MenuItems(null));

        var item = Assert.Single(tracks.Camera.MenuItems("key:1"));
        Assert.Equal("Delete key", item.Label);
        item.Run();

        Assert.Equal(new[] { 2d, 13d }, tracks.Camera.Items.Select(i => i.From));
        Assert.Equal("Delete camera key", undo.UndoDescription);
        undo.Undo(null!);
        Assert.Equal(new[] { 2d, 5d, 13d }, tracks.Camera.Items.Select(i => i.From));
        Assert.Equal(3, editor.Take.Keys.Count);
    }

    /// <summary>A v1 take that is not edited is never re-written (T-L4) — Save does nothing, the file is byte-identical; an EDITED one is written as v2 with its
    /// shots read as keys, plus the edit. 🔴 Mutation: <c>Save</c> writing whenever called ⇒ the first block fails.</summary>
    [Fact]
    public void CE1364_T3_AV1Take_IsNeverRewrittenUnlessEdited_AndThenWritesV2()
    {
        var path = Write("old", V1);
        var bytes = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        var (editor, tracks, _) = Open("old");

        Assert.Equal(new[] { 1d, 6d, 8d }, tracks.Camera.Items.Select(i => i.From));   // the shots as keys: a cut at 1; a cut at 6 and its fly's end at 8
        Assert.Null(editor.Save());
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));

        tracks.Camera.Move("key:0", 0.5, commit: true);
        editor.Save();

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var keys = json["keys"]!.AsArray();
        Assert.Equal(3, keys.Count);                       // every shot-as-key is there ...
        Assert.Equal(0.5, keys[0]!["at"]!.GetValue<double>());   // ... with the edit
        Assert.Equal(6d, keys[1]!["at"]!.GetValue<double>());    // the others where they were
        Assert.Equal(new[] { 0.5, 6d, 8d }, _store.Load("sc", "old").Value!.Keys.Select(k => k.At.Offset));
    }

    /// <summary>A segment moves whole (its length kept) from the track, and by one EDGE through the editor; a preview moves nothing. 🔴 Mutation: the whole move
    /// setting only <c>From</c> ⇒ the length assertion fails.</summary>
    [Fact]
    public void CE1364_T3_ASegment_MovesWhole_OrByAnEdge()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        Assert.Equal(new[] { (0d, 4d), (8d, 12d) }, tracks.Time.Items.Select(i => (i.From, i.To!.Value)));

        tracks.Time.Move("seg:1", 9, commit: false);
        Assert.False(editor.Dirty);
        Assert.Equal((9d, 13d), (tracks.Time.Items[1].From, tracks.Time.Items[1].To!.Value));

        tracks.Time.Move("seg:1", 9, commit: true);
        Assert.Equal((9d, 13d), (tracks.Time.Items[1].From, tracks.Time.Items[1].To!.Value));
        Assert.True(editor.Apply(new TakeEdit.MoveSegment(0, SegmentEdge.To, 6)));
        Assert.Equal((0d, 6d), (tracks.Time.Items[0].From, tracks.Time.Items[0].To!.Value));
        Assert.True(editor.Apply(new TakeEdit.MoveSegment(0, SegmentEdge.From, 9)));
        Assert.Equal(6d - TakeEditor.MinSegmentSeconds, tracks.Time.Items[0].From, 9);   // an edge never crosses the other (CE-1364: it stops one minimum length short)
        Assert.Equal(6d, tracks.Time.Items[0].To!.Value);
        undo.Undo(null!); undo.Undo(null!); undo.Undo(null!);
        Assert.Equal((8d, 12d), (tracks.Time.Items[1].From, tracks.Time.Items[1].To!.Value));
    }

    /// <summary>An edit naming no item changes nothing and records nothing. 🔴 Mutation: no index check ⇒ the call throws.</summary>
    [Fact]
    public void CE1364_T3_AnEditOfNoItem_ChangesNothing()
    {
        Write("main", V2);
        var (editor, _, undo) = Open("main");
        Assert.False(editor.Apply(new TakeEdit.MoveKey(9, 1)));
        Assert.False(editor.Apply(new TakeEdit.RemoveKey(-1)));
        Assert.False(editor.Dirty);
        Assert.False(undo.CanUndo);
    }

    // ── CE-1364 leftovers: segment edges, add / delete a segment, K's lens ─────────────────────────────────────────

    /// <summary>An EDGE drag previews (the take untouched, only the grabbed edge shown moved) and commits ONE edit that moves just that edge; Undo puts it back.
    /// 🔴 Mutation: the 4-argument <c>Move</c> mapping both edges to <c>SegmentEdge.Whole</c> ⇒ the other edge moves too and the assertions fail.</summary>
    [Fact]
    public void CE1364_AnEdgeDrag_PreviewsThenCommits_MovingOnlyThatEdge()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");

        tracks.Time.Move("seg:1", 9, commit: false, TrackItemPart.StartEdge);
        Assert.False(editor.Dirty);
        Assert.False(undo.CanUndo);
        Assert.Equal((9d, 12d), (tracks.Time.Items[1].From, tracks.Time.Items[1].To!.Value));   // only the start moved, on screen
        Assert.Equal((8d, 12d), (editor.Take.Segments[1].From.Offset, editor.Take.Segments[1].To.Offset));   // the take did not

        tracks.Time.Move("seg:1", 9, commit: true, TrackItemPart.StartEdge);
        Assert.Null(editor.Preview);
        Assert.Equal((9d, 12d), (tracks.Time.Items[1].From, tracks.Time.Items[1].To!.Value));
        tracks.Time.Move("seg:0", 7, commit: true, TrackItemPart.EndEdge);
        Assert.Equal((0d, 7d), (tracks.Time.Items[0].From, tracks.Time.Items[0].To!.Value));

        undo.Undo(null!); undo.Undo(null!);
        Assert.Equal(new[] { (0d, 4d), (8d, 12d) }, tracks.Time.Items.Select(i => (i.From, i.To!.Value)));
    }

    /// <summary>An edge cannot cross the other: dragged past it, it stops <see cref="TakeEditor.MinSegmentSeconds"/> short — in the preview AND in the commit; the body drag
    /// still moves the segment whole (length kept). 🔴 Mutation: no minimum (<c>Math.Min(s.ToSim, to)</c>) ⇒ a zero-length segment and the first assertion fails.</summary>
    [Fact]
    public void CE1364_TheEdges_CannotCross_AndTheBodyStillMovesWhole()
    {
        Write("main", V2);
        var (editor, tracks, _) = Open("main");

        tracks.Time.Move("seg:1", 99, commit: false, TrackItemPart.StartEdge);
        Assert.Equal(12d - TakeEditor.MinSegmentSeconds, tracks.Time.Items[1].From, 9);
        tracks.Time.Move("seg:1", 99, commit: true, TrackItemPart.StartEdge);
        Assert.Equal(12d - TakeEditor.MinSegmentSeconds, tracks.Time.Items[1].From, 9);
        Assert.Equal(12d, tracks.Time.Items[1].To!.Value);

        tracks.Time.Move("seg:0", -5, commit: true, TrackItemPart.EndEdge);
        Assert.Equal(TakeEditor.MinSegmentSeconds, tracks.Time.Items[0].To!.Value, 9);
        Assert.Equal(0d, tracks.Time.Items[0].From);

        tracks.Time.Move("seg:0", 20, commit: true, TrackItemPart.Body);   // the body: whole, length kept
        Assert.Equal(20d, tracks.Time.Items[0].From);
        Assert.Equal(20d + TakeEditor.MinSegmentSeconds, tracks.Time.Items[0].To!.Value, 9);
    }

    /// <summary>The Time track's right-click on EMPTY space offers "Add a segment here": a default-length segment, speed 1, no gap, at the CLICKED sim time, selected;
    /// Undo removes it. A segment's own menu has no "add". 🔴 Mutation: the item adding at 0 instead of the clicked time ⇒ the From assertion fails.</summary>
    [Fact]
    public void CE1364_AddASegmentHere_AddsAtTheClickedTime_AndUndoRemovesIt()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");

        Assert.Empty(tracks.Time.MenuItems(null));   // the plain menu knows no time: nothing to add at
        var add = Assert.Single(tracks.Time.MenuItems(null, 5.5));
        Assert.Equal("Add a segment here", add.Label);
        add.Run();

        Assert.Equal(3, editor.Take.Segments.Count);
        // ⭐ CE-1303 P2b (§11o S12) — inserted in SOURCE order (between 0–4 and 8–12), so it is not a replay: was appended at [2] (T3)
        var seg = editor.Take.Segments[1];
        Assert.Equal((5.5, 5.5 + TakeEditor.DefaultSegmentSeconds, 1d, SegmentGap.Cut), (seg.From.Offset, seg.To.Offset, seg.Speed, seg.Gap));
        Assert.True(tracks.Time.Items[1].Selected);
        Assert.False(FilmTimeMap.Of(editor.Take, _ => null).Segments.Any(s => s.Back));
        Assert.Equal("Add time segment", undo.UndoDescription);

        undo.Undo(null!);
        Assert.Equal(2, editor.Take.Segments.Count);
        Assert.Equal(2, tracks.Time.Items.Count);
        Assert.DoesNotContain(tracks.Time.MenuItems("seg:0", 1.0), m => m.Label.StartsWith("Add", StringComparison.Ordinal));
    }

    /// <summary>"Delete segment" (on a segment) removes THAT segment, and Undo restores it in place. 🔴 Mutation: the item removing index + 1 ⇒ the wrong segment goes.</summary>
    [Fact]
    public void CE1364_DeleteSegment_RemovesIt_AndUndoRestoresIt()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");

        var del = tracks.Time.MenuItems("seg:0", 1.0).First();   // ⭐ P2b: then "Replay this segment"
        Assert.Equal("Delete segment", del.Label);
        del.Run();

        Assert.Equal(new[] { (8d, 12d) }, tracks.Time.Items.Select(i => (i.From, i.To!.Value)));
        Assert.Equal("Delete time segment", undo.UndoDescription);
        undo.Undo(null!);
        Assert.Equal(new[] { (0d, 4d), (8d, 12d) }, tracks.Time.Items.Select(i => (i.From, i.To!.Value)));
        Assert.Equal(3, editor.Take.Keys.Count);   // the camera keys were never touched
    }

    /// <summary>K stores the pilot camera's lens: <see cref="PilotCamera.Of"/> carries the map camera's field of view into the camera (look-at AND follow), and the key
    /// K adds holds it in <see cref="CameraKey.Fov"/>. 🔴 Mutation: <c>Of</c> dropping <c>fov</c> ⇒ the key's lens is null and the assertions fail.</summary>
    [Fact]
    public void CE1364_K_StoresThePilotsLens_InTheKeysFov()
    {
        Write("main", V2);
        var pose = new CameraPose(new Vector3(1, 2, 3), 40f, 0.5f, -0.4f);
        Assert.Equal(38f, PilotCamera.Of(pose, fov: 38f).Fov);
        Assert.Equal(38f, PilotCamera.Of(pose, "Tank 1", new Vector3(9, 9, 0), fov: 38f).Fov);
        Assert.Null(PilotCamera.Of(pose).Fov);

        var (editor, tracks, _) = Open("main", () => PilotCamera.Of(pose, fov: 38f));
        Assert.True(tracks.Camera.Key('K', 6.5));
        Assert.Equal(38f, editor.Take.Keys[^1].Fov);
        Assert.Equal(38f, editor.Take.Keys[^1].Camera.Fov);
    }

    // ── CE-1303 P2b (docs/DESIGN_Director_Timeline_Editor.md §11o) — the Subtitles track, update key from view, replay a segment ──────────

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-11 (§11o S10): the THIRD director track edits the take's subtitles with the Time track's machinery — "Add a subtitle
    /// here" at the clicked source second (3 s, placeholder text, selected), a body drag moves it whole, an end-edge drag only its end, "Delete subtitle"
    /// removes it — each ONE undo step, undone in order back to no subtitles; Save writes <c>subtitles[]</c>. 🔴 Mutation: <c>TakeEditor.MoveSubtitle</c>
    /// passing <c>SegmentEdge.Whole</c> for every edge ⇒ the edge drag moves the start too ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_TheSubtitlesTrack_AddsMovesResizesDeletes_AndUndoes()
    {
        var path = Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        Assert.Equal(new[] { DirectorTracks.CameraId, DirectorTracks.TimeId, DirectorTracks.SubtitlesId, DirectorTracks.ViewId, DirectorTracks.GraphicsId },
                     tracks.Tracks.Select(t => t.Id));   // ⭐ CE-1304 / CE-1305 P6 — + View, Graphics
        Assert.Empty(tracks.Subtitles.Items);                         // V2 has no caption: nothing read as a subtitle

        Assert.Equal("Add a subtitle here", Assert.Single(tracks.Subtitles.MenuItems(null, 6.0)).Label);
        tracks.Subtitles.MenuItems(null, 6.0).Single().Run();
        var item = Assert.Single(tracks.Subtitles.Items);
        Assert.Equal((6.0, 6.0 + DirectorTracks.DefaultSubtitleSeconds, DirectorTracks.DefaultSubtitleText, true), (item.From, item.To!.Value, item.Label, item.Selected));
        Assert.Equal(0, tracks.SelectedSubtitle);

        tracks.Subtitles.Move("sub:0", 7.0, commit: false);           // a preview: the take is untouched
        Assert.Equal(6.0, editor.Take.Subtitles[0].At.Offset);
        tracks.Subtitles.Move("sub:0", 7.0, commit: true);            // the body: the whole subtitle, same length
        Assert.Equal((7.0, 10.0), (editor.Take.Subtitles[0].At.Offset, editor.Take.Subtitles[0].Until.Offset));
        tracks.Subtitles.Move("sub:0", 11.5, commit: true, TrackItemPart.EndEdge);   // the end edge only
        Assert.Equal((7.0, 11.5), (editor.Take.Subtitles[0].At.Offset, editor.Take.Subtitles[0].Until.Offset));
        Assert.True(editor.Apply(new TakeEdit.SetSubtitle(0, editor.Take.Subtitles[0] with { Text = "Contact!", Position = SubtitlePosition.Top })));

        Assert.Equal(path, editor.Save());
        var saved = _store.Load("sc", "main").Value!.Subtitles.Single();
        Assert.Equal((7.0, 11.5, "Contact!", SubtitlePosition.Top), (saved.At.Offset, saved.Until.Offset, saved.Text, saved.Position));

        var del = tracks.Subtitles.MenuItems("sub:0", 8.0).Single();
        Assert.Equal("Delete subtitle", del.Label);
        del.Run();
        Assert.Empty(editor.Take.Subtitles);
        Assert.Equal("Delete subtitle", undo.UndoDescription);
        foreach (var expected in new[] { "Delete subtitle", "Change subtitle", "Move subtitle", "Move subtitle", "Add subtitle" })
        {
            Assert.Equal(expected, undo.UndoDescription);
            undo.Undo(null!);
        }
        Assert.Empty(editor.Take.Subtitles);
        Assert.Equal(3, editor.Take.Keys.Count);                       // the camera keys were never touched
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b — CR-11 (§11o S8): a v1 take's caption is READ as a subtitle over each shot (the track shows it, nothing is written), its first
    /// subtitle edit writes them as they play; and where a shot has a <c>why</c> (the director's note, never seeded) empty space offers it as a subtitle.
    /// 🔴 Mutation: <c>TakeEditor.Apply</c> not converting a v1 take's captions before a subtitle edit (basis = before) ⇒ the edit names no subtitle ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_AV1TakesCaption_IsReadAsSubtitles_AndTheShotsNoteIsOffered()
    {
        Write("main", """
            {"caption":"Ambush at the ford",
             "shots":[{"at":1,"seconds":3,"lookAt":[1,2,3],"distance":40},
                      {"at":4,"seconds":2,"follow":"Tank 1","distance":10,"why":"the tank turns"}]}
            """);
        var (editor, tracks, _) = Open("main");
        var read = Assert.Single(tracks.Subtitles.Items);                         // the two adjacent shots' one caption, merged
        Assert.Equal((1d, 6d, "Ambush at the ford"), (read.From, read.To!.Value, read.Label));
        Assert.Empty(editor.Take.Subtitles);                                      // read, not written

        tracks.Subtitles.Move("sub:0", 2d, commit: true, TrackItemPart.StartEdge);
        Assert.Equal((2d, 6d, "Ambush at the ford"), (editor.Take.Subtitles.Single().At.Offset, editor.Take.Subtitles.Single().Until.Offset, editor.Take.Subtitles.Single().Text));

        var note = tracks.Subtitles.MenuItems(null, 4.5).Single(m => m.Label == "Add the shot's note as a subtitle");
        note.Run();
        Assert.Equal(("the tank turns", 4d, 6d), (editor.Take.Subtitles[1].Text, editor.Take.Subtitles[1].At.Offset, editor.Take.Subtitles[1].Until.Offset));
        Assert.DoesNotContain(tracks.Subtitles.MenuItems(null, 2.0), m => m.Label.Contains("note"));   // shot 1 has no why
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b (§11o S11): <b>update key from view</b> — the SELECTED key takes the pilot camera (its lens too) in ONE undo step that puts the
    /// old camera back; with nothing selected, U on the Camera track updates the key ON SCREEN at the playhead (the last at or before it), and the key's
    /// menu offers it. Times never move. 🔴 Mutation: <c>TakeEditor.SetKeyCamera</c> keeping the key's old camera ⇒ the pose assertion fails.
    /// </summary>
    [Fact]
    public void CE1303_P2b_UpdateKeyFromView_TakesThePilotsCamera_AndUndoRestoresIt()
    {
        Write("main", V2);
        var pilot = PilotCamera.Of(new CameraPose(new Vector3(70, 80, 2), 25f, 1.0f, -0.3f), fov: 33f);
        var (editor, tracks, undo) = Open("main", () => pilot);
        var before = editor.Take.Keys[1];

        tracks.Camera.Select("key:1");
        Assert.True(tracks.UpdateKeyFromView(playheadSim: 0.5));               // the selection wins over the playhead
        Assert.Equal(pilot, editor.Take.Keys[1].Camera);
        Assert.Equal(33f, editor.Take.Keys[1].Fov);
        Assert.Equal(before.At, editor.Take.Keys[1].At);
        Assert.Equal("Update key from view", undo.UndoDescription);
        undo.Undo(null!);
        Assert.Equal(before, editor.Take.Keys[1]);

        tracks.Camera.Select("seg:0");                                         // no key selected: the key on screen at 6 s is key 1 (at 5)
        Assert.True(tracks.Camera.Key('U', 6.0));
        Assert.Equal(pilot, editor.Take.Keys[1].Camera);
        Assert.Equal(before.Camera, CameraKeys.Of(_store.Load("sc", "main").Value!)[1].Camera);   // unsaved
        Assert.Contains(tracks.Camera.MenuItems("key:0"), m => m.Label.StartsWith("Update key from view", StringComparison.Ordinal));

        var (_, blind, _) = Open("main");                                       // no pilot camera: nothing to take
        Assert.False(blind.UpdateKeyFromView(6.0));
    }

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b (§11o S12): "Replay this segment" inserts a copy RIGHT AFTER it — the film plays that stretch again (a BACK segment in the map),
    /// labelled "replay" on the Time track — one undo step; the selected segment's speed is one more (SetSegment). 🔴 Mutation: the copy appended at the
    /// end instead of after its segment ⇒ the map's back segment is the third, not the second ⇒ red.
    /// </summary>
    [Fact]
    public void CE1303_P2b_ReplayThisSegment_PlaysTheStretchAgain()
    {
        Write("main", V2);
        var (editor, tracks, undo) = Open("main");
        tracks.Time.MenuItems("seg:0", 1.0).Single(m => m.Label == "Replay this segment").Run();
        Assert.Equal(new[] { (0d, 4d), (0d, 4d), (8d, 12d) }, editor.Take.Segments.Select(s => (s.From.Offset, s.To.Offset)).ToArray());
        Assert.Equal(new[] { false, true, false }, FilmTimeMap.Of(editor.Take, _ => null).Segments.Select(s => s.Back).ToArray());
        Assert.Equal("replay", tracks.Time.Items[1].Label);
        Assert.Equal(1, tracks.SelectedSegment);
        Assert.True(editor.Apply(new TakeEdit.SetSegment(1, editor.Take.Segments[1] with { Speed = 0.5 })));
        Assert.Equal("replay x0.5", tracks.Time.Items[1].Label);
        undo.Undo(null!);
        undo.Undo(null!);
        Assert.Equal(2, editor.Take.Segments.Count);
    }

    /// <summary>
    /// ⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q K2 / K3 / K4): while the runner plays a REPLAY of this take (the segment on screen goes
    /// back), <b>U</b> does not change the main pass's key on screen — the replay gets its OWN key with the view, where that key took the screen (the segment gets an
    /// id in the same undo step); a second U changes that own key in place; <b>K</b> adds a key of the replay at the playhead. Such a key is drawn in its own colour,
    /// its label naming the segment. Off a replay (the main pass) K and U act as before (unscoped, in place). Deleting the replay deletes its keys. 🔴 Mutation:
    /// <c>ReplayOnScreen</c> returning null ⇒ U overwrites key 0 (the main pass's) and K adds an unscoped key ⇒ red.
    /// </summary>
    [Fact]
    public void CE4059_CR7_DuringAReplay_KAndU_GiveTheReplayItsOwnKeys()
    {
        Write("main", V2);
        var pilot = Look(77f);
        var loaded = _store.Load("sc", "main").Value!;
        var undo = new GizmoUndoStack();
        var editor = new TakeEditor(_store, loaded, undo);
        FilmSegment? onScreen = null;
        var tracks = new DirectorTracks(editor, () => pilot, segmentNow: () => onScreen);
        Assert.True(editor.Apply(new TakeEdit.ReplaySegment(0)));                     // segments [0–4] [0–4 again] [8–12]
        var map = FilmTimeMap.Of(editor.Take, _ => 10d);
        var key0 = editor.Take.Keys[0];

        onScreen = map.Segments[1];                                                     // the runner is in the replay
        Assert.True(map.Segments[1].Back);
        Assert.True(tracks.Camera.Key('U', 3.0));
        Assert.Equal(key0, editor.Take.Keys[0]);                                        // ⭐ the main pass's key is untouched …
        var own = editor.Take.Keys[^1];
        Assert.Equal(("s1", 2d, pilot), (own.Segment, own.At.Offset, own.Camera));      // … the replay has its own, from where key 0 took the screen
        Assert.Equal("s1", editor.Take.Segments[1].Id);
        Assert.Null(editor.Take.Segments[0].Id);
        int count = editor.Take.Keys.Count;
        pilot = Look(88f);
        Assert.True(tracks.Camera.Key('U', 3.0));                                        // its own key is on screen now: changed in place
        Assert.Equal(count, editor.Take.Keys.Count);
        Assert.Equal(pilot, editor.Take.Keys[^1].Camera);
        Assert.True(tracks.Camera.Key('K', 3.5));                                        // K: a key of the replay at the playhead
        Assert.Equal(("s1", 3.5), (editor.Take.Keys[^1].Segment, editor.Take.Keys[^1].At.Offset));

        var item = tracks.Camera.Items.Single(i => i.Id == "key:" + (editor.Take.Keys.Count - 1));
        Assert.Equal(DirectorTracks.ScopedKeyColour, item.Colour);
        Assert.EndsWith("| segment s1", item.Label);
        Assert.Null(tracks.Camera.Items.Single(i => i.Id == "key:0").Colour);

        onScreen = map.Segments[0];                                                     // the main pass: as before
        Assert.True(tracks.Camera.Key('K', 3.7));
        Assert.Null(editor.Take.Keys[^1].Segment);
        Assert.True(editor.Apply(new TakeEdit.RemoveKey(editor.Take.Keys.Count - 1)));

        Assert.True(editor.Apply(new TakeEdit.RemoveSegment(1)));                       // the replay goes, and its keys with it
        Assert.DoesNotContain(editor.Take.Keys, k => k.Segment != null);
        undo.Undo(null!);
        Assert.Equal(2, editor.Take.Keys.Count(k => k.Segment == "s1"));
        undo.Undo(null!); undo.Undo(null!); undo.Undo(null!); undo.Undo(null!); undo.Undo(null!);   // remove-key, K, K, U, U
        Assert.Null(editor.Take.Segments[1].Id);                                        // the id came with the first scoped key's undo step
        Assert.DoesNotContain(editor.Take.Keys, k => k.Segment != null);
    }

    // ── ⭐ CE-1304 / CE-1305 P6 (docs/DESIGN_Director_Timeline_Editor.md §11t) — the View track and the Graphics track ─────────────────────────────

    private const string WithLayers = """
        {"layers":{"on":["Ai.VisionCones"],"off":[]},
         "shots":[{"at":1,"seconds":3,"lookAt":[1,2,3],"distance":40},
                  {"at":6,"seconds":2,"lookAt":[1,2,3],"distance":40,"layers":{"on":["Units.Cards"]}}]}
        """;

    /// <summary>
    /// ⭐⭐ <c>CE-1304</c> P6 (§11t V3, success condition 1): the View track SHOWS the layers a take already plays (the take's at 0, the second shot's at 6 — nothing
    /// written), and EDITS them: "2-D map here" at 3 s writes those items AND the new one in ONE undo step, a drag moves it (a preview first), "Switch to the 3-D
    /// map" flips it, "Delete view change" removes it — each one undo step, undone in order back to a take with no views; Save writes <c>views[]</c>.
    /// 🔴 Mutation: the view edits' basis being the take as it is (not <c>WithViews</c>) ⇒ the first edit drops the read items (one view, not three) ⇒ red.
    /// </summary>
    [Fact]
    public void CE1304_P6_TheViewTrack_ShowsTheLayersPlayed_AddsMovesFlipsDeletes_AndUndoes()
    {
        var path = Write("main", WithLayers);
        var (editor, tracks, undo) = Open("main");
        var shown = tracks.View.Items;
        Assert.Equal(new[] { (0d, "+Ai.VisionCones"), (6d, "+Ai.VisionCones +Units.Cards") }, shown.Select(i => (i.From, i.Label)));
        Assert.Empty(editor.Take.Views);                                               // read, never written by looking

        var add = tracks.View.MenuItems(null, 3.0).ToList();
        Assert.Equal(new[] { "2-D map here", "3-D map here" }, add.Select(m => m.Label));
        add[0].Run();
        Assert.Equal(3, editor.Take.Views.Count);                                      // ⭐ the read items AND the new one, one step
        Assert.Equal("Add view change", undo.UndoDescription);
        Assert.Equal(2, tracks.SelectedView);
        Assert.Equal((3d, (bool?)true), (editor.Take.Views[2].At.Offset, editor.Take.Views[2].TwoD));
        Assert.Contains(tracks.View.Items, i => i.Id == "view:2" && i.Label == "2-D" && i.Selected);

        tracks.View.Move("view:2", 4.5, commit: false);                               // a preview: the take is untouched
        Assert.Equal(3d, editor.Take.Views[2].At.Offset);
        Assert.Equal(4.5, tracks.View.Items.Single(i => i.Id == "view:2").From);
        tracks.View.Move("view:2", 4.5, commit: true);
        Assert.Equal(4.5, editor.Take.Views[2].At.Offset);
        var flip = tracks.View.MenuItems("view:2", 4.5).Single(m => m.Label == "Switch to the 3-D map");
        flip.Run();
        Assert.False(editor.Take.Views[2].TwoD);
        Assert.True(editor.Apply(new TakeEdit.SetView(2, editor.Take.Views[2] with { TwoD = true, Layers = new LayerChange(new[] { "Units.Labels" }, Array.Empty<string>()) })));

        Assert.Equal(path, editor.Save());
        var saved = _store.Load("sc", "main").Value!.Views;
        Assert.Equal(3, saved.Count);
        Assert.Equal((4.5, (bool?)true, "Units.Labels"), (saved[2].At.Offset, saved[2].TwoD, saved[2].Layers!.On.Single()));

        tracks.View.MenuItems("view:2", 4.5).Single(m => m.Label == "Delete view change").Run();
        Assert.Equal(2, editor.Take.Views.Count);
        foreach (var expected in new[] { "Delete view change", "Change view", "Change view", "Move view change", "Add view change" })
        {
            Assert.Equal(expected, undo.UndoDescription);
            undo.Undo(null!);
        }
        Assert.Empty(editor.Take.Views);
        Assert.Equal(2, editor.Take.Shots.Count);                                      // the shots (and their layers) were never touched
    }

    /// <summary>
    /// ⭐ <c>CE-1305</c> P6 (§11t G4, success condition 2): the Graphics track — "Add a graphic here" at 6 s puts a RING on the unit the pilot camera follows (a LABEL
    /// at its look-at when it follows none), 3 s long and selected; a body drag moves it whole, an end-edge drag only its end, a window edit sets its text, "Delete
    /// graphic" removes it — each one undo step, undone in order back to none; Save writes <c>graphics[]</c>. 🔴 Mutation: <c>TakeEditor.MoveGraphic</c> passing
    /// <c>SegmentEdge.Whole</c> for every edge ⇒ the end-edge drag moves the start too ⇒ red.
    /// </summary>
    [Fact]
    public void CE1305_P6_TheGraphicsTrack_AddsMovesResizesDeletes_AndUndoes()
    {
        var path = Write("main", V2);
        CameraSpec? pilot = new() { Follow = "Tank 1", Distance = 12 };
        var (editor, tracks, undo) = Open("main", () => pilot);
        Assert.Empty(tracks.Graphics.Items);

        Assert.Equal("Add a graphic here", Assert.Single(tracks.Graphics.MenuItems(null, 6.0)).Label);
        tracks.Graphics.MenuItems(null, 6.0).Single().Run();
        var g = Assert.Single(editor.Take.Graphics);
        Assert.Equal((GraphicKind.Ring, "Tank 1", 6d, 6d + DirectorTracks.DefaultGraphicSeconds), (g.Kind, g.Who, g.At.Offset, g.Until.Offset));
        var item = Assert.Single(tracks.Graphics.Items);
        Assert.Equal(("ring Tank 1", true), (item.Label, item.Selected));
        Assert.Equal(0, tracks.SelectedGraphic);

        tracks.Graphics.Move("gfx:0", 7.0, commit: true);                              // the body: whole, same length
        Assert.Equal((7d, 10d), (editor.Take.Graphics[0].At.Offset, editor.Take.Graphics[0].Until.Offset));
        tracks.Graphics.Move("gfx:0", 11.5, commit: true, TrackItemPart.EndEdge);       // the end edge only
        Assert.Equal((7d, 11.5), (editor.Take.Graphics[0].At.Offset, editor.Take.Graphics[0].Until.Offset));
        Assert.True(editor.Apply(new TakeEdit.SetGraphic(0, editor.Take.Graphics[0] with { Text = "Lead tank" })));
        Assert.Equal("ring Tank 1 \"Lead tank\"", tracks.Graphics.Items.Single().Label);

        Assert.Equal(path, editor.Save());
        var saved = _store.Load("sc", "main").Value!.Graphics.Single();
        Assert.Equal((GraphicKind.Ring, "Tank 1", "Lead tank", 7d, 11.5), (saved.Kind, saved.Who, saved.Text, saved.At.Offset, saved.Until.Offset));

        tracks.Graphics.MenuItems("gfx:0", 8.0).Single(m => m.Label == "Delete graphic").Run();
        Assert.Empty(editor.Take.Graphics);
        foreach (var expected in new[] { "Delete graphic", "Change graphic", "Move graphic", "Move graphic", "Add graphic" })
        {
            Assert.Equal(expected, undo.UndoDescription);
            undo.Undo(null!);
        }
        Assert.Empty(editor.Take.Graphics);

        pilot = new CameraSpec { LookAt = new Vector3(5, 6, 0), Distance = 50 };        // a look-at pilot: a label where it looks
        var label = tracks.NewGraphic(2.0);
        Assert.Equal((GraphicKind.Label, new Vector3(5, 6, 0), DirectorTracks.DefaultGraphicText), (label.Kind, label.Points.Single(), label.Text));
    }
}
