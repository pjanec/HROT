using System;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Core.Action;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
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

    // BCP-BATCH-02-FIX Task 2: stable base title (the "{assetKind} Canvas" empty-state
    // label). The dynamic title is rebuilt from this + the active document name.
    private readonly string _baseTitle;

    // Track whether focus was already activated this activation cycle.
    private AiDocument? _lastActivatedDoc;

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

        var context = ActiveContext;
        if (doc == null || context == null) return;

        DrawPickerAndPumpHotkeys(context, suppressHotkeys);
    }
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
