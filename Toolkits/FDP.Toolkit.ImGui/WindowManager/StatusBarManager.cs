using ImGuiNET;

namespace FDP.Toolkit.ImGui.WindowManager;

/// <summary>
/// Manages a persistent global status bar rendered at the bottom of the main viewport.
/// Subsystems register named sections (sorted by <c>SortOrder</c>); the manager
/// renders them left-to-right with vertical separators between each section.
/// </summary>
public class StatusBarManager
{
    // ── Inner type ──────────────────────────────────────────────────────────

    private struct Section
    {
        public string Id;
        public int    SortOrder;
        public Action RenderDelegate;
    }

    // ── Fields ──────────────────────────────────────────────────────────────

    private readonly List<Section> _sections  = new();
    private          bool          _needsSort = false;

    // ── Properties ──────────────────────────────────────────────────────────

    /// <summary>
    /// Height of the status bar in pixels, as computed during the last <see cref="Render"/> call.
    /// Returns <c>0</c> until <see cref="Render"/> has been called at least once.
    /// </summary>
    public float Height { get; private set; }

    // ── Registration ────────────────────────────────────────────────────────

    /// <summary>
    /// Registers (or replaces) a named section in the status bar.
    /// </summary>
    /// <param name="id">Unique section identifier. Duplicate ids replace the previous entry.</param>
    /// <param name="sortOrder">Ascending sort order; lower values render to the left.</param>
    /// <param name="renderDelegate">
    /// Delegate called each frame to render this section's ImGui content.
    /// Must not be <c>null</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderDelegate"/> is <c>null</c>.</exception>
    public void RegisterSection(string id, int sortOrder, Action renderDelegate)
    {
        ArgumentNullException.ThrowIfNull(renderDelegate, nameof(renderDelegate));

        // Replace existing section with same id (last-write-wins).
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].Id == id)
            {
                _sections[i] = new Section { Id = id, SortOrder = sortOrder, RenderDelegate = renderDelegate };
                _needsSort = true;
                return;
            }
        }

        _sections.Add(new Section { Id = id, SortOrder = sortOrder, RenderDelegate = renderDelegate });
        _needsSort = true;
    }

    // ── Render ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the status bar at the bottom of the main viewport.
    /// Must be called inside an active ImGui frame.
    /// Updates <see cref="Height"/> each call.
    /// </summary>
    public void Render()
    {
        if (_needsSort)
        {
            _sections.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            _needsSort = false;
        }

        float height = Gui.GetFrameHeight() + Gui.GetStyle().WindowPadding.Y * 2f;
        Height = height;

        var viewport = Gui.GetMainViewport();
        Gui.SetNextWindowPos(new System.Numerics.Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height));
        Gui.SetNextWindowSize(new System.Numerics.Vector2(viewport.WorkSize.X, height));
        Gui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove;

        Gui.Begin("##GlobalStatusBar", flags);

        for (int i = 0; i < _sections.Count; i++)
        {
            _sections[i].RenderDelegate();

            if (i < _sections.Count - 1)
            {
                Gui.SameLine();
                Gui.Text("|");   // vertical visual separator (SeparatorEx not exposed in ImGui.NET 1.91.x)
                Gui.SameLine();
            }
        }

        Gui.End();
    }
}
