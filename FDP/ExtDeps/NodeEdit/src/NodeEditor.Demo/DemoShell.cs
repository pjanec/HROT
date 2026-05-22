using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Demo.Scenarios;
using NodeEditor.Primitives;
using NodeEditor.UI.Action;
using NodeEditor.UI.Canvas;
using NodeEditor.UI.Find;
using NodeEditor.UI.MiniEditors;
using NodeEditor.UI.Panels;
using NodeEditor.UI.Picker;
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
    private          EditorCommandsImpl      _commands = new();
    private          HotkeyDispatcher?       _hotkeys;

    private readonly List<Scenario>          _scenarios = new();
    private int                              _scenarioIndex;
    private S13_DebugVizMock?                _debugScenario;
    private double                           _timeAccum;

    private string _lastPick = "(none)";

    public DemoShell()
    {
        // Build the initial graph
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _host  = new FakeHostServices(_graph);

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

        ApplyScenario(0);
    }

    // ── per-frame entry ───────────────────────────────────────────────────────

    public void Frame(double elapsedSeconds)
    {
        _timeAccum += elapsedSeconds;
        _host.Input_.BeginFrame();

        // Update debug session if active
        if (_debugScenario?.Session is { IsAttached: true } s)
            s.Update(_timeAccum);

        _hotkeys?.ProcessThisFrame();

        // Draw picker window (if open)
        _host.PickerRegistry_.DrawFrame();

        ImGui.DockSpaceOverViewport();

        DrawMenuBar();
        DrawMyBlueprintWindow();
        DrawCanvasWindow();
        DrawDetailsWindow();
        DrawStatusBar();
    }

    // ── menu bar ──────────────────────────────────────────────────────────────

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            ImGui.MenuItem("(demo — no real file ops)");
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
            _canvas.Render(_view, _findBar);

            // Debug scenario overlay
            if (_debugScenario?.Session is { } session)
            {
                ImGui.SetCursorPos(new Vector2(10, 30));
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
            ImGui.SameLine(viewport.Size.X - 150f);
            ImGui.TextDisabled($"Undo: {(_view.Undo.CanUndo ? "Yes" : "—")} / Redo: {(_view.Undo.CanRedo ? "Yes" : "—")}");
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    // ── scenario management ───────────────────────────────────────────────────

    private void ApplyScenario(int index)
    {
        _scenarioIndex = index;
        _debugScenario = null;
        _host.Debug    = null;

        // Rebuild graph and host
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _host  = new FakeHostServices(_graph);
        _view  = CreateView();

        var scenario = _scenarios[index];
        scenario.Build(_view, _graph, _host.CommandSink_, _host.NodeCatalog_);

        // Wire up debug session if applicable
        if (scenario is S13_DebugVizMock dbg)
        {
            _debugScenario = dbg;
            _host.Debug    = dbg.Session;
        }

        // Rebuild panels and commands
        _findBar = new FindBar(_view, new FindEngine(_view.Model, null));
        _mbPanel = new MyBlueprintPanel(
            _host.MyBlueprint, _host, _commands,
            NavigateToGraph, NavigateToItem);

        var editorReg  = PinDefaultValueEditorRegistry.CreateWithBuiltins();

        var detailsReg = new DetailsViewRegistry();
        var detailsCtx = new DetailsContextProxy(_host.CommandSink_, editorReg, _host.Icons, _host.Theme);
        _details = new DetailsPanel(detailsReg, detailsCtx);

        _commands = new EditorCommandsImpl();
        BuiltinCommandHandlers.RegisterAll(_commands, _view, _findBar);
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
        // No-op in demo
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
