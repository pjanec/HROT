using System.Numerics;
using ImGuiNET;

namespace FDP.Toolkit.ImGui.WindowManager;

/// <summary>
/// Manages a persistent global status bar rendered at the bottom of the main viewport.
/// Subsystems register named sections (sorted by <c>SortOrder</c>); the manager
/// renders them left-to-right with vertical separators between each section.
/// Sections can be restricted to a specific perspective or marked as global (always visible).
/// </summary>
public class StatusBarManager
{
    // ── Inner type ──────────────────────────────────────────────────────────

    private struct Section
    {
        public string  Id;
        public int     SortOrder;
        /// <summary>
        /// Perspective filter. <c>null</c> = global (always visible).
        /// Any non-null value restricts this section to the named perspective.
        /// </summary>
        public string? Perspective;
        public Action  RenderDelegate;
    }

    // ── Fields ──────────────────────────────────────────────────────────────

    private readonly List<Section> _sections  = new();
    private          bool          _needsSort = false;

    // Very dark gray used as the status-bar window background so it is
    // visually distinct from normal window client-area backgrounds.
    private static readonly Vector4 StatusBarBg = new(0.12f, 0.12f, 0.12f, 1f);

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
    /// <param name="perspective">
    /// Optional perspective name. When <c>null</c> (default) the section is global and always
    /// rendered. When set, the section is only rendered when the active perspective matches.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderDelegate"/> is <c>null</c>.</exception>
    public void RegisterSection(string id, int sortOrder, Action renderDelegate, string? perspective = null)
    {
        ArgumentNullException.ThrowIfNull(renderDelegate, nameof(renderDelegate));

        // Replace existing section with same id (last-write-wins).
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].Id == id)
            {
                _sections[i] = new Section { Id = id, SortOrder = sortOrder, Perspective = perspective, RenderDelegate = renderDelegate };
                _needsSort = true;
                return;
            }
        }

        _sections.Add(new Section { Id = id, SortOrder = sortOrder, Perspective = perspective, RenderDelegate = renderDelegate });
        _needsSort = true;
    }

    // ── Render ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the status bar at the bottom of the main viewport.
    /// Must be called inside an active ImGui frame.
    /// Updates <see cref="Height"/> each call.
    /// </summary>
    /// <param name="currentPerspective">
    /// The active perspective name used to filter perspective-bound sections.
    /// Pass an empty string (default) or the current perspective from
    /// <see cref="WindowManager.CurrentPerspective"/>.
    /// </param>
    public void Render(string currentPerspective = "")
    {
        if (_needsSort)
        {
            _sections.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            _needsSort = false;
        }

        float height = Gui.GetFrameHeight() + Gui.GetStyle().WindowPadding.Y * 2f;
        Height = height;

        var viewport = Gui.GetMainViewport();
        Gui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height));
        Gui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, height));
        Gui.SetNextWindowViewport(viewport.ID);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoDocking
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove;

        // Dark background so the status bar is visually distinct from window client areas.
        Gui.PushStyleColor(ImGuiCol.WindowBg, StatusBarBg);
        Gui.Begin("##GlobalStatusBar", flags);
        Gui.PopStyleColor();

        bool first = true;
        for (int i = 0; i < _sections.Count; i++)
        {
            var s = _sections[i];

            // Filter: global sections (null perspective) always render;
            // perspective-bound sections only render when the perspective matches.
            bool visible = s.Perspective == null || s.Perspective == currentPerspective;
            if (!visible) continue;

            if (!first)
            {
                Gui.SameLine();
                Gui.Text("|");   // vertical separator (SeparatorEx not exposed in ImGui.NET 1.91.x)
                Gui.SameLine();
            }
            first = false;

            s.RenderDelegate();
        }

        Gui.End();
    }
}
