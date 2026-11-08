using System;
using System.IO;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — the master's ONE note list: the CRUD message's rules, the branch's keep rule, <c>notes.json</c>, and the fold every host does.
/// </summary>
public sealed class ExerciseNotesTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static ExerciseNote N(string mark = "", string name = "", double at = 0) => new(Guid.NewGuid(), mark, at, name, "words", "Instructor-1", 7, "Alpha 1-2");

    /// <summary>🔴 Mutation: <c>Apply</c> Add that ignores an id already held (appends again) ⇒ a resent message doubles the note ⇒ red.</summary>
    [Fact]
    public void CE4325_AddEditDelete_AreAppliedInOrder_AndAResentAddNeverDoublesANote()
    {
        var list = new ExerciseNotes();
        list.Reset((ExerciseId)Guid.NewGuid());
        var a = N("Mark 1 · 0:10", "Bridge crossed");
        Assert.True(list.Apply(NoteEditKind.Add, a, T0, out _));
        Assert.True(list.Apply(NoteEditKind.Add, a, T0.AddSeconds(1), out _));          // resent
        Assert.Single(list.Notes);
        Assert.Equal(T0.AddSeconds(1), list.Notes[0].EditedUtc);                          // the master stamps it
        Assert.True(list.Apply(NoteEditKind.Edit, a with { Text = "later wins" }, T0.AddSeconds(2), out _));
        Assert.Equal("later wins", list.Notes[0].Text);
        Assert.True(list.Apply(NoteEditKind.Delete, a, T0.AddSeconds(3), out _));
        Assert.Empty(list.Notes);
    }

    /// <summary>🔴 Mutation: an unknown id edit that adds instead of refusing ⇒ red.</summary>
    [Fact]
    public void CE4325_AnEditOrDeleteOfAnUnknownNote_IsRefusedWithAReason_AndChangesNothing()
    {
        var list = new ExerciseNotes();
        list.Reset((ExerciseId)Guid.NewGuid());
        var before = list.Notes;
        Assert.False(list.Apply(NoteEditKind.Edit, N(), T0, out var why1)); Assert.Equal("no such note", why1);
        Assert.False(list.Apply(NoteEditKind.Delete, N(), T0, out var why2)); Assert.Equal("no such note", why2);
        Assert.Same(before, list.Notes);
    }

    [Fact]
    public void CE4325_AnEmptyIdIsFilledByTheMaster()
    {
        var list = new ExerciseNotes();
        list.Reset((ExerciseId)Guid.NewGuid());
        list.Apply(NoteEditKind.Add, N() with { Id = Guid.Empty }, T0, out _);
        Assert.NotEqual(Guid.Empty, list.Notes[0].Id);
    }

    /// <summary>🔴 Mutation: <c>RetainMarks</c> keeping time notes ⇒ a branch inherits notes that have no moment to restore ⇒ red.</summary>
    [Fact]
    public void CE4325_ABranchKeepsTheNotesOfTheMarksThatStay_AndDropsTheRest()
    {
        var list = new ExerciseNotes();
        list.Reset((ExerciseId)Guid.NewGuid());
        list.Apply(NoteEditKind.Add, N("m1"), T0, out _);
        list.Apply(NoteEditKind.Add, N("m2"), T0, out _);
        list.Apply(NoteEditKind.Add, N("", "AAR note", 30), T0, out _);        // a time note: not restorable (R-4168)
        Assert.True(list.RetainMarks(new[] { "m1" }));
        Assert.Single(list.Notes);
        Assert.Equal("m1", list.Notes[0].Mark);
        Assert.False(list.RetainMarks(new[] { "m1" }));                         // nothing more to drop
    }

    /// <summary>🔴 Mutation: <c>TrySave</c> writing the file in place (no part rename) is not observable here; the round-trip is: a notes.json that loses a field ⇒ red.</summary>
    [Fact]
    public void CE4325_NotesJson_RoundTripsEveryField_AndATornFileIsNoFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ex = (ExerciseId)Guid.NewGuid();
            var list = new ExerciseNotes();
            list.Reset(ex);
            var note = N("Mark 3 · 3:18", "Contact east") with { AtSimSeconds = 42.5, Text = "say \"hi\"\nline 2", EditedUtc = T0 };
            list.Apply(NoteEditKind.Add, note, T0, out _);
            var path = Path.Combine(dir, "x", ExerciseNotes.FileName);
            Assert.True(list.TrySave(path));
            Assert.False(File.Exists(path + ".part"));                          // renamed whole — a reader never sees half a file

            var loaded = new ExerciseNotes();
            Assert.True(loaded.TryLoad(path, ex));
            Assert.Equal(ex, loaded.ExerciseId);
            Assert.Equal(list.Notes[0], loaded.Notes[0]);

            File.WriteAllText(path, "{ \"notes\": [ { \"id\": ");              // torn
            Assert.False(new ExerciseNotes().TryLoad(path, ex));
            Assert.False(new ExerciseNotes().TryLoad(Path.Combine(dir, "none.json"), ex));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>🔴 Mutation: <c>DrainNotes</c> not run from <c>Update</c> ⇒ the list stays empty ⇒ red.</summary>
    [Fact]
    public void CE4325_ClusterView_FoldsTheMastersNotes_LastWins()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var view = new ClusterView(bus);
        Assert.Empty(view.Notes);
        var ex = (ExerciseId)Guid.NewGuid();
        var one = N("m1");
        bus.PublishManaged(new ExerciseNotesUpdateEvent { ExerciseId = ex, Notes = new[] { one } });
        bus.SwapBuffers(); view.Update();
        Assert.Equal(new[] { one }, view.Notes);
        Assert.Equal(ex, view.NotesExerciseId);
        bus.PublishManaged(new ExerciseNotesUpdateEvent { ExerciseId = ex, Notes = Array.Empty<ExerciseNote>() });
        bus.SwapBuffers(); view.Update();
        Assert.Empty(view.Notes);
    }
}
