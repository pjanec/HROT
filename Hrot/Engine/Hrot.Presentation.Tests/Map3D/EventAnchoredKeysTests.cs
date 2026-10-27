using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-1303</c> CR-6 (docs/DESIGN_Director_Timeline_Editor.md §11e "CR-6 detail") — K hangs the new key on the nearest preceding index EVENT
/// (<c>"shot:Tank#2"</c> + an offset in sim seconds), through the take's existing anchor vocabulary: <c>Take.Anchors</c> + <c>Take.Baked</c> +
/// <see cref="AtExpr"/>, resolved by <see cref="TakeEditor.Resolve"/> (and live by <see cref="AnchorBook"/>) like any anchor. The index here is built by
/// hand: ten frames at 0.2 sim s each (frame f = f × 0.2 s).
/// </summary>
public sealed class EventAnchoredKeysTests : IDisposable
{
    private const long TankId = 1, RifleId = 2;
    private const string Shot = "combat.shot", Hit = "combat.hit", Blast = "combat.blast";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cr6-" + Guid.NewGuid().ToString("N"));
    private readonly TakeStore _store;

    public EventAnchoredKeysTests() => _store = new TakeStore(() => _root, () => null);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static string? NameOf(long id) => id switch { TankId => "Tank", RifleId => "Rifle", _ => null };

    private static TimelineEvent At(int frame, string lane, long subject) => new(frame, lane, subject, 0, 0f, 0f, 0f);

