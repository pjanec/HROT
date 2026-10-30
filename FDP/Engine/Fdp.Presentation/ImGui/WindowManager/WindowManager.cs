using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using Fdp.Core.Logging;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels;
using Fdp.Core.Serialization;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Central orchestrator for all <see cref="ManagedWindow"/> instances.
/// Manages window registration, programmatic show/hide/pin/focus,
/// perspective switching, and per-frame rendering of the global menu bar and all windows.
/// </summary>
public class WindowManager
{
    // �� Fields �����������������������������������������������������������������

    private readonly Dictionary<string, ManagedWindow> _windows = new();
    private readonly Dictionary<string, string> _perspectiveIconKeys = new();
    private readonly Dictionary<string, string> _perspectiveLabels = new();
    private readonly IconAtlas _atlas;
    private const int ActionAbout = -1;
    private const int ActionSettings = -2;

    /// <summary>
    /// BATCH-26: Target height (px) of the main menu bar so the inline toolbar
    /// icons render at ~32×32 — large enough to click comfortably with the mouse.
    /// The menu labels keep their font size and are vertically centred within this
    /// taller bar. Applied via a <c>FramePadding.Y</c> push around the menu bar.
    /// </summary>
    private const float MainMenuBarTargetHeight = 32f;
    private readonly List<string> _windowToggleMap = new();
    private bool _openAboutModal;
    private bool _openSettingsModal;
    private IFileDialogService? _fileDialogService;

    private readonly List<Action> _frameOverlays = new();

    /// <summary>
    /// Everything registered to draw in the final per-frame slot, in registration order.
    /// <para>
    /// Exposed so a test can assert on the CONSTRUCTED manager — that a given object's draw
    /// really is in the per-frame path — rather than on the source of whoever registered it.
    /// That distinction is the whole reason this hook exists: <c>VariableEditModal</c> shipped
    /// complete, constructed in all three perspectives, with zero callers of its <c>Draw</c>,
    /// and every test of it was green because each one constructed the modal itself.
    /// </para>
    /// </summary>
    public IReadOnlyList<Action> FrameOverlays => _frameOverlays;

    /// <summary>
    /// Registers a delegate drawn each frame AFTER all windows and the status bar, in the same
    /// final slot as the file dialog — so a modal it opens overlays every other window.
    /// <para>
    /// This is the slot a MODAL needs and a window cannot provide: <see cref="ManagedWindow.Render"/>
    /// returns early when the window is closed or belongs to another perspective, so a modal drawn
    /// inside a window's client area vanishes with that window. A modal that survives a perspective
    /// switch is correct; its own open-state is what gates it.
    /// </para>
    /// <para>
    /// Registration is idempotent by delegate equality, so a caller that registers the same method
    /// group on the same target twice does not draw twice.
    /// </para>
    /// </summary>
    /// <param name="draw">The per-frame draw call. Must not be null.</param>
    public void RegisterFrameOverlay(Action draw)
    {
        if (draw is null) throw new ArgumentNullException(nameof(draw));
        if (_frameOverlays.Contains(draw)) return;
        _frameOverlays.Add(draw);
    }

    /// <summary>
    /// Editor font pipeline, injected by the presentation shell. When set, the Settings
    /// window's UI-scale slider drives live rescaling; null in headless / test hosts.
    /// </summary>
    public Fdp.Presentation.Fonts.EditorFontService? FontService { get; set; }

    /// <summary>
    /// Persisted user UI-scale multiplier (1.0 = 100%). Applied on top of the autodetected
    /// monitor DPI. Round-tripped through <see cref="SaveSettings"/> / <see cref="LoadSettings"/>.
    /// </summary>
    public float UiScale { get; set; } = 1f;

    /// <summary>
    /// Optional resolver mapping a menu item's semantic icon key to a colored atlas sprite,
    /// injected by the editor composition (which owns the icon vocabulary). When set, main-menu
    /// and gizmo-menu items with an <c>Icon</c> render a colored icon in an aligned gutter.
    /// </summary>
    public GizmoMap.Presentation.MenuIconResolver? MenuIcons { get; set; }

    // �� Construction �����������������������������������������������������������

    /// <summary>Initialises a new <see cref="WindowManager"/>.</summary>
    /// <param name="atlas">Icon atlas used by managed windows for title-bar controls.</param>
    public WindowManager(IconAtlas atlas)
    {
        _atlas = atlas;

        // ⭐⭐⭐ VC-2 — THE ONE `Settings` MENU, and it lives in GlobalMenu.
        // 📐 Measured 2026-08-22: the menu bar renders TWO independent models side by side —
        //    RenderGlobalMenu(GlobalMenu.Root) and ImGuiMenuRenderer.DrawMenus(BuildHostMenuDtos()).
        //    `Settings` used to be a DTO block here, so a host registering "Settings/…" through
        //    GlobalMenu would have produced a SECOND top-level menu with the same name, side by side.
        // ⇒ ⭐ this item moves into GlobalMenu (route, don't duplicate) so `Settings` is ONE menu that
        //    hosts can extend — which is what the layout items now do.
        //    ⭐ CE-4085: Settings is a SUBMENU of View (View ▸ Settings ▸ …), not a top-level menu — ⛔ SUPERSEDED: the top-level Settings.
        GlobalMenu.RegisterItem(GlobalMenuRegistry.SettingsMenuPath + "/UI Scale & Fonts…", () => _openSettingsModal = true);

        // ⭐⭐ CE-1243 MB-3 (docs/UX/UX_Feature_Menu_Follows_Focus.md §MB) — the shell's own per-frame menus are CONTRIBUTORS of the one
        //    menu tree, drawn by the same walk and the same rows as every registered item. ⛔ SUPERSEDED: RenderPerspectiveMenu (a
        //    hand-drawn block) and BuildHostMenuDtos drawn by a second renderer (ImGuiMenuRenderer) — VC-2's "two independent models".
        GlobalMenu.RegisterContributor("wm/perspective", () => OnlyIfAny(PerspectiveMenuDto()), OnPerspectiveMenuAction);
        GlobalMenu.RegisterContributor("wm/windows",     () => OnlyIfAny(WindowsMenuDto()),     HandleHostMenuAction);
        GlobalMenu.RegisterContributor("wm/help",        () => new[] { HelpMenuDto() },        HandleHostMenuAction);
        // ⭐⭐ CE-4096 (AQ108 D16) — while Start shows, the bar's model holds only items marked allowed-in-Idle (one visibility test, GlobalMenu.Offers).
        GlobalMenu.IdleOnlyWhen = () => IsStartShowing;
    }

    /// <summary>The icon atlas supplied at construction time.</summary>
    public IconAtlas Atlas => _atlas;

    // �� Registration �����������������������������������������������������������

