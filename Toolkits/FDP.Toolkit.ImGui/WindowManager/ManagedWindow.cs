using FDP.Toolkit.ImGui.Icons;
using ImGuiNET;

namespace FDP.Toolkit.ImGui.WindowManager;

/// <summary>
/// Abstract base class for all windows managed by the FDP Window Manager.
/// Subclasses implement <see cref="DrawClientArea"/> to render their content.
/// </summary>
/// <remarks>
/// Uses <c>global using Gui = ImGuiNET.ImGui</c> from <c>GlobalUsings.cs</c>.
/// The window name format is <c>"{Title}###{Id}"</c> which lets ImGui use the stable
/// <c>###Id</c> suffix for dock-node identity while allowing dynamic title changes.
/// </remarks>
public abstract class ManagedWindow
{
    // ── Backing fields ─────────────────────────────────────────────────────────

    // Must be a field (not auto-property) so it can be passed as `ref _isOpen` to Gui.Begin.
    private bool _isOpen;

    // Must be a field so it can be passed as `ref _isPinned` to AlternatingFaceToggleIcon.
    private bool _isPinned;

    private bool _focusRequested;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Stable identifier used as ImGui window ID (the <c>###Id</c> suffix).</summary>
    public string Id { get; }

    /// <summary>
    /// Display title used in the ImGui title bar. <c>protected set</c> allows subclasses
    /// to update the title dynamically (e.g. showing active entity name).
    /// </summary>
    public string Title { get; protected set; }

    /// <summary>The perspective name that owns this window (used for visibility gating).</summary>
    public string OwningPerspective { get; }

    /// <summary>Controls whether the window is perspective-filtered or always visible.</summary>
    public WindowScope Scope { get; }

    /// <summary>
    /// Whether the window is open. Setting to <c>false</c> hides the window immediately.
    /// The backing field <c>_isOpen</c> is passed as <c>ref</c> to <c>Gui.Begin</c> so ImGui's
    /// own close button also updates this value.
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    /// <summary>
    /// Whether the window is pinned (visible across all perspectives).
    /// A pinned <see cref="WindowScope.PerspectiveBound"/> window is shown even when its
    /// owning perspective is not active.
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => _isPinned = value;
    }

    // ── Internal test-support properties ──────────────────────────────────────

    /// <summary>
    /// Exposed <c>internal</c> (via <c>InternalsVisibleTo</c>) so unit tests can verify
    /// that <see cref="RequestFocus"/> sets the flag and that <see cref="Render"/> clears it.
    /// </summary>
    internal bool FocusRequested => _focusRequested;

    /// <summary>
    /// The formatted ImGui window name used in <see cref="Render"/>: <c>"{Title}###{Id}"</c>.
    /// Exposed <c>internal</c> for test verification of condition WM-S201.10.
    /// </summary>
    internal string WindowInternalName => $"{Title}###{Id}";

    // ── Protected API for subclasses ───────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, <see cref="Render"/> passes <see cref="ImGuiWindowFlags.MenuBar"/>
    /// to <c>Gui.Begin</c> and calls <see cref="DrawLocalMenuBar"/> inside a
    /// <c>BeginMenuBar</c>/<c>EndMenuBar</c> block. Default is <c>false</c>.
    /// </summary>
    protected virtual bool HasMenuBar => false;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <summary>Initialises a new managed window.</summary>
    /// <param name="id">Stable ImGui ID (###Id suffix). Must be unique within the manager.</param>
    /// <param name="title">Initial display title shown in the title bar.</param>
    /// <param name="owningPerspective">Perspective name that owns this window.</param>
    /// <param name="scope">Visibility scope for this window.</param>
    protected ManagedWindow(
        string id,
        string title,
        string owningPerspective,
        WindowScope scope)
    {
        Id = id;
        Title = title;
        OwningPerspective = owningPerspective;
        Scope = scope;
    }

