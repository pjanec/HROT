using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>⭐ CE-4325 (E7, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — the ONE change message's verb: add a note, replace one, remove one.</summary>
    public enum NoteEditKind : byte { Add = 0, Edit = 1, Delete = 2 }

    /// <summary>
    /// ⭐⭐ CE-4325 (E7, R-4177) — ONE note of an exercise: what the instructor wrote about a MOMENT. ⭐ A note either rides a MARK (a checkpoint, <see cref="Mark"/> = its label —
    /// the moment IS the checkpoint, no second bookmark mechanism) or sits at a TIME of the recording (<see cref="AtSimSeconds"/>, an AAR note: not restorable, R-4168).
    /// ⭐ Immutable — an edit is a new instance (NEW COMPONENTS rule: a managed value is replaced whole).
    /// </summary>
    /// <param name="Id">The note's identity (the client mints it; the master fills an empty one).</param>
    /// <param name="Mark">The checkpoint label this note is about; empty = a time note.</param>
    /// <param name="AtSimSeconds">A time note's moment on the recording's sim clock (ignored while <paramref name="Mark"/> is set).</param>
    /// <param name="Name">A short name (<i>Bridge crossed</i>); empty = unnamed.</param>
    /// <param name="Text">The words.</param>
    /// <param name="Author">Who wrote it: seat · station (<i>Instructor-1</i>).</param>
    /// <param name="SubjectId">The unit it is about (its network entity id); 0 = none.</param>
    /// <param name="SubjectName">That unit's name, as it was when the note was written.</param>
    /// <param name="EditedUtc">When the master last applied it.</param>
    public sealed record ExerciseNote(Guid Id, string Mark = "", double AtSimSeconds = 0, string Name = "", string Text = "", string Author = "",
                                      long SubjectId = 0, string SubjectName = "", DateTime EditedUtc = default)
    {
        /// <summary>A note on a mark (a checkpoint) rather than at a bare time.</summary>
        [JsonIgnore]
        public bool OnMark => !string.IsNullOrEmpty(Mark);
    }

    /// <summary>
    /// ⭐ CE-4325 — THE ONE CRUD MESSAGE: any station asks the master to add / edit / delete a note (as a cluster op <c>EditExerciseNote</c> over the wire).
    /// The master applies the requests in arrival order (two stations editing one note: the later wins) and republishes the whole list.
    /// </summary>
    [EventId(9078)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NoteEditIntent
    {
        public Guid RequestId;
        public NoteEditKind Kind;
        /// <summary>The note (for <see cref="NoteEditKind.Delete"/> only its <see cref="ExerciseNote.Id"/> counts).</summary>
        public ExerciseNote Note;
    }

    /// <summary>⭐ CE-4325 — the master's note list for the DDS writer (one producer: the master).</summary>
    [EventId(9079)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ExerciseNotesPublishedEvent
    {
        public ExerciseId ExerciseId;
        public ExerciseNote[] Notes;
    }

    /// <summary>⭐ CE-4325 — the exercise's notes, folded by every host (<see cref="ClusterView.Notes"/>).</summary>
    [EventId(9080)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ExerciseNotesUpdateEvent
    {
        public ExerciseId ExerciseId;
        public ExerciseNote[] Notes;
    }

    /// <summary>
    /// ⭐⭐ CE-4325 (E7, R-4177) — <b>the master's ONE list of the notes of the exercise on show</b>, and its <c>notes.json</c>. One writer (the master applies every edit),
    /// every station a reader (through the one topic). ⭐ A new array per change (readers compare the reference; 0 B per read).
    /// </summary>
    public sealed class ExerciseNotes
    {
        /// <summary>The file inside an exercise folder (the master's staging, and the NAS beside the recordings).</summary>
        public const string FileName = "notes.json";

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private ExerciseNote[] _notes = Array.Empty<ExerciseNote>();

        /// <summary>The exercise these notes belong to (empty = none).</summary>
        public ExerciseId ExerciseId { get; private set; }

        /// <summary>Oldest first. A NEW array per change.</summary>
        public ExerciseNote[] Notes => _notes;

        /// <summary>Starts the list for <paramref name="exerciseId"/>, empty.</summary>
        public void Reset(ExerciseId exerciseId) { ExerciseId = exerciseId; _notes = Array.Empty<ExerciseNote>(); }

        /// <summary>Replaces the list (a load); the order is kept.</summary>
        public void Set(ExerciseId exerciseId, ExerciseNote[]? notes) { ExerciseId = exerciseId; _notes = notes ?? Array.Empty<ExerciseNote>(); }

        /// <summary>
        /// Applies ONE request. Returns false with the reason when it changes nothing: an edit / delete of an unknown id. ⭐ An add of an id already held is an edit (a resent message
        /// never doubles a note); an empty id is filled here.
        /// </summary>
        public bool Apply(NoteEditKind kind, ExerciseNote? note, DateTime nowUtc, out string? refusal)
        {
            refusal = null;
            if (note == null) { refusal = "no note"; return false; }
            int at = Array.FindIndex(_notes, n => n.Id == note.Id);
            switch (kind)
            {
                case NoteEditKind.Delete:
                    if (at < 0) { refusal = "no such note"; return false; }
                    var rest = new ExerciseNote[_notes.Length - 1];
                    Array.Copy(_notes, 0, rest, 0, at);
                    Array.Copy(_notes, at + 1, rest, at, _notes.Length - at - 1);
                    _notes = rest;
                    return true;
                case NoteEditKind.Edit:
                    if (at < 0) { refusal = "no such note"; return false; }
                    _notes = (ExerciseNote[])_notes.Clone();
                    _notes[at] = Stamp(KeepMoment(_notes[at], note), nowUtc);
                    return true;
                default:   // Add
                    if (at >= 0) { _notes = (ExerciseNote[])_notes.Clone(); _notes[at] = Stamp(KeepMoment(_notes[at], note), nowUtc); return true; }
                    var more = new ExerciseNote[_notes.Length + 1];
                    Array.Copy(_notes, more, _notes.Length);
                    more[^1] = Stamp(note.Id == Guid.Empty ? note with { Id = Guid.NewGuid() } : note, nowUtc);
                    _notes = more;
                    return true;
            }
        }

        private static ExerciseNote Stamp(ExerciseNote n, DateTime nowUtc) => n with { EditedUtc = nowUtc };

        /// <summary>⭐ CE-1467 (e) — an edit changes the WORDS (name, text, unit), never the moment the note rides: the mark's label is the one the master minted, which a station's
        /// popup may not know (it asked for another).</summary>
        private static ExerciseNote KeepMoment(ExerciseNote stored, ExerciseNote edit) => edit with { Mark = stored.Mark, AtSimSeconds = stored.AtSimSeconds };

        /// <summary>
        /// A branch from a recording keeps the parent's marks before the branch point — and their notes (copied into the child's own list): a note stays when it rides a mark in
        /// <paramref name="keptMarks"/>; a time note (an AAR note) does not (R-4168 — it has no moment to restore). Returns whether anything went.
        /// </summary>
        /// <summary>
        /// ⭐ CE-1467 (d) — a branch from a replay at <paramref name="branchWallTicks"/>: the notes of the marks EARLIER than it stay (the child's recording keeps the marks of the frames
        /// before the branch, <c>RecordingModule.StartBranch</c>); a mark at or after it, a mark not in <paramref name="markWalls"/> and a time note go. Returns whether anything went.
        /// </summary>
        public bool RetainMarksBefore(IReadOnlyDictionary<string, long> markWalls, long branchWallTicks)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in markWalls) if (kv.Value < branchWallTicks) keep.Add(kv.Key);
            return RetainMarks(keep);
        }

        public bool RetainMarks(ICollection<string> keptMarks)
        {
            var keep = new List<ExerciseNote>(_notes.Length);
            foreach (var n in _notes) if (n.OnMark && keptMarks.Contains(n.Mark)) keep.Add(n);
            if (keep.Count == _notes.Length) return false;
            _notes = keep.ToArray();
            return true;
        }

        // ── notes.json ───────────────────────────────────────────────────────────────────────────

        private sealed class FileShape
        {
            public int Version { get; set; } = 1;
            public Guid ExerciseId { get; set; }
            public ExerciseNote[] Notes { get; set; } = Array.Empty<ExerciseNote>();
        }

        /// <summary>The list as the file's text.</summary>
        public string ToJson() => JsonSerializer.Serialize(new FileShape { ExerciseId = (Guid)ExerciseId, Notes = _notes }, Json);

        /// <summary>Loads <paramref name="text"/> (a torn or foreign file is false, the list kept).</summary>
        public bool TryLoadJson(string text, ExerciseId expectedExercise)
        {
            try
            {
                var shape = JsonSerializer.Deserialize<FileShape>(text, Json);
                if (shape == null) return false;
                Set(expectedExercise != Guid.Empty ? expectedExercise : (ExerciseId)shape.ExerciseId, shape.Notes ?? Array.Empty<ExerciseNote>());
                return true;
            }
            catch (JsonException) { return false; }
        }

        /// <summary>Writes the list to <paramref name="path"/> whole (a <c>.part</c> renamed — a reader never sees half a file). Returns false when it could not.</summary>
        public bool TrySave(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ArchiveManifest.WriteWhole(path, ToJson());
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        /// <summary>Loads <paramref name="path"/> when it exists and parses; false otherwise (the list kept).</summary>
        public bool TryLoad(string path, ExerciseId exerciseId)
        {
            try { return File.Exists(path) && TryLoadJson(File.ReadAllText(path), exerciseId); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }
}
