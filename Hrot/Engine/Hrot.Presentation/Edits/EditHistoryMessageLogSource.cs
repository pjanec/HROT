using System;
using System.Collections.Generic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Edits;

namespace Hrot.Presentation.Edits;

/// <summary>
/// ⭐ Edit framework F7 (📄 docs/UX/DESIGN_Edit_Framework.md §2.5 "the history", §2.5a; B-12) — the Message Log's <b>Edits</b> tab: the journal stays BROWSABLE there
/// (a review / debugging aid — the signal is the sticky notification, <see cref="EditNotificationOverlay"/>). ONE line per edit when it reaches its final answer
/// (Applied · Refused · Conflict · Lost), read from the ONE <see cref="EditJournal"/> — nothing of its own decides an outcome.
/// <para>Fed by <see cref="Sync"/>, called by the overlay right after it folds the journal (its one per-frame caller), so every host that draws the notifications
/// also fills the history. ⭐ 0 B in a steady frame: nothing is walked unless the journal's <see cref="EditJournal.Version"/> moved. Registered once, beside the NLog
/// source, by the shared window frame (<c>LocalWindowController</c>) — every host by construction.</para>
/// </summary>
public sealed class EditHistoryMessageLogSource : IMessageLogSource
{
    /// <summary>The one instance every host's Message Log shows and every overlay feeds.</summary>
    public static readonly EditHistoryMessageLogSource Shared = new();

    /// <summary>How many logged request ids are remembered before the set is rebuilt from the journal (bounded; the journal holds <see cref="EditJournal.Capacity"/>).</summary>
    public const int RememberedIds = EditJournal.Capacity * 4;

    private readonly List<MessageLogEntry> _messages = new();
    private readonly HashSet<Guid> _logged = new();
    private readonly object _lock = new();
    private EditJournal? _journal;
    private int _version = -1;

    public string SourceId => "edits";
    public string DisplayName => "Edits";
    public event Action<MessageLogEntry>? OnMessageAdded;

    /// <summary>Logs every edit of <paramref name="journal"/> that reached its final answer since the last call. Cheap when nothing moved.</summary>
    public void Sync(EditJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (ReferenceEquals(journal, _journal) && journal.Version == _version) return;
        _journal = journal; _version = journal.Version;
        if (_logged.Count > RememberedIds) Rebuild(journal);
        for (int i = 0; i < journal.Count; i++)
        {
            var e = journal[i];
            if (!e.IsFinal || e.RequestId == Guid.Empty || !_logged.Add(e.RequestId)) continue;
            string text = LineOf(e);
            var entry = new MessageLogEntry(DateTime.Now, e.Status == EditStatus.Applied ? LogSeverity.Info : LogSeverity.Warning, "Edits", text, LogSyntaxHighlighter.Parse(text));
            lock (_lock) MessageLogLimits.Add(_messages, entry);
            OnMessageAdded?.Invoke(entry);
        }
    }

    /// <summary>The history line of a final entry: <i>"Delete refused — Alpha 1-1 is a player's unit"</i> (the notification's own text), <i>"Rename applied"</i>; the unit's
    /// network id and the mode the edit was made in follow, when known.</summary>
    public static string LineOf(EditJournalEntry e)
    {
        string head = e.Status == EditStatus.Applied ? (e.Label.Length > 0 ? e.Label : e.Kind.ToString()) + " applied" : EditNotifications.Text(e);
        string where = e.NetworkId != 0 ? " · #" + e.NetworkId : "";
        string mode = e.Mode.Length > 0 ? " · " + e.Mode : "";
        return head + where + mode;
    }

    private void Rebuild(EditJournal journal)
    {
        _logged.Clear();
        for (int i = 0; i < journal.Count; i++) { var e = journal[i]; if (e.IsFinal) _logged.Add(e.RequestId); }
    }

    public IReadOnlyList<MessageLogEntry> GetMessages() { lock (_lock) return _messages.ToArray(); }

    public void CopyMessagesTo(List<MessageLogEntry> into)
    {
        into.Clear();
        lock (_lock) into.AddRange(_messages);
    }

    public void Clear() { lock (_lock) _messages.Clear(); }
}
