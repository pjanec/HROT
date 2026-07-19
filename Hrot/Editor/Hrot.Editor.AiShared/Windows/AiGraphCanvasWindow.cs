using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Core.Action;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Find;

namespace Hrot.Editor.AiShared.Windows;

// ── Canvas context ────────────────────────────────────────────────────────────

/// <summary>
/// Opaque context stored in <see cref="AiDocument.ViewState"/> by a document factory.
/// Carries the constructed <see cref="GraphView"/> and the asset-kind string so the
/// canvas window can verify it owns the right kind of document.
/// </summary>
public sealed class AiCanvasContext
{
    /// <summary>The NodeEdit graph view (graph model + selection + viewport state).</summary>
    public GraphView View { get; }

    /// <summary>Asset kind name ("BTree", "HSM", …) for this context.</summary>
    public string Kind { get; }

    /// <summary>
    /// Optional opaque reference to the host's backing asset model.
    /// Set by the document factory (e.g. Blueprint sets this to the
    /// <c>BlueprintAsset</c>) so the composition root can retrieve it without
    /// adding a kind-specific dependency to this shared assembly.
    /// </summary>
    public object? AssetRef { get; set; }

    /// <summary>
    /// Optional find bar built by the document factory and threaded into the canvas render call.
    /// When non-null, Ctrl+F opens the find overlay for this document.
    /// </summary>
    public FindBar? FindBar { get; set; }

    /// <summary>
    /// Optional editor-command dispatcher built by the document factory.
    /// When non-null, canvas context menus and keyboard shortcuts are wired via
    /// <see cref="BuiltinCommandHandlers.RegisterAll"/>.
    /// </summary>
    public IEditorCommands? Commands { get; set; }

    /// <summary>
    /// Optional per-document bookmark store built by the document factory. When non-null,
    /// the composition root can draw the off-screen bookmark edge-marker overlay (see
    /// <c>BlueprintEditorBootstrap.DrawBookmarkEdgeMarkers</c>) and/or a Bookmarks panel
    /// window for this document. Set/jump commands are registered directly on
    /// <see cref="Commands"/> by the document factory (Ctrl+1..9 / Ctrl+Shift+1..9), so this
    /// property only needs to be read by the rendering/overlay path.
    /// </summary>
    public BookmarkStore? Bookmarks { get; set; }

    /// <summary>
    /// Creates a canvas context.
    /// </summary>
    /// <param name="view">Constructed graph view for the document.</param>
    /// <param name="kind">Asset kind name matching the owning perspective.</param>
    public AiCanvasContext(GraphView view, string kind)
    {
        View = view  ?? throw new ArgumentNullException(nameof(view));
        Kind = kind  ?? throw new ArgumentNullException(nameof(kind));
    }
}

// ── Seam interface for headless tests ────────────────────────────────────────

/// <summary>
/// Seam that abstracts the <c>CanvasRenderer.Render</c> call so headless unit
/// tests can intercept it without an ImGui context.
/// </summary>
public interface ICanvasRenderSeam
{
    /// <summary>Render the given view into the current ImGui content region.</summary>
    void Render(GraphView view);

    /// <summary>
    /// Render the given view with optional find bar and editor commands.
    /// Default implementation delegates to <see cref="Render(GraphView)"/>.
    /// </summary>
    void Render(GraphView view, FindBar? findBar, IEditorCommands? commands)
        => Render(view);
}

// ── AiGraphCanvasWindow ───────────────────────────────────────────────────────

