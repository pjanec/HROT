using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker.Layouts;
using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Internal concrete implementation of <see cref="IPickerRenderContext"/>.
/// </summary>
internal sealed class PickerRenderContext : IPickerRenderContext
{
    public required IIconProvider Icons { get; init; }
    public required IEditorTheme Theme { get; init; }
    public IReadOnlyList<int>? MatchPositions { get; set; }
}

/// <summary>
/// The single floating picker window. Shared across all picker invocations;
/// a new Open() call cancels the previous one.
/// </summary>
public sealed class PickerWindow
{
    // ── session ───────────────────────────────────────────────────────────────

    private bool _isOpen;
    private string _title    = "";
    private PickerLayout _layout;
    private PickerSelectionMode _selectionMode;
    private Vector2? _screenPos;
    private CategoryNode? _categoryRoot;
    private bool _foldSingleChildFolders;
    private bool _showPreview;

    // ⭐ CE-4168 (AQ108 D26) — the opt-in extensions of the open request (all null/false for a classic picker).
    private bool _sidePaneOn;
    private float _paneWidth = 280f;
    private Action<PickerEntry, PickerPaneContext>? _drawPane;
    private bool _drawPaneAfterDefault;
    private Func<PickerEntry?, object?>? _capturePaneState;
    private IReadOnlyList<PickerAction>? _actions;
    private IReadOnlyList<PickerLayout>? _layoutChoices;

    // Entry-driven path (OpenFromRequest).
    private PickerEntry[]? _requestEntries;
    private System.Action<PickerResult>? _onPickResult;

    // Source-driven path (Open from IPickerRegistry).
    private IPickerSourceAdapter? _adapter;
    private IReadOnlyDictionary<string, object?>? _adapterContext;
    private System.Action<object>? _onPickRaw;
    private System.Action? _onCancel;

    private readonly PickerState _state = new();

    // ── services (injected by PickerRegistry) ─────────────────────────────────

    internal IIconProvider? Icons;
    internal IEditorTheme? Theme;

    /// <summary>⭐ CE-4098 — the Recent lists this window reads and pushes (a host swaps in its persisted store; default in-memory).</summary>
    internal RecentStore Recent { get => _state.Recent; set => _state.Recent = value ?? new RecentStore(); }

    /// <summary>⭐ CE-4168 (P1/P4) — the remembered layout choice and side-pane width per context (a host swaps in its persisted store; default in-memory).</summary>
    internal PickerViewStore View { get; set; } = new();

    /// <summary>⭐ CE-4168 (P5) — the shared lazy image cache of this window (a host swaps in one wired to its texture loader).</summary>
    internal PickerImageCache Images { get => _state.Images; set => _state.Images = value ?? new PickerImageCache(); }

    /// <summary>The layout the open picker draws now (after a P4 toggle) — for the headless rails.</summary>
    internal PickerLayout CurrentLayout => _layout;

    /// <summary>The window size the open picker asks for (a request's <c>DefaultSize</c>, else the layout's rule) — for the headless rails.</summary>
    internal Vector2 DefaultSizeForRail => GetDefaultSize();

    /// <summary>True when the open request shows the side pane — for the headless rails.</summary>
    internal bool SidePaneOn => _sidePaneOn;

    /// <summary>The side-pane width now — for the headless rails.</summary>
    internal float PaneWidth => _paneWidth;

    /// <summary>The session state — for the headless rails (PickerState is internal).</summary>
    internal PickerState State => _state;

    // ── size helpers ──────────────────────────────────────────────────────────

    private Vector2? _requestSize;   // ⭐ CE-4141 — PickerRequest.DefaultSize

    private Vector2 GetDefaultSize() => _requestSize ?? _layout switch
    {
        PickerLayout.Compact => new Vector2(320f, 360f),
        PickerLayout.Wide    => new Vector2(800f, 600f),
        PickerLayout.Grid    => new Vector2(600f, 500f),
        _                    => new Vector2(520f, 520f),
    };