    /// <summary>
    /// Registers a window with this manager, keyed by <see cref="ManagedWindow.Id"/>.
    /// Re-registering the same Id overwrites the previous entry.
    /// </summary>
    /// <remarks>
    /// ⭐⭐⭐ <b><c>VC-1</c> — registration NOTIFIES the window</b> *(<see cref="ManagedWindow.OnRegistered"/>)*.
    /// 📐 Measured <c>2026-08-22</c>: <c>DetailsWindow.AttachWindowManager</c> had exactly ONE caller
    /// *(<c>PerspectiveWorkspaceRegistrar</c>)*, so the Scenario Details host — built directly at the
    /// composition root — never got a manager and its float/pin buttons could not draw at all.
    /// ⇒ ⭐ a window that needs its manager now RECEIVES it, and ⛔ no registration path can forget:
    /// 📌 the same PULL argument <c>R-126</c> makes for the staged-write drain — <i>"no path can forget
    /// to raise what is never raised."</i>
    /// </remarks>
    public void RegisterWindow(ManagedWindow window)
    {
        _windows[window.Id] = window;
        window.OnRegistered(this);

        // ⭐⭐⭐ CE-076 (user-approved `2026-08-27`) — EVERY registered window is visible to `/panels`
        //    BY CONSTRUCTION, so the phase-2 UI baseline cannot be blind to one.
        //
        // 📐 THE GAP THIS CLOSES: `PanelSnapshot.DeclareInstrumented` was per-window OPT-IN — neither
        //    `ManagedWindow`'s ctor nor this method called it — so a window that never opted in was
        //    INVISIBLE to `TheUiBaselineIsPinnedPerHostRails`, and the size of that blind spot was
        //    UNMEASURED (no route reported the true registered total). ⇒ a window could be dropped or
        //    renamed by a unification slice with the baseline still GREEN.
        //
        // ⚠⚠ THIS WIDENS WHAT `registered[]` MEANS, deliberately: it was "every panel that is
        //    INSTRUMENTED at all" and is now "every registered WINDOW, plus every panel that declared
        //    itself". ⭐ The field is named `registered`, and completeness is the property the baseline
        //    needs. ⛔ What did NOT change: `TryGet` still returns null for a window that publishes no
        //    model (exactly as for an un-instrumented panel), and `captured[]`/`kinds{}` are untouched
        //    because they derive from what actually drew.
        // 📌 `tools/ai-debug-mcp`'s `list_panels` doc still says "instrumented at all" — that text lives
        //    in the MCP lane's `skill-parts/` (⛔ never the generated SKILL.md) and is filed as CE-085.
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §5c.4e.
        Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.DeclareInstrumented(window.Id);
    }

    /// <summary>
    /// Attempts to retrieve a registered window by id.
    /// </summary>
    /// <param name="id">The window id to look up.</param>
    /// <param name="window">The found window, or <c>null</c> when not found.</param>
    /// <returns><c>true</c> if a window with <paramref name="id"/> is registered; otherwise <c>false</c>.</returns>
    /// <summary>
    /// ⭐⭐ <b>Batch 103 (<c>103b</c>) — every registered window id.</b>
    ///
    /// <para>⭐ A RAIL SURFACE on the CONSTRUCTED manager: the shipped default layout names 55 window
    /// ids, and ⛔ <b>a renamed or retired window silently ORPHANS its entry</b> — the layout still
    /// loads, that window just never appears, and nothing says so. ⇒ ⭐ a rail needs to ask what was
    /// actually registered, not re-derive the ids from a naming convention that could itself be the
    /// thing that moved.</para>
    ///
    /// <para>⚠ <c>TryGetWindow</c> answers the other direction *(is this id real?)*; ⛔ it cannot answer
    /// <i>"is every real id in the file?"</i>, which is the half that catches a NEW window nobody added
    /// to the default.</para>
    /// </summary>
    public IReadOnlyCollection<string> RegisteredWindowIds => _windows.Keys.ToList();

    public bool TryGetWindow(string id, [MaybeNullWhen(false)] out ManagedWindow window)
        => _windows.TryGetValue(id, out window);

    // �� Programmatic API �������������������������������������������������������

    /// <summary>
    /// Sets <see cref="ManagedWindow.IsOpen"/> to <c>true</c>.
    /// For <see cref="WindowScope.PerspectiveBound"/> windows whose
    /// <see cref="ManagedWindow.OwningPerspective"/> differs from <see cref="CurrentPerspective"/>,
    /// also sets <see cref="ManagedWindow.IsPinned"/> to <c>true</c> so the window remains visible.
    /// Silent no-op for unknown ids.
    /// </summary>
    public void ShowWindow(string id)
    {
        if (!_windows.TryGetValue(id, out var win)) return;

        win.IsOpen = true;

        if (win.Scope == WindowScope.PerspectiveBound
            && win.OwningPerspective != CurrentPerspective)
        {
            win.IsPinned = true;
        }
    }

    /// <summary>
    /// Sets <see cref="ManagedWindow.IsOpen"/> and <see cref="ManagedWindow.IsPinned"/> to <c>false</c>.
    /// Silent no-op for unknown ids.
    /// </summary>
    public void HideWindow(string id)
    {
        if (!_windows.TryGetValue(id, out var win)) return;

        win.IsOpen = false;
        win.IsPinned = false;
    }

    /// <summary>
    /// Updates <see cref="ManagedWindow.IsPinned"/> for <see cref="WindowScope.PerspectiveBound"/> windows.
    /// No-op for <see cref="WindowScope.Global"/> windows or unknown ids.
    /// </summary>
    public void SetWindowPinned(string id, bool isPinned)
    {
        if (!_windows.TryGetValue(id, out var win)) return;
        if (win.Scope == WindowScope.Global) return;

        win.IsPinned = isPinned;
    }

    /// <summary>
    /// Applies <see cref="ShowWindow"/> logic and queues a focus request on the window.
    /// Silent no-op for unknown ids.
    /// </summary>
    public void FocusWindow(string id)
    {
        if (!_windows.TryGetValue(id, out var win)) return;

        ShowWindow(id);
        win.RequestFocus();
    }

    // �� Perspective ������������������������������������������������������������

    /// <summary>The currently active perspective name. Defaults to <c>"Default"</c>.</summary>
    public string CurrentPerspective { get; private set; } = "Default";

    /// <summary>
    /// Fired when the active perspective changes.
    /// Arguments are (oldPerspective, newPerspective).
    /// Not fired when called with the current perspective.
    /// </summary>
    public event Action<string, string>? OnPerspectiveChanged;

    /// <summary>
    /// Switches to <paramref name="newPerspective"/> and fires <see cref="OnPerspectiveChanged"/>.
    /// No-op (event not fired) if <paramref name="newPerspective"/> equals <see cref="CurrentPerspective"/>.
    ///
    /// <para>⭐⭐⭐ <b><c>A0</c> — AN UNKNOWN PERSPECTIVE IS REFUSED: logged once, then a no-op.</b>
    /// 📄 <c>DESIGN_Perspective_Unification.md</c> §3 <c>A0</c> · <c>UX/UX_Feature_Perspective_Restore.md</c>
    /// §3 <i>("Log and no-op instead of silently hiding every bound window")</i>.</para>
    ///
    /// <para>⛔⛔ <b>Why this is a prerequisite and not a nicety.</b> A perspective exists only because a
    /// window CLAIMS it *(§2)*, so a name no window claims fails every
    /// <see cref="ManagedWindow"/>'s visibility gate — 🔴 <b>the UI comes up BLANK, with no error and no
    /// log line.</b> 📐 The reachable path is a stored <c>ActivePerspective</c> that a rename has orphaned,
    /// or a hand-edited layout file.</para>
    ///
    /// <para>⚠ <b>The refusal is logged ONCE per name</b> — the caller may be a per-frame toolbar, and a
    /// refusal that floods the log is a refusal nobody reads.</para>
    /// </summary>
    public void SwitchPerspective(string newPerspective)
        => TrySwitchPerspective(newPerspective, out _);