    // ── Frame entry point ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders this window for the current frame. Called by <c>WindowManager.Render()</c>
    /// once per frame. Applies visibility gating; if the window is not visible the method
    /// returns without issuing any ImGui calls.
    /// </summary>
    /// <param name="currentPerspective">The active perspective name.</param>
    /// <param name="atlas">Icon atlas used for title-bar controls.</param>
    public void Render(string currentPerspective, IconAtlas atlas)
    {
        // Step 1 — open gate.
        if (!_isOpen) return;

        // Step 2 — visibility gate.
        var isVisible = Scope == WindowScope.Global
            || _isPinned
            || OwningPerspective == currentPerspective;

        // Step 3 — exit if not visible.
        if (!isVisible) return;

        var windowInternalName = WindowInternalName;
        var perspectiveActive = OwningPerspective == currentPerspective;

        // Step 4 — consume pending focus request before Begin so ImGui can find the window.
        if (_focusRequested)
        {
            Gui.SetWindowFocus(windowInternalName);
            _focusRequested = false;
        }

        // Step 5 — begin the window. Pass ref _isOpen so ImGui's built-in close button
        //          writes back directly to the backing field.
        var flags = HasMenuBar ? ImGuiWindowFlags.MenuBar : ImGuiWindowFlags.None;
        Gui.Begin(windowInternalName, ref _isOpen, flags);

        // Step 6 — title bar controls (pin + close). Always drawn regardless of collapse state
        //          so that the pin/close buttons remain functional on collapsed windows.
        DrawCustomTitleBarControls(currentPerspective, perspectiveActive, atlas);

        // Step 7 — optional local menu bar.
        if (HasMenuBar && Gui.BeginMenuBar())
        {
            DrawLocalMenuBar();
            Gui.EndMenuBar();
        }

        // Step 8 — client area content.
        DrawClientArea();

        // Step 9 — end the window.
        Gui.End();
    }

    // ── Internal focus API ─────────────────────────────────────────────────────

    /// <summary>
    /// Queues a <c>Gui.SetWindowFocus</c> call to be issued on the next <see cref="Render"/>
    /// call. Called internally by <c>WindowManager.FocusWindow()</c>.
    /// </summary>
    internal void RequestFocus() => _focusRequested = true;

    // ── Private title-bar helpers ──────────────────────────────────────────────

    /// <summary>
    /// Draws the custom pin and close icons in the right portion of the title bar.
    /// Called unconditionally after <c>Gui.Begin()</c> (regardless of collapse state).
    /// </summary>
    private void DrawCustomTitleBarControls(
        string currentPerspective,
        bool perspectiveActive,
        IconAtlas atlas)
    {
        // Spacing step = icon width + a small margin so the icons don't touch the edge
        // or each other. Using 8 px margin gives comfortable hit targets.
        var iconStep = atlas.IconSizeVec.X + 8f;

        // ── Pin icon — PerspectiveBound windows only ───────────────────────────
        if (Scope == WindowScope.PerspectiveBound)
        {
            // Position the pin two icon-steps from the right (leaves room for close).
            Gui.SameLine(Gui.GetWindowWidth() - 2f * iconStep);
            bool pinChanged = IconWidgets.AlternatingFaceToggleIcon(
                atlas, "##pin", "pin_on", "pin_off", ref _isPinned);

            // Show a tooltip when the user just unpinned a window that will now be hidden
            // because its perspective is not the current one.
            if (pinChanged && !_isPinned && !perspectiveActive && Gui.IsItemHovered())
                Gui.SetTooltip("Unpinning will hide this window in the current perspective.");
        }

        // ── Close icon — always present ────────────────────────────────────────
        Gui.SameLine(Gui.GetWindowWidth() - iconStep);
        if (IconWidgets.IconButton(atlas, "##close", "cross"))
        {
            _isOpen = false;
            _isPinned = false;
        }
    }

    // ── Overridable content hooks ──────────────────────────────────────────────

    /// <summary>
    /// Override to populate the window's local menu bar.
    /// Only called when <see cref="HasMenuBar"/> is <c>true</c> and <c>Gui.BeginMenuBar()</c>
    /// succeeds. Default implementation does nothing.
    /// </summary>
    protected virtual void DrawLocalMenuBar() { }

    /// <summary>Render all content inside the window's client area.</summary>
    protected abstract void DrawClientArea();
}
