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
        Assert.Equal((6d, 6d), (tracks.Time.Items[0].From, tracks.Time.Items[0].To!.Value));   // an edge never crosses the other
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
}