    /// <summary>
    /// ⭐⭐⭐ <b>CE-4094 (AQ108 D16/M7) — the name of the START perspective</b>: what every host shows while the cluster rests in
    /// Idle. 📄 <c>Architect_Question_108_Run_Mode_Picker.md</c> §4 D16, §5 M7. ⭐ One constant, so the dispatcher, the HTTP
    /// surface and the start gate (CE-4096) cannot disagree about the spelling.
    /// </summary>
    public const string StartPerspectiveName = "Start";

    /// <summary>
    /// ⭐⭐⭐ <b>The perspective that was current when <see cref="StartPerspectiveName"/> was entered</b> — the one leaving Start
    /// restores (D16) and the one HTTP reads route to while Start shows (the dispatcher's alias). <see langword="null"/> until
    /// Start has been entered, and again once it is left. ⛔ The ONE owner: set inside <see cref="TrySwitchPerspective"/>, the
    /// only funnel into Start, so no host can enter Start without remembering.
    /// </summary>
    public string? RememberedPerspective { get; private set; }

    /// <summary>⭐ True while <see cref="StartPerspectiveName"/> is the current perspective.</summary>
    public bool IsStartShowing => CurrentPerspective == StartPerspectiveName;

    /// <summary>
    /// ⭐⭐⭐ <b>The switch with its reason</b> — <see cref="SwitchPerspective"/> delegates here. Returns <see langword="false"/>
    /// (and a <paramref name="reason"/>) when the switch is REFUSED: ① <b>Start is current</b> — the gate owns leaving it
    /// (<see cref="LeaveStartPerspective"/>), so a toolbar click, a menu action or <c>POST /perspective</c> cannot walk out of
    /// Idle; ② the name is claimed by no window (<c>A0</c>). Returns <see langword="true"/> for a switch that happened and for the
    /// no-op onto the current perspective. ⭐ Entering Start remembers the outgoing perspective in
    /// <see cref="RememberedPerspective"/>. ⚠ When Start is NOT current this behaves exactly as <see cref="SwitchPerspective"/>
    /// always did.
    /// </summary>
    public bool TrySwitchPerspective(string newPerspective, out string? reason)
    {
        reason = null;
        if (newPerspective == CurrentPerspective) return true;

        if (IsStartShowing)
        {
            reason = $"The Start perspective is showing (the cluster is Idle) — '{newPerspective}' cannot be selected until a scenario or "
                   + $"recording is opened or created; leaving Start restores '{RememberedPerspective ?? "the previous perspective"}'.";
            return false;
        }

        return ApplyPerspective(newPerspective, out reason);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The gate's own way out of Start</b> — restores <see cref="RememberedPerspective"/> and clears it (D16: "leaving Idle
    /// restores the host's remembered perspective"). ⛔ The ONLY path out of Start; ⭐ the start gate (CE-4096) calls it when the
    /// cluster leaves Idle — ⛔ not another remember-and-restore. Returns <see langword="false"/> with a reason when Start is not
    /// showing or the remembered perspective is no longer claimed (Start then stays, never a blank UI — <c>A0</c>).
    /// </summary>
    public bool LeaveStartPerspective(out string? reason)
    {
        reason = null;
        if (!IsStartShowing) { reason = "The Start perspective is not showing."; return false; }
        if (RememberedPerspective is not { } target)
        { reason = "No perspective was remembered when Start was entered."; return false; }

        // The gate's path bypasses the Start refusal but keeps the A0 claimed-set check.
        if (!ApplyPerspective(target, out reason)) return false;
        RememberedPerspective = null;
        return true;
    }

    private bool ApplyPerspective(string newPerspective, out string? reason)
    {
        reason = null;

        // ⭐⭐⭐ A0 — the claimed set is the only legal set. ⛔ Do NOT relax this to "non-empty string":
        //    the whole defect is that an unclaimed name is syntactically fine and semantically blank.
        if (!GetPerspectives().Contains(newPerspective))
        {
            reason = $"'{newPerspective}' is not a perspective any registered window claims.";
            if (_refusedPerspectives.Add(newPerspective))
                FdpLog<WindowManager>.Warn(
                    "[WindowManager] Refused to switch to unknown perspective '{0}' — "
                    + "no registered window claims it. Staying on '{1}'. Claimed: [{2}].",
                    newPerspective,
                    CurrentPerspective,
                    string.Join(", ", GetPerspectives()));
            return false;
        }

        var old = CurrentPerspective;
        // ⭐ The ONE place Start is remembered into: every entry funnels through here.
        if (newPerspective == StartPerspectiveName) RememberedPerspective = old;
        CurrentPerspective = newPerspective;
        OnPerspectiveChanged?.Invoke(old, newPerspective);
        return true;
    }

    /// <summary>
    /// ⭐ <c>A0</c> — names already refused, so the warning is logged once each rather than per frame.
    /// </summary>
    private readonly HashSet<string> _refusedPerspectives = new();

    /// <summary>
    /// Returns the distinct <see cref="ManagedWindow.OwningPerspective"/> values of all
    /// <see cref="WindowScope.PerspectiveBound"/> windows, sorted alphabetically.
    /// This is the testable seam for perspective enumeration (§8.1).
    /// </summary>
    public IReadOnlyList<string> GetPerspectives()
    {
        return _windows.Values
            .Where(w => w.Scope == WindowScope.PerspectiveBound)
            .Select(w => w.OwningPerspective)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="perspective"/> is the <see cref="CurrentPerspective"/>.
    /// </summary>
    public bool IsPerspectiveActive(string perspective)
        => perspective == CurrentPerspective;

    /// <summary>
    /// Returns the perspective menu model: a read-only list of
    /// (perspective name, whether it is the current perspective) tuples.
    /// Pure data seam — does not issue ImGui calls (§8.1).
    /// </summary>
    public IReadOnlyList<(string Perspective, bool IsChecked)> BuildPerspectiveMenuModel()
    {
        return GetSelectablePerspectives()
            .Select(p => (p, IsPerspectiveActive(p)))
            .ToList();
    }

    /// <summary>
    /// ⭐ <c>CE-4096</c> — the perspectives a USER may pick (the menu and the toolbar): <see cref="GetPerspectives"/> without
    /// <see cref="StartPerspectiveName"/>, which only the start gate enters. ⛔ <see cref="GetPerspectives"/> itself still lists Start —
    /// <c>A0</c> needs the full claimed set.
    /// </summary>
    public IReadOnlyList<string> GetSelectablePerspectives()
        => GetPerspectives().Where(p => p != StartPerspectiveName).ToList();

    /// <summary>
    /// Selects a perspective via <see cref="SwitchPerspective"/>.
    /// This is the testable dispatch seam; ImGui menu items call it on click (§8.1).
    /// </summary>
    public void SelectPerspective(string perspective)
        => SwitchPerspective(perspective);

    /// <summary>
    /// Directly registers an icon key for a perspective, used by
    /// <c>PerspectiveToolbarSection</c> to render togglable icon buttons.
    /// Takes precedence over the window-scan fallback in <see cref="GetPerspectiveIconKey"/>.
    /// Call from subsystem <c>RegisterWindows</c> for each known perspective.
    /// </summary>
    public void RegisterPerspectiveIconKey(string perspective, string iconKey)
        => _perspectiveIconKeys[perspective] = iconKey;

    /// <summary>
    /// Registers a display label override for <paramref name="perspective"/>.
    /// When set, <see cref="GetPerspectiveLabel"/> returns the label instead of the id.
    /// Used by the Perspective menu (<c>PerspectiveMenuDto</c>, CE-1243) to show a human-readable name while
    /// <see cref="SelectPerspective"/> continues to use the id (§MTB2-T5).
    /// </summary>
    public void RegisterPerspectiveLabel(string perspective, string label)
        => _perspectiveLabels[perspective] = label;

    /// <summary>
    /// Returns the display label for <paramref name="perspective"/> when one was
    /// registered via <see cref="RegisterPerspectiveLabel"/>; otherwise returns
    /// <paramref name="perspective"/> unchanged.
    /// </summary>
    public string GetPerspectiveLabel(string perspective)
        => _perspectiveLabels.TryGetValue(perspective, out var l) ? l : perspective;

    /// <summary>
    /// Returns the icon key for <paramref name="perspective"/>: checks the directly
    /// registered map first, then falls back to scanning <see cref="WindowScope.PerspectiveBound"/>
    /// windows for a non-null <see cref="ManagedWindow.IconKey"/>.
    /// Returns <c>null</c> when neither source yields a key (text-button fallback).
    /// Used by <c>PerspectiveToolbarSection</c> to resolve toolbar faces (§8.1).
    /// </summary>
    public string? GetPerspectiveIconKey(string perspective)
    {
        if (_perspectiveIconKeys.TryGetValue(perspective, out var direct))
            return direct;
        return _windows.Values
            .Where(w => w.Scope == WindowScope.PerspectiveBound
                     && w.OwningPerspective == perspective
                     && w.IconKey != null)
            .Select(w => w.IconKey)
            .FirstOrDefault();
    }

    // �� Message Log Registry ���������������������������������������������������

    /// <summary>
    /// Optional shared <see cref="Fdp.Core.Logging.MessageLogRegistry"/> instance.
    /// Set by the application host (e.g. <c>Program.cs</c>) after creating the
    /// <c>MessageLogWindow</c>; subsystems read it from their
    /// <c>RegisterWindows</c> override to register additional log sources.
    /// </summary>
    public Fdp.Core.Logging.MessageLogRegistry? MessageLogRegistry { get; set; }

    // �� Menu Registry ����������������������������������������������������������

    /// <summary>The global application menu registry.</summary>
    public GlobalMenuRegistry GlobalMenu { get; } = new();

    // �� Status Bar �������������������������������������������������������������

    private readonly StatusBarManager _statusBar = new();

    /// <summary>
    /// The global status bar manager. Subsystems register named sections here.
    /// Height is updated each frame after <see cref="Render"/> is called.
    /// </summary>
    public StatusBarManager StatusBar => _statusBar;

    private readonly MainToolbarManager _mainToolbar = new();

    /// <summary>
    /// The global main toolbar manager. Subsystems register named entries here.
    /// Rendered as a band anchored at the top of the viewport, directly under
    /// the main menu bar. Height is jitter-free (max declared height over all
    /// registered entries, constant across perspective switches).
    /// </summary>
    public MainToolbarManager MainToolbar => _mainToolbar;

    // ── Shell Commands ──────────────────────────────────────────────────────

    private readonly ShellEditorCommands _shellCommands = new();

    /// <summary>
    /// The global shell-level editor command set. Subsystems register their global
    /// editor commands here once at startup (scenario lifecycle, AI-debug stepping,
    /// open-browser, new-asset, etc.). Per-document command sets are separate and unchanged.
    /// </summary>
    public ShellEditorCommands ShellCommands => _shellCommands;

    // �� Settings Persistence ���������������������������������������������������
    //
    // NOTE(DEBT-003): ImGui.NET 1.91.x does not expose ImGuiSettingsHandler or
    // ImGui.AddSettingsHandler via its managed bindings.  A JSON-based fallback
    // is implemented here instead.  When the bindings expose the native hook, the
    // SaveSettings/LoadSettings calls can be wired to the ImGui ini pipeline and
    // this code can be deleted.

    /// <summary>
    /// Serializes the current <see cref="ManagedWindow.IsOpen"/> and
    /// <see cref="ManagedWindow.IsPinned"/> state of all registered windows to the
    /// ImGui ini-section format used by the settings handler write callback.
    /// One line per window: <c>{id}={IsOpen},{IsPinned}</c>.
    /// </summary>
    internal string SerializeToIniSection()
    {
        var sb = new StringBuilder();
        foreach (var (id, win) in _windows)
        {
            if (win.IsVolatile) continue;
            sb.AppendLine($"{id}={win.IsOpen},{win.IsPinned}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Restores window state from the ini-section text produced by
    /// <see cref="SerializeToIniSection"/>.  Malformed lines and unknown window
    /// ids are silently skipped.  Windows registered after this call are not
    /// affected.
    /// </summary>
    internal void DeserializeFromIniSection(string data)
    {
        foreach (var raw in data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;          // malformed � no '='

            var key   = line[..eq];
            var value = line[(eq + 1)..];

            var parts = value.Split(',');
            if (parts.Length != 2) continue; // malformed � wrong value format

            if (!bool.TryParse(parts[0].Trim(), out var isOpen))  continue;
            if (!bool.TryParse(parts[1].Trim(), out var isPinned)) continue;

            if (_windows.TryGetValue(key, out var win))
            {
                win.IsOpen   = isOpen;
                win.IsPinned = isPinned;
            }
            // unknown id � silently skip
        }
    }

    /// <summary>
    /// Persists all window states and the current perspective to <paramref name="filePath"/> as JSON.
    /// When <paramref name="filePath"/> is <c>null</c> the default path
    /// (<c>fdp_windows.json</c> next to the executable) is used.
    /// </summary>
    public void SaveSettings(string? filePath = null)
    {
        filePath ??= DefaultSettingsPath;
        var state = new WindowManagerSettings(
            // ⭐ CE-4094 — Start is a transient resting view, never a layout to restore at the next boot (D16).
            IsStartShowing ? RememberedPerspective ?? CurrentPerspective : CurrentPerspective,
            UiScale,
            new Dictionary<string, WindowState>(
                _windows
                    .Where(kv => !kv.Value.IsVolatile)
                    .Select(kv => KeyValuePair.Create(
                        kv.Key,
                        new WindowState(kv.Value.IsOpen, kv.Value.IsPinned)))));
        var json = JsonSerializer.Serialize(state, FdpJsonOptionsRegistry.Indented);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Restores window states from the JSON file at <paramref name="filePath"/> and returns the
    /// persisted active perspective name (or <c>null</c> when not recorded or file absent).
    /// </summary>
    public string? LoadSettings(string? filePath = null)
    {
        filePath ??= DefaultSettingsPath;
        if (!File.Exists(filePath)) return null;

        var json = File.ReadAllText(filePath);

        // Try new schema first (WindowManagerSettings with ActivePerspective).
        try
        {
            var settings = JsonSerializer.Deserialize<WindowManagerSettings>(json, FdpJsonOptionsRegistry.DefaultRelaxed);
            if (settings != null)
            {
                foreach (var (id, ws) in settings.Windows)
                {
                    if (_windows.TryGetValue(id, out var win))
                    {
                        win.IsOpen   = ws.IsOpen;
                        win.IsPinned = ws.IsPinned;
                    }
                }
                if (settings.UiScale > 0f)
                    UiScale = System.Math.Clamp(settings.UiScale, 0.5f, 3.0f);
                return string.IsNullOrEmpty(settings.ActivePerspective) ? null : settings.ActivePerspective;
            }
        }
        catch { /* fall through to legacy format */ }

        // Legacy format: plain Dictionary<string, WindowState>.
        try
        {
            var legacy = JsonSerializer.Deserialize<Dictionary<string, WindowState>>(json, FdpJsonOptionsRegistry.DefaultRelaxed);
            if (legacy != null)
            {
                foreach (var (id, ws) in legacy)
                {
                    if (_windows.TryGetValue(id, out var win))
                    {
                        win.IsOpen   = ws.IsOpen;
                        win.IsPinned = ws.IsPinned;
                    }
                }
            }
        }
        catch { /* ignore malformed settings */ }

        return null;
    }

    private static string DefaultSettingsPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fdp_windows.json");

    private record WindowState(bool IsOpen, bool IsPinned);
    private record WindowManagerSettings(string ActivePerspective, float UiScale, Dictionary<string, WindowState> Windows);

    // �� Render �����������������������������������������������������������������

    /// <summary>
    /// Renders the global menu bar and all registered windows for the current frame.
    /// Must be called inside an active ImGui frame (between <c>NewFrame</c> and <c>Render</c>).
    /// </summary>
    /// ⭐ CE-1243 — a host's gizmo menus reach the bar as a CONTRIBUTOR (GlobalMenuRegistry.RegisterContributor); ⛔ SUPERSEDED: the
    ///   gizmoMenuItems / onGizmoMenuAction parameters, which no host ever passed (UXI-13).
    /// <summary>
    /// ⭐ CE-4038 B9 (docs/DESIGN_Film_Director.md D-7) — the film director's CINEMA mode: no menu bar, no panels, no status bar, so the map
    /// fills the window (and a frame costs only the map); the frame overlays still draw (the director's own captions and cards are one).
    /// Set and cleared by the director only.
    /// </summary>
    public bool Cinema { get; set; }

    public void Render()
    {
        if (Cinema)
        {
            foreach (var overlay in _frameOverlays.ToList())
                overlay();
            return;
        }

        // BATCH-26: Make the menu bar tall enough for 32px toolbar icons.
        // ImGui's menu-bar height = FontSize + FramePadding.Y*2, and the inline
        // toolbar icons size to ImGui.GetFrameHeight() (see ToolbarCommandAdapter /
        // PerspectiveToolbarSection / MainToolbarTimeControlSection). Pushing a
        // larger FramePadding.Y BEFORE BeginMainMenuBar therefore scales BOTH the
        // bar and the icons together — the icons fill the taller bar exactly (the
        // IconWidgets draw at exactly `size` via InvisibleButton, no extra padding),
        // and the menu labels stay the same font, just vertically centered with
        // roomier — easier to click — hit areas. The dockspace shrinks automatically
        // because Program.cs uses the viewport WorkPos/WorkSize, which ImGui insets
        // by the (now taller) menu bar. No other call sites change.
        var style = Gui.GetStyle();
        var savedFramePadding = style.FramePadding;
        float targetPadY = System.Math.Max(
            savedFramePadding.Y,
            (MainMenuBarTargetHeight - Gui.GetFontSize()) * 0.5f);
        Gui.PushStyleVar(
            ImGuiNET.ImGuiStyleVar.FramePadding,
            new System.Numerics.Vector2(savedFramePadding.X, targetPadY));

        if (Gui.BeginMainMenuBar())
        {
            // ⭐⭐⭐ UXI-05 item ⑤ — PUBLISH THE MENU MODEL, for exactly the reason the toolbar's
            //    PublishSnapshot sits outside its draw guard (see below): a host that registers NO menu
            //    items must be distinguishable from a host whose menu was never instrumented.
            // ⭐⭐ CE-1243 — ONE walk of ONE tree: the registered items and every contributor's menus (asked ONCE this frame — a
            //    contributor may consume what it returns), merged by top-level name, in CE-1026's bar order.
            var contributions = GlobalMenu.CollectContributions(CurrentPerspective);
            GlobalMenu.PublishSnapshot(CurrentPerspective, contributions);
            RenderMenuBar(contributions);

            // BATCH-25: Render the main toolbar inline inside the menu bar,
            // to the right of the menus. Graphical separators are drawn by
            // MainToolbarManager.DrawSeparator (registered via RegisterSeparator);
            // no ImGui.Separator() pipe character is added here.
            // ⭐⭐⭐ cgf==editor slice 2 (CE-016) — PUBLISH THE TOOLBAR MODEL EVERY FRAME, ⛔ outside the
            //    draw guard. 📐 Measured: only EditorSubsystem registers entries, so on a cluster host
            //    Height is 0 and the guard below skips the render — which would have left `main-toolbar`
            //    unpublished and made "this host offers no toolbar entries" look identical to "nobody
            //    instrumented the toolbar". ⭐ See PublishSnapshot's own remarks for why this is a
            //    deliberate departure from the usual publish-inside-the-draw rule.
            _mainToolbar.PublishSnapshot(CurrentPerspective);

            if (_mainToolbar.Height > 0f && !IsStartShowing)   // ⭐ CE-4096: Start shows no toolbar (no panels, only the Idle-allowed menu items)
                _mainToolbar.RenderInline(CurrentPerspective);

            // ⭐ CE-4085: Help (the right-aligned menu) is drawn LAST, flush with the bar's right edge, after the toolbar.
            RenderMenuBar(contributions, rightAligned: true);

            Gui.EndMainMenuBar();
        }

        // Restore frame padding for everything rendered after the menu bar
        // (popups/modals below must use the normal style).
        Gui.PopStyleVar();

        // Popups must be opened outside BeginMainMenuBar.
        if (_openAboutModal)
        {
            Gui.OpenPopup("About HROT##Modal");
            _openAboutModal = false;
        }

        bool modalOpen = true;
        if (Gui.BeginPopupModal("About HROT##Modal", ref modalOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (Gui.IsKeyPressed(ImGuiNET.ImGuiKey.Escape)) Gui.CloseCurrentPopup();
            RenderAboutModalContent();
            if (!modalOpen)
                Gui.CloseCurrentPopup();
        }

        if (_openSettingsModal)
        {
            Gui.OpenPopup("Settings##Modal");
            _openSettingsModal = false;
        }

        bool settingsOpen = true;
        if (Gui.BeginPopupModal("Settings##Modal", ref settingsOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (Gui.IsKeyPressed(ImGuiNET.ImGuiKey.Escape)) Gui.CloseCurrentPopup();
            RenderSettingsModalContent();
            if (!settingsOpen)
                Gui.CloseCurrentPopup();
        }

        // Iterate a copy to allow safe removal of closed volatile windows.
        foreach (var win in _windows.Values.ToList())
        {
            if (win.IsVolatile && !win.IsOpen)
            {
                _windows.Remove(win.Id);
                continue;
            }

            // ⭐⭐ CE-4096 (AQ108 D16) — "no panels": Global-scope and pinned windows draw in EVERY perspective (ManagedWindow.Render's gate), so
            //    Start must hide them explicitly. Measured: ManagedWindow.Render shows `Scope == Global || IsPinned || owner == current`.
            //    ⭐ A VOLATILE window is a dialog spawned on demand (a file picker, a confirm) and still draws.
            if (IsStartShowing && !win.IsVolatile && win.OwningPerspective != StartPerspectiveName) continue;

            win.Render(CurrentPerspective, _atlas);
        }

        // BATCH-25: Toolbar is now rendered inline inside BeginMainMenuBar
        // (see RenderInline call above). No separate band here.
        _statusBar.Render(CurrentPerspective);

        // Draw file dialog service last so the modal overlays all other windows.
        (_fileDialogService as ImGuiFileDialogService)?.Draw();

        // Registered frame overlays share that same final slot, for the same reason. Iterate a copy
        // so an overlay that registers another one (or unregisters itself) cannot invalidate this
        // enumeration mid-frame.
        //
        // NOTE: the file dialog above is deliberately NOT moved onto this list. It is a behaviour
        // change in another subsystem — its draw is conditional on a service cast — and nothing in
        // this batch needs it. Folding it in is a follow-up, not a side effect.
        foreach (var overlay in _frameOverlays.ToList())
            overlay();
    }

    /// <summary>
    /// Registers the file dialog service to be drawn each frame AFTER all other windows.
    /// </summary>
    public void SetFileDialogService(IFileDialogService service)
    {
        _fileDialogService = service;
    }

    // �� Private render helpers �������������������������������������������������

    /// <summary>
    /// ⭐⭐ <c>CE-1243</c> MB-3 — the bar: every top-level menu (<see cref="GlobalMenuRegistry.BarMenus"/>) as ONE menu, its tree items
    /// first, then each contributor's items under the same label. Rows are <see cref="GizmoMap.Presentation.MenuRows"/>'.
    /// </summary>
    private void RenderMenuBar(IReadOnlyList<MenuContribution> contributions, bool rightAligned = false)
    {
        var bar = GlobalMenu.BarMenus(contributions);
        bool reserveTop = MenuIcons != null && bar.Any(m => !string.IsNullOrEmpty(TopIcon(m)));
        foreach (var menu in bar)
        {
            // ⭐ CE-4085: two passes over the ONE bar — the left-flowing menus, then (after the toolbar) the right-aligned one (Help).
            if (menu.RightAligned != rightAligned) continue;
            if (rightAligned)
            {
                // Flush with the bar's right edge, but never over what is already drawn (a narrow window keeps the flow position).
                float width = (menu.IconOnly ? IconOnlyLabelWidth() : Gui.CalcTextSize(menu.Name).X + Gui.GetStyle().FramePadding.X * 2f
                              + (MenuIcons != null && !string.IsNullOrEmpty(TopIcon(menu)) ? Gui.GetFrameHeight() : 0f));
                float x = System.Math.Max(Gui.GetCursorPosX(), Gui.GetWindowWidth() - Gui.GetStyle().WindowPadding.X - width);
                Gui.SameLine();
                Gui.SetCursorPosX(x);
            }
            bool treeShows = menu.Node is { } n && (n.Children.Count > 0 ? HasVisibleDescendant(n) : GlobalMenu.Offers(n, CurrentPerspective));
            if (!treeShows && menu.Parts.Count == 0) continue;   // ⛔ UXI-05: no dead headers

            bool isLeaf = (menu.Node == null || menu.Node.Children.Count == 0)
                          && menu.Parts.All(p => p.Item.Children is not { Length: > 0 });
            if (isLeaf)
            {
                if (treeShows) RenderTreeRow(menu.Node!, reserveTop);
                foreach (var (contributor, item) in menu.Parts)
                    GizmoMap.Presentation.MenuRows.Dto(item, contributor.OnAction, MenuIcons, reserveTop);
                continue;
            }

            // ⭐ CE-4114: an icon-only menu (Help) has an EMPTY visible label — "##Name" keeps the ImGui id; the icon fills the gutter.
            if (!GizmoMap.Presentation.MenuRows.Submenu(menu.DrawLabel, TopIcon(menu), true, MenuIcons, reserveTop || menu.IconOnly)) continue;
            bool reserve = MenuIcons != null
                && ((menu.Node?.Children.Values.Any(c => !string.IsNullOrEmpty(c.Icon)) ?? false)
                    || menu.Parts.Any(p => p.Item.Children is { } cs && GizmoMap.Presentation.MenuIconRenderer.AnyHasIcon(cs)));
            if (treeShows) RenderTreeChildren(menu.Node!, reserve);
            foreach (var (contributor, item) in menu.Parts)
                foreach (var child in item.Children ?? Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>())
                    GizmoMap.Presentation.MenuRows.Dto(child, contributor.OnAction, MenuIcons, reserve);
            Gui.EndMenu();
        }
    }

    /// <summary>⭐ CE-4114 — the width of an icon-only top-level entry: the icon gutter plus the frame padding.</summary>
    private static float IconOnlyLabelWidth()
    {
        GizmoMap.Presentation.MenuIconRenderer.Pad("", out float gutter);
        return gutter + Gui.GetStyle().FramePadding.X * 2f;
    }

    private static string? TopIcon(BarMenu menu)
        => !string.IsNullOrEmpty(menu.Node?.Icon) ? menu.Node!.Icon : menu.Parts.Select(p => p.Item.Icon).FirstOrDefault(i => !string.IsNullOrEmpty(i));

    /// <summary>The children of a menu-tree node (one level, recursing into submenus); one icon gutter per level.</summary>
    private void RenderTreeChildren(MenuItemNode node, bool reserve)
    {
        foreach (var child in node.OrderedChildren())   // ⭐ CE-1443: by Order, then registration order
            RenderTreeRow(child, reserve);
    }

    /// <summary>One row of the menu tree: a separator, a submenu, a checkable item or an action item (UXI-05: this perspective's
    /// binding, else the global one, else not drawn).</summary>
    private void RenderTreeRow(MenuItemNode child, bool reserve)
    {
        if (child.IsSeparator)
        {
            // ⭐ UXI-05: a separator scoped to another perspective is skipped with its group.
            if (child.ResolveBinding(CurrentPerspective) == null || GlobalMenu.IdleOnly || !(child.GetVisible?.Invoke() ?? true)) return;   // ⭐ CE-4096: no separators in the Idle-only bar; ⭐ CE-1443: a separator of a hidden group is hidden with it
            Gui.Separator();
            return;
        }

        if (child.Children.Count > 0)
        {
            // ⛔⛔ UXI-05's named RISK: a submenu whose every leaf is filtered away would still draw its header. 📄 design §3 ②.
            if (!HasVisibleDescendant(child)) return;
            if (GizmoMap.Presentation.MenuRows.Submenu(child.Name, child.Icon, true, MenuIcons, reserve))
            {
                bool childReserve = MenuIcons != null && child.Children.Values.Any(c => !string.IsNullOrEmpty(c.Icon));
                RenderTreeChildren(child, childReserve);
                Gui.EndMenu();
            }
            return;
        }

        // ⚠ Read from CurrentPerspective at DRAW time, never cached — the whole feature is that it changes when focus does.
        var binding = child.ResolveBinding(CurrentPerspective);
        if (binding == null || !GlobalMenu.Offers(child, CurrentPerspective)) return;   // ⭐ CE-4096: Offers = the binding AND (in Start) allowed-in-Idle
        bool enabled = child.GetEnabled?.Invoke() ?? true;

        if (binding.GetChecked != null && binding.OnCheckedChanged != null)
        {
            bool checkedState = binding.GetChecked();
            if (GizmoMap.Presentation.MenuRows.Item(child.ResolveLabel(), child.Icon, child.Shortcut, checkedState, enabled, MenuIcons, reserve))
                binding.OnCheckedChanged(!checkedState);
            return;
        }

        if (binding.OnClick != null
            && GizmoMap.Presentation.MenuRows.Item(child.ResolveLabel(), child.Icon, child.Shortcut, null, enabled, MenuIcons, reserve))
            binding.OnClick();
    }

    /// <summary>
    /// ⭐⭐ <b><c>UXI-05</c> — does any leaf under <paramref name="node"/> resolve a binding for the
    /// current perspective?</b>
    ///
    /// <para>⛔⛔ Without this, a submenu whose every leaf is filtered away still draws its header — a
    /// DEAD HEADER that opens onto an empty popup. 📄 UXI-05 names it as the risk of the model; the
    /// design's §3 ② makes the skip mandatory.</para>
    ///
    /// <para>⚠ Recursive and per-frame. ⭐ Cheap: the trie is a handful of nodes deep and a few dozen
    /// wide, and it short-circuits on the first visible descendant.</para>
    /// </summary>
    internal bool HasVisibleDescendant(MenuItemNode node)
    {
        foreach (var child in node.Children.Values)
        {
            if (child.IsSeparator) continue;   // ⛔ a separator alone never justifies a submenu

            if (child.Children.Count > 0)
            {
                if (HasVisibleDescendant(child)) return true;
                continue;
            }

            if (GlobalMenu.Offers(child, CurrentPerspective)) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a stable negative action id for a window id.
    /// </summary>
    private int GetWindowActionId(string windowId)
    {
        int idx = _windowToggleMap.IndexOf(windowId);
        if (idx < 0)
        {
            _windowToggleMap.Add(windowId);
            idx = _windowToggleMap.Count - 1;
        }
        return -100 - idx;
    }
    private void HandleHostMenuAction(int actionId)
    {
        if (actionId == ActionAbout)
        {
            _openAboutModal = true;
            return;
        }
        // ⚠ VC-2: no DTO produces ActionSettings any more — the Settings item is a GlobalMenu entry,
        //   which calls the same flag directly. ⛔ NOT deleted on my own initiative: it is DORMANT, not
        //   harmful (a unique id that overwrites nothing), and 📌 "unreferenced is not unintentional" —
        //   the three-way call is the user's. A host wanting a DTO-driven Settings item still works.
        if (actionId == ActionSettings)
        {
            _openSettingsModal = true;
            return;
        }
        if (actionId <= -100)
        {
            int idx = -(actionId + 100);
            if (idx >= 0 && idx < _windowToggleMap.Count)
            {
                if (_windows.TryGetValue(_windowToggleMap[idx], out var win))
                    win.IsOpen = !win.IsOpen;
            }
        }
    }
    /// <summary>
    /// ⭐⭐ <b><c>VC-2</c> — the TOP-LEVEL labels the DTO half of the menu bar contributes.</b>
    ///
    /// <para>⭐ A RAIL SURFACE, and a narrow one on purpose: the bar is drawn by two independent
    /// models, so <i>"is there exactly one <c>Settings</c> menu?"</i> cannot be answered from
    /// <c>GlobalMenu</c> alone. ⛔ Exposing the DTOs themselves would invite rails about their shape;
    /// ⭐ the labels are the only part another model can COLLIDE with.</para>
    /// </summary>
    public IReadOnlyList<string> HostMenuLabelsForRail()
        => new[] { PerspectiveMenuDto(), WindowsMenuDto(), HelpMenuDto() }.Select(m => m.Label ?? "").ToList();   // ⭐ CE-1243: the shell's own contributors

    /// <summary>⭐ CE-1243 — the Windows menu: perspective-bound windows grouped by perspective, then the global ones (win-mgr-1 §4.4.2).</summary>
    private Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto WindowsMenuDto()
    {
        var winChildren = new List<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>();
        var perspectiveGroups = _windows.Values
            .Where(w => w.Scope == WindowScope.PerspectiveBound && w.ShowInMenu)
            .GroupBy(w => w.OwningPerspective)
            .OrderBy(g => g.Key);
        foreach (var group in perspectiveGroups)
        {
            winChildren.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
            {
                Label = group.Key,
                Children = group.Select(w => new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                {
                    Id = GetWindowActionId(w.Id), Label = w.Title, IsChecked = w.IsOpen
                }).ToArray()
            });
        }
        var globalWindows = _windows.Values.Where(w => w.Scope == WindowScope.Global && w.ShowInMenu).ToList();
        if (globalWindows.Count > 0)
        {
            winChildren.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
            {
                Label = "Global",
                Children = globalWindows.Select(w => new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                {
                    Id = GetWindowActionId(w.Id), Label = w.Title, IsChecked = w.IsOpen
                }).ToArray()
            });
        }
        return new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "View",   // ⭐ CE-4114: a submenu of the top-level View (merged into the one View menu by BarMenus), like Perspective
            Children = winChildren.Count == 0 ? Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>() : new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                {
                    Label = GlobalMenuRegistry.WindowsMenu, Priority = 90, Icon = "folder", Children = winChildren.ToArray()
                }
            }
        };
    }

    /// <summary>⭐ CE-4114 — the silk atlas key of the Help icon (the famfamfam question mark; see SilkIconProvider's <c>help</c>).</summary>
    public const string HelpIconKey = "help";

    /// <summary>⭐ CE-1243 — the Help menu: About. ⭐ CE-1332 (🔒 user, 2026-10-25: <i>"leaving just the About in the Help menu … if the
    /// Window → Global is same as the debug windows then lets remove the debug field from the View also"</i>) — the global (debug) windows are
    /// listed once, under Windows → Global. ⛔ SUPERSEDED: Help → Debug, and the View → Debug it briefly moved to.</summary>
    private Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto HelpMenuDto()
        => new()
        {
            Label = "Help",
            Priority = 100,
            Icon = HelpIconKey,   // ⭐ CE-4114: the bar draws ONLY this icon (the silk question mark), no "Help" text
            Children = new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = ActionAbout, Label = "About", Icon = "status/info", AllowedInIdle = true }   // ⭐ CE-4096: Help stays in Idle
            }
        };

    /// <summary>⭐ A menu with nothing under it is not offered (⛔ a dead header, UXI-05).</summary>
    private static IReadOnlyList<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto> OnlyIfAny(
        Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto menu)
        => menu.Children is { Length: > 0 } ? new[] { menu } : Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>();

    private IReadOnlyList<(string Perspective, bool IsChecked)> _perspectiveMenuModel = Array.Empty<(string, bool)>();
    private const int PerspectiveActionBase = -1000;

    /// <summary>⭐ CE-1243 — the Perspective menu: each perspective, checked when active, with its icon (§8.1); a click selects it.</summary>
    private Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto PerspectiveMenuDto()
    {
        _perspectiveMenuModel = BuildPerspectiveMenuModel();
        // ⭐ CE-4085: a "Perspective" SUBMENU contributed under the top-level "View" (merged into the one View menu by BarMenus).
        //    An empty switcher contributes nothing (no dead header) — see OnlyIfAny.
        var submenu = new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = GlobalMenuRegistry.PerspectiveMenu,
            Children = _perspectiveMenuModel.Select((e, i) => new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
            {
                Id = PerspectiveActionBase - i, Label = GetPerspectiveLabel(e.Perspective), IsChecked = e.IsChecked,
                Icon = GetPerspectiveIconKey(e.Perspective),
            }).ToArray(),
        };
        return new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "View",
            Children = submenu.Children is { Length: > 0 } ? new[] { submenu } : Array.Empty<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>(),
        };
    }

    private void OnPerspectiveMenuAction(int id)
    {
        int idx = PerspectiveActionBase - id;
        if (idx >= 0 && idx < _perspectiveMenuModel.Count) SelectPerspective(_perspectiveMenuModel[idx].Perspective);
    }

    private void RenderAboutModalContent()
    {
        Gui.Text("Loaded Subsystems and Assemblies");
        Gui.Separator();

        if (Gui.BeginTable("##AssembliesTable", 3,
            ImGuiNET.ImGuiTableFlags.Borders |
            ImGuiNET.ImGuiTableFlags.RowBg |
            ImGuiNET.ImGuiTableFlags.SizingFixedFit |
            ImGuiNET.ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 400)))
        {
            Gui.TableSetupScrollFreeze(0, 1);
            Gui.TableSetupColumn("Assembly Name");
            Gui.TableSetupColumn("Version");
            Gui.TableSetupColumn("Informational Version (Git SHA)");
            Gui.TableHeadersRow();

            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    var name = a.GetName().Name;
                    return name != null && (name.StartsWith("Hrot") || name.StartsWith("FDP") ||
                           name.StartsWith("Fdp") || name.StartsWith("ModuleHost") ||
                           name.StartsWith("CycloneDDS"));
                })
                .OrderBy(a => a.GetName().Name);

            foreach (var asm in assemblies)
            {
                Gui.TableNextRow();

                Gui.TableNextColumn();
                Gui.TextUnformatted(asm.GetName().Name ?? string.Empty);

                Gui.TableNextColumn();
                Gui.TextUnformatted(asm.GetName().Version?.ToString() ?? string.Empty);

                Gui.TableNextColumn();
                var infoAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)
                    System.Attribute.GetCustomAttribute(asm,
                        typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                Gui.TextUnformatted(infoAttr?.InformationalVersion ?? "N/A");
            }

            Gui.EndTable();
        }

