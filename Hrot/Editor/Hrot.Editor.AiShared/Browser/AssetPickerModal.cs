using Hrot.Editor.AiShared.Catalog;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// A modal popup that hosts an <see cref="AssetBrowserPanel"/> and returns the
/// user's pick via a callback.  The modal performs <b>no side effects</b> — it
/// never opens documents or loads scenarios; it only invokes the supplied
/// <see cref="Action{IEditableAsset?}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (§10.3):</b> open/close/activate/cancel logic is separated from
/// ImGui draw so the modal is testable headlessly.  Call <see cref="HandleActivated"/>
/// or <see cref="HandleCancel"/> to simulate user actions without an ImGui context.
/// </para>
/// <para>
/// A modal services exactly one callback per <see cref="Open"/>; the callback is
/// guarded against double-invocation (subsequent activates or cancels before the
/// next <see cref="Open"/> are no-ops).
/// </para>
/// </remarks>
public sealed class AssetPickerModal
{
    private readonly IAssetCatalog _catalog;
    private readonly IIconProvider _icons;

    private AssetBrowserPanel? _panel;
    private Action<IEditableAsset?>? _callback;
    private bool _callbackInvoked;

    /// <summary>
    /// Whether the modal is currently open.
    /// </summary>
    public bool IsOpen => _panel != null;

    /// <summary>
    /// Initialises a new <see cref="AssetPickerModal"/>.
    /// </summary>
    /// <param name="catalog">The asset catalog (never <see langword="null"/>).</param>
    /// <param name="icons">The icon provider for resolving kind-icon keys (never <see langword="null"/>).</param>
    public AssetPickerModal(IAssetCatalog catalog, IIconProvider icons)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
    }

    // ── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens the modal with the given <paramref name="options"/> and
    /// <paramref name="callback"/>.  The callback is invoked exactly once:
    /// <list type="bullet">
    ///   <item>When an asset is activated: <c>callback(asset)</c>, then the modal closes.</item>
    ///   <item>When Esc is pressed or the modal is cancelled: <c>callback(null)</c>, then the modal closes.</item>
    /// </list>
    /// </summary>
    /// <param name="options">Panel options controlling visible kinds, tabs, initial reveal, etc.</param>
    /// <param name="callback">
    /// Invoked when the user makes a selection (<see langword="null"/> on cancel).
    /// Stored and invoked exactly once per open — re-opening with a new callback
    /// replaces the previous one.
    /// </param>
    /// <param name="lastOpened">
    /// Optional per-kind last-opened map to restore (e.g. from editor session prefs).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="callback"/> is <see langword="null"/>.
    /// </exception>
    public void Open(
        AssetBrowserPanelOptions options,
        Action<IEditableAsset?> callback,
        IReadOnlyDictionary<AssetKind, string>? lastOpened = null)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        // Dispose previous panel if re-opening while already open.
        ClosePanel();

        _callback = callback;
        _callbackInvoked = false;

        _panel = new AssetBrowserPanel(_catalog, _icons, options, lastOpened);
        _panel.AssetActivated += OnPanelAssetActivated;
    }

    /// <summary>
    /// Closes the modal without invoking the callback (e.g. programmatic close).
    /// If a callback is pending, it is discarded without invocation.
    /// </summary>
    public void Close()
    {
        ClosePanel();
        _callback = null;
        _callbackInvoked = false;
    }

    // ── Headless test seams ────────────────────────────────────────────

    /// <summary>
    /// Invokes the callback with <paramref name="asset"/> and closes the modal.
    /// No-op when no callback is pending or the callback has already been invoked
    /// for this open session.
    /// </summary>
    internal void HandleActivated(IEditableAsset asset)
    {
        if (_callback == null || _callbackInvoked) return;
        _callbackInvoked = true;

        var cb = _callback;
        ClosePanel();
        cb(asset);
    }

    /// <summary>
    /// Invokes the callback with <see langword="null"/> and closes the modal.
    /// No-op when no callback is pending or the callback has already been invoked
    /// for this open session.
    /// </summary>
    internal void HandleCancel()
    {
        if (_callback == null || _callbackInvoked) return;
        _callbackInvoked = true;

        var cb = _callback;
        ClosePanel();
        cb(null);
    }

    // ── ImGui draw ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders the modal popup via ImGui.  Must be called every frame while
    /// <see cref="IsOpen"/> is <see langword="true"/>.  Handles Esc to cancel
    /// and wires double-click / Enter activation through the panel.
    /// </summary>
    /// <param name="title">The title shown in the popup title bar.</param>
    public void DrawModal(string title = "Pick an Asset")
    {
        if (!IsOpen)
            return;

        // Open the popup on the first frame, then draw it.
        if (!ImGui.IsPopupOpen("##AssetPickerPopup"))
            ImGui.OpenPopup("##AssetPickerPopup");

        bool isOpen = true;
        if (ImGui.BeginPopupModal($"{title}###AssetPickerPopup", ref isOpen,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking))
        {
            // Close on Esc.
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
                HandleCancel();
            }

            // The X button also closes — treat as cancel.
            if (!isOpen)
            {
                ImGui.CloseCurrentPopup();
                HandleCancel();
            }

            // Constrain the content area to a reasonable size.
            var avail = ImGui.GetContentRegionAvail();
            if (avail.X < 400f) avail.X = 400f;
            if (avail.Y < 300f) avail.Y = 300f;
            ImGui.BeginChild("##AssetPickerContent", avail, ImGuiChildFlags.None);

            _panel!.DrawContent();

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void ClosePanel()
    {
        if (_panel == null) return;

        _panel.AssetActivated -= OnPanelAssetActivated;
        _panel = null;
    }

    private void OnPanelAssetActivated(IEditableAsset asset)
        => HandleActivated(asset);
}
