using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Demo.Panels;
using NodeEditor.Demo.Scenarios;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
using NodeEditor.UI.Canvas;
using NodeEditor.UI.Find;
using NodeEditor.UI.MiniEditors;
using NodeEditor.UI.Panels;
using NodeEditor.UI.Picker;
using System;
using System.Linq;
using System.Numerics;

namespace NodeEditor.Demo;

/// <summary>
/// Main demo orchestrator. Builds host services, manages scenarios,
/// and renders the full editor UI each frame.
/// </summary>
public sealed class DemoShell
{
    private FakeGraphModel   _graph;
    private FakeHostServices _host;
    private GraphView        _view;

    private readonly CanvasRenderer          _canvas   = new();
    private          MyBlueprintPanel?       _mbPanel;
    private          DetailsPanel?           _details;
    private          FindBar?                _findBar;
    private          FindResultsPanel?       _findResults;
    private          EditorCommandsImpl      _commands = new();
    private          EditorIndicatorsImpl    _indicators = null!;
    private          HotkeyDispatcher?       _hotkeys;

    private readonly List<Scenario>          _scenarios = new();
    private int                              _scenarioIndex;
    private S13_DebugVizMock?                _debugScenario;
    private FakeGraphContainer?              _graphContainer;
    private double                           _timeAccum;
    private double                           _lastElapsed;
    private int?                             _pendingTabSwitch;
    private readonly Dictionary<FakeGraphModel, (FakeHostServices Host, GraphView View)> _tabState = new();
    private readonly NodeEditor.Core.Bookmarks.BookmarkStore _bookmarks = new();
    private int _lastSelectionCount = -1;
    private SelectionEntry? _lastPrimarySelection;

    private string _lastPick = "(none)";
    private bool _showCreateVarModal;
    private string _newVarName = "NewVariable";
    private string _newVarType = "System.Single";
    private bool _showCreateEventModal;
    private string _newEventName = "OnEnemyKilled";
    private bool _showCollapseModal;
    private string _collapseName = "CalculateDamage";
    private int _collapseMode = 2; // 0 = Function, 1 = Macro, 2 = Auto-detect
    private readonly Dictionary<float, nint> _fonts;
    private readonly List<(EditorNotification Notification, float TimeRemaining)> _activeToasts = new();

    public DemoShell(Dictionary<float, nint>? fonts = null)
    {
        _fonts = fonts ?? new Dictionary<float, nint>();

        // Build the initial graph
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _host  = new FakeHostServices(_graph, _fonts);

        _view  = CreateView();

        // Register scenarios
        _scenarios.Add(new S01_HelloCanvas());
        _scenarios.Add(new S02_DragWireDropToCanvas());
        _scenarios.Add(new S03_BoxSelectAndDrag());
        _scenarios.Add(new S04_UndoRedo());
        _scenarios.Add(new S05_InlineEditors());
        _scenarios.Add(new S06_Reroutes());
        _scenarios.Add(new S07_AddNodePicker());
        _scenarios.Add(new S08_WireDropPicker());
        _scenarios.Add(new S09_VariablePicker());
        _scenarios.Add(new S10_TypePicker());
        _scenarios.Add(new S11_FlagsEnumMultiPicker());
        _scenarios.Add(new S12_AssetGridPicker());
        _scenarios.Add(new S13_DebugVizMock());
        _scenarios.Add(new S15_VariablesGetSet());
        _scenarios.Add(new S16_PromoteToVariable());
        _scenarios.Add(new S17_CustomEvent());
        _scenarios.Add(new S18_FunctionAuthoring());
        _scenarios.Add(new S19_MultipleReturnNodes());
        _scenarios.Add(new S20_MacroWithWildcards());
        _scenarios.Add(new S21_EventDispatcher());
        _scenarios.Add(new S22_CollapseToFunction());
        _scenarios.Add(new S23_CollapseToMacro());
        _scenarios.Add(new S24_ExpandNode());
        _scenarios.Add(new S25_MultiTab());
        _scenarios.Add(new S26_Comments());
        _scenarios.Add(new S27_NestedComments());
        _scenarios.Add(new S28_FindInGraph());
        _scenarios.Add(new S29_FindInAsset());
        _scenarios.Add(new S30_GoToDefinition());
        _scenarios.Add(new S31_Bookmarks());
        _scenarios.Add(new S32_HotReloadConflict());
        _scenarios.Add(new S33_BigGraph());
        _scenarios.Add(new S34_NodeAttachments());
        _scenarios.Add(new S35_ContainerNodes());
        _scenarios.Add(new S36_CustomRenderers());

        ApplyScenario(0);
    }

