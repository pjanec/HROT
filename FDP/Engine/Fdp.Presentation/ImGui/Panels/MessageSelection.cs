using Fdp.Core.Logging;

namespace Fdp.Presentation.Panels;

/// <summary>
/// ⭐ W-H3 (ui lane, CE-4236) — the Message Log's SELECTION, keyed by the message ITSELF (its entry object — immutable, handed out unchanged by every source) and not by its index in the source's list. A source now drops its
/// oldest lines once it is full (<see cref="MessageLogLimits"/>) and a filter changes which lines are listed, so an index (into the source, or into the filtered list) names a DIFFERENT message after either; the entry does not.
/// Pure — no ImGui; the panel's click handling and the rails drive it.
/// </summary>
internal sealed class MessageSelection
{
    private sealed class ByReference : IEqualityComparer<MessageLogEntry>
    {
        public bool Equals(MessageLogEntry? a, MessageLogEntry? b) => ReferenceEquals(a, b);
        public int GetHashCode(MessageLogEntry e) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e);
    }

    private readonly HashSet<MessageLogEntry> _selected = new(new ByReference());
    private MessageLogEntry? _lastClicked;

    public int Count => _selected.Count;
    public bool Contains(MessageLogEntry e) => _selected.Contains(e);
    public void Clear() { _selected.Clear(); _lastClicked = null; }

    /// <summary>The selected messages in the source's order (what Copy writes).</summary>
    public IEnumerable<MessageLogEntry> InOrder(IReadOnlyList<MessageLogEntry> messages) => messages.Where(_selected.Contains);

    /// <summary>
    /// A click on the row at <paramref name="clicked"/> of <paramref name="view"/> (the filtered list of indices into <paramref name="messages"/>, as drawn THIS frame): shift extends from the last click (found again by its message,
    /// so the anchor survives a trim or a filter change; gone ⇒ the click starts a new anchor), ctrl toggles, a plain click replaces the selection.
    /// </summary>
    public void Click(IReadOnlyList<MessageLogEntry> messages, List<int> view, int clicked, bool ctrl, bool shift)
    {
        if (clicked < 0 || clicked >= view.Count) return;
        var entry = messages[view[clicked]];
        int anchor = shift && _lastClicked != null ? view.FindIndex(i => ReferenceEquals(messages[i], _lastClicked)) : -1;
        if (shift && anchor >= 0)
        {
            for (int i = Math.Min(anchor, clicked); i <= Math.Max(anchor, clicked); i++) _selected.Add(messages[view[i]]);
        }
        else if (ctrl)
        {
            if (!_selected.Remove(entry)) _selected.Add(entry);
            _lastClicked = entry;
        }
        else
        {
            _selected.Clear(); _selected.Add(entry);
            _lastClicked = entry;
        }
    }
}
