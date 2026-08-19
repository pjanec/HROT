using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels;

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
    }

    /// <summary>The icon atlas supplied at construction time.</summary>
    public IconAtlas Atlas => _atlas;

    // �� Registration �����������������������������������������������������������

    /// <summary>
    /// Registers a window with this manager, keyed by <see cref="ManagedWindow.Id"/>.
    /// Re-registering the same Id overwrites the previous entry.
    /// </summary>
    public void RegisterWindow(ManagedWindow window)
    {
        _windows[window.Id] = window;
    }

    /// <summary>
    /// Attempts to retrieve a registered window by id.
    /// </summary>
    /// <param name="id">The window id to look up.</param>
    /// <param name="window">The found window, or <c>null</c> when not found.</param>
    /// <returns><c>true</c> if a window with <paramref name="id"/> is registered; otherwise <c>false</c>.</returns>
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
    /// </summary>
    public void SwitchPerspective(string newPerspective)
    {
        if (newPerspective == CurrentPerspective) return;

        var old = CurrentPerspective;
        CurrentPerspective = newPerspective;
        OnPerspectiveChanged?.Invoke(old, newPerspective);
    }

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
        return GetPerspectives()
            .Select(p => (p, IsPerspectiveActive(p)))
            .ToList();
    }

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
    /// Used by <see cref="RenderPerspectiveMenu"/> to show a human-readable name while
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
            CurrentPerspective,
            UiScale,
            new Dictionary<string, WindowState>(
                _windows
                    .Where(kv => !kv.Value.IsVolatile)
                    .Select(kv => KeyValuePair.Create(
                        kv.Key,
                        new WindowState(kv.Value.IsOpen, kv.Value.IsPinned)))));
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
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
            var settings = JsonSerializer.Deserialize<WindowManagerSettings>(json);
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
            var legacy = JsonSerializer.Deserialize<Dictionary<string, WindowState>>(json);
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
    /// <param name="gizmoMenuItems">
    /// Optional list of gizmo-contributed menu items to append inside the main menu bar.
    /// Pass the result of <c>DebugGizmoLayer.ConsumeMainMenu()</c> here when available.
    /// </param>
    /// <param name="onGizmoMenuAction">
    /// Callback invoked with the action id when the operator clicks a gizmo-contributed menu item.
    /// </param>
    public void Render(
        IReadOnlyList<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>? gizmoMenuItems = null,
        Action<int>? onGizmoMenuAction = null)
    {
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
            RenderGlobalMenu(GlobalMenu.Root);
            RenderPerspectiveMenu();
            var hostMenus = BuildHostMenuDtos();
            GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(hostMenus, HandleHostMenuAction, MenuIcons);

            if (gizmoMenuItems != null && gizmoMenuItems.Count > 0)
                GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenuItems, onGizmoMenuAction, MenuIcons);

            // BATCH-25: Render the main toolbar inline inside the menu bar,
            // to the right of the menus. Graphical separators are drawn by
            // MainToolbarManager.DrawSeparator (registered via RegisterSeparator);
            // no ImGui.Separator() pipe character is added here.
            if (_mainToolbar.Height > 0f)
                _mainToolbar.RenderInline(CurrentPerspective);

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
    /// Recursively renders the trie rooted at <paramref name="node"/>.
    /// The root itself is not rendered � only its children are iterated.
    /// </summary>
    private void RenderGlobalMenu(MenuItemNode node)
    {
        // Reserve an aligned icon gutter for this whole level if any sibling carries an icon.
        bool reserve = MenuIcons != null;
        if (reserve)
        {
            reserve = false;
            foreach (var c in node.Children.Values)
                if (!string.IsNullOrEmpty(c.Icon)) { reserve = true; break; }
        }

        foreach (var child in node.Children.Values)
        {
            if (child.IsSeparator)
            {
                Gui.Separator();
                continue;
            }

            var p0 = Gui.GetCursorScreenPos();
            float gutter = 0f;

            if (child.Children.Count > 0)
            {
                string subLabel = reserve
                    ? GizmoMap.Presentation.MenuIconRenderer.Pad(child.Name, out gutter)
                    : child.Name;
                bool open = Gui.BeginMenu(subLabel);
                GizmoMap.Presentation.MenuIconRenderer.DrawIcon(MenuIcons, child.Icon, p0, gutter);
                if (open)
                {
                    RenderGlobalMenu(child);
                    Gui.EndMenu();
                }
                continue;
            }

            string label = reserve
                ? GizmoMap.Presentation.MenuIconRenderer.Pad(child.ResolveLabel(), out gutter)
                : child.ResolveLabel();

            // Leaf: checkable item.
            if (child.GetCheckedState != null && child.OnCheckedChanged != null)
            {
                bool checkedState = child.GetCheckedState();
                bool enabled = child.GetEnabled?.Invoke() ?? true;
                bool clicked = Gui.MenuItem(label, child.Shortcut ?? "", ref checkedState, enabled);
                GizmoMap.Presentation.MenuIconRenderer.DrawIcon(MenuIcons, child.Icon, p0, gutter);
                if (clicked)
                {
                    child.OnCheckedChanged(checkedState);
                }
                continue;
            }

            // Leaf: plain action item.
            if (child.OnClick != null)
            {
                bool enabled = child.GetEnabled?.Invoke() ?? true;
                bool clicked = Gui.MenuItem(label, child.Shortcut ?? "", false, enabled);
                GizmoMap.Presentation.MenuIconRenderer.DrawIcon(MenuIcons, child.Icon, p0, gutter);
                if (clicked)
                {
                    child.OnClick();
                }
            }
        }
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
    private IReadOnlyList<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto> BuildHostMenuDtos()
    {
        var menus = new List<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto>();
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
        menus.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "Windows",
            Priority = 90,
            Icon = "folder",
            Children = winChildren.ToArray()
        });
        menus.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "Settings",
            Priority = 95,
            Icon = "asset/utility",
            Children = new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                { Id = ActionSettings, Label = "UI Scale & Fonts…", Icon = "asset/utility" }
            }
        });
        menus.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "Help",
            Priority = 100,
            Icon = "status/info",
            Children = new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                {
                    Label = "Debug",
                    Children = _windows.Values.Where(w => w.Scope == WindowScope.Global && w.ShowInMenu)
                        .Select(w => new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
                        {
                            Id = GetWindowActionId(w.Id), Label = w.Title, IsChecked = w.IsOpen
                        }).ToArray()
                },
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = ActionAbout, Label = "About", Icon = "status/info" }
            }
        });
        return menus;
    }

    /// <summary>
    /// Renders the top-level "Perspective" menu inside the main menu bar.
    /// Each entry is checkable (checked = active) and selects that perspective on click.
    /// Supersedes the former <c>RenderPerspectiveSwitcher</c> inline buttons (§8.1).
    /// </summary>
    private void RenderPerspectiveMenu()
    {
        if (Gui.BeginMenu("Perspective"))
        {
            var model = BuildPerspectiveMenuModel();
            // Reserve the icon gutter if any perspective has a registered icon key.
            bool reserve = false;
            if (MenuIcons != null)
                foreach (var (p, _) in model)
                    if (!string.IsNullOrEmpty(GetPerspectiveIconKey(p))) { reserve = true; break; }

            foreach (var (perspective, isChecked) in model)
            {
                bool isCheckedCopy = isChecked;
                var p0 = Gui.GetCursorScreenPos();
                float gutter = 0f;
                string label = reserve
                    ? GizmoMap.Presentation.MenuIconRenderer.Pad(GetPerspectiveLabel(perspective), out gutter)
                    : GetPerspectiveLabel(perspective);

                bool clicked = Gui.MenuItem(label, "", ref isCheckedCopy);
                GizmoMap.Presentation.MenuIconRenderer.DrawIcon(MenuIcons, GetPerspectiveIconKey(perspective), p0, gutter);
                if (clicked)
                {
                    SelectPerspective(perspective);
                }
            }
            Gui.EndMenu();
        }
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

