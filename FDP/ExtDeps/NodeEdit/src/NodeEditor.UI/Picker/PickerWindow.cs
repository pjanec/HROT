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

    // ── size helpers ──────────────────────────────────────────────────────────

    private Vector2 GetDefaultSize() => _layout switch
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
        _layout         = request.Layout;
        _selectionMode  = request.SelectionMode;
        _screenPos      = request.AnchorScreen;
        _categoryRoot   = request.CategoryRoot;
        _requestEntries = request.ItemsProvider().ToArray();
        _adapter        = null;
        _adapterContext = null;
        _onPickResult   = onChosen;
        _onPickRaw      = null;
        _onCancel       = null;

        _state.Reset(request.ContextKey, request.InitialQuery);
        _state.AllEntries = _requestEntries;
        _state.Refilter();

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
        _requestEntries = null;
        _onPickRaw      = onPick;
        _onPickResult   = null;
        _onCancel       = onCancel;

        _state.Reset(contextKey, "");

        // For cheap/sync sources, pre-load items immediately.
        if (!adapter.IsAsync)
        {
            var items = adapter.Query("", context);
            _state.AllEntries = items.Select(it => new PickerEntry(
                it.Key, it.SearchText, null, null, null, null, it.Raw)).ToArray();
            _state.Refilter();
        }

        _isOpen = true;
    }

    // ── DrawFrame ─────────────────────────────────────────────────────────────

    /// <summary>Render the picker window. Call once per ImGui frame.</summary>
    public void DrawFrame()
    {
        if (!_isOpen) return;

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

        if (!windowOpen)
        {
            ImGui.End();
            Cancel();
            return;
        }

        if (!visible)
        {
            ImGui.End();
            return;
        }

        DrawContent();
        ImGui.End();
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
        ImGui.SetNextItemWidth(-1f);

        if (_state.FocusSearchNextFrame)
        {
            ImGui.SetKeyboardFocusHere();
            _state.FocusSearchNextFrame = false;
        }

        string buf = _state.SearchText;
        if (ImGui.InputText("##picker_search", ref buf, 256))
            _state.SearchText = buf;

        // ESC handled in footer / key polling.
    }

    private void DrawBody()
    {
        var ctx = BuildRenderContext();

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
                TreeLayout.Draw(_state, ctx, _categoryRoot);
                break;

            default: // Standard
                StandardLayout.Draw(_state, ctx);
                break;
        }
    }

    private void DrawFooter()
    {
        HandleKeyboardNavigation();

        if (_state.Confirmed || ImGui.Button("OK") || ImGui.IsKeyPressed(ImGuiKey.Enter))
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

    private void HandleKeyboardNavigation()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow))
            return;

        int count = _state.Filtered.Count;
        if (count == 0) return;

        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            MoveFocus(-1, count);
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            MoveFocus(1, count);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
            MoveFocus(-10, count);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
            MoveFocus(10, count);
        else if (ImGui.IsKeyPressed(ImGuiKey.Home))
            SetFocus(0);
        else if (ImGui.IsKeyPressed(ImGuiKey.End))
            SetFocus(count - 1);
    }

    private void MoveFocus(int delta, int count)
        => SetFocus(Math.Clamp(_state.KeyboardFocusIndex + delta, 0, count - 1));

    private void SetFocus(int idx)
    {
        _state.KeyboardFocusIndex = idx;
        _state.SelectedFilteredIndices.Clear();
        _state.SelectedFilteredIndices.Add(idx);
    }

    // ── confirm / cancel ──────────────────────────────────────────────────────

    private void Confirm()
    {
        if (_state.SelectedFilteredIndices.Count == 0 && _state.Filtered.Count > 0)
        {
            // Default confirm: first item.
            _state.SelectedFilteredIndices.Add(_state.KeyboardFocusIndex >= 0
                ? _state.KeyboardFocusIndex : 0);
        }

        var selected = _state.SelectedFilteredIndices
            .Where(i => i >= 0 && i < _state.Filtered.Count)
            .OrderBy(i => i)
            .Select(i => _state.Filtered[i])
            .ToList();

        // Update recent store.
        foreach (var re in selected)
            _state.Recent.Push(_state.ContextKey, re.Entry.Id);

        if (_onPickResult is not null)
        {
            var result = new PickerResult(selected.Select(r => r.Entry).ToArray());
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
