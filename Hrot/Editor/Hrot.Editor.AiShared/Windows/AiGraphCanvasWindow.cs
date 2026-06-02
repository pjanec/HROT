using System;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Core.View;

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

    // Track whether focus was already activated this activation cycle.
    private AiDocument? _lastActivatedDoc;

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
    /// <param name="idOverride">
    ///   Optional stable ImGui id suffix. When <c>null</c> a default of
    ///   <c>"ai_canvas_{assetKind.ToLowerInvariant()}"</c> is used.
    /// </param>
    public AiGraphCanvasWindow(
        string            assetKind,
        AiDocumentManager docManager,
        ICanvasRenderSeam renderer,
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

        if (doc == null || ActiveContext == null)
        {
            DrawEmptyState();
            return;
        }

        // Render the cached GraphView via the seam.
        _renderer.Render(ActiveContext.View);
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
}

// ── Production seam wrapping CanvasRenderer ───────────────────────────────────

/// <summary>
/// Production <see cref="ICanvasRenderSeam"/> that delegates to a
/// <c>NodeEditor.UI.Canvas.CanvasRenderer</c>.  Construction is deferred to avoid
/// a dependency on NodeEditor.UI from <c>Hrot.Editor.AiShared</c>; the caller
/// supplies an <see cref="Action{GraphView}"/> delegate that closes over the
/// renderer instance.
/// </summary>
public sealed class DelegatingCanvasRenderSeam : ICanvasRenderSeam
{
    private readonly Action<GraphView> _renderDelegate;

    /// <summary>
    /// Creates the seam.
    /// </summary>
    /// <param name="renderDelegate">
    ///   Delegate that calls <c>canvasRenderer.Render(view, null)</c>
    ///   (or similar) for the supplied view.
    /// </param>
    public DelegatingCanvasRenderSeam(Action<GraphView> renderDelegate)
    {
        _renderDelegate = renderDelegate ?? throw new ArgumentNullException(nameof(renderDelegate));
    }

    /// <inheritdoc/>
    public void Render(GraphView view) => _renderDelegate(view);
}