    // ── per-frame entry ───────────────────────────────────────────────────────

    public void Frame(double elapsedSeconds)
    {
        _lastElapsed = elapsedSeconds;
        _timeAccum  += elapsedSeconds;
        _host.Input_.BeginFrame();
        _view.Interaction.UpdateTween(elapsedSeconds, _view.Viewport);

        // Update debug session if active
        if (_debugScenario?.Session is { IsAttached: true } s)
            s.Update(_timeAccum);

        _hotkeys?.ProcessThisFrame();

        // Handle Ctrl+Tab / Ctrl+Shift+Tab for multi-graph switching
        if (_graphContainer is not null)
        {
            var mods = _host.Input_.Modifiers;
            if (mods.HasFlag(KeyModifiers.Ctrl) && _host.Input_.IsKeyPressed(EditorKey.Tab))
            {
                if (mods.HasFlag(KeyModifiers.Shift))
                    _graphContainer.ActivatePrev();
                else
                    _graphContainer.ActivateNext();

                _graph = _graphContainer.Active;
                _host  = _tabState[_graph].Host;
                _view  = _tabState[_graph].View;
                RebuildPanels();
            }
        }

        // Draw picker window (if open)
        _host.PickerRegistry_.DrawFrame();

        ImGui.DockSpaceOverViewport();

        DrawMenuBar();
        DrawMyBlueprintWindow();
        DrawCanvasWindow();
        SyncSelectionToDetails();
        DrawDetailsWindow();
        DrawFindResultsWindow();
        DrawStatusBar();
        DrawToasts();
        DrawCreateVariableModal();
        DrawCreateEventModal();
        DrawCollapseModal();
    }

    // ── menu bar ──────────────────────────────────────────────────────────────

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            ImGui.MenuItem("(demo — no real file ops)");
            ImGui.Separator();
            if (ImGui.MenuItem("Save (mock)", "Ctrl+S"))
                _host.ToastQueue_.Enqueue(new EditorNotification(
                    System.Guid.NewGuid().ToString(),
                    NotificationSeverity.Info,
                    "Saved (no-op in demo)", null,
                    System.TimeSpan.FromSeconds(2), null));
            if (ImGui.MenuItem("Compile (mock)", "F7"))
                _host.ToastQueue_.Enqueue(new EditorNotification(
                    System.Guid.NewGuid().ToString(),
                    NotificationSeverity.Success,
                    "Compiled (no-op in demo)", null,
                    System.TimeSpan.FromSeconds(2), null));
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            DrawCommandMenuItem(CommandCatalog.Undo);
            DrawCommandMenuItem(CommandCatalog.Redo);
            ImGui.Separator();
            DrawCommandMenuItem(CommandCatalog.SelectAll);
            DrawCommandMenuItem(CommandCatalog.SelectNone);
            ImGui.Separator();
            DrawCommandMenuItem(CommandCatalog.DeleteSelection);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            DrawCommandMenuItem(CommandCatalog.ZoomIn);
            DrawCommandMenuItem(CommandCatalog.ZoomOut);
            DrawCommandMenuItem(CommandCatalog.ZoomReset);
            ImGui.Separator();
            DrawCommandMenuItem(CommandCatalog.FrameAll);
            DrawCommandMenuItem(CommandCatalog.FrameSelection);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Find"))
        {
            DrawCommandMenuItem(CommandCatalog.FindInGraph);
            DrawCommandMenuItem(CommandCatalog.FindInAsset);
            DrawCommandMenuItem(CommandCatalog.FindNext);
            DrawCommandMenuItem(CommandCatalog.FindPrev);
            ImGui.EndMenu();
        }