    // ── open (entry-driven) ───────────────────────────────────────────────────

    /// <summary>Open the picker from a <see cref="PickerRequest"/>.</summary>
    public void Open(PickerRequest request, System.Action<PickerResult> onChosen)
    {
        Cancel(); // cancel any open session

        _title          = request.Title;
        _layoutChoices  = request.LayoutChoices is { Count: > 1 } ? request.LayoutChoices : null;
        _layout         = View.ResolveLayout(request.ContextKey, request.Layout, _layoutChoices);
        _selectionMode  = request.SelectionMode;
        _screenPos      = request.AnchorScreen;
        _categoryRoot   = request.CategoryRoot;
        _foldSingleChildFolders = request.FoldSingleChildFolders;
        _showPreview    = request.ShowPreview;
        _sidePaneOn     = request.ShowSidePane || request.DrawPane is not null;
        _requestSize    = request.DefaultSize;
        _paneWidth      = View.GetPaneWidth(request.ContextKey) ?? request.SidePaneWidth;
        _drawPane       = request.DrawPane;
        _drawPaneAfterDefault = request.DrawPaneAfterDefault;
        _capturePaneState = request.CapturePaneState;
        _actions        = request.Actions is { Count: > 0 } ? request.Actions : null;
        _requestEntries = request.ItemsProvider().ToArray();
        _adapter        = null;
        _adapterContext = null;
        _onPickResult   = onChosen;
        _onPickRaw      = null;
        _onCancel       = null;

        _state.Reset(request.ContextKey, request.InitialQuery, request.SelectionMode);
        _state.Groups = request.GroupSidebar;
        _state.TileWidth = Math.Clamp(View.GetTileWidth(request.ContextKey) ?? PickerState.DefaultTileWidth,
                                      PickerState.MinTileWidth, PickerState.MaxTileWidth);
        _state.AllEntries = _requestEntries;
        _state.Refilter();
        _state.ApplyInitialSelection(request.InitialSelectionId);

        _isOpen = true;
    }

    // ── open (source-driven from IPickerRegistry) ─────────────────────────────

    /// <summary>Open the picker using a registered <see cref="IPickerSourceAdapter"/>.</summary>
    internal void OpenFromAdapter(
        IPickerSourceAdapter adapter,
        string contextKey,
        Vector2 screenPos,
        System.Action<object> onPick,
        System.Action? onCancel,
        IReadOnlyDictionary<string, object?>? context)
    {
        Cancel(); // cancel any open session

        _adapter        = adapter;
        _adapterContext = context;
        _title          = adapter.Title;
        _layout         = adapter.PreferredLayout;
        _selectionMode  = adapter.SelectionMode;
        _screenPos      = screenPos;
        _categoryRoot   = null;
        _foldSingleChildFolders = false;
        _showPreview    = false;
        _sidePaneOn     = false;
        _requestSize    = null;
        _drawPane       = null;
        _capturePaneState = null;
        _actions        = null;
        _layoutChoices  = null;
        _requestEntries = null;
        _onPickRaw      = onPick;
        _onPickResult   = null;
        _onCancel       = onCancel;

        _state.Reset(contextKey, "", adapter.SelectionMode);

        // For cheap/sync sources, pre-load items immediately.
        if (!adapter.IsAsync)
        {
            var items = adapter.Query("", context);
            _state.AllEntries = items.Select(it => new PickerEntry(
                it.Key, it.SearchText, it.Description, it.Category, null, null, it.Raw, it.IconKey)).ToArray();
            _state.Refilter();
        }

        _isOpen = true;
    }

    // ── DrawFrame ─────────────────────────────────────────────────────────────

