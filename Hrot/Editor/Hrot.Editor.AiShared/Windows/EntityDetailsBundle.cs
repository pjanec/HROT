using System;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.Scenario;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐ <c>CE-1150</c> (docs/designs/gizmos-1/canvas-context-menu-design.md §12 HA-3 / HA-4 / HA-7) — what one ECS host gives its Details
/// panel. <paramref name="Host"/> returns null on a host with no actions (Stride mode 2 composes no map): the panel is there, the actions
/// are not.
/// </summary>
/// <param name="WindowId">The panel's window id — the Editor keeps <c>scenario_details</c>; the others <c>{prefix}details</c>.</param>
/// <param name="Perspective">The perspective the panel belongs to (the one that holds the host's map and inspector).</param>
/// <param name="World">The world whose selection the panel follows (R-122) — re-asked every frame (a replay swaps it).</param>
/// <param name="Inspector">The host's wired inspector panel; the Components view BORROWS it (§6 L6.3).</param>
/// <param name="Session">The host's inspectable session over <paramref name="World"/>; null until there is one.</param>
/// <param name="RunState">The run state a context is built with.</param>
/// <param name="Host">The host's actions (<see cref="HostActions"/>), or null.</param>
/// <param name="SelectionStore">The asset/sub-selection store — the Editor passes its shared one; a new one otherwise.</param>
public sealed record EntityDetailsHostServices(
    string WindowId,
    string Perspective,
    Func<EntityRepository?> World,
    Func<EntityInspectorPanel?> Inspector,
    Func<IInspectableSession?> Session,
    Func<VariableRunState> RunState,
    Func<HostActions?> Host,
    EditorSelectionStore? SelectionStore = null);

/// <summary>
/// ⭐⭐⭐ <c>CE-1150</c> (canvas-context-menu-design §12) — <b>the Details panel of an ECS host, and the actions that open it.</b>
/// 🔒 User, 2026-10-22: <i>"the details panel is not something just the editor has, it is shared and unified across hosts and the
/// "properties..." click should show it on every ECS host"</i> · <i>"Windows themselves should not answer, they should have no idea there is
/// any "Properties..." menu or some global ui actions. the global action handler should open the detail panel."</i>
/// <list type="bullet">
///   <item><b>HA-4</b> — builds the perspective's workspace, the shell (<see cref="DetailsWindow"/>) and its Components view; a host adds
///     its own views on <see cref="Workspace"/> (the Editor: Environment, Zones, Mission, Entity AI).</item>
///   <item><b>HA-3</b> — registers <c>Properties…</c> on the host's actions: select the entity, show THIS panel, ask it for the
///     Components view. The shell knows nothing of it — the handler uses the window manager and the shell's public
///     <see cref="DetailsWindow.RequestView"/>.</item>
///   <item><b>HA-7</b> — View ▸ Details ▸ Float / Pin current view: menu items that REQUEST two actions, whose handlers act on the
///     current perspective's Details panel (any of the host's — the AI perspectives' too).</item>
/// </list>
/// </summary>
public sealed class EntityDetailsBundle : IUiBundle
{
    private readonly EntityDetailsHostServices _host;

    public EntityDetailsBundle(EntityDetailsHostServices host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Workspace = new PerspectiveWorkspace(
            perspectiveName: host.Perspective,
            selectionStore:  host.SelectionStore ?? new EditorSelectionStore(),
            runState:        host.RunState,
            entitySelection: new WorldEntitySelectionSource(host.World));
        // ⭐ A raw decoder: no blueprint session to decode through here (the honest default, as on the Editor's Scenario before).
        Window = new DetailsWindow(
            id:                host.WindowId,
            owningPerspective: host.Perspective,
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             Workspace.DetailsViews,
            context:           Workspace.ContextSource());
        Workspace.Contribute(Window);
        Workspace.DetailsViews.Add(ScenarioComponentsViewDescriptor.For(panel: host.Inspector, session: host.Session));
        Workspace.DetailsViews.Add(EntitySummaryViewDescriptor.For(host.World));   // ⭐ CE-1247 — the IG's former card, every host
        // ⭐ CE-1248 (BV-1) — the Brain tab, every host; behaviour names through the registry the host already publishes for the renderers
        Workspace.DetailsViews.Add(EntityBrainViewDescriptor.For(host.World,
            hash => Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor is { } reg && reg.TryGetName(hash, out var n) ? n : null,
            hash => Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor is { } reg && reg.TryGetDefinition(hash, out var d) ? d : null,
            // ⭐ CE-1315 — both registries, so the Running behaviours section names and decodes every slot (a held value is passed)
            registry:   () => Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor,
            blueprints: () => Hrot.Presentation.Renderers.BlueprintBlackboardRenderers.Registry));
    }