        // Scenario picker in menu bar
        ImGui.Separator();
        ImGui.SetNextItemWidth(280);
        var current = _scenarios[_scenarioIndex].Name;
        if (ImGui.BeginCombo("##scenario", current))
        {
            for (int i = 0; i < _scenarios.Count; i++)
            {
                bool sel = i == _scenarioIndex;
                if (ImGui.Selectable(_scenarios[i].Name, sel) && i != _scenarioIndex)
                    ApplyScenario(i);
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Make Dirty"))
        {
            var nop = new GraphCommand.Batch("make dirty", System.Array.Empty<GraphCommand>());
            _view.Execute(nop, nop, "make dirty");
        }
        ImGui.SameLine();
        ImGui.TextDisabled(_scenarios[_scenarioIndex].Description);

        ImGui.EndMainMenuBar();
    }

    private void DrawCommandMenuItem(string id)
    {
        var desc = _commands.Get(id);
        if (desc is null) { ImGui.MenuItem(id + " (unregistered)"); return; }
        bool enabled = desc.IsEnabled();
        var label = desc.DefaultKey.HasValue
            ? $"{desc.DisplayName}##{id}"
            : desc.DisplayName;
        var shortcut = desc.DefaultKey?.ToString() ?? "";
        if (ImGui.MenuItem(label, shortcut, false, enabled))
            _commands.Invoke(id);
    }

    // ── windows ───────────────────────────────────────────────────────────────

    private void DrawMyBlueprintWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(240, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("My Blueprint"))
            _mbPanel?.Draw();
        ImGui.End();
    }

