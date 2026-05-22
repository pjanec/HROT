using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.UI.HotReload;

/// <summary>
/// Subscribes to <see cref="IGraphModel.Changed"/>, records changes in a
/// <see cref="RecentChanges"/> ring buffer, posts editor notifications (toasts),
/// and clears the undo stack on wholesale reloads.
/// </summary>
public sealed class ChangeNotifier : IDisposable
{
    private readonly IGraphModel       _model;
    private readonly IEditorIndicators _indicators;
    private readonly RecentChanges     _changes;
    private readonly UndoStack         _undo;

    public ChangeNotifier(
        IGraphModel       model,
        IEditorIndicators indicators,
        RecentChanges     changes,
        UndoStack         undo)
    {
        _model      = model;
        _indicators = indicators;
        _changes    = changes;
        _undo       = undo;
        _model.Changed += OnGraphChanged;
    }

    /// <inheritdoc/>
    public void Dispose() => _model.Changed -= OnGraphChanged;

    // ── private ───────────────────────────────────────────────────────────────

    private void OnGraphChanged(GraphChangeNotification n)
    {
        var now = TimeProvider.System.GetLocalNow().TimeOfDay;
        _changes.Add(n, now);

        // Wholesale → invalidate undo
        if (n.Kind == GraphChangeKind.Wholesale)
            _undo.Clear();

        // Summarise affected counts
        int affected = n.AffectedNodes?.Count ?? 0;
        var verb     = n.Kind switch
        {
            GraphChangeKind.NodesAdded    => "added",
            GraphChangeKind.NodesRemoved  => "removed",
            GraphChangeKind.NodesModified => "modified",
            GraphChangeKind.NodesMoved    => "moved",
            GraphChangeKind.LinksAdded    => "added (links)",
            GraphChangeKind.LinksRemoved  => "removed (links)",
            GraphChangeKind.VariablesChanged => "variables changed",
            GraphChangeKind.Wholesale     => "wholesale reload",
            _                             => "changed",
        };

        var body = affected > 0
            ? $"{affected} element{(affected == 1 ? "" : "s")} {verb}."
            : $"Graph {verb}.";

        _indicators.Notify(new EditorNotification(
            Id:          "hot-reload-" + Guid.NewGuid().ToString("N")[..8],
            Severity:    NotificationSeverity.Info,
            Title:       "↻ Asset reloaded.",
            Body:        body,
            AutoDismiss: TimeSpan.FromSeconds(5),
            Actions:     null));
    }
}
