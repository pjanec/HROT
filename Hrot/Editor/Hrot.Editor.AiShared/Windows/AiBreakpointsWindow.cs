using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>⭐ <c>U-obs-5</c> — the whole of what <see cref="AiBreakpointsWindow"/> shows, this frame.</summary>
public sealed record AiBreakpointsPanelViewModel(
    string PanelId,
    string PanelKind,
    int ActiveCount,
    int TotalCount) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Per-perspective Breakpoints window for the AI editor.
/// Shows all breakpoints registered in the shared <see cref="IDataBreakpointManager"/>
/// and allows enabling/disabling/removing them.
/// Registered with <see cref="WindowScope.PerspectiveBound"/> so each AI
/// perspective (BTree, HSM, Blueprint) has its own docking slot.
/// <para>
/// Drawing is delegated to the <see cref="IDataBreakpointManager"/> draw callback
/// provided at construction, so this class contains no ImGui calls and is
/// headless-constructible (safe for unit tests).
/// </para>
/// </summary>
public sealed class AiBreakpointsWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Single-host: stays a local literal.</summary>
    internal const string Kind = "ai-breakpoints";

    private readonly IDataBreakpointManager _manager;
    /// <summary>⭐⭐ <c>CE-1285</c> (Q44-A, R-97) — THE one breakpoint list, the same panel the editor's global window draws.</summary>
    private readonly Hrot.Presentation.Panels.Breakpoints.DataBreakpointManagerPanel _panel;

    /// <summary>
    /// Constructs the window.
    /// </summary>
    /// <param name="id">Unique ImGui window id (must include <c>###</c> suffix for stable docking).</param>
    /// <param name="owningPerspective">Perspective key (e.g. "BTree").</param>
    /// <param name="manager">Shared data breakpoint manager.</param>
    public AiBreakpointsWindow(
        string id,
        string owningPerspective,
        IDataBreakpointManager manager)
        : base(id, "Breakpoints", owningPerspective, WindowScope.PerspectiveBound)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _panel = new Hrot.Presentation.Panels.Breakpoints.DataBreakpointManagerPanel(_manager, new TemporalStatusBannerState());
        IsOpen = false;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>Exposes the manager for test verification (shared-instance check).</summary>
    public IDataBreakpointManager Manager => _manager;

    /// <summary>⭐⭐⭐ BUILD · CAPTURE. ⛔⛔ No ImGui — pure counting, published before any render call.</summary>
    private AiBreakpointsPanelViewModel BuildAndPublish()
    {
        int total  = _manager.AllBreakpoints.Count;
        int active = _manager.AllBreakpoints.Count(bp => bp.Enabled);

        var vm = new AiBreakpointsPanelViewModel(Id, Kind, active, total);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal AiBreakpointsPanelViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        // Headless-safe: DrawClientArea is only called by the window manager when
        // an ImGui frame is active, so no ImGui guard is needed here.
        BuildAndPublish();
        // ⭐⭐ CE-1285 (Q44-A, R-97) — the list itself, not a count with "open the other window": ⛔ the banner and its dead end are gone
        _panel.DrawContent();
    }
}