        Gui.Spacing();
        Gui.Separator();

        if (Gui.Button("Close", new System.Numerics.Vector2(120, 0)))
            Gui.CloseCurrentPopup();

        Gui.EndPopup();
    }

    // Working buffer for the UI-scale slider (percent). Applied on release, not per-frame,
    // so dragging does not rebake the font atlas on every frame.
    private float _uiScalePercentDraft = -1f;

    private void RenderSettingsModalContent()
    {
        Gui.Text("Editor UI Scale");
        Gui.Separator();

        float dpi = FontService?.DpiScale ?? 1f;
        if (dpi > 1.001f)
            Gui.TextDisabled($"Auto monitor DPI: {dpi * 100f:F0}% (multiplied by the scale below)");
        Gui.TextDisabled("Scales all editor fonts and widget spacing. Fonts re-bake crisply on change.");
        Gui.Spacing();

        // Initialise the draft from the committed value when the modal (re)opens.
        if (_uiScalePercentDraft < 0f)
            _uiScalePercentDraft = UiScale * 100f;

        // Preset buttons apply immediately.
        int[] presets = { 100, 125, 150, 175, 200 };
        for (int i = 0; i < presets.Length; i++)
        {
            if (i > 0) Gui.SameLine();
            bool active = System.Math.Abs(UiScale * 100f - presets[i]) < 0.5f;
            if (active) Gui.PushStyleColor(ImGuiNET.ImGuiCol.Button, new System.Numerics.Vector4(0.26f, 0.59f, 0.98f, 1f));
            if (Gui.Button($"{presets[i]}%##uiscale"))
                ApplyUiScale(presets[i] / 100f);
            if (active) Gui.PopStyleColor();
        }

        Gui.Spacing();

        // Fine slider — apply only on release to avoid per-frame atlas rebakes.
        Gui.SetNextItemWidth(260f);
        Gui.SliderFloat("##uiscale_slider", ref _uiScalePercentDraft, 75f, 200f, "%.0f%%");
        if (Gui.IsItemDeactivatedAfterEdit())
            ApplyUiScale(_uiScalePercentDraft / 100f);

        Gui.Spacing();
        Gui.Separator();
        if (Gui.Button("Reset to 100%", new System.Numerics.Vector2(140, 0)))
            ApplyUiScale(1f);
        Gui.SameLine();
        if (Gui.Button("Close", new System.Numerics.Vector2(120, 0)))
        {
            _uiScalePercentDraft = -1f; // reset draft so it re-syncs next open
            Gui.CloseCurrentPopup();
        }

        // REQUIRED: BeginPopupModal returned true, so the popup must be closed with EndPopup
        // (mirrors RenderAboutModalContent). Omitting it corrupts ImGui's stack → native crash.
        Gui.EndPopup();
    }

    /// <summary>Commit a new UI-scale multiplier: persists it and drives the live font rebake.</summary>
    private void ApplyUiScale(float multiplier)
    {
        multiplier = System.Math.Clamp(multiplier, 0.5f, 3.0f);
        UiScale = multiplier;
        _uiScalePercentDraft = multiplier * 100f;
        FontService?.SetUserScale(multiplier);
    }
}