    /// <inheritdoc/>
    public string Name => "entity-details";

    /// <summary>The perspective's workspace — a host adds its own views to <see cref="PerspectiveWorkspace.DetailsViews"/>.</summary>
    public PerspectiveWorkspace Workspace { get; }

    /// <summary>The Details panel.</summary>
    public DetailsWindow Window { get; }

    /// <inheritdoc/>
    public void RegisterInto(UiBundleContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        ctx.Windows.RegisterWindow(Window);
        if (_host.Host() is not { } host) return;   // no actions on this host (Stride mode 2): the panel only
        RegisterProperties(host, ctx.Windows, Window);
        RegisterViewMenu(host, ctx.Windows);
    }

    /// <summary>
    /// ⭐ HA-3 — <c>Properties…</c> (this node's own clicks: <c>RegisterLocal</c>): select, show <paramref name="details"/>, ask it for
    /// the Components view.
    /// </summary>
    public static void RegisterProperties(HostActions host, WindowManager windows, DetailsWindow details)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(details);
        host.Actions.RegisterLocal(GlobalActionIds.Properties, (view, target) =>
        {
            if (target.IsNull || view is not EntityRepository w) return;
            w.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ReplaceWith(target, "ContextMenu.Properties"));
            windows.FocusWindow(details.Id);
            // ⭐ DS-6 — pending until the selection lands and the view joins the offer, then picked and revealed.
            details.RequestView(ScenarioComponentsViewDescriptor.ViewId);
        });
    }

    /// <summary>
    /// ⭐ HA-7 (was <c>DetailsWindow.RegisterViewMenu</c>, <c>BP-403</c>) — ONE pair of items for all perspectives, acting on the current
    /// perspective's panel at click time; greyed with a reason when nothing is showing (🔒 user, 2026-08-17: a tooltip beats a dead click).
    /// A click REQUESTS the action; the two actions' handlers act through the shell's public <c>OpenFloat</c> / <c>Pin</c>.
    /// </summary>
    public static void RegisterViewMenu(HostActions host, WindowManager windows)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(windows);
        host.Actions.RegisterLocal(GlobalActionIds.FloatDetailsView, (_, _) => ActiveDetails(windows)?.OpenFloat(windows));
        host.Actions.RegisterLocal(GlobalActionIds.PinDetailsView,   (_, _) => ActiveDetails(windows)?.Pin(windows));
        windows.GlobalMenu.RegisterItem(FloatItemPath, () => host.Request(GlobalActionIds.FloatDetailsView, Entity.Null));
        windows.GlobalMenu.RegisterItem(PinItemPath,   () => host.Request(GlobalActionIds.PinDetailsView,   Entity.Null));

        foreach (var (name, verb) in new[] { ("Float current view", "float"), ("Pin current view", "pin") })
        {
            var node = windows.GlobalMenu.Root.Children["View"].Children["Details"].Children[name];
            node.GetEnabled   = () => ActiveDetails(windows)?.CurrentDescriptor() is not null;
            node.DynamicLabel = () => ActiveDetails(windows)?.CurrentDescriptor() is { } d
                ? $"{name} ({d.Title})"
                : $"{name} (nothing to {verb} — no view is showing)";
        }
    }

    public const string FloatItemPath = "View/Details/Float current view";
    public const string PinItemPath   = "View/Details/Pin current view";

    /// <summary>
    /// The <see cref="DetailsWindow"/> of the perspective the user is in, or null. ⚠ Scans the registered windows: the question is about
    /// the OWNING PERSPECTIVE, and the ids are per host.
    /// </summary>
    public static DetailsWindow? ActiveDetails(WindowManager windows)
    {
        foreach (var id in windows.RegisteredWindowIds)
            if (windows.TryGetWindow(id, out var w) && w is DetailsWindow d && d.OwningPerspective == windows.CurrentPerspective)
                return d;
        return null;
    }
}
