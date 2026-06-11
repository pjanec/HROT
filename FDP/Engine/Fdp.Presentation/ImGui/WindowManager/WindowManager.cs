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
    private readonly IconAtlas _atlas;
    private const int ActionAbout = -1;
    private readonly List<string> _windowToggleMap = new();
    private bool _openAboutModal;
    private IFileDialogService? _fileDialogService;

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
    /// Returns the first non-null <see cref="ManagedWindow.IconKey"/> among the
    /// <see cref="WindowScope.PerspectiveBound"/> windows owned by <paramref name="perspective"/>,
    /// or <c>null</c> when no window in that perspective carries an icon key.
    /// Used by <c>PerspectiveToolbarSection</c> to resolve toolbar faces (§8.1).
    /// </summary>
    public string? GetPerspectiveIconKey(string perspective)
    {
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
    private record WindowManagerSettings(string ActivePerspective, Dictionary<string, WindowState> Windows);

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
        if (Gui.BeginMainMenuBar())
        {
            RenderGlobalMenu(GlobalMenu.Root);
            RenderPerspectiveMenu();
            var hostMenus = BuildHostMenuDtos();
            GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(hostMenus, HandleHostMenuAction);

            if (gizmoMenuItems != null && gizmoMenuItems.Count > 0)
                GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenuItems, onGizmoMenuAction);

            Gui.EndMainMenuBar();
        }

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

        _mainToolbar.Render(CurrentPerspective);
        _statusBar.Render(CurrentPerspective);

        // Draw file dialog service last so the modal overlays all other windows.
        (_fileDialogService as ImGuiFileDialogService)?.Draw();
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
        foreach (var child in node.Children.Values)
        {
            if (child.IsSeparator)
            {
                Gui.Separator();
                continue;
            }

            if (child.Children.Count > 0)
            {
                if (Gui.BeginMenu(child.Name))
                {
                    RenderGlobalMenu(child);
                    Gui.EndMenu();
                }
                continue;
            }

            // Leaf: checkable item.
            if (child.GetCheckedState != null && child.OnCheckedChanged != null)
            {
                bool checkedState = child.GetCheckedState();
                bool enabled = child.GetEnabled?.Invoke() ?? true;
                if (Gui.MenuItem(child.Name, child.Shortcut ?? "", ref checkedState, enabled))
                {
                    child.OnCheckedChanged(checkedState);
                }
                continue;
            }

            // Leaf: plain action item.
            if (child.OnClick != null)
            {
                bool enabled = child.GetEnabled?.Invoke() ?? true;
                if (Gui.MenuItem(child.Name, child.Shortcut ?? "", false, enabled))
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
            Children = winChildren.ToArray()
        });
        menus.Add(new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto
        {
            Label = "Help",
            Priority = 100,
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
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = ActionAbout, Label = "About" }
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
            foreach (var (perspective, isChecked) in BuildPerspectiveMenuModel())
            {
                bool isCheckedCopy = isChecked;
                if (Gui.MenuItem(perspective, "", ref isCheckedCopy))
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
}

