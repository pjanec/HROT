using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — <b>the master's ONE list of the notes of the exercise on show</b>, ONE writer.
/// <list type="bullet">
/// <item>every change (<see cref="NoteEditIntent"/> — the ONE CRUD message, from any station) is applied here in arrival order and SAVED AT ONCE to the master's staging copy
/// (<c>{staging}/nodes/node-0/exercises/&lt;id&gt;/notes.json</c>), then the whole list is republished (<see cref="ExerciseNotesPublishedEvent"/> → the one topic; <see cref="ExerciseNotesUpdateEvent"/> → every host's <c>ClusterView.Notes</c>);</item>
/// <item>the list follows the exercise on show (<c>_activeExerciseId</c> in Live / DAR / AAR): entering one LOADS its file (the master's staging, else the NAS copy — the one an archive carries with the recordings), leaving it
/// WRITES BACK to the NAS (<c>{nas}/exercises/&lt;id&gt;/notes.json</c>) when it changed — ⭐ a small file written by the master, beside the recordings;</item>
/// <item>a restore from a checkpoint (a branch) keeps the notes riding the marks that stay (<see cref="ExerciseNotes.RetainMarks"/>).</item>
/// </list>
/// ⛔ notes.json is deliberately NOT in the archive manifest's size-checked file list: an AAR edit rewrites it AFTER the archive, and a size mismatch would turn a complete archive INCOMPLETE.
/// </summary>
public sealed partial class ClusterMaster
{
    private readonly ExerciseNotes _notes = new();
    private bool _notesDirtySinceLoad;       // an edit since the list was loaded ⇒ the NAS copy is stale
    private bool _notesPrevWasReplay;        // the list on show was an AAR's (a branch from it keeps its marks' notes)
    private bool _notesPendingSwept;         // ⭐ CE-1467 (b) — the start's retry of write-backs a previous master left undone ran
    private Dictionary<string, long>? _branchMarkWalls;   // ⭐ CE-1467 (d) — the inherited marks' wall times, until the branch point is known
    private (Guid Parent, long WallTicks) _branchCut;   // ⭐ CE-1467 (d) — the branch point the nodes answered (and the exercise it branches from), until the inherited list exists

    /// <summary>⭐ CE-1467 (b) — beside the staging copy while the NAS copy is stale (an edit not yet written back): a master that dies first, or a write that failed, leaves it, and the
    /// next master start writes the notes back (<see cref="RetryPendingNotesWriteBacks"/>).</summary>
    public const string NotesPendingMarker = "notes.nas-pending";

    /// <summary>The notes of the exercise on show (a NEW array per change); empty outside Live / DAR / AAR.</summary>
    public ExerciseNote[] Notes => _notes.Notes;

    /// <summary>The exercise <see cref="Notes"/> belong to (empty = none on show).</summary>
    public Guid NotesExerciseId => (Guid)_notes.ExerciseId;

    /// <summary>The master's staging copy of an exercise's notes.</summary>
    internal string NotesStagingFile(Guid exerciseId)
        => Path.Combine(OrchestrationConstants.GetNodeExercisesRoot(_config.StagingRoot, 0), exerciseId.ToString(), ExerciseNotes.FileName);

    /// <summary>The NAS copy of an exercise's notes (beside its recordings).</summary>
    internal string NotesNasFile(Guid exerciseId)
        => Path.Combine(_config.NasBasePath, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString(), ExerciseNotes.FileName);

    private void ProcessNoteEditIntents()
    {
        if (!_notesPendingSwept) { _notesPendingSwept = true; RetryPendingNotesWriteBacks(); }
        foreach (var done in _eventBus.ReadManaged<ClusterOpCompletedEvent>())
            if (done.ResultPayload is LiveBranchResult { BranchWallTicks: > 0 } branch) CutBranchNotes((Guid)branch.ParentExerciseId, branch.BranchWallTicks);
        foreach (var intent in _eventBus.ReadManaged<NoteEditIntent>())
            ProcessNoteEdit(intent);
        SyncNotesToExercise();   // after the edits: an edit and the end of the run in one frame still save into the right list
    }