    private static TimelineIndex Index(params TimelineEvent[] events)
    {
        const int frames = 10;
        var lanes = events.GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => new SharedList<TimelineEvent>(g.OrderBy(e => e.Frame).ToArray()));
        var ticks = Enumerable.Range(0, frames).Select(f => (long)f * 2_000_000).ToArray();
        var sim = Enumerable.Range(0, frames).Select(f => f * 0.2).ToArray();
        return new TimelineIndex(lanes, new SharedList<long>(ticks), frames, frames, null, new SharedList<double>(sim));
    }

    /// <summary>Tank fires at 0.4 s and 1.0 s (its 1st and 2nd round); Rifle at 0.6 s (its 1st).</summary>
    private static TimelineIndex Shots() => Index(At(2, Shot, TankId), At(3, Shot, RifleId), At(5, Shot, TankId));

    private (TakeEditor Editor, DirectorTracks Tracks) Open(TimelineIndex? index)
    {
        var path = _store.TakePath("sc", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"keys":[{"at":0.1,"lookAt":[1,2,3],"distance":60}]}""");
        var loaded = _store.Load("sc", "main");
        Assert.True(loaded.Ok, string.Join("; ", loaded.Problems));
        var editor = new TakeEditor(_store, loaded.Value!, new GizmoUndoStack());
        Func<double, EventAnchor?>? at = index is null ? null : sim => EventAnchors.NearestBefore(index, sim, NameOf);
        return (editor, new DirectorTracks(editor, () => new CameraSpec { LookAt = new Vector3(5, 0, 0), Distance = 40 }, null, at));
    }

    /// <summary>K with Tank's 2nd round 0.4 s before the playhead stores THAT event + 0.4 — the anchor is defined and baked in the same edit, and the key resolves
    /// back to the playhead. 🔴 Mutation: <c>NearestBefore</c> returning null ⇒ the key is a fixed time and the anchor assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_K_WithAShotBeforeThePlayhead_StoresThatEventAndTheOffset()
    {
        var (editor, tracks) = Open(Shots());

        Assert.True(tracks.Camera.Key('K', 1.4));

        var added = editor.Take.Keys[^1];
        Assert.Equal("shot:Tank#2", added.At.Anchor);
        Assert.Equal(0.4, added.At.Offset, 6);
        var spec = editor.Take.Anchors["shot:Tank#2"];
        Assert.Equal(("shot", "Tank", 2), (spec.On, spec.By, spec.N));
        Assert.Equal(1.0, editor.Take.Baked!.Times["shot:Tank#2"], 6);
        Assert.Equal(1.4, editor.Resolve(added.At)!.Value, 6);
        Assert.Equal(1.4, tracks.Camera.Items[^1].From, 6);
    }

    /// <summary>With no event at or before the playhead the key is a plain fixed time, and the take gains no anchor and no bake. A host with no index behaves the same.
    /// 🔴 Mutation: the "no preceding event" case still naming the first event later in the index ⇒ the fixed-time assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_K_WithNoPrecedingEvent_StoresAFixedTime()
    {
        foreach (var index in new TimelineIndex?[] { Shots(), null })
        {
            var (editor, tracks) = Open(index);
            Assert.True(tracks.Camera.Key('K', 0.2));   // the first round is at 0.4 s
            Assert.Equal(new AtExpr(0.2), editor.Take.Keys[^1].At);
            Assert.Empty(editor.Take.Anchors);
            Assert.Null(editor.Take.Baked);
        }
    }

    /// <summary>The ordinal is PER SHOOTER (Rifle's first round is <c>Rifle#1</c> though it is the 2nd round of the run), and the nearest event of any lane wins. 🔴 Mutation:
    /// the ordinal counted over the whole lane ⇒ the name is <c>shot:Rifle#2</c> and the first assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_TheOrdinalIsPerShooter_AndTheNearestEventOfAnyLaneWins()
    {
        var found = EventAnchors.NearestBefore(Shots(), 0.7, NameOf);
        Assert.Equal("shot:Rifle#1", found!.Name);
        Assert.Equal(0.6, found.EventSim, 6);

        var withBlast = Index(At(2, Shot, TankId), At(6, Blast, 0));   // a burst at 1.2 s is nearer than the round at 0.4 s
        var blast = EventAnchors.NearestBefore(withBlast, 1.3, NameOf)!;
        Assert.Equal("blast#1", blast.Name);
        Assert.Equal(("blast", 1), (blast.Spec.On, blast.Spec.N));
    }

    /// <summary>An event <see cref="AnchorBook"/> cannot find again on a live run is not offered: it finds a unit's FIRST hit only, so a second hit is skipped and the nearest
    /// expressible event is used. 🔴 Mutation: the first-hit check removed ⇒ the second hit is named and the assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_ASecondHit_IsNotExpressible_SoTheNearestExpressibleEventIsUsed()
    {
        var index = Index(At(2, Shot, TankId), At(3, Hit, TankId), At(5, Hit, TankId));
        var found = EventAnchors.NearestBefore(index, 1.2, NameOf)!;
        Assert.Equal("hit:Tank", found.Name);                 // the FIRST hit (0.6 s), not the 2nd (1.0 s)
        Assert.Equal(0.6, found.EventSim, 6);
        Assert.Equal(("hit", "Tank"), (found.Spec.On, found.Spec.Target));
        Assert.Null(EventAnchors.NearestBefore(Index(At(3, Shot, 99)), 1.2, NameOf));   // a shooter with no name here: nothing to hang a key on
    }

    /// <summary>The anchor survives Save and Load: the key still reads <c>shot:Tank#2</c> + 0.4, the anchor's spec and its baked time are in the file, and the loaded take
    /// resolves the key to the same sim time. 🔴 Mutation: the editor adding the key without the anchor's bake ⇒ the loaded key resolves to nothing.</summary>
    [Fact]
    public void CE1303_CR6_TheAnchor_RoundTripsThroughTakeJson()
    {
        var (editor, tracks) = Open(Shots());
        Assert.True(tracks.Camera.Key('K', 1.4));
        Assert.NotNull(editor.Save());

        var loaded = _store.Load("sc", "main");
        Assert.True(loaded.Ok, string.Join("; ", loaded.Problems));
        var take = loaded.Value!;
        var key = take.Keys[^1];
        Assert.Equal("shot:Tank#2", key.At.Anchor);
        Assert.Equal(0.4, key.At.Offset, 6);
        Assert.Equal("Tank", take.Anchors["shot:Tank#2"].By);
        Assert.Equal(2, take.Anchors["shot:Tank#2"].N);
        var reopened = new TakeEditor(_store, take, new GizmoUndoStack());
        Assert.Equal(1.4, reopened.Resolve(key.At)!.Value, 6);
    }

    /// <summary>The Camera track's label for the key shows its anchor — <c>shot Tank#2 +0.4s</c>; a fixed-time key shows none. 🔴 Mutation: <c>Describe</c> not appending the
    /// anchor ⇒ the label assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_TheCameraTrackLabel_ShowsTheAnchor()
    {
        var (_, tracks) = Open(Shots());
        Assert.True(tracks.Camera.Key('K', 1.4));
        Assert.Contains("shot Tank#2 +0.4s", tracks.Camera.Items[^1].Label);
        Assert.DoesNotContain("@", tracks.Camera.Items[0].Label);   // the seeded fixed-time key
    }

    /// <summary>Dragging the event-anchored key keeps its anchor and changes the OFFSET (T3's rule, reused): a drop at 1.9 s is <c>shot:Tank#2</c> + 0.9, and it still
    /// resolves to the drop. 🔴 Mutation: <c>Retimed</c> returning a fixed time ⇒ the anchor assertion fails.</summary>
    [Fact]
    public void CE1303_CR6_ADragOfTheAnchoredKey_KeepsTheAnchor_AndChangesTheOffset()
    {
        var (editor, tracks) = Open(Shots());
        Assert.True(tracks.Camera.Key('K', 1.4));
        string id = tracks.Camera.Items[^1].Id;

        tracks.Camera.Move(id, 1.9, commit: true);

        var key = editor.Take.Keys[^1];
        Assert.Equal("shot:Tank#2", key.At.Anchor);
        Assert.Equal(0.9, key.At.Offset, 6);
        Assert.Equal(1.9, tracks.Camera.Items[^1].From, 6);
    }

    /// <summary>K is ONE undo step: undoing it removes the key AND the anchor and its bake it brought (no stray anchor left in the take).
    /// 🔴 Mutation: the anchor added in a second, separate edit ⇒ one undo leaves the anchor behind.</summary>
    [Fact]
    public void CE1303_CR6_UndoingK_RemovesTheKeyAndItsAnchorTogether()
    {
        var (editor, tracks) = Open(Shots());
        var undo = new GizmoUndoStack();
        editor = new TakeEditor(_store, editor.Take, undo);
        tracks = new DirectorTracks(editor, () => new CameraSpec { LookAt = Vector3.Zero }, null, sim => EventAnchors.NearestBefore(Shots(), sim, NameOf));

        Assert.True(tracks.Camera.Key('K', 1.4));
        Assert.Single(editor.Take.Anchors);
        undo.Undo(null!);

        Assert.Empty(editor.Take.Anchors);
        Assert.Null(editor.Take.Baked);
        Assert.Single(editor.Take.Keys);
    }

    /// <summary>
    /// ⭐ <c>CE-1304</c> P4 — CR-5 (§11s D2): the ONE naming switch, both ways. Every event K can name (<see cref="EventAnchors.Of"/>) is found AGAIN by the resolver
    /// (<see cref="EventAnchors.TimeOf"/>) at its own sim time — per-shooter ordinals, the global blast ordinal, a unit's first strike — and a blast that STRUCK a unit
    /// (<c>Other</c>) is its first strike, so the later <c>combat.hit</c> on it is not offered. Events before <c>since</c> (a live run's start) are not this run's.
    /// 🔴 Mutation: <c>TimeOf</c> counting every shooter's rounds for <c>shot</c> (the <c>By</c> filter dropped) ⇒ <c>shot:Tank#2</c> resolves to Rifle's round (0.6 s) ⇒ red.
    /// </summary>
    [Fact]
    public void CE1304_CR5_EveryNamedEvent_IsFoundAgain_ByTheResolver_AtItsOwnTime()
    {
        var struck = new TimelineEvent(7, Blast, RifleId, TankId, 0f, 0f, 0f);   // Rifle's round bursts ON the Tank at 1.4 s
        var index = Index(At(2, Shot, TankId), At(3, Shot, RifleId), At(5, Shot, TankId), At(4, Hit, RifleId), At(6, Blast, 0), struck, At(8, Hit, TankId));
        bool IsUnit(string who, long id) => NameOf(id) == who;

        int named = 0;
        foreach (var lane in EventAnchors.DefaultLanes)
            for (int i = 0; i < index.Lane(lane).Count; i++)
            {
                if (EventAnchors.Of(index, lane, i, NameOf) is not { } a) continue;
                named++;
                Assert.Equal(a.EventSim, EventAnchors.TimeOf(index, a.Spec, IsUnit)!.Value, 6);
            }
        Assert.Equal(6, named);   // 3 shots, Rifle's hit, 2 blasts — the Tank's hit at 1.6 s is not its first strike (the blast at 1.4 s is)
        Assert.Null(EventAnchors.Of(index, Hit, 1, NameOf));
        Assert.Equal(1.4, EventAnchors.TimeOf(index, new AnchorSpec { On = "hit", Target = "Tank" }, IsUnit)!.Value, 6);
        Assert.Equal(1.0, EventAnchors.TimeOf(index, new AnchorSpec { On = "shot", By = "Tank", N = 2 }, IsUnit)!.Value, 6);
        Assert.Null(EventAnchors.TimeOf(index, new AnchorSpec { On = "shot", By = "Tank", N = 3 }, IsUnit));             // not (yet) in the index
        Assert.Equal(1.0, EventAnchors.TimeOf(index, new AnchorSpec { On = "shot", By = "Tank", N = 1 }, IsUnit, since: 0.5)!.Value, 6);   // 0.4 s is before the run
        Assert.Null(EventAnchors.TimeOf(index, new AnchorSpec { On = "near", Who = "Tank" }, IsUnit));                    // a state cue: the world's, not the index's
    }
}
