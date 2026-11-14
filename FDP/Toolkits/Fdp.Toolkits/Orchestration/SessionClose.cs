using System;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ CE-4330 ② / ⑥ — the ONE construction of every storage request (a station's button, an HTTP route and the close-the-session answer build the
    /// same intent). <c>AskedBy</c> names the station on the job (fact ①).
    /// </summary>
    public static class StorageIntents
    {
        public static ExecuteStorageOpIntent Archive(Guid exerciseId, string? askedBy = null)
            => new() { RequestId = Guid.NewGuid(), Operation = StorageOpType.Export, ExerciseId = (ExerciseId)exerciseId, AskedBy = askedBy };

        public static ExecuteStorageOpIntent Import(Guid exerciseId, string? askedBy = null)
            => new() { RequestId = Guid.NewGuid(), Operation = StorageOpType.Import, ExerciseId = (ExerciseId)exerciseId, AskedBy = askedBy };

        /// <summary>An UNARCHIVED recording off the nodes (refused by the master while a job moves it).</summary>
        public static ExecuteStorageOpIntent Discard(Guid exerciseId, string? askedBy = null)
            => new() { RequestId = Guid.NewGuid(), Operation = StorageOpType.Delete, ExerciseId = (ExerciseId)exerciseId, AskedBy = askedBy };

        /// <summary>⭐ §8 ⑦ (R-4174) — an ARCHIVED exercise off the NAS; the station confirms before it sends this.</summary>
        public static ExecuteStorageOpIntent DeleteFromArchive(Guid exerciseId, string? askedBy = null)
            => new() { RequestId = Guid.NewGuid(), Operation = StorageOpType.Delete, ExerciseId = (ExerciseId)exerciseId, AskedBy = askedBy, FromArchive = true };
    }

    /// <summary>⭐ CE-4330 ⑥ (§3 P1, Q1) — the answer to ⏏ <i>Close the session</i> about its recording.</summary>
    public enum SessionCloseAnswer : byte
    {
        /// <summary>The default (Q1): the recording goes to the NAS in the background (a storage job).</summary>
        Archive = 0,
        /// <summary>It stays on the nodes only (archive it later).</summary>
        Keep = 1,
        /// <summary>It is removed from the nodes.</summary>
        Discard = 2,
    }

    /// <summary>
    /// ⭐⭐ CE-4330 ⑥ (📄 docs/DESIGN_Recording_Storage.md §3 P1, §4 Q1 / Q3) — what ⏏ <i>Close the session</i> asks about the run's recording, and what each
    /// answer does. ⭐ The ONE place: the station's dialog (ui) and <c>POST /session/close</c> both read it. ⛔ No question when the run was not recorded (Q3).
    /// </summary>
    public static class SessionClose
    {
        /// <summary>Q1 — the answer a station preselects (and the HTTP route takes when none is given).</summary>
        public const SessionCloseAnswer DefaultAnswer = SessionCloseAnswer.Archive;

        /// <summary>The question's text, or null when nothing is asked (the run was not recorded, or it has no exercise).</summary>
        public static string? Question(bool recorded, Guid exerciseId, string lengthText, string? scenario)
            => !recorded || exerciseId == Guid.Empty ? null
             : $"Close the session — {(string.IsNullOrWhiteSpace(scenario) ? "exercise" : scenario)} ({lengthText}): Archive to NAS · Keep on the nodes · Discard";

        /// <summary>The buttons in order (the default first).</summary>
        public static readonly SessionCloseAnswer[] Answers = { SessionCloseAnswer.Archive, SessionCloseAnswer.Keep, SessionCloseAnswer.Discard };

        /// <summary>The words a station / HTTP uses: <c>archive</c> | <c>keep</c> | <c>discard</c> (any case).</summary>
        public static bool TryParse(string? text, out SessionCloseAnswer answer) => Enum.TryParse(text, ignoreCase: true, out answer) && Enum.IsDefined(typeof(SessionCloseAnswer), answer);

        /// <summary>The storage request <paramref name="answer"/> sends for the closed run's exercise; null for Keep (nothing to do).</summary>
        public static ExecuteStorageOpIntent? IntentFor(SessionCloseAnswer answer, Guid exerciseId, string? askedBy = null) => answer switch
        {
            SessionCloseAnswer.Archive => StorageIntents.Archive(exerciseId, askedBy),
            SessionCloseAnswer.Discard => StorageIntents.Discard(exerciseId, askedBy),
            _ => null,
        };
    }
}