    /// <summary>Render the picker window. Call once per ImGui frame.</summary>
    public void DrawFrame()
    {
        if (!_isOpen) return;

        _state.Images.BeginFrame();
        var size = GetDefaultSize();
        ImGui.SetNextWindowSize(size, ImGuiCond.Appearing);

        if (_screenPos.HasValue)
        {
            // Clamp to viewport.
            var vp    = ImGui.GetMainViewport();
            var pos   = _screenPos.Value;
            var maxP  = vp.Pos + vp.Size - size;
            pos = Vector2.Clamp(pos, vp.Pos, maxP);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Appearing);
        }

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;

        bool windowOpen = true;
        bool visible    = ImGui.Begin(_title + "##picker", ref windowOpen, flags);

        try
        {
            if (!windowOpen)
            {
                Cancel();
                return;
            }

            if (!visible)
            {
                return;
            }

            DrawContent();
        }
        finally
        {
            ImGui.End();
        }
    }

    // ── content ───────────────────────────────────────────────────────────────

    private void DrawContent()
    {
        // Lazily refilter when query changed.
        if (_state.SearchText != _state.LastQuery)
            _state.Refilter();

        DrawSearchBar();
        ImGui.Separator();
        DrawBody();
        ImGui.Separator();
        DrawFooter();
    }

    private void DrawSearchBar()
    {
        // ⭐ CE-4168 (P4) — the layout toggle sits at the right end of the search row.
        float toggleW = _layoutChoices is null ? 0f : LayoutToggleWidth();
        ImGui.SetNextItemWidth(-(toggleW > 0f ? toggleW + ImGui.GetStyle().ItemSpacing.X : 1f));

        if (_state.FocusSearchNextFrame)
        {
            ImGui.SetKeyboardFocusHere();
            _state.FocusSearchNextFrame = false;
        }

        string buf = _state.SearchText;
        if (ImGui.InputText("##picker_search", ref buf, 256))
            _state.SearchText = buf;

        if (_layoutChoices is not null)
        {
            ImGui.SameLine();
            DrawLayoutToggle();
        }

        // ESC handled in footer / key polling.
    }

    // ── CE-4168 (P4) layout toggle ────────────────────────────────────────────

    /// <summary>The label of a layout in the toggle: thumbnails for Grid, a list for everything else.</summary>
    internal static string LayoutLabel(PickerLayout l) => l == PickerLayout.Grid ? "Thumbnails" : "List";

    private float LayoutToggleWidth()
    {
        float w = 0f;
        foreach (var c in _layoutChoices!) w += ImGui.CalcTextSize(LayoutLabel(c)).X + ImGui.GetStyle().FramePadding.X * 2f;
        return w + ImGui.GetStyle().ItemInnerSpacing.X * (_layoutChoices!.Count - 1);
    }

    private void DrawLayoutToggle()
    {
        for (int i = 0; i < _layoutChoices!.Count; i++)
        {
            var choice = _layoutChoices[i];
            bool active = choice == _layout;
            if (i > 0) ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (active)
                ImGui.PushStyleColor(ImGuiCol.Button, BuildRenderContext().Theme.SelectionAccent with { W = 0.55f });
            bool clicked = ImGui.Button(LayoutLabel(choice) + "##layout_" + choice);
            if (active) ImGui.PopStyleColor();
            if (clicked) SetLayout(choice);
        }
    }

    /// <summary>
    /// ⭐ CE-4168 (P4) — switches the open picker to <paramref name="layout"/> (one of the request's <see cref="PickerRequest.LayoutChoices"/>)
    /// and remembers it for this context. The current entry stays selected and is revealed in the new layout. False when not allowed.
    /// </summary>
    internal bool SetLayout(PickerLayout layout)
    {
        if (!_isOpen || _layoutChoices is null || !_layoutChoices.Contains(layout)) return false;
        if (layout == _layout) return true;
        _layout = layout;
        View.SetLayout(_state.ContextKey, layout);
        if (_state.CurrentEntry() is { } cur)
        {
            int idx = _state.Filtered.FindIndex(r => r.Entry.Id == cur.Id);
            _state.KeyboardFocusIndex = idx;
            _state.RevealCategory = cur.Category;
            _state.FocusLeafOnDraw = idx;
            _state.ScrollToFocus = true;
        }
        return true;
    }

    private void DrawBody()
    {
        var ctx = BuildRenderContext();
        if (!_sidePaneOn)
        {
            DrawLayout(ctx);
            return;
        }

        // ⭐ CE-4168 (P1) — the layout left, a draggable splitter, the pane right. The split's children end above the footer row,
        // so the layout inside reserves nothing more (PickerState.InSidePane).
        var style = ImGui.GetStyle();
        var avail = ImGui.GetContentRegionAvail();
        float height = Math.Max(avail.Y - ImGui.GetFrameHeightWithSpacing(), 40f);
        const float splitterW = 6f;
        _paneWidth = ClampPaneWidth(_paneWidth, avail.X, splitterW);
        float leftW = Math.Max(avail.X - _paneWidth - splitterW, 40f);

        if (ImGui.BeginChild("##picker_split_left", new Vector2(leftW, height), ImGuiChildFlags.None))
        {
            _state.InSidePane = true;
            try { DrawLayout(ctx); }
            finally { _state.InSidePane = false; ImGui.EndChild(); }
        }
        else ImGui.EndChild();

        ImGui.SameLine(0f, 0f);
        ImGui.InvisibleButton("##picker_splitter", new Vector2(splitterW, height));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        if (ImGui.IsItemActive())
            _paneWidth = ClampPaneWidth(_paneWidth - ImGui.GetIO().MouseDelta.X, avail.X, splitterW);
        if (ImGui.IsItemDeactivated())
            View.SetPaneWidth(_state.ContextKey, _paneWidth);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        float lx = (min.X + max.X) * 0.5f;
        ImGui.GetWindowDrawList().AddLine(new Vector2(lx, min.Y), new Vector2(lx, max.Y),
            ImGui.GetColorU32(ImGui.IsItemActive() ? ctx.Theme.SelectionAccent : ctx.Theme.TextMuted with { W = 0.4f }));

        ImGui.SameLine(0f, 0f);
        if (ImGui.BeginChild("##picker_side_pane", new Vector2(0f, height), ImGuiChildFlags.Borders))
        {
            try { DrawSidePane(ctx); }
            finally { ImGui.EndChild(); }
        }
        else ImGui.EndChild();
    }

    /// <summary>Pane width kept within [<see cref="MinPaneWidth"/>, 70 % of the body] so neither side can be dragged away.</summary>
    internal static float ClampPaneWidth(float width, float bodyWidth, float splitterWidth)
    {
        float max = Math.Max(MinPaneWidth, (bodyWidth - splitterWidth) * 0.7f);
        return Math.Clamp(width, MinPaneWidth, max);
    }

    internal const float MinPaneWidth = 160f;

    /// <summary>The entry the side pane shows: the focused tree leaf, else the keyboard-focused entry.</summary>
    private PickerEntry? PaneEntry()
    {
        int idx = _layout == PickerLayout.Tree ? TreeLayout.FocusedLeafIndex(_state) : _state.KeyboardFocusIndex;
        return idx >= 0 && idx < _state.Filtered.Count ? _state.Filtered[idx].Entry : null;
    }

    private void DrawSidePane(PickerRenderContext ctx)
    {
        var entry = PaneEntry();
        if (entry is null)
        {
            PickerPaneContent.DrawMuted(ctx.Theme, "(no selection)");
            return;
        }

        float width = ImGui.GetContentRegionAvail().X;
        void DrawDefault() => PickerPaneContent.DrawDefault(entry, ctx, _state.Images, width);
        if (_drawPane is null) { DrawDefault(); return; }

        if (_drawPaneAfterDefault) { DrawDefault(); ImGui.Separator(); }
        _drawPane(entry, new PickerPaneContext
        {
            Width = width, Theme = ctx.Theme, Icons = ctx.Icons, Images = _state.Images, DrawDefault = DrawDefault,
        });
    }

    private void DrawLayout(PickerRenderContext ctx)
    {
        DrawLayoutCore(ctx);
        if (_state.TileWidthChanged)
        {
            _state.TileWidthChanged = false;
            View.SetTileWidth(_state.ContextKey, _state.TileWidth);
        }
    }

    private void DrawLayoutCore(PickerRenderContext ctx)
    {
        switch (_layout)
        {
            case PickerLayout.Compact:
                CompactLayout.Draw(_state, ctx);
                break;

            case PickerLayout.Wide:
                WideLayout.Draw(_state, ctx);
                break;

            case PickerLayout.Grid:
                GridLayout.Draw(_state, ctx);
                break;

            case PickerLayout.Tree:
                TreeLayout.Draw(_state, ctx, _categoryRoot, _foldSingleChildFolders, _showPreview && !_sidePaneOn);
                break;

            default: // Standard
                StandardLayout.Draw(_state, ctx);
                break;
        }
    }

    private void DrawFooter()
    {
        HandleKeyboardNavigation();

        bool enterPressed = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);

        // Tree layout: Enter on a folder expands it (if collapsed) instead of confirming.
        if (enterPressed && _layout == PickerLayout.Tree && _state.VisualRows.Count > 0)
        {
            int tf = _state.TreeFocusRow;
            if (tf >= 0 && tf < _state.VisualRows.Count)
            {
                var focusRow = _state.VisualRows[tf];
                if (focusRow.IsFolder)
                {
                    // Request expand via one-shot toggle (A16).
                    _state.PendingToggleFolderPath = focusRow.FolderPath;
                    _state.PendingToggleOpen = true;
                    enterPressed = false; // suppress confirm
                }
            }
        }

        // Architecturally modified: Enter universally confirms the picker
        // regardless of Single or Multi-Select mode.
        bool shouldConfirm;
        if (_actions is null)
            shouldConfirm = _state.Confirmed || ImGui.Button("OK") || enterPressed;
        else
        {
            // ⭐ CE-4168 (P3) — the request's actions replace OK; Enter / double-click take the default one.
            if (DrawActionButtons() is { } clicked)
            {
                Confirm(clicked);
                return;
            }
            shouldConfirm = _state.Confirmed || enterPressed;
        }

        if (shouldConfirm)
        {
            Confirm();
            return;
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Cancel();
            return;
        }

        if (_state.SelectedFilteredIndices.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(BuildRenderContext().Theme.TextMuted,
                $"{_state.SelectedFilteredIndices.Count} selected");
        }
    }

    /// <summary>Draws the action buttons (disabled ones greyed with their reason as a tooltip); the id of the one clicked, else null.</summary>
    private string? DrawActionButtons()
    {
        string? clicked = null;
        var entry = _state.CurrentEntry();
        var theme = BuildRenderContext().Theme;
        var defaultId = (_actions!.FirstOrDefault(a => a.IsDefault) ?? _actions![0]).Id;
        foreach (var a in _actions!)
        {
            string? reason = a.DisabledReason?.Invoke(entry);
            bool isDefault = a.Id == defaultId;
            ImGui.BeginDisabled(reason is not null);
            if (isDefault) ImGui.PushStyleColor(ImGuiCol.Button, theme.SelectionAccent with { W = 0.6f });
            if (ImGui.Button(a.Label + "##act_" + a.Id)) clicked = a.Id;
            if (isDefault) ImGui.PopStyleColor();
            ImGui.EndDisabled();
            if (reason is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(reason);
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }
        return clicked;
    }

    private void HandleKeyboardNavigation()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow))
            return;

        // ── Tree layout: navigate visual rows (folders + leaves in DFS order) ──
        if (_layout == PickerLayout.Tree && _state.VisualRows.Count > 0)
        {
            HandleTreeKeyboardNavigation();
            return;
        }

        // ── ⭐ CE-4142: the gallery Grid moves over the tiles it DREW (arrows are 2-D) ──
        if (_layout == PickerLayout.Grid && _state.Groups is not null && _state.VisibleFiltered.Count > 0)
        {
            HandleGridKeyboardNavigation();
            return;
        }

        // ── Flat layouts (Standard/Compact/Wide/Grid): Filtered-index navigation ──
        int count = _state.Filtered.Count;
        if (count == 0) return;

        bool shift = ImGui.GetIO().KeyShift;

        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            MoveFocus(-1, count, shift);
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            MoveFocus(1, count, shift);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
            MoveFocus(-10, count, shift);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
            MoveFocus(10, count, shift);
        else if (ImGui.IsKeyPressed(ImGuiKey.Home))
            SetFocus(0, shift);
        else if (ImGui.IsKeyPressed(ImGuiKey.End))
            SetFocus(count - 1, shift);

        if (_state.SelectionMode != PickerSelectionMode.Single)
        {
            // Enter no longer toggles. Only Space manipulates the checklist state.
            if (ImGui.IsKeyPressed(ImGuiKey.Space))
            {
                // Target all highlighted rows, or just the focused one if no highlight span exists
                var targets = _state.HighlightedIndices.Count > 0
                    ? _state.HighlightedIndices
                    : new HashSet<int> { _state.KeyboardFocusIndex };

                // Intelligent batch toggle: if ANY target is unchecked, check them all. Otherwise uncheck all.
                bool anyUnchecked = targets.Any(idx => !_state.SelectedFilteredIndices.Contains(idx));

                foreach (var idx in targets)
                {
                    if (idx >= 0 && idx < count)
                    {
                        if (anyUnchecked)
                            _state.SelectedFilteredIndices.Add(idx);
                        else
                            _state.SelectedFilteredIndices.Remove(idx);
                    }
                }
            }
        }
    }

    private void HandleGridKeyboardNavigation()
    {
        var vis = _state.VisibleFiltered;
        int cols = Math.Max(1, _state.GridColumns);
        int pos = Math.Max(0, vis.IndexOf(_state.KeyboardFocusIndex));
        int next = pos;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) next = pos - 1;
        else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) next = pos + 1;
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) next = pos - cols;
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) next = pos + cols;
        else if (ImGui.IsKeyPressed(ImGuiKey.Home)) next = 0;
        else if (ImGui.IsKeyPressed(ImGuiKey.End)) next = vis.Count - 1;
        next = Math.Clamp(next, 0, vis.Count - 1);
        if (next != pos || !vis.Contains(_state.KeyboardFocusIndex)) SetFocus(vis[next], extendSelection: false);
    }

    private void HandleTreeKeyboardNavigation()
    {
        int count = _state.VisualRows.Count;
        if (count == 0) return;

        ref int focusRow = ref _state.TreeFocusRow;
        int oldFocusRow = focusRow;

        // ↑/↓ — move focus through visual rows.
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            focusRow = Math.Max(0, focusRow - 1);
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            focusRow = Math.Min(count - 1, focusRow + 1);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
            focusRow = Math.Max(0, focusRow - 10);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
            focusRow = Math.Min(count - 1, focusRow + 10);
        else if (ImGui.IsKeyPressed(ImGuiKey.Home))
            focusRow = 0;
        else if (ImGui.IsKeyPressed(ImGuiKey.End))
            focusRow = count - 1;

        // One-shot scroll to the newly focused row (BATCH-52: only on keyboard focus change).
        if (focusRow != oldFocusRow)
            _state.ScrollToFocus = true;

        // → — request expand for focused folder (A16).
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
        {
            var row = _state.VisualRows[focusRow];
            if (row.IsFolder)
            {
                _state.PendingToggleFolderPath = row.FolderPath;
                _state.PendingToggleOpen = true;
            }
        }

        // ← — request collapse for focused folder (A16).
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
        {
            var row = _state.VisualRows[focusRow];
            if (row.IsFolder)
            {
                _state.PendingToggleFolderPath = row.FolderPath;
                _state.PendingToggleOpen = false;
            }
        }

        // Sync KeyboardFocusIndex from tree focus: mirror leaf's FilteredIndex
        // so the existing leaf highlight + default-confirm logic continues to work;
        // clear it when a folder is focused.
        SyncTreeFocusToLeaf();
    }

    private void SyncTreeFocusToLeaf()
    {
        if (_state.VisualRows.Count == 0) return;

        int tf = _state.TreeFocusRow;
        if (tf < 0 || tf >= _state.VisualRows.Count) return;

        var row = _state.VisualRows[tf];
        if (row.IsFolder)
        {
            _state.KeyboardFocusIndex = -1;
            if (_state.SelectionMode == PickerSelectionMode.Single)
                _state.SelectedFilteredIndices.Clear();
        }
        else
        {
            _state.KeyboardFocusIndex = row.FilteredIndex;
            if (_state.SelectionMode == PickerSelectionMode.Single)
            {
                _state.SelectedFilteredIndices.Clear();
                _state.SelectedFilteredIndices.Add(row.FilteredIndex);
            }
        }
    }

    private void MoveFocus(int delta, int count, bool extendSelection)
        => SetFocus(Math.Clamp(_state.KeyboardFocusIndex + delta, 0, count - 1), extendSelection);

    private void SetFocus(int idx, bool extendSelection)
    {
        if (_state.SelectionMode == PickerSelectionMode.Single)
        {
            _state.SelectedFilteredIndices.Clear();
            _state.SelectedFilteredIndices.Add(idx);
        }
        else
        {
            if (extendSelection)
            {
                // Anchor geometrically to the start of the Shift sequence
                int anchor = _state.SelectionAnchorIndex;
                int lo = Math.Min(anchor, idx);
                int hi = Math.Max(anchor, idx);
                _state.HighlightedIndices.Clear();
                for (int k = lo; k <= hi; k++)
                    _state.HighlightedIndices.Add(k);
            }
            else
            {
                // Establish a new anchor point
                _state.SelectionAnchorIndex = idx;
                _state.HighlightedIndices.Clear();
                _state.HighlightedIndices.Add(idx);
            }
        }
        _state.KeyboardFocusIndex = idx;
        _state.ScrollToFocus = true;
    }

    // ── confirm / cancel ──────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ CE-4098 — the Recent section of the OPEN picker as <see cref="PickerEntry"/>s, last picked first: what a layout draws before
    /// any other row. Empty when the picker is closed.
    /// </summary>
    internal IReadOnlyList<PickerEntry> RecentSection()
        => _isOpen
            ? _state.RecentFilteredIndices().Select(i => _state.Filtered[i].Entry).ToList()
            : Array.Empty<PickerEntry>();

    /// <summary>
    /// ⭐ CE-4098 — confirms the entry <paramref name="id"/> exactly as OK / double-click / Enter would (same <see cref="Confirm"/>: a disabled
    /// entry never confirms; the pick is recorded in its kind's Recent list; the host's callback runs). False when the picker is closed or
    /// the entry is not listed — a headless rail's stand-in for the click.
    /// </summary>
    internal bool ConfirmEntry(string id, string? actionId = null)
    {
        if (!_isOpen) return false;
        int idx = _state.Filtered.FindIndex(r => r.Entry.Id == id);
        if (idx < 0) return false;
        _state.SelectedFilteredIndices.Clear();
        _state.SelectedFilteredIndices.Add(idx);
        _state.KeyboardFocusIndex = idx;
        Confirm(actionId);
        return !_isOpen;
    }

    /// <param name="actionId">⭐ CE-4168 (P3) — the footer action clicked; null = Enter / double-click ⇒ the request's default action.</param>
    private void Confirm(string? actionId = null)
    {
        // Tree layout guard: if the focused row is a folder, expand it instead of
        // confirming a wrong item (e.g. when user clicks OK while a folder is focused).
        if (_layout == PickerLayout.Tree && _state.VisualRows.Count > 0)
        {
            int tf = _state.TreeFocusRow;
            if (tf >= 0 && tf < _state.VisualRows.Count && _state.VisualRows[tf].IsFolder)
            {
                var path = _state.VisualRows[tf].FolderPath;
                _state.PendingToggleFolderPath = path;
                _state.PendingToggleOpen = true;
                return;
            }
        }

        // Default confirm (nothing selected): the focused item. Disabled entries never confirm; if ONLY
        // disabled ones were picked, stay open rather than return an empty choice.
        var selected = _state.ConfirmableSelection(out bool onlyDisabled);
        if (onlyDisabled)
        {
            _state.Confirmed = false;
            return;
        }

        // ⭐ CE-4168 (P3) — with actions, the confirm runs one of them; a disabled (or unknown) action never confirms.
        var action = PickerAction.Resolve(_actions, actionId, selected.FirstOrDefault()?.Entry, out bool blocked, out _);
        if (blocked)
        {
            _state.Confirmed = false;
            return;
        }

        // Update recent store (⭐ CE-4098: each entry's own kind list).
        _state.RecordPick(selected);

        if (_onPickResult is not null)
        {
            var result = new PickerResult(selected.Select(r => r.Entry).ToArray())
            {
                ActionId  = action?.Id,
                PaneState = _capturePaneState?.Invoke(selected.FirstOrDefault()?.Entry),
            };
            _isOpen = false;
            _onPickResult(result);
        }
        else if (_onPickRaw is not null && selected.Count > 0)
        {
            var raw = selected[0].Entry.Tag;
            _isOpen = false;
            if (raw is not null) _onPickRaw(raw);
        }
        else
        {
            _isOpen = false;
        }
    }

    private void Cancel()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _onCancel?.Invoke();
        _onPickResult?.Invoke(new PickerResult(Array.Empty<PickerEntry>()));
        _onPickResult = null;
        _onPickRaw    = null;
        _onCancel     = null;
        _adapter      = null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private PickerRenderContext BuildRenderContext() => new()
    {
        Icons = Icons ?? NullIconProvider.Instance,
        Theme = Theme ?? DefaultPickerTheme.Instance,
    };
}