    private void DrawCanvasWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("Canvas"))
        {
            // Multi-graph tab bar
            if (_graphContainer is not null)
            {
                if (ImGui.BeginTabBar("##graphs"))
                {
                    for (int i = 0; i < _graphContainer.Graphs.Count; i++)
                    {
                        bool isActive = i == _graphContainer.ActiveIndex;
                        var flags = ImGuiTabItemFlags.None;
                        if (_pendingTabSwitch == i)
                        {
                            flags |= ImGuiTabItemFlags.SetSelected;
                            _pendingTabSwitch = null;
                        }

                        bool isOpen = true;
                        if (ImGui.BeginTabItem(_graphContainer.Graphs[i].DisplayName, ref isOpen, flags))
                        {
                            if (!isActive)
                            {
                                _graphContainer.Activate(i);
                                _graph = _graphContainer.Active;
                                _host  = _tabState[_graph].Host;
                                _view  = _tabState[_graph].View;
                                RebuildPanels();
                            }
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }
            }

            _canvas.Render(_view, _findBar, _commands);
            ImGui.SetCursorScreenPos(_view.Viewport.CanvasScreenOrigin);
            if (ImGui.BeginChild("##canvas_edge_markers", _view.Viewport.CanvasScreenSize, ImGuiChildFlags.None,
                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs))
            {
                NodeEditor.UI.Bookmarks.BookmarkEdgeMarkerRenderer.Render(_view, _bookmarks, _host.Theme);
            }
            ImGui.EndChild();

            ImGui.SetCursorPos(new Vector2(10, _graphContainer is not null ? 60 : 30));
            var overlayFlags = ImGuiWindowFlags.NoTitleBar
                             | ImGuiWindowFlags.NoBackground
                             | ImGuiWindowFlags.NoResize
                             | ImGuiWindowFlags.NoScrollbar
                             | ImGuiWindowFlags.NoSavedSettings
                             | ImGuiWindowFlags.NoFocusOnAppearing
                             | ImGuiWindowFlags.NoNav
                             | ImGuiWindowFlags.NoDocking;
            var childFlags = ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY;

            if (ImGui.BeginChild("##shell_overlay", Vector2.Zero, childFlags, overlayFlags))
            {
                _scenarios[_scenarioIndex].DrawOverlay(_host);

                // Debug scenario overlay
                if (_debugScenario?.Session is { } session)
                {
                    if (session.IsAttached)
                    {
                        if (session.IsPaused)
                        {
                            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), "PAUSED");
                            ImGui.SameLine();
                            if (ImGui.SmallButton("Continue")) session.Continue();
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(0, 1, 0.4f, 1), "Attached");
                        }
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Detach")) session.Detach();
                    }
                    else
                    {
                        if (ImGui.SmallButton("Attach Debugger")) session.Attach();
                    }
                }
            }
            ImGui.EndChild();
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawDetailsWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(280, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Details"))
            _details?.Draw();
        ImGui.End();
    }

    private void DrawFindResultsWindow()
    {
        if (_findBar != null && _findResults != null)
        {
            bool shouldShow = _findBar.IsVisible && _findBar.Scope != FindScope.CurrentGraph;
            _findResults.IsVisible = shouldShow;

            if (shouldShow)
            {
                _findResults.Results = _findBar.Results;

                ImGui.SetNextWindowSize(new Vector2(350, 500), ImGuiCond.FirstUseEver);
                if (ImGui.Begin("Find Results"))
                {
                    _findResults.Draw();
                }
                ImGui.End();
            }
        }
    }

    private void SyncSelectionToDetails()
    {
        if (_details == null) return;

        var sel = _view.Selection;
        var primary = sel.Items.FirstOrDefault();

        if (sel.Count != _lastSelectionCount || !primary.Equals(_lastPrimarySelection))
        {
            _lastSelectionCount = sel.Count;
            _lastPrimarySelection = primary;

            if (sel.Count == 0)
            {
                if (_mbPanel?.SelectedItem == null)
                    _details.Target = new DetailsTarget.None();
            }
            else if (sel.Count == 1)
            {
                if (primary.Kind == SelectionEntryKind.Node)
                    _details.Target = new DetailsTarget.SingleNode(primary.Node);
                else if (primary.Kind == SelectionEntryKind.Comment)
                    _details.Target = new DetailsTarget.Comment(primary.Comment);
                else
                    _details.Target = new DetailsTarget.None();
            }
            else if (sel.Nodes.Any())
            {
                _details.Target = new DetailsTarget.MultipleNodes(sel.Nodes.ToList());
            }
        }
    }

    private void DrawStatusBar()
    {
        var viewport = ImGui.GetMainViewport();
        float height = ImGui.GetFrameHeight();
        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y + viewport.Size.Y - height));
        ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        var flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar  | ImGuiWindowFlags.NoSavedSettings;
        if (ImGui.Begin("##statusbar", flags))
        {
            ImGui.SetCursorPosY((height - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.Text($"Scenario: {_scenarios[_scenarioIndex].Name} | Nodes: {_graph.Nodes.Count} | Last pick: {_lastPick}");
            ImGui.SameLine(viewport.Size.X - 280f);
            var fps = 1.0 / System.Math.Max(_lastElapsed, 0.001);
            ImGui.TextDisabled($"FPS: {fps:F0} | Undo: {(_view.Undo.CanUndo ? "Yes" : "—")} / Redo: {(_view.Undo.CanRedo ? "Yes" : "—")}");
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawToasts()
    {
        while (_host.ToastQueue_.TryDequeue(out var toast))
        {
            float duration = (float)(toast.AutoDismiss?.TotalSeconds ?? 3.0);
            _activeToasts.Add((toast, duration));
        }

        if (_activeToasts.Count == 0)
            return;

        var viewport = ImGui.GetMainViewport();
        Vector2 pos = new(viewport.Pos.X + viewport.Size.X - 20f, viewport.Pos.Y + viewport.Size.Y - 50f);

        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            var active = _activeToasts[i];
            active.TimeRemaining -= (float)_lastElapsed;

            if (active.TimeRemaining <= 0f)
            {
                _activeToasts.RemoveAt(i);
                continue;
            }

            _activeToasts[i] = active;

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always, new Vector2(1.0f, 1.0f));
            ImGui.SetNextWindowBgAlpha(0.85f);

            var flags = ImGuiWindowFlags.NoDecoration
                      | ImGuiWindowFlags.AlwaysAutoResize
                      | ImGuiWindowFlags.NoSavedSettings
                      | ImGuiWindowFlags.NoFocusOnAppearing
                      | ImGuiWindowFlags.NoNav
                      | ImGuiWindowFlags.NoMove;

            if (ImGui.Begin($"##toast_{i}", flags))
            {
                var color = active.Notification.Severity switch
                {
                    NotificationSeverity.Info    => new Vector4(0.4f, 0.7f, 1.0f, 1f),
                    NotificationSeverity.Success => new Vector4(0.4f, 0.9f, 0.4f, 1f),
                    NotificationSeverity.Warning => new Vector4(1.0f, 0.8f, 0.2f, 1f),
                    NotificationSeverity.Error   => new Vector4(1.0f, 0.3f, 0.3f, 1f),
                    _                            => new Vector4(1f, 1f, 1f, 1f),
                };

                ImGui.TextColored(color, active.Notification.Title);
                if (!string.IsNullOrEmpty(active.Notification.Body))
                    ImGui.TextUnformatted(active.Notification.Body);
            }

            pos.Y -= ImGui.GetWindowHeight() + 10f;
            ImGui.End();
        }
    }

    private void DrawCreateVariableModal()
    {
        if (_showCreateVarModal)
        {
            ImGui.OpenPopup("Create Variable");
            _showCreateVarModal = false;
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Create Variable", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            var inputFlags = ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue;
            bool inputEnter = ImGui.InputText("Name", ref _newVarName, 128, inputFlags);

            bool nameExists = _host.MyBlueprint.GetItems("variables")
                .Any(v => v.DisplayName.Equals(_newVarName, StringComparison.OrdinalIgnoreCase));

            if (nameExists)
                ImGui.TextColored(_host.Theme.ErrorColor, "A variable with this name already exists.");
            else
                ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight()));

            float inputWidth = ImGui.CalcItemWidth();
            if (ImGui.Button($"{_newVarType} \u25bc##pick_type", new Vector2(inputWidth, 0)))
            {
                ImGui.CloseCurrentPopup();

                var request = S10_TypePicker.CreateTypePickerRequest();
                _host.PickerRegistry_.OpenPicker(request, result =>
                {
                    if (!result.Cancelled && result.First?.Tag is TypeKey chosenType)
                        _newVarType = chosenType.Id;

                    _showCreateVarModal = true;
                });
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Type");

            ImGui.Spacing();

            bool isValid = !nameExists && !string.IsNullOrWhiteSpace(_newVarName);
            bool globalEnter = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);

            ImGui.BeginDisabled(!isValid);
            if (ImGui.Button("Create", new Vector2(120, 0)) || ((inputEnter || globalEnter) && isValid))
            {
                var tk = new TypeKey(_newVarType);
                var color = _host.TypeSystem_.GetPinColor(tk);

                _host.MyBlueprint.AddVariable(
                    $"var.{Guid.NewGuid():N}",
                    _newVarName,
                    color,
                    $"User created variable ({_newVarType})");

                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0)) || !open || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawCreateEventModal()
    {
        if (_showCreateEventModal)
        {
            ImGui.OpenPopup("Create Custom Event");
            _showCreateEventModal = false;
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Create Custom Event", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            var inputFlags = ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue;
            bool inputEnter = ImGui.InputText("Name", ref _newEventName, 128, inputFlags);

            ImGui.Spacing();
            ImGui.TextDisabled("Parameters (Mocked for S17)");

            bool isValid = !string.IsNullOrWhiteSpace(_newEventName);
            bool globalEnter = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);

            ImGui.BeginDisabled(!isValid);
            if (ImGui.Button("Create Event", new Vector2(120, 0)) || ((inputEnter || globalEnter) && isValid))
            {
                string eventName = _newEventName.Trim();

                // 1. Add to My Blueprint Panel
                string eventId = $"evt.{Guid.NewGuid():N}";
                _host.MyBlueprint.AddCustomEvent(eventId, eventName);

                // 2. Create the New Graph Tab
                var newGraph = new FakeGraphModel(GraphId.NewId(), eventName);

                if (_graphContainer == null)
                {
                    // Promote single-graph view to multi-tab view
                    _graphContainer = new FakeGraphContainer(_graph, newGraph);
                    _tabState[_graph] = (_host, _view); // Ensure current graph is tracked
                }
                else
                {
                    _graphContainer.AddGraph(newGraph);
                }

                // 3. Initialize Host Services for the new tab
                var newHost = new FakeHostServices(newGraph, _fonts);
                newHost.OverrideMyBlueprint(_host.MyBlueprint); // Share the global MyBlueprint
                var newView = new GraphView(newGraph, newHost.CommandSink_, newHost.Validator, newHost.TypeSystem_, newHost.NodeCatalog_, newHost);
                _tabState[newGraph] = (newHost, newView);

                // 4. Place the Entry Node
                var cb = new CommandBuilder(newGraph);
                var props = new Dictionary<string, object?> { ["EventName"] = eventName };
                var (fwd, inv) = cb.AddNode(new NodeKindKey("Event.CustomEntry"), new Vector2(400, 300), props);
                newView.Execute(fwd, inv, "Add Event Entry");

                // 5. Activate the new Tab
                _graphContainer.Activate(_graphContainer.Graphs.Count - 1);
                _graph = _graphContainer.Active;
                _host = newHost;
                _view = newView;
                RebuildPanels();

                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)) || !open || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawCollapseModal()
    {
        if (_showCollapseModal)
        {
            ImGui.OpenPopup("Collapse Selection To...");
            _showCollapseModal = false;
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Collapse Selection To...", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();

            ImGui.InputText("Name", ref _collapseName, 128, ImGuiInputTextFlags.AutoSelectAll);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Target Type:");

            ImGui.RadioButton("Function (callable, returns one value)", ref _collapseMode, 0);
            ImGui.RadioButton("Macro (inline-expanded, multi-exit)", ref _collapseMode, 1);
            ImGui.RadioButton("Auto: choose based on selection content", ref _collapseMode, 2);

            ImGui.Spacing();

            // Auto-detect heuristic: latent nodes require macro.
            bool hasLatent = _view.Selection.Nodes.Any(nid => _graph.FindNode(nid)?.Kind.Id == "Flow.Delay");
            int effectiveMode = _collapseMode == 2 ? (hasLatent ? 1 : 0) : _collapseMode;

            if (hasLatent && effectiveMode == 0)
            {
                ImGui.TextColored(_host.Theme.ErrorColor, "Selection contains a latent node, so a Macro is required.");
            }

            bool isValid = !string.IsNullOrWhiteSpace(_collapseName) && !(hasLatent && effectiveMode == 0);
            ImGui.BeginDisabled(!isValid);

            if (ImGui.Button("Collapse", new Vector2(120, 0)) || (ImGui.IsKeyPressed(ImGuiKey.Enter) && isValid))
            {
                if (effectiveMode == 1)
                {
                    var cmd = new GraphCommand.CollapseToMacro(_view.Selection.Nodes.ToList(), _collapseName, "Default");
                    _view.Execute(cmd, new GraphCommand.Batch("Undo Collapse", Array.Empty<GraphCommand>()), "Collapse to Macro");
                }
                else
                {
                    var cmd = new GraphCommand.CollapseToFunction(_view.Selection.Nodes.ToList(), _collapseName, false, "Default");
                    _view.Execute(cmd, new GraphCommand.Batch("Undo Collapse", Array.Empty<GraphCommand>()), "Collapse to Function");
                }

                ImGui.CloseCurrentPopup();
                _view.Selection.Clear();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    // ── scenario management ───────────────────────────────────────────────────

    private void ApplyScenario(int index)
    {
        _scenarioIndex = index;
        _debugScenario = null;
        _tabState.Clear();

        var scenario = _scenarios[index];

        // Create a fresh graph + host so we have a catalog for BuildMultiGraph
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _host  = new FakeHostServices(_graph, _fonts);

        var container = scenario.BuildMultiGraph(_host.NodeCatalog_, out var customMbModel);
        if (container is not null)
        {
            _graphContainer = container;
            foreach (var g in container.Graphs)
            {
                var h = new FakeHostServices(g, _fonts);
                if (customMbModel is not null) h.OverrideMyBlueprint(customMbModel);
                var v = new GraphView(g, h.CommandSink_, h.Validator, h.TypeSystem_, h.NodeCatalog_, h);
                scenario.SetupHost(h);
                scenario.Setup(h.MyBlueprint);
                _tabState[g] = (h, v);
            }
            _graph = container.Active;
            _host  = _tabState[_graph].Host;
            _view  = _tabState[_graph].View;

            scenario.Build(_view, _graph, _host.CommandSink_, _host.NodeCatalog_);
        }
        else
        {
            _graphContainer = null;
            scenario.SetupHost(_host);
            scenario.Setup(_host.MyBlueprint);
            _view = CreateView();
            scenario.Build(_view, _graph, _host.CommandSink_, _host.NodeCatalog_);
        }

        // Wire up debug session
        _host.Debug = scenario.Session;
        if (scenario is S13_DebugVizMock dbg) _debugScenario = dbg;

        RebuildPanels();
    }

    private void RebuildPanels()
    {
        _commands = new EditorCommandsImpl();
        _findBar = new FindBar(_view, new FindEngine(_view.Model, null));
        _findResults = new FindResultsPanel((graphId, nodeId) =>
        {
            // Basic navigation: pan the camera to the node when clicked in the results.
            if (nodeId.HasValue && _graph.FindNode(nodeId.Value) is { } n)
            {
                var canvasCenter = _view.Viewport.ScreenToGraph(
                    _view.Viewport.CanvasScreenOrigin + _view.Viewport.CanvasScreenSize * 0.5f);
                var delta = canvasCenter - n.Position;
                _view.Viewport.Pan(-delta);
            }
        });
        _mbPanel = new MyBlueprintPanel(
            _host.MyBlueprint, _host, _commands,
            NavigateToGraph, NavigateToItem);

        var detailsReg = new DetailsViewRegistry();
        detailsReg.Register(new DemoNodeDetailsProvider(_graph));
        var detailsCtx = new DetailsContextProxy(_host.CommandSink_, _host.EditorRegistry, _host.Icons, _host.Theme);
        _details = new DetailsPanel(detailsReg, detailsCtx);

        _mbPanel.SelectionChanged += item =>
        {
            if (item != null && _details != null)
            {
                if (item.SectionId == "variables")
                    _details.Target = new DetailsTarget.Variable(item.ItemId);
                else if (item.SectionId == "functions")
                    _details.Target = new DetailsTarget.Function(item.ItemId);
                else if (item.SectionId == "macros")
                    _details.Target = new DetailsTarget.Macro(item.ItemId);
            }
        };

        BuiltinCommandHandlers.RegisterAll(_commands, _view, _findBar);
        var reg = new CommandRegistration(_commands);
        reg.Add(CommandCatalog.CreateVariable, "Create Variable", "Add", _ =>
        {
            _showCreateVarModal = true;
            _newVarName = "NewVariable";
        });
        reg.Add(CommandCatalog.CreateCustomEvent, "Create Custom Event", "Add", _ =>
        {
            _showCreateEventModal = true;
            _newEventName = "OnEnemyKilled";
        });
        reg.Add(CommandCatalog.CollapseToFunction, "Collapse to Function", "Refactor", _ =>
        {
            _showCollapseModal = true;
            _collapseName = "CalculateDamage";
        },
        defaultKey: new KeyBinding(EditorKey.E, KeyModifiers.Ctrl),
        isEnabled: () => _view.Selection.Nodes.Any());
        reg.Add(CommandCatalog.GoToDefinition, "Go to Definition", "Find", _ =>
        {
            var primaryNodeId = _view.Selection.Nodes.FirstOrDefault();
            var node = _graph.FindNode(primaryNodeId);
            if (node != null && node.Kind.Id == "Function.Call")
            {
                // Resolve the call node back to its My Blueprint function definition.
                var fnName = node.Title;
                var item = _host.MyBlueprint.GetItems("functions")
                    .FirstOrDefault(i => i.DisplayName == fnName);

                if (item != null)
                    NavigateToItem("functions", item.ItemId);
            }
        },
        defaultKey: new KeyBinding(EditorKey.F12, KeyModifiers.None),
        isEnabled: () => _view.Selection.Nodes.Any(n => _graph.FindNode(n)?.Kind.Id == "Function.Call"));
        _indicators = new EditorIndicatorsImpl(_host.ToastQueue_);
        NodeEditor.UI.Bookmarks.BookmarkCommands.RegisterAll(_commands, _view, _bookmarks, _indicators, NavigateToGraph);
        _hotkeys = new HotkeyDispatcher(_host.Input, _commands);
    }

    private GraphView CreateView() => new GraphView(
        _graph,
        _host.CommandSink_,
        _host.Validator,
        _host.TypeSystem_,
        _host.NodeCatalog_,
        _host);

    private void NavigateToGraph(GraphId id)
    {
        // No-op in demo (single graph)
    }

    private void NavigateToItem(string sectionId, string itemId)
    {
        var item = _host.MyBlueprint.GetItems(sectionId).FirstOrDefault(i => i.ItemId == itemId);
        if (item == null) return;

        // 1. Check if the tab is already open (matching by name for the demo)
        if (_graphContainer != null)
        {
            for (int i = 0; i < _graphContainer.Graphs.Count; i++)
            {
                if (_graphContainer.Graphs[i].DisplayName == item.DisplayName)
                {
                    _graphContainer.Activate(i);
                    _pendingTabSwitch = i;
                    _graph = _graphContainer.Active;
                    _host = _tabState[_graph].Host;
                    _view = _tabState[_graph].View;
                    RebuildPanels();
                    return;
                }
            }
        }

        // 2. Spawn the new graph
        var newGraph = new FakeGraphModel(GraphId.NewId(), item.DisplayName);

        if (_graphContainer == null)
        {
            // Promote single-graph view to multi-tab view
            _graphContainer = new FakeGraphContainer(_graph, newGraph);
            _tabState[_graph] = (_host, _view);
        }
        else
        {
            _graphContainer.AddGraph(newGraph);
        }

        // 3. Initialize services for the new tab
        var newHost = new FakeHostServices(newGraph, _fonts);
        newHost.OverrideMyBlueprint(_host.MyBlueprint);
        var newView = new GraphView(newGraph, newHost.CommandSink_, newHost.Validator, newHost.TypeSystem_, newHost.NodeCatalog_, newHost);
        _tabState[newGraph] = (newHost, newView);

        // 4. Place structural Entry and Return nodes for functions.
        if (sectionId.Equals("functions", StringComparison.OrdinalIgnoreCase))
        {
            var cb = new CommandBuilder(newGraph);
            var (fwdEntry, invEntry) = cb.AddNode(
                new NodeKindKey("Function.Entry"),
                new Vector2(100, 300),
                new Dictionary<string, object?> { ["FunctionName"] = item.DisplayName });
            newView.Execute(fwdEntry, invEntry, "Add Entry");

            // S22: mock the preserved function body for "CalculateDamage".
            if (item.DisplayName == "CalculateDamage")
            {
                var (f1, i1) = cb.AddNode(new NodeKindKey("Util.GetVar"), new Vector2(300, 200),
                    new Dictionary<string, object?> { ["VariableName"] = "Base" });
                newView.Execute(f1, i1, "Add Body Node");

                var (f2, i2) = cb.AddNode(new NodeKindKey("Util.GetVar"), new Vector2(300, 320),
                    new Dictionary<string, object?> { ["VariableName"] = "Multiplier" });
                newView.Execute(f2, i2, "Add Body Node");

                var (f3, i3) = cb.AddNode(new NodeKindKey("Math.Multiply"), new Vector2(520, 200), null);
                newView.Execute(f3, i3, "Add Body Node");

                var (f4, i4) = cb.AddNode(new NodeKindKey("Math.Add"), new Vector2(700, 200), null);
                newView.Execute(f4, i4, "Add Body Node");

                var (f5, i5) = cb.AddNode(new NodeKindKey("Math.Clamp"), new Vector2(900, 200), null);
                newView.Execute(f5, i5, "Add Body Node");

                var (fwdReturn, invReturn) = cb.AddNode(
                    new NodeKindKey("Function.Return"),
                    new Vector2(1100, 300),
                    null);
                newView.Execute(fwdReturn, invReturn, "Add Return");
            }
            else
            {
                var (fwdReturn, invReturn) = cb.AddNode(
                    new NodeKindKey("Function.Return"),
                    new Vector2(600, 300),
                    null);
                newView.Execute(fwdReturn, invReturn, "Add Return");
            }
        }

        // 5. Activate the new Tab
        int newIndex = _graphContainer.Graphs.Count - 1;
        _graphContainer.Activate(newIndex);
        _pendingTabSwitch = newIndex;
        _graph = _graphContainer.Active;
        _host = newHost;
        _view = newView;
        RebuildPanels();
    }

    // ── details context proxy ─────────────────────────────────────────────────

    private sealed class DetailsContextProxy : IDetailsContext
    {
        public IGraphCommandSink           CommandSink { get; }
        public IPinDefaultValueEditorRegistry Editors { get; }
        public IIconProvider               Icons       { get; }
        public IEditorTheme                Theme       { get; }

        public DetailsContextProxy(
            IGraphCommandSink sink,
            IPinDefaultValueEditorRegistry editors,
            IIconProvider icons,
            IEditorTheme theme)
        {
            CommandSink = sink;
            Editors     = editors;
            Icons       = icons;
            Theme       = theme;
        }
    }
}
