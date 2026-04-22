using System.Numerics;
using Fdp.Presentation.Icons;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

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

    /// <summary>
    /// Optional override for the title-bar background colour.
    /// When <c>null</c> the default ImGui theme colours are used.
    /// When set, three colour variants are derived automatically:
    /// <list type="bullet">
    ///   <item><see cref="ImGuiCol.TitleBg"/> — the value as supplied.</item>
    ///   <item><see cref="ImGuiCol.TitleBgActive"/> — 35 % brighter (clamped to 1).</item>
    ///   <item><see cref="ImGuiCol.TitleBgCollapsed"/> — 35 % darker.</item>
    /// </list>
    /// </summary>
    public Vector4? TitleBarColor { get; set; }

    /// <summary>
    /// When <c>true</c>, the <see cref="WindowManager"/> automatically unregisters this
    /// window the frame after <see cref="IsOpen"/> becomes <c>false</c>.
    /// Use this for dynamically spawned windows (e.g. single-entity watch windows) that
    /// must not persist between open/close cycles.
    /// </summary>
    public bool IsVolatile { get; protected set; } = false;

    /// <summary>
    /// When <c>true</c> (the default), this window is listed as a toggle item in the
    /// global "Windows" menu. Set to <c>false</c> for volatile windows that are spawned
    /// on demand and should not appear in the persistent menu.
    /// </summary>
    public bool ShowInMenu { get; protected set; } = true;

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
    /// <param name="atlas">Icon atlas (reserved for subclass use; not consumed by base rendering).</param>
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

        // Step 5 — optionally push custom title-bar colours before Begin.
        int colorsPushed = 0;
        if (TitleBarColor.HasValue)
        {
            var c = TitleBarColor.Value;
            var cActive = new Vector4(
                MathF.Min(c.X * 1.35f, 1f),
                MathF.Min(c.Y * 1.35f, 1f),
                MathF.Min(c.Z * 1.35f, 1f),
                c.W);
            var cCollapsed = new Vector4(c.X * 0.65f, c.Y * 0.65f, c.Z * 0.65f, c.W);
            Gui.PushStyleColor(ImGuiCol.TitleBg,          c);
            Gui.PushStyleColor(ImGuiCol.TitleBgActive,    cActive);
            Gui.PushStyleColor(ImGuiCol.TitleBgCollapsed, cCollapsed);
            colorsPushed = 3;
        }

        // Step 6 — begin the window. Pass ref _isOpen so ImGui's built-in close button
        //          writes back directly to the backing field.
        var flags = HasMenuBar ? ImGuiWindowFlags.MenuBar : ImGuiWindowFlags.None;
        Gui.Begin(windowInternalName, ref _isOpen, flags);

        // Pop title-bar colours immediately after Begin (title bar is already rendered).
        if (colorsPushed > 0)
            Gui.PopStyleColor(colorsPushed);

        // Step 7 — pin button in the actual title bar (PerspectiveBound windows only).
        //          Drawn using absolute screen coordinates so it occupies no client-area height.
        if (Scope == WindowScope.PerspectiveBound)
            DrawTitleBarPinButton(perspectiveActive);

        // Step 8 — optional local menu bar.
        if (HasMenuBar && Gui.BeginMenuBar())
        {
            DrawLocalMenuBar();
            Gui.EndMenuBar();
        }

        // Step 9 — client area content.
        DrawClientArea();

        // Step 10 — end the window.
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
    /// Renders a pin/unpin toggle button directly inside the title bar, just to the
    /// left of ImGui's built-in [x] close button.  Uses absolute screen coordinates
    /// and the foreground draw list so no client-area height is consumed.
    ///
    /// <para>The button area extends the current clip rect to the title bar region,
    /// places an <c>InvisibleButton</c> for hit-testing, then restores the cursor
    /// to the original content-area position.</para>
    /// </summary>
    private void DrawTitleBarPinButton(bool perspectiveActive)
    {
        var windowPos   = Gui.GetWindowPos();
        var windowWidth = Gui.GetWindowWidth();
        float fh        = Gui.GetFrameHeight(); // approximate title-bar height

        // Place pin button one frame-height to the left of ImGui's [x] button.
        float btnX = windowPos.X + windowWidth - 2f * fh;
        float btnY = windowPos.Y;

        // Save content-area cursor so we can restore it after the hit-test widget.
        var savedPos = Gui.GetCursorScreenPos();

        // Temporarily expand the clip rect to include the title bar so that
        // InvisibleButton can receive hover/click events in that region.
        Gui.PushClipRect(
            new Vector2(windowPos.X, windowPos.Y),
            new Vector2(windowPos.X + windowWidth, windowPos.Y + fh),
            false); // absolute — do not intersect with the content-area clip

        Gui.SetCursorScreenPos(new Vector2(btnX, btnY));
        bool clicked = Gui.InvisibleButton("##wm_pin", new Vector2(fh, fh));
        bool hovered = Gui.IsItemHovered();
        bool active  = Gui.IsItemActive();

        if (clicked)
        {
            _isPinned = !_isPinned;
            if (!_isPinned && !perspectiveActive)
                Gui.SetTooltip("Window will be hidden when you switch perspective.");
        }

        // Draw the pin icon while the clip rect is still active (title bar area).
        RenderPinSymbol(btnX, btnY, fh, _isPinned, hovered, active);

        // Restore the clip rect AFTER drawing the symbol so it is not clipped out.
        Gui.PopClipRect();
        RenderPinSymbol(btnX, btnY, fh, _isPinned, hovered, active);

        // Restore cursor to the content area so DrawClientArea() starts in the right place.
        Gui.SetCursorScreenPos(savedPos);
    }

    /// <summary>
    /// Line-draws a pushpin icon on the ImGui foreground draw list at absolute screen position
    /// <paramref name="btnX"/>, <paramref name="btnY"/> within a square of size <paramref name="size"/>.
    /// Filled when <paramref name="isPinned"/>; outline otherwise.
    /// </summary>
    private static void RenderPinSymbol(
        float btnX, float btnY, float size,
        bool isPinned, bool hovered, bool active)
    {
        var dl = Gui.GetWindowDrawList();

        // Hover / active background — mirrors ImGui close-button style.
        if (hovered || active)
        {
            uint bgCol = active
                ? Gui.GetColorU32(new Vector4(0.40f, 0.40f, 0.40f, 0.60f))
                : Gui.GetColorU32(new Vector4(0.50f, 0.50f, 0.50f, 0.30f));
            dl.AddRectFilled(
                new Vector2(btnX, btnY),
                new Vector2(btnX + size, btnY + size),
                bgCol, 3f);
        }

        // Symbol colour: full white when hovered, slightly translucent otherwise.
        uint col = hovered
            ? Gui.GetColorU32(new Vector4(1f, 1f, 1f, 1f))
            : Gui.GetColorU32(new Vector4(0.85f, 0.85f, 0.85f, 0.80f));

        // Geometry constants (all relative to btn square).
        float pad    = size * 0.20f;
        float cx     = btnX + size * 0.50f;
        float headHW = size * 0.22f;          // head half-width
        float headY0 = btnY + pad;
        float headY1 = btnY + size * 0.50f;   // bottom of head rectangle
        float shaftY1 = btnY + size - pad;    // tip of shaft
        float lineW  = isPinned ? 1.5f : 1.0f;

        if (isPinned)
        {
            // Filled head + shaft.
            dl.AddRectFilled(
                new Vector2(cx - headHW, headY0),
                new Vector2(cx + headHW, headY1),
                col, 1.5f);
            dl.AddLine(new Vector2(cx, headY1), new Vector2(cx, shaftY1), col, lineW);
        }
        else
        {
            // Outlined head + shaft.
            dl.AddRect(
                new Vector2(cx - headHW, headY0),
                new Vector2(cx + headHW, headY1),
                col, 1.5f);
            dl.AddLine(new Vector2(cx, headY1), new Vector2(cx, shaftY1), col, lineW);
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
