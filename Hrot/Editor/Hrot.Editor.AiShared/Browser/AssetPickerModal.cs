using System.Numerics;
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
/// <para>
/// <b>BATCH-26 lock-up fix:</b> while <see cref="IsOpen"/>, <see cref="DrawModal"/> retries
/// <see cref="ImGui.OpenPopup"/> until the popup is actually open (a one-shot open proved
/// unreliable depending on call timing/scope), using the <b>identical</b> ID string
/// (<c>"Open Asset"</c>) as <see cref="ImGui.BeginPopupModal"/>, plus an explicit
/// <see cref="ImGui.SetNextWindowSize"/> so the modal can't collapse to zero/invisible size.
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
    /// The popup ID string used for both <see cref="ImGui.OpenPopup"/> and
    /// <see cref="ImGui.BeginPopupModal"/>.  Must be identical — see BATCH-26
    /// lock-up diagnosis.
    /// </summary>
    public const string PopupId = "Open Asset";

    /// <summary>
    /// Default modal window size (BATCH-26: explicit size prevents zero-size collapse).
    /// </summary>
    public static readonly Vector2 DefaultWindowSize = new(720f, 520f);

    /// <summary>
    /// Whether the modal is currently open.
    /// </summary>
    public bool IsOpen => _panel != null;

    /// <summary>
    /// The active <see cref="AssetBrowserPanel"/>, or <see langword="null"/> when
    /// the modal is closed.  Exposed for tests that need to verify or manipulate
    /// internal panel state (e.g. setting <see cref="AssetBrowserPanel.Selection"/>
    /// before simulating Enter).
    /// </summary>
    internal AssetBrowserPanel? Panel => _panel;

    /// <summary>
    /// The current callback, or <see langword="null"/> when no session is active.
    /// Exposed for tests that verify the callback identity.
    /// </summary>
    internal Action<IEditableAsset?>? Callback => _callback;

    /// <summary>
    /// Whether the callback has been invoked for the current open session.
    /// </summary>
    internal bool CallbackInvoked => _callbackInvoked;

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
        // DrawModal opens the ImGui popup on the next draw while IsOpen is true (retry pattern).
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
    /// <see cref="IsOpen"/> is <see langword="true"/>.  Handles Esc to cancel,
    /// Enter to confirm selection, Ctrl+Tab / Ctrl+Shift+Tab to cycle tabs,
    /// and wires double-click activation through the panel.
    /// </summary>
    public void DrawModal()
    {
        if (!IsOpen)
            return;

        // Open the popup using the EXACT SAME string for OpenPopup/IsPopupOpen/BeginPopupModal
        // (mirrors the working "Rename Entity" modal). Do NOT use a "label###id" form here: ImGui
        // hashes the id from the "###..." segment, so OpenPopup("Open Asset") and
        // BeginPopupModal("Open Asset###Open Asset") resolve to DIFFERENT ids — the popup opens under
        // one id while BeginPopupModal waits on another, so it never renders (the diagnosed
        // "began=False forever" lock). Plain identical id avoids that; explicit size keeps it visible.
        if (!ImGui.IsPopupOpen(PopupId))
            ImGui.OpenPopup(PopupId);
        ImGui.SetNextWindowSize(DefaultWindowSize, ImGuiCond.Appearing);

        bool isOpen = true;
        if (ImGui.BeginPopupModal(PopupId, ref isOpen, ImGuiWindowFlags.NoDocking))
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

            // BATCH-26: Enter confirms current selection.
            if (ImGui.IsKeyPressed(ImGuiKey.Enter) && _panel!.Selection != null)
            {
                ImGui.CloseCurrentPopup();
                HandleActivated(_panel.Selection);
            }

            // BATCH-26: Ctrl+Tab / Ctrl+Shift+Tab cycle tabs.
            if (ImGui.IsKeyPressed(ImGuiKey.Tab, repeat: false))
            {
                bool ctrlHeld = ImGui.IsKeyDown(ImGuiKey.ModCtrl);
                bool shiftHeld = ImGui.IsKeyDown(ImGuiKey.ModShift);
                if (ctrlHeld && !shiftHeld)
                {
                    _panel!.SelectNextTab();
                }
                else if (ctrlHeld && shiftHeld)
                {
                    _panel!.SelectPreviousTab();
                }
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