    /// <summary>⭐ The ONE CRUD message, applied. Refused (Rejected) when no exercise is on show or the note is unknown.</summary>
    private void ProcessNoteEdit(NoteEditIntent intent)
    {
        SyncNotesToExercise();   // the list must be the one on show before it is edited
        if (_notes.ExerciseId == Guid.Empty)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: a note {0} refused — no exercise is on show.", intent.Kind);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }
        if (!_notes.Apply(intent.Kind, intent.Note, DateTime.UtcNow, out var why))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: a note {0} refused — {1}.", intent.Kind, why ?? "unknown");
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }
        _notesDirtySinceLoad = true;
        SaveNotesStaging();
        PublishNotes();
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
    }

    /// <summary>⭐ CE-1467 (e) — a mark's record, added with its checkpoint (under the label the master minted). No op status of its own: the checkpoint's request answers.</summary>
    private void AddMarkNote(Guid requestId, ExerciseNote note)
    {
        SyncNotesToExercise();
        string? why = "no exercise on show";
        if (_notes.ExerciseId == Guid.Empty || !_notes.Apply(NoteEditKind.Add, note, DateTime.UtcNow, out why))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-1467: the record of mark '{0}' (request {1}) was not added — {2}.", note.Mark, requestId, why ?? "unknown");
            return;
        }
        _notesDirtySinceLoad = true;
        SaveNotesStaging();
        PublishNotes();
    }

    /// <summary>
    /// Makes the list the one of the exercise on show: leaving one writes it back to the NAS (when it changed), entering one loads its file. Cheap (a compare) every frame.
    /// ⭐ A branch from an AAR (a new exercise while the previous list was an AAR's) inherits the notes riding the marks the replay held.
    /// </summary>
    private void SyncNotesToExercise()
    {
        bool shown = _currentDsmState is ClusterState.OperatingLive or ClusterState.OperatingReview or ClusterState.OperatingReplay;
        var want = shown ? _activeExerciseId : Guid.Empty;
        if ((Guid)want == (Guid)_notes.ExerciseId) { _notesPrevWasReplay = _currentDsmState == ClusterState.OperatingReplay && want != Guid.Empty; return; }

        var inherited = Array.Empty<ExerciseNote>();
        _branchMarkWalls = null;   // a list moving on: an inheritance whose branch point never came (a listener-only cluster) keeps every replay mark's note, as before
        if ((Guid)_notes.ExerciseId != Guid.Empty)
        {
            WriteBackNotes();
            if (want != Guid.Empty && _notesPrevWasReplay && _currentDsmState == ClusterState.OperatingLive)   // a branch from an AAR
            {
                // ⭐ CE-1467 (d) — the marks the replay held, with their wall times: cut at the branch point when the nodes answer it (CutBranchNotes) —
                //   the child's recording inherits only marks before it (RecordingModule.StartBranch), so its notes must match
                _branchMarkWalls = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var m in _replayCheckpoints) _branchMarkWalls[m.Label] = m.WallTicks;
                bool cutKnown = _branchCut.WallTicks > 0 && _branchCut.Parent == (Guid)_notes.ExerciseId;
                var parent = new ExerciseNotes();
                parent.Set(_notes.ExerciseId, _notes.Notes);
                if (cutKnown) parent.RetainMarksBefore(_branchMarkWalls, _branchCut.WallTicks);
                else parent.RetainMarks(new HashSet<string>(_branchMarkWalls.Keys, StringComparer.Ordinal));
                inherited = parent.Notes;
                if (cutKnown) { _branchMarkWalls = null; _branchCut = default; }
            }
        }

        _notesDirtySinceLoad = false;
        if (want == Guid.Empty) _notes.Reset(default);
        else if (inherited.Length > 0) { _notes.Set((ExerciseId)want, inherited); _notesDirtySinceLoad = true; SaveNotesStaging(); }
        else if (!_notes.TryLoad(NotesStagingFile(want), (ExerciseId)want) && !_notes.TryLoad(NotesNasFile(want), (ExerciseId)want)) _notes.Reset((ExerciseId)want);
        _notesPrevWasReplay = _currentDsmState == ClusterState.OperatingReplay && want != Guid.Empty;
        PublishNotes();
    }

    /// <summary>⭐ Leaving Live / AAR: the notes go back to the NAS exercise folder when they changed since they were loaded ("exported on leaving").</summary>
    private void WriteBackNotes()
    {
        if (!_notesDirtySinceLoad || _notes.ExerciseId == Guid.Empty) return;
        if (_notes.TrySave(NotesNasFile(_notes.ExerciseId))) { _notesDirtySinceLoad = false; TryDeleteFile(NotesPendingFile(_notes.ExerciseId)); }
        else FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: notes.json of {0} could not be written to the NAS — the master's staging copy stays; the next master start retries it.", _notes.ExerciseId);
    }

    /// <summary>⭐ A restore from checkpoint <paramref name="newExercise"/>'s parent keeps the notes riding the marks that stay; the new exercise starts with them (copied).</summary>
    private void BranchNotes(Guid newExercise, ICollection<string> keptMarks)
    {
        if (_notes.ExerciseId == Guid.Empty) return;
        WriteBackNotes();
        _notes.RetainMarks(keptMarks);
        _notes.Set((ExerciseId)newExercise, _notes.Notes);
        _notesDirtySinceLoad = _notes.Notes.Length > 0;
        if (_notesDirtySinceLoad) SaveNotesStaging(); else _notes.TrySave(NotesStagingFile(newExercise));
        PublishNotes();
    }

    /// <summary>⭐ CE-4325 — every change is saved to the master's staging at once; ⭐ CE-1467 (b) — with the marker that says the NAS copy is behind.</summary>
    private void SaveNotesStaging()
    {
        var id = (Guid)_notes.ExerciseId;
        if (!_notes.TrySave(NotesStagingFile(id)))
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: notes.json of {0} could not be saved to the master's staging.", id);
            return;
        }
        try { File.WriteAllText(NotesPendingFile(id), DateTime.UtcNow.ToString("O")); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal string NotesPendingFile(Guid exerciseId) => Path.Combine(Path.GetDirectoryName(NotesStagingFile(exerciseId))!, NotesPendingMarker);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// ⭐ CE-1467 (b) — at the master's start: every staging exercise carrying <see cref="NotesPendingMarker"/> has notes the NAS never received (the master died first, or the write
    /// failed) — they are written back now, and the marker goes on success. A failure keeps the marker for the next start. Returns how many were written.
    /// </summary>
    internal int RetryPendingNotesWriteBacks()
    {
        string root = OrchestrationConstants.GetNodeExercisesRoot(_config.StagingRoot, 0);
        if (!Directory.Exists(root)) return 0;
        int written = 0;
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (!Guid.TryParse(Path.GetFileName(dir), out var id) || !File.Exists(Path.Combine(dir, NotesPendingMarker))) continue;
            var copy = new ExerciseNotes();
            if (!copy.TryLoad(Path.Combine(dir, ExerciseNotes.FileName), (ExerciseId)id)) continue;
            if (copy.TrySave(NotesNasFile(id))) { TryDeleteFile(Path.Combine(dir, NotesPendingMarker)); written++; }
            else FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-1467: notes.json of {0} still cannot be written to the NAS — kept for the next start.", id);
        }
        if (written > 0) FdpLog<ClusterMaster>.Info("[Orchestrator] CE-1467: wrote back {0} exercise notes file(s) a previous master left unexported.", written);
        return written;
    }

    /// <summary>
    /// ⭐ CE-1467 (c) — <i>Discard</i> of an unarchived exercise: its notes go too — the master's list (when on show), its staging copy and marker, and the NAS copy the run's end wrote,
    /// together with that NAS folder when the notes were all it held (an unarchived run has no recordings there). ⛔ A NAS folder with anything else in it is never touched.
    /// </summary>
    private void DiscardNotes(Guid exerciseId)
    {
        if ((Guid)_notes.ExerciseId == exerciseId) { _notes.Reset(default); _notesDirtySinceLoad = false; PublishNotes(); }
        TryDeleteFile(NotesStagingFile(exerciseId));
        TryDeleteFile(NotesPendingFile(exerciseId));
        var nas = NotesNasFile(exerciseId);
        var folder = Path.GetDirectoryName(nas)!;
        try
        {
            if (!File.Exists(nas)) return;
            File.Delete(nas);
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// ⭐ CE-1467 (d) — a branch from an AAR inherits the notes of the marks BEFORE the branch point only (the child's recording keeps marks of earlier frames, <c>RecordingModule.StartBranch</c>).
    /// The point comes with the nodes' answer (<see cref="LiveBranchResult.BranchWallTicks"/>), which may arrive before or after the child's list exists — both orders end in the same cut.
    /// </summary>
    /// <summary>Test seam (CE-1467 d): the marks an inheritance from a replay took over, with their wall times — as <see cref="SyncNotesToExercise"/> leaves them when the branch point is not known yet.</summary>
    internal void SeedBranchMarksForTests(Dictionary<string, long> markWalls) => _branchMarkWalls = markWalls;

    internal void CutBranchNotes(Guid parent, long branchWallTicks)
    {
        if (_branchMarkWalls == null) { _branchCut = (parent, branchWallTicks); return; }   // the child's list is made next: it cuts as it inherits (only the notes of THAT parent)
        var walls = _branchMarkWalls;
        _branchMarkWalls = null;
        if (_notes.ExerciseId == Guid.Empty || !_notes.RetainMarksBefore(walls, branchWallTicks)) return;
        SaveNotesStaging();
        PublishNotes();
    }

    private void PublishNotes()
    {
        var notes = _notes.Notes;
        _eventBus.PublishManaged(new ExerciseNotesPublishedEvent { ExerciseId = _notes.ExerciseId, Notes = notes });
        _eventBus.PublishManaged(new ExerciseNotesUpdateEvent { ExerciseId = _notes.ExerciseId, Notes = notes });
    }
}
