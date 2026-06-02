using Fdp.Presentation.WindowManager;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Production implementation of <see cref="IPerspectiveSwitcher"/> that delegates to
/// <see cref="WindowManager.SwitchPerspective"/>.
/// <para>
/// Additionally, subscribes to <see cref="WindowManager.OnPerspectiveChanged"/>:
/// when the user manually switches to a perspective (e.g. via the toolbar), and
/// an <see cref="AiDocumentManager"/> is wired in, the switcher focuses/activates
/// the most-recently-opened document of the new perspective's kind.
/// </para>
/// <para>
/// The wiring is one-way: the canvas window (Phase 2) will also call
/// <see cref="AiDocumentManager.Activate"/> when a document gains ImGui focus,
/// which in turn calls <see cref="SwitchPerspective"/>. This class guards against
/// the resulting re-entry by checking whether the <see cref="WindowManager.CurrentPerspective"/>
/// already matches (no-op path).
/// </para>
/// </summary>
public sealed class WindowManagerPerspectiveSwitcher : IPerspectiveSwitcher
{
    private readonly WindowManager _windowManager;
    private AiDocumentManager? _documentManager;

    /// <summary>
    /// Creates the switcher and subscribes to <see cref="WindowManager.OnPerspectiveChanged"/>.
    /// </summary>
    /// <param name="windowManager">The window manager to delegate perspective switches to.</param>
    public WindowManagerPerspectiveSwitcher(WindowManager windowManager)
    {
        _windowManager = windowManager
            ?? throw new ArgumentNullException(nameof(windowManager));

        // Subscribe to the window manager's change event so that manual
        // perspective switches (toolbar / menu) can activate a document.
        _windowManager.OnPerspectiveChanged += OnPerspectiveChanged;
    }

    /// <summary>
    /// Wires an <see cref="AiDocumentManager"/> so that manual perspective switches
    /// activate the most-recent document of the new kind.
    /// Call this once during editor startup after creating both objects.
    /// </summary>
    public void SetDocumentManager(AiDocumentManager documentManager)
    {
        _documentManager = documentManager
            ?? throw new ArgumentNullException(nameof(documentManager));
    }

    // ── IPerspectiveSwitcher ──────────────────────────────────────────────────

    /// <inheritdoc />
    public void SwitchPerspective(string perspectiveName)
        => _windowManager.SwitchPerspective(perspectiveName);

    // ── WindowManager.OnPerspectiveChanged handler ────────────────────────────

    private void OnPerspectiveChanged(string oldPerspective, string newPerspective)
    {
        if (_documentManager is null) return;

        // Find the most-recently opened document whose kind matches the new perspective.
        // "Most recent" = the document that appears last in the open list (i.e. last opened
        // or last activated — AiDocumentManager appends new docs in open order).
        AiDocument? candidate = null;
        foreach (var doc in _documentManager.OpenDocuments)
        {
            if (doc.Kind.ToString() == newPerspective)
                candidate = doc; // take the last match
        }

        if (candidate is not null && !ReferenceEquals(candidate, _documentManager.Active))
        {
            // Use Activate rather than Open to avoid adding a duplicate entry.
            _documentManager.Activate(candidate);
        }
        // If no matching document is open: no-op (canvas shows empty state).
    }
}
