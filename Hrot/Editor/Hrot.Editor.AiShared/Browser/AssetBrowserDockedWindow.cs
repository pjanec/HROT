using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// A <see cref="ManagedWindow"/> that hosts an <see cref="AssetBrowserPanel"/> as a
/// permanent docked window in the editor.  The window performs <b>no side effects</b>
/// — when an asset is activated it invokes the callback supplied by the registrant;
/// the window itself stays open.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (§10.3):</b> this is the generic docked-window host.  The callback
/// determines what happens on activation (open document, load scenario, etc.).
/// The window is global-scope so it is always available regardless of active
/// perspective.
/// </para>
/// <para>
/// <b>Stable identity:</b>
/// <list type="bullet">
///   <item><see cref="ManagedWindow.Id"/> = <c>"AssetBrowser"</c> (exposed as <see cref="ExpectedId"/>).</item>
///   <item><see cref="ManagedWindow.Scope"/> = <see cref="WindowScope.Global"/> — the browser is a
///       shared tool, not tied to any one perspective.</item>
/// </list>
/// Register with <c>WindowManager.RegisterWindow</c> to make it available in the
/// "Windows" menu and restore it from persisted settings.
/// </para>
/// </remarks>
public sealed class AssetBrowserDockedWindow : ManagedWindow
{
    /// <summary>
    /// The stable, documented window <see cref="ManagedWindow.Id"/>.
    /// Callers use this when calling <c>WindowManager.TryGetWindow</c>,
    /// <c>ShowWindow</c>, <c>FocusWindow</c>, etc.
    /// </summary>
    public const string ExpectedId = "AssetBrowser";

    /// <summary>
    /// Default title shown in the window title bar.
    /// </summary>
    public const string DefaultTitle = "Asset Browser";

    private readonly AssetBrowserPanel _panel;

    /// <summary>⭐ The panel this window hosts — 📌 <c>R-67</c>: a rail asks the CONSTRUCTED window
    /// which options its host opted into, ⛔ never the call site that built it.</summary>
    public AssetBrowserPanel Panel => _panel;
    private readonly Action<IEditableAsset> _onAssetActivated;

    /// <summary>
    /// Creates a new <see cref="AssetBrowserDockedWindow"/>.
    /// </summary>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="icons">The icon provider for resolving kind-icon keys (never <see langword="null"/>).</param>
    /// <param name="options">Panel options controlling visible kinds, tabs, initial reveal, etc.</param>
    /// <param name="onAssetActivated">
    /// Callback invoked when the user activates an asset in the browser.
    /// The window <b>stays open</b> — it is the caller's responsibility to decide
    /// what to do (e.g. open a document, load a scenario).
    /// <b>Never <see langword="null"/>.</b>
    /// </param>
    /// <param name="owningPerspective">
    /// The perspective this window belongs to. Defaults to <c>"Authoring"</c> but is
    /// effectively irrelevant because <see cref="WindowScope"/> is <see cref="WindowScope.Global"/>.
    /// </param>
    /// <param name="id">
    /// Override for the window id. Defaults to <see cref="ExpectedId"/>.
    /// Only change this when you intentionally need a second browser instance.
    /// </param>
    /// <param name="title">
    /// Override for the window title. Defaults to <see cref="DefaultTitle"/>.
    /// </param>
    public AssetBrowserDockedWindow(
        IAssetCatalog catalog,
        IIconProvider icons,
        AssetBrowserPanelOptions options,
        Action<IEditableAsset> onAssetActivated,
        string owningPerspective = "Authoring",
        string? id = null,
        string? title = null)
        : base(
            id: id ?? ExpectedId,
            title: title ?? DefaultTitle,
            owningPerspective: owningPerspective,
            scope: WindowScope.Global)
    {
        _onAssetActivated = onAssetActivated
            ?? throw new ArgumentNullException(nameof(onAssetActivated));

        // The panel is owned by this window and lives for its lifetime.
        _panel = new AssetBrowserPanel(
            catalog ?? throw new ArgumentNullException(nameof(catalog)),
            icons ?? throw new ArgumentNullException(nameof(icons)),
            options ?? throw new ArgumentNullException(nameof(options)));

        _panel.AssetActivated += OnPanelAssetActivated;
    }

    /// <summary>
    /// Optional custom toolbar draw action injected by the host.
    /// Invoked before the panel content each frame.
    /// </summary>
    public Action? CustomToolbarDraw { get; set; }

    // ── ManagedWindow overrides ────────────────────────────────────────

    /// <inheritdoc />
    protected override void DrawClientArea()
    {
        CustomToolbarDraw?.Invoke();
        _panel.DrawContent();
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void OnPanelAssetActivated(IEditableAsset asset)
    {
        // The window stays open — the callback decides what to do.
        _onAssetActivated(asset);
    }
}