/// <summary>
/// Per-perspective <see cref="ManagedWindow"/> that renders the active document's
/// <see cref="GraphView"/> via the NodeEdit canvas pipeline.
///
/// <para>
/// <b>Design contract:</b>
/// <list type="bullet">
///   <item>The window does <b>not</b> build <c>GraphView</c> instances — that is the
///   responsibility of the per-kind document factory (AIE-021 / AIE-022). The factory
///   stores an <see cref="AiCanvasContext"/> in <see cref="AiDocument.ViewState"/> and
///   this window simply retrieves and renders it.</item>
///   <item>On <c>DrawClientArea</c>: if no active document exists for this perspective →
///   empty-state text; otherwise the cached context's view is passed to the render seam.</item>
///   <item>On ImGui focus (<c>IsActive</c> first frame) → <c>AiDocumentManager.Activate</c>
///   is called so the perspective switches accordingly.</item>
///   <item>All ImGui calls are guarded by <c>ImGui.GetCurrentContext() != IntPtr.Zero</c> so
///   the window can be constructed and driven in headless unit tests.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AiGraphCanvasWindow : ManagedWindow
{
    private readonly AiDocumentManager _docManager;
    private readonly string            _assetKind;
    private readonly ICanvasRenderSeam _renderer;

    // BCP-BATCH-02-FIX Task 1: the shared picker registry whose DrawFrame() must run
    // once per frame so an opened picker (TAB add-node, wire-drop-to-empty) is visible
    // and can close. Null only in legacy headless tests that do not exercise the picker.
    private readonly IPickerRegistry? _pickers;

    // BCP-BATCH-02-FIX Task 1: per-frame hotkey pump (Ctrl+F find, etc.). Null only in
    // legacy headless tests that do not supply a host input source.
    private readonly EditorHotkeyDispatcher? _hotkeys;

    // MULTI-TAB: raw host input, kept alongside _hotkeys so the Alt+Left back-navigation
    // check can read modifiers/keys directly (EditorHotkeyDispatcher only dispatches
    // registered IEditorCommands bindings). Null in legacy headless tests.
    private readonly IInputSource? _input;

    // BCP-BATCH-02-FIX Task 2: stable base title (the "{assetKind} Canvas" empty-state
    // label). The dynamic title is rebuilt from this + the active document name.
    private readonly string _baseTitle;

    // Track whether focus was already activated this activation cycle.
    private AiDocument? _lastActivatedDoc;

    // MULTI-TAB: the document the tab bar last synced its ImGui-selected tab to. When
    // AiDocumentManager.Active changes without going through a tab click in THIS window
    // (e.g. the asset browser opened/activated a document), the matching tab is given
    // ImGuiTabItemFlags.SetSelected for one frame so ImGui's own selection follows.
    private AiDocument? _lastSyncedActive;

    // MULTI-TAB: per-window "navigate back" history (Alt+Left). Push happens when the
    // active document (of this window's kind) changes to something new; pop happens on
    // Alt+Left. _suppressHistoryPush guards against the back-navigation activation
    // itself being pushed back onto the stack.
    private readonly Stack<AiDocument> _backHistory = new();
    private AiDocument? _historyTrackedDoc;
    private bool _suppressHistoryPush;

    /// <summary>
    /// Optional per-frame callback invoked at the end of <see cref="DrawClientArea"/> when an
    /// active document context is present.  Receives the active <see cref="AiCanvasContext"/>.
    /// Used to wire cross-cutting per-frame logic (e.g. selection→details bridge) without
    /// requiring a subclass of the sealed window.
    /// </summary>
    public Action<AiCanvasContext>? AfterDraw { get; set; }

    // BCP-BATCH-02-FIX Task 2: the document whose name is currently reflected in Title,
    // so we only rebuild the title string when the active document actually changes.
    private AiDocument? _titleDoc;

    /// <summary>
    /// Constructs a graph canvas window.
    /// </summary>
    /// <param name="assetKind">
    ///   Asset kind string (e.g. <c>"BTree"</c>, <c>"HSM"</c>). Must match
    ///   <see cref="AiCanvasContext.Kind"/> stored in the document's view state.
    /// </param>
    /// <param name="docManager">
    ///   The shared document manager; used to resolve the active document and to
    ///   call <see cref="AiDocumentManager.Activate"/> on focus.
    /// </param>
    /// <param name="renderer">
    ///   Canvas render seam. In production supply a <see cref="ProductionCanvasRenderSeam"/>
    ///   wrapping a <c>CanvasRenderer</c>; in tests supply a spy/fake.
    /// </param>
    /// <param name="pickers">
    ///   Shared picker registry (from <c>AiEditorAdapterBundle.PickerRegistry</c>). Its
    ///   <see cref="IPickerRegistry.DrawFrame"/> is called once per frame so an opened
    ///   picker is rendered and can close (BCP-BATCH-02-FIX Task 1). May be <c>null</c>
    ///   in headless tests that do not exercise the picker overlay.
    /// </param>
    /// <param name="input">
    ///   Host input source used to drive the per-frame <see cref="EditorHotkeyDispatcher"/>
    ///   (Ctrl+F find and other command shortcuts). May be <c>null</c> in headless tests.
    /// </param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id suffix. When <c>null</c> a default of
    ///   <c>"ai_canvas_{assetKind.ToLowerInvariant()}"</c> is used.
    /// </param>
    public AiGraphCanvasWindow(
        string            assetKind,
        AiDocumentManager docManager,
        ICanvasRenderSeam renderer,
        IPickerRegistry?  pickers    = null,
        IInputSource?     input      = null,
        string?           idOverride = null)
        : base(
            id:                 idOverride ?? $"ai_canvas_{assetKind.ToLowerInvariant()}",
            title:              $"{assetKind} Canvas",
            owningPerspective:  assetKind,
            scope:              WindowScope.PerspectiveBound)
    {
        _assetKind  = assetKind  ?? throw new ArgumentNullException(nameof(assetKind));
        _docManager = docManager ?? throw new ArgumentNullException(nameof(docManager));
        _renderer   = renderer   ?? throw new ArgumentNullException(nameof(renderer));
        _pickers    = pickers;
        _hotkeys    = input != null ? new EditorHotkeyDispatcher(input) : null;
        _input      = input;
        _baseTitle  = $"{assetKind} Canvas";

        IsOpen = true;
    }

    /// <summary>
    /// Resolves the active document for this perspective.
    /// Returns <c>null</c> when no document of the matching kind is active.
    /// </summary>
    public AiDocument? ActiveDocument
    {
        get
        {
            var active = _docManager.Active;
            if (active == null) return null;
            // Compare case-insensitively: AssetKind.Hsm.ToString() == "Hsm"
            // but perspective names may use "HSM" or "BTree".
            return string.Equals(active.Kind.ToString(), _assetKind,
                StringComparison.OrdinalIgnoreCase) ? active : null;
        }
    }

    /// <summary>
    /// Resolves the canvas context for the active document, or <c>null</c> when no
    /// document is active or its <see cref="AiDocument.ViewState"/> has not been
    /// populated yet.
    /// </summary>
    public AiCanvasContext? ActiveContext
    {
        get
        {
            var doc = ActiveDocument;
            return doc?.ViewState as AiCanvasContext;
        }
    }

    // ── ManagedWindow implementation ─────────────────────────────────────────

    /// <inheritdoc/>
    protected override void DrawClientArea()
    {
        // Focus gate: if ImGui reports this window gained focus and the active doc is
        // different from the last activation, call Activate.  Gate the ImGui call for
        // headless safety.
        var doc = ActiveDocument;

        if (ImGuiAvailable())
            HandleFocusActivation(doc);

        // BCP-BATCH-02-FIX Task 2: reflect the active asset name in the window title,
        // keeping the stable "###id" so docking identity is preserved. Empty-state title
        // when no document is active.
        UpdateTitle(doc);

        // MULTI-TAB: host the tab bar above the canvas (only when ≥1 document of this
        // perspective's kind is open — otherwise fall through to the existing empty state).
        // A tab click re-activates the clicked document immediately via AiDocumentManager;
        // Alt+Left pops the per-window back-navigation history built from activation changes.
        if (ImGuiAvailable())
        {
            DrawTabBar();
            HandleBackNavigationHotkey();
        }

        // Re-resolve: a tab click (or Alt+Left) above may have activated a different
        // document of this window's kind within this same frame — reflect it immediately
        // rather than lagging a frame behind.
        doc = ActiveDocument;
        UpdateTitle(doc);
        TrackHistory(doc);

        if (doc == null || ActiveContext == null)
        {
            DrawEmptyState();
            return;
        }

        var context = ActiveContext;

        // Render the cached GraphView via the seam, threading FindBar and Commands when present.
        _renderer.Render(context.View, context.FindBar, context.Commands);

        // BCP-BATCH-02-FIX Task 1 (THE key fix): draw the picker overlay and pump command
        // hotkeys once per frame, gated behind an available ImGui context for headless safety.
        // Without DrawFrame the TAB add-node picker / wire-drop picker open invisibly and the
        // interaction Mode sticks (PickerOpen / PendingWire), killing RMB-pan, context menu and
        // wire-drag (all of which live in HandleIdle).
        if (ImGuiAvailable())
        {
            // Yield command hotkeys while the user is typing into a text field so we do not
            // steal keystrokes from inline editors / search boxes.
            bool wantText = ImGuiNET.ImGui.GetIO().WantTextInput;
            DrawPickerAndPumpHotkeys(context, suppressHotkeys: wantText);
        }

        // BF-UX1 FIX C: per-frame hook for cross-cutting logic (e.g. selection→details bridge).
        AfterDraw?.Invoke(context);
    }

    /// <summary>
    /// Draws the shared picker overlay once and pumps the per-frame command hotkeys.
    /// Extracted from <see cref="DrawClientArea"/> so it can be exercised headlessly
    /// via <see cref="SimulatePickerAndHotkeyFrame"/> (the <c>DrawFrame</c> spy and the
    /// hotkey dispatcher do not require an ImGui context themselves).
    /// </summary>
    /// <param name="context">The active canvas context (supplies the command set).</param>
    /// <param name="suppressHotkeys">
    /// When <c>true</c>, the hotkey pump is skipped (used when the user is typing into a
    /// text field so command shortcuts do not steal keystrokes).
    /// </param>
    private void DrawPickerAndPumpHotkeys(AiCanvasContext context, bool suppressHotkeys)
    {
        _pickers?.DrawFrame();

        if (!suppressHotkeys)
            _hotkeys?.ProcessThisFrame(context.Commands);
    }

    /// <summary>
    /// Sets <see cref="ManagedWindow.Title"/> to include the active document's asset name
    /// when the active document changes. Keeps the stable <c>###Id</c> suffix (provided by
    /// <see cref="ManagedWindow"/>) so ImGui dock identity is preserved across title changes.
    /// </summary>
    private void UpdateTitle(AiDocument? doc)
    {
        if (ReferenceEquals(doc, _titleDoc)) return;
        _titleDoc = doc;

        var assetName = doc?.Asset?.Name;
        // Use an ASCII hyphen separator: the engine ImGui font cannot render an em-dash
        // ("—"), which showed up as "?" in the window title (BCP-BATCH-02-FIX2 Task 4).
        Title = string.IsNullOrEmpty(assetName)
            ? _baseTitle
            : $"{assetName} - {_assetKind}";
    }

    // ── MULTI-TAB: tab bar ────────────────────────────────────────────────────

    /// <summary>
    /// The open documents belonging to this window's perspective kind, in
    /// <see cref="AiDocumentManager.OpenDocuments"/> order. Backs the tab bar; exposed
    /// (read-only) so the projection logic is headlessly testable without ImGui.
    /// </summary>
    public IReadOnlyList<AiDocument> TabDocuments =>
        _docManager.OpenDocuments
            .Where(d => string.Equals(d.Kind.ToString(), _assetKind, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Builds the ImGui tab label for <paramref name="doc"/>: a per-kind FontAwesome glyph,
    /// the asset name, and a stable <c>###AssetId</c> suffix so ImGui keeps the tab's identity
    /// across reorders/renames. Exposed internally for headless label-format tests.
    /// </summary>
    internal static string GetTabLabel(AiDocument doc) =>
        $"{GetTabGlyph(doc)} {doc.Asset.Name}###{doc.Asset.AssetId}";

    /// <summary>
    /// Resolves the FontAwesome glyph for a document's tab: prefers a finer-grained
    /// <see cref="IAssetIconKeyProvider.IconKey"/> when the backing asset supplies one
    /// (e.g. a Blueprint's Action/Condition/Function intent), otherwise falls back to a
    /// per-<see cref="AssetKind"/> default. Kept kind-agnostic — no blueprint-specific types.
    /// </summary>
    internal static string GetTabGlyph(AiDocument doc)
    {
        if (doc.Asset is IAssetIconKeyProvider iconProvider && !string.IsNullOrEmpty(iconProvider.IconKey))
        {
            switch (iconProvider.IconKey)
            {
                case AssetKindIcons.BlueprintActionIconKey:    return IconsFontAwesome6.Bolt;
                case AssetKindIcons.BlueprintConditionIconKey: return IconsFontAwesome6.CircleQuestion;
                case AssetKindIcons.BlueprintFunctionIconKey:  return IconsFontAwesome6.Gear;
            }
        }

        return doc.Kind switch
        {
            AssetKind.Blueprint  => IconsFontAwesome6.Bolt,
            AssetKind.BTree      => IconsFontAwesome6.Sitemap,
            AssetKind.Hsm        => IconsFontAwesome6.CircleNodes,
            AssetKind.Blackboard => IconsFontAwesome6.LayerGroup,
            AssetKind.Utility    => IconsFontAwesome6.Gear,
            AssetKind.Scenario   => IconsFontAwesome6.DiagramProject,
            _                    => IconsFontAwesome6.File,
        };
    }

    /// <summary>
    /// Draws the multi-tab bar for this perspective's open documents (skipped entirely when
    /// none are open, so the existing empty state is unaffected). A tab click activates the
    /// clicked document via <see cref="AiDocumentManager.Activate"/>; the X close button calls
    /// <see cref="AiDocumentManager.Close"/> after the loop (save-on-close is already wired via
    /// <c>BeforeDocumentClosed</c> upstream — no save prompt needed here). <c>TabListPopupButton</c>
    /// gives the ▾ dropdown listing every open tab by name, since asset names can be long.
    /// </summary>
    private void DrawTabBar()
    {
        var docs = TabDocuments;
        if (docs.Count == 0) return;

        var active = _docManager.Active;
        // Sync the ImGui-selected tab to the manager's Active doc only when Active changed
        // since the last frame we drew this bar (e.g. the asset browser activated a document) —
        // otherwise we'd fight the user's own tab clicks every frame.
        bool syncSelection = !ReferenceEquals(active, _lastSyncedActive);

        if (!ImGuiNET.ImGui.BeginTabBar("##ai_graph_tabs",
                ImGuiNET.ImGuiTabBarFlags.TabListPopupButton
              | ImGuiNET.ImGuiTabBarFlags.AutoSelectNewTabs
              | ImGuiNET.ImGuiTabBarFlags.NoCloseWithMiddleMouseButton))
        {
            return;
        }

        List<AiDocument>? toClose = null;

        foreach (var d in docs)
        {
            bool tabOpen = true;
            var flags = ImGuiNET.ImGuiTabItemFlags.None;
            if (syncSelection && ReferenceEquals(d, active))
                flags |= ImGuiNET.ImGuiTabItemFlags.SetSelected;

            if (ImGuiNET.ImGui.BeginTabItem(GetTabLabel(d), ref tabOpen, flags))
            {
                if (!ReferenceEquals(d, _docManager.Active))
                    _docManager.Activate(d);
                ImGuiNET.ImGui.EndTabItem();
            }

            if (!tabOpen)
                (toClose ??= new List<AiDocument>()).Add(d);
        }

        ImGuiNET.ImGui.EndTabBar();

        _lastSyncedActive = _docManager.Active;

        // Close after the loop so we never mutate _docManager.OpenDocuments mid-iteration.
        if (toClose != null)
            foreach (var d in toClose)
                _docManager.Close(d);
    }

    // ── MULTI-TAB: Alt+Left "navigate back" history ──────────────────────────

    /// <summary>
    /// Records activation changes for this window's kind into <see cref="_backHistory"/>.
    /// Skips the push when the change was caused by <see cref="NavigateBack"/> itself
    /// (<see cref="_suppressHistoryPush"/>), and never pushes a document that has since
    /// been closed.
    /// </summary>
    private void TrackHistory(AiDocument? doc)
    {
        if (doc == null)
        {
            _historyTrackedDoc = null;
            return;
        }
        if (ReferenceEquals(doc, _historyTrackedDoc)) return;

        if (!_suppressHistoryPush
            && _historyTrackedDoc != null
            && _docManager.OpenDocuments.Contains(_historyTrackedDoc))
        {
            _backHistory.Push(_historyTrackedDoc);
        }

        _suppressHistoryPush = false;
        _historyTrackedDoc = doc;
    }

    /// <summary>
    /// Checks for the Alt+Left chord this frame (skipped while the user is typing into a
    /// text field, mirroring the hotkey-pump gate) and navigates back when pressed.
    /// No-op when this window was constructed without a host <see cref="IInputSource"/>.
    /// </summary>
    private void HandleBackNavigationHotkey()
    {
        if (_input == null) return;
        if (!ImGuiNET.ImGui.IsWindowFocused(ImGuiNET.ImGuiFocusedFlags.ChildWindows)) return;
        if (ImGuiNET.ImGui.GetIO().WantTextInput) return;
        if (_input.Modifiers != KeyModifiers.Alt) return;
        if (!_input.IsKeyPressed(EditorKey.Left, allowRepeat: false)) return;

        NavigateBack();
    }

    /// <summary>
    /// Pops the most recent still-open, non-active document off <see cref="_backHistory"/>
    /// and activates it. Stale entries (documents closed since they were pushed) are
    /// discarded. No-op when the history is empty or only contains stale/current entries.
    /// </summary>
    private void NavigateBack()
    {
        while (_backHistory.Count > 0)
        {
            var prev = _backHistory.Pop();
            if (!_docManager.OpenDocuments.Contains(prev)) continue;
            if (ReferenceEquals(prev, _docManager.Active)) continue;

            _suppressHistoryPush = true;
            _docManager.Activate(prev);
            return;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool ImGuiAvailable() =>
        ImGuiNET.ImGui.GetCurrentContext() != IntPtr.Zero;

    private void HandleFocusActivation(AiDocument? doc)
    {
        // ImGui.IsWindowFocused() returns true when the window has keyboard focus.
        if (!ImGuiNET.ImGui.IsWindowFocused(ImGuiNET.ImGuiFocusedFlags.ChildWindows)) return;
        if (doc == null) return;
        if (doc == _lastActivatedDoc) return;

        _lastActivatedDoc = doc;
        _docManager.Activate(doc);
    }

    private static void DrawEmptyState()
    {
        if (!ImGuiAvailable()) return;
        ImGuiNET.ImGui.TextDisabled("No asset open. Double-click an asset in the Browser to open it.");
    }

    // ── Test seam: allow tests to invoke focus logic directly ─────────────────

    /// <summary>
    /// Test hook: simulates the window receiving ImGui focus for <paramref name="doc"/>.
    /// Calls <see cref="AiDocumentManager.Activate"/> exactly once per unique document
    /// (mirrors the production focus path without requiring an ImGui context).
    /// </summary>
    public void SimulateFocus(AiDocument doc)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (doc == _lastActivatedDoc) return;
        _lastActivatedDoc = doc;
        _docManager.Activate(doc);
    }

    /// <summary>
    /// Test hook: runs the non-ImGui portion of <see cref="DrawClientArea"/> for the active
    /// document — updates <see cref="ManagedWindow.Title"/> from the asset name and, when a
    /// context is present, draws the picker overlay and pumps command hotkeys.
    /// Mirrors the production per-frame path without requiring an ImGui context.
    /// </summary>
    /// <param name="suppressHotkeys">
    /// Simulates the "user is typing into a text field" gate that suppresses the hotkey pump.
    /// </param>
    internal void SimulateDrawClientArea(bool suppressHotkeys = false)
    {
        var doc = ActiveDocument;
        UpdateTitle(doc);
        TrackHistory(doc);

        var context = ActiveContext;
        if (doc == null || context == null) return;

        DrawPickerAndPumpHotkeys(context, suppressHotkeys);
    }

    // ── MULTI-TAB: test seams (mirror production tab-bar / back-nav logic, no ImGui) ──

    /// <summary>
    /// Test hook: simulates clicking <paramref name="doc"/>'s tab (mirrors the production
    /// <c>BeginTabItem</c> branch — activates the document unless it is already active).
    /// </summary>
    public void SimulateTabClick(AiDocument doc)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (!ReferenceEquals(doc, _docManager.Active))
            _docManager.Activate(doc);
    }

    /// <summary>
    /// Test hook: simulates clicking a tab's X close button for <paramref name="doc"/>
    /// (mirrors production: calls <see cref="AiDocumentManager.Close"/> directly — no save
    /// prompt, since <c>BeforeDocumentClosed</c> already flushes dirty documents upstream).
    /// </summary>
    public void SimulateTabClose(AiDocument doc)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        _docManager.Close(doc);
    }

    /// <summary>
    /// Test hook: simulates the Alt+Left back-navigation chord without requiring ImGui or a
    /// host <see cref="IInputSource"/>. Mirrors <see cref="NavigateBack"/> exactly.
    /// </summary>
    public void SimulateBackNavigation() => NavigateBack();
}

// ── Production seam wrapping CanvasRenderer ───────────────────────────────────

/// <summary>
/// Production <see cref="ICanvasRenderSeam"/> that delegates to a
/// <c>NodeEditor.UI.Canvas.CanvasRenderer</c>.  Construction is deferred to avoid
/// a dependency on NodeEditor.UI from <c>Hrot.Editor.AiShared</c>; the caller
/// supplies delegates that close over the renderer instance.
/// </summary>
public sealed class DelegatingCanvasRenderSeam : ICanvasRenderSeam
{
    private readonly Action<GraphView> _renderDelegate;
    private readonly Action<GraphView, FindBar?, IEditorCommands?>? _renderWithFindBar;

    /// <summary>
    /// Creates the seam with an optional find-bar-aware render delegate.
    /// </summary>
    /// <param name="renderDelegate">
    ///   Delegate that calls <c>canvasRenderer.Render(view, null)</c>
    ///   for the supplied view (used when no find-bar delegate is given).
    /// </param>
    /// <param name="renderWithFindBar">
    ///   Optional delegate that calls <c>canvasRenderer.Render(view, findBar, commands)</c>.
    ///   When supplied, the three-argument overload is used instead of the fallback.
    /// </param>
    public DelegatingCanvasRenderSeam(
        Action<GraphView> renderDelegate,
        Action<GraphView, FindBar?, IEditorCommands?>? renderWithFindBar = null)
    {
        _renderDelegate    = renderDelegate    ?? throw new ArgumentNullException(nameof(renderDelegate));
        _renderWithFindBar = renderWithFindBar;
    }

    /// <inheritdoc/>
    public void Render(GraphView view) => _renderDelegate(view);

    /// <inheritdoc/>
    public void Render(GraphView view, FindBar? findBar, IEditorCommands? commands)
    {
        if (_renderWithFindBar != null)
            _renderWithFindBar(view, findBar, commands);
        else
            _renderDelegate(view);
    }
}
