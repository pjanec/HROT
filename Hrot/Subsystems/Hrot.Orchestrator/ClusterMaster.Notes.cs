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
        if (!_notes.TrySave(NotesStagingFile(_notes.ExerciseId)))
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: notes.json of {0} could not be saved to the master's staging.", _notes.ExerciseId);
        PublishNotes();
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
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
        if ((Guid)_notes.ExerciseId != Guid.Empty)
        {
            WriteBackNotes();
            if (want != Guid.Empty && _notesPrevWasReplay && _currentDsmState == ClusterState.OperatingLive)   // a branch from an AAR
            {
                var keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in _replayCheckpoints) keep.Add(m.Label);
                var parent = new ExerciseNotes();
                parent.Set(_notes.ExerciseId, _notes.Notes);
                parent.RetainMarks(keep);
                inherited = parent.Notes;
            }
        }

        _notesDirtySinceLoad = false;
        if (want == Guid.Empty) _notes.Reset(default);
        else if (inherited.Length > 0) { _notes.Set((ExerciseId)want, inherited); _notesDirtySinceLoad = true; _notes.TrySave(NotesStagingFile(want)); }
        else if (!_notes.TryLoad(NotesStagingFile(want), (ExerciseId)want) && !_notes.TryLoad(NotesNasFile(want), (ExerciseId)want)) _notes.Reset((ExerciseId)want);
        _notesPrevWasReplay = _currentDsmState == ClusterState.OperatingReplay && want != Guid.Empty;
        PublishNotes();
    }

    /// <summary>⭐ Leaving Live / AAR: the notes go back to the NAS exercise folder when they changed since they were loaded ("exported on leaving").</summary>
    private void WriteBackNotes()
    {
        if (!_notesDirtySinceLoad || _notes.ExerciseId == Guid.Empty) return;
        if (_notes.TrySave(NotesNasFile(_notes.ExerciseId))) _notesDirtySinceLoad = false;
        else FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-4325: notes.json of {0} could not be written to the NAS — the master's staging copy stays.", _notes.ExerciseId);
    }

    /// <summary>⭐ A restore from checkpoint <paramref name="newExercise"/>'s parent keeps the notes riding the marks that stay; the new exercise starts with them (copied).</summary>
    private void BranchNotes(Guid newExercise, ICollection<string> keptMarks)
    {
        if (_notes.ExerciseId == Guid.Empty) return;
        WriteBackNotes();
        _notes.RetainMarks(keptMarks);
        _notes.Set((ExerciseId)newExercise, _notes.Notes);
        _notesDirtySinceLoad = _notes.Notes.Length > 0;
        _notes.TrySave(NotesStagingFile(newExercise));
        PublishNotes();
    }

    private void PublishNotes()
    {
        var notes = _notes.Notes;
        _eventBus.PublishManaged(new ExerciseNotesPublishedEvent { ExerciseId = _notes.ExerciseId, Notes = notes });
        _eventBus.PublishManaged(new ExerciseNotesUpdateEvent { ExerciseId = _notes.ExerciseId, Notes = notes });
    }
}
