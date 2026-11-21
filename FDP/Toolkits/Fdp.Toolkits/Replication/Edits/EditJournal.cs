using System;
using System.Runtime.CompilerServices;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>One journal entry: the op the user submitted and its LATEST outcome.</summary>
    public sealed class EditJournalEntry
    {
        public Guid RequestId { get; internal set; }
        public EditKind Kind { get; internal set; }
        public string Label { get; internal set; } = string.Empty;
        /// <summary>The target's network id when it had one (0 otherwise).</summary>
        public int NetworkId { get; internal set; }
        /// <summary>The station's editing mode when the edit was made (the gate's <see cref="IEditGate"/> mode text; empty when no gate).</summary>
        public string Mode { get; internal set; } = string.Empty;
        public EditStatus Status { get; internal set; }
        /// <summary>The owner's (or the gate's) reason — the whole text.</summary>
        public string Reason { get; internal set; } = string.Empty;
        /// <summary>When it was submitted (wall clock, UTC ticks — the journal is station UI state, not sim state).</summary>
        public long SubmittedUtcTicks { get; internal set; }
        /// <summary>⭐ Edit mode only (undo, docs/UX/DESIGN_Edit_Mode_Undo.md): what the caller captured before the edit. Null otherwise.</summary>
        public object? Before { get; set; }
        /// <summary>The user dismissed its notification (F7).</summary>
        public bool Dismissed { get; set; }

        public bool IsFinal => Status is EditStatus.Applied or EditStatus.Refused or EditStatus.Conflict or EditStatus.Lost;
    }

    /// <summary>
    /// ⭐ Edit framework (📄 docs/UX/DESIGN_Edit_Framework.md §2.1, Q5) — THIS STATION's edits, newest last: the op, its label and mode, its latest outcome.
    /// The one place the notifications, the field marks (F7) and undo read. UI state: per world, ⛔ not recorded, not on the wire, not saved.
    /// <see cref="Refresh"/> (once a frame, by whoever draws it) folds in the outcomes that arrived and marks an unanswered edit <see cref="EditStatus.Lost"/>
    /// after <see cref="LostAfter"/> — 0 B in the steady state.
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    public sealed class EditJournal
    {
        public const int Capacity = 256;
        /// <summary>How long a Pending edit waits before it is Lost (the design's interaction time-out, 5 s, generalised — a deferred verdict is reported Pending by the owner and
        /// re-arms the wait).</summary>
        public static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(5);

        private static readonly ConditionalWeakTable<EntityRepository, EditJournal> PerWorld = new();

        private readonly EditJournalEntry[] _ring = new EditJournalEntry[Capacity];
        private int _next, _count;

        /// <summary>Bumped whenever an entry is added or changes — a reader redraws only when it moved.</summary>
        public int Version { get; private set; }
        public int Count => _count;

        public static EditJournal Of(EntityRepository repo)
        {
            ArgumentNullException.ThrowIfNull(repo);
            return PerWorld.GetValue(repo, static _ => new EditJournal());
        }

        /// <summary>The <paramref name="i"/>-th entry, oldest first (0 … <see cref="Count"/> − 1).</summary>
        public EditJournalEntry this[int i] => _ring[(_next - _count + i + Capacity * 2) % Capacity];

        /// <summary>The entry of <paramref name="requestId"/>, newest first; null when it is not held.</summary>
        public EditJournalEntry? Find(Guid requestId)
        {
            for (int i = _count - 1; i >= 0; i--) { var e = this[i]; if (e.RequestId == requestId) return e; }
            return null;
        }

        internal EditJournalEntry Append(Guid requestId, EditOp op, int networkId, string mode)
        {
            var e = _ring[_next] ??= new EditJournalEntry();
            e.RequestId = requestId; e.Kind = op.Kind; e.Label = op.Label; e.NetworkId = networkId; e.Mode = mode;
            e.Status = EditStatus.Pending; e.Reason = string.Empty; e.SubmittedUtcTicks = DateTime.UtcNow.Ticks; e.Before = null; e.Dismissed = false;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            Version++;
            return e;
        }

        /// <summary>Folds <paramref name="log"/>'s latest outcomes into the entries still waiting, and marks the ones waiting longer than <see cref="LostAfter"/> Lost.</summary>
        public void Refresh(EditOutcomeLog log, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(log);
            long lostTicks = utcNow.Ticks - LostAfter.Ticks;
            for (int i = 0; i < _count; i++)
            {
                var e = this[i];
                if (e.IsFinal) continue;
                if (log.TryGet(e.RequestId, out var o) && o.Status != EditStatus.None)
                {
                    if (o.Status != e.Status || !string.Equals(o.Reason, e.Reason, StringComparison.Ordinal))
                    {
                        e.Status = o.Status; e.Reason = o.Reason ?? string.Empty;
                        if (o.Status == EditStatus.Pending) e.SubmittedUtcTicks = utcNow.Ticks;   // a deferred verdict: the owner is alive, wait again
                        Version++;
                    }
                    if (e.IsFinal) continue;
                }
                if (e.SubmittedUtcTicks < lostTicks)
                {
                    e.Status = EditStatus.Lost; e.Reason = "no answer from the owner";
                    Version++;
                }
            }
        }

        /// <summary>Refresh against the world's own log, now.</summary>
        public void Refresh(EntityRepository repo) => Refresh(EditOutcomeLog.Of(repo), DateTime.UtcNow);
    }
}