// ── null-object helpers ───────────────────────────────────────────────────────

file sealed class NullIconProvider : IIconProvider
{
    public static readonly NullIconProvider Instance = new();
    public bool TryGet(string key, out IconHandle handle) { handle = default; return false; }
}

file sealed class DefaultPickerTheme : IEditorTheme
{
    public static readonly DefaultPickerTheme Instance = new();
    public Vector4 BackgroundColor         => new(0.12f, 0.12f, 0.12f, 1f);
    public Vector4 GridMinorColor          => new(0.2f, 0.2f, 0.2f, 1f);
    public Vector4 GridMajorColor          => new(0.3f, 0.3f, 0.3f, 1f);
    public Vector4 SelectionAccent         => new(0.26f, 0.59f, 0.98f, 1f);
    public Vector4 PrimarySelectionAccent  => new(0.26f, 0.59f, 0.98f, 1f);
    public Vector4 ErrorColor              => new(1f, 0.3f, 0.3f, 1f);
    public Vector4 WarningColor            => new(1f, 0.8f, 0.2f, 1f);
    public Vector4 TextDefault             => new(0.9f, 0.9f, 0.9f, 1f);
    public Vector4 TextMuted               => new(0.6f, 0.6f, 0.6f, 0.8f);
    public float NodeCornerRadius          => 4f;
    public float NodeBorderThickness       => 1.5f;
    public float NodeHeaderHeight          => 24f;
    public float PinGlyphSize              => 8f;
    public float WireThicknessExec         => 3f;
    public float WireThicknessData         => 2f;
    public Vector4 GetCategoryHeaderColor(NodeEditor.Primitives.NodeCategory c) => new(0.2f, 0.2f, 0.3f, 1f);
    public nint GetFontForSize(float targetPixelSize) => 0;
}
