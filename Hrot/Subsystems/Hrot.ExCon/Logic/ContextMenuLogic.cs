using Hrot.Core.Network;
using Hrot.Common.Constants;
using Fdp.Toolkit.DER;
using Hrot.ExCon.Adapters;
using Hrot.Map.Common;
using Hrot.UI.Common.Menus;
using Newtonsoft.Json;

namespace Hrot.ExCon.Logic;

/// <summary>
/// Strategy-based context menu generator.
///
/// <para>
/// On each <see cref="OnSelectionChanged"/> call the logic:
/// <list type="number">
///   <item>Builds a menu item list according to <see cref="CurrentStrategy"/>.</item>
///   <item>Serialises the list to the JSON schema expected by the IG.</item>
///   <item>Pushes context actions via the injected <see cref="IExConEgressWriters"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// When constructed with a non-null <see langword="logic"/> reference (Phase 6),
/// <see cref="SharedContextMenuPopulator"/> is used to build entity menus and the
/// resulting callbacks are stored in <c>_activeCallbacks</c> for dispatch
/// when a <see cref="ContextActionInvokedDto"/> event arrives.
/// </para>
/// </summary>
public sealed class ContextMenuLogic : IContextMenuLogic
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDerRepo _repo;
    private readonly IExConEgressWriters _egressWriters;
    private readonly IExConLogic? _logic;

    // ── State ─────────────────────────────────────────────────────────────────

    private MenuStrategy _currentStrategy = MenuStrategy.Standard;

    /// <summary>
    /// Callback registry produced by the most recently built entity menu.
    /// Keys are the integer IDs assigned by <see cref="JsonContextMenuBuilder"/>;
    /// values are the <see cref="Action"/>s to invoke when the IG echoes the action ID.
    /// </summary>
    private IReadOnlyDictionary<int, Action> _activeCallbacks =
        new Dictionary<int, Action>();

    // ── Events ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public event Action<ContextActionInvokedDto>? ActionInvoked;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="repo">DER repository for entity descriptor reads.</param>
    /// <param name="menuWriter">DDS writer for pushing <see cref="ContextActionsUpdate"/> messages.</param>
    /// <param name="logic">
    /// Optional ExCon logic facade.
    /// When non-null the <see cref="SharedContextMenuPopulator"/> path is used and
    /// action callbacks are stored for dispatch in <see cref="OnActionInvoked"/>.
    /// Pass <c>null</c> (default) to retain the legacy strategy-based item list.
    /// </param>
    public ContextMenuLogic(
        IDerRepo repo,
        IExConEgressWriters egressWriters,
        IExConLogic? logic = null)
    {
        _repo          = repo          ?? throw new ArgumentNullException(nameof(repo));
        _egressWriters = egressWriters ?? throw new ArgumentNullException(nameof(egressWriters));
        _logic         = logic;
    }

    // ── IContextMenuLogic ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public MenuStrategy CurrentStrategy => _currentStrategy;

    /// <inheritdoc/>
    public void SetStrategy(MenuStrategy strategy)
    {
        _currentStrategy = strategy;
    }

    /// <inheritdoc/>
    public void OnSelectionChanged(SelectionChangedEventDto evt, Func<int, bool>? isEntityPending = null)
    {
        List<ContextMenuItem> menuItems;

        if (evt.SelectedEntityIds is not { Count: > 0 })
        {
            // No entity selected — this is a map-canvas right-click.
            menuItems = BuildMapCanvasMenu();
            _activeCallbacks = new Dictionary<int, Action>();
        }
        else
        {
            int entityId = evt.SelectedEntityIds[0];

            // Guard: pending entity means Phase 1 received but ELM not yet confirmed.
            // Return an empty menu so the operator cannot act on a half-baked entity.
            if (isEntityPending != null && isEntityPending(entityId))
            {
                menuItems = new List<ContextMenuItem>();
                _activeCallbacks = new Dictionary<int, Action>();
            }
            else
            {
                var entity = _repo.GetEntity(entityId);

                if (_logic != null)
                {
                    // ── Phase 6: use SharedContextMenuPopulator ────────────────
                    var builder = new JsonContextMenuBuilder();
                    var actions = new ExConEntityActionAdapter(_logic);

                    bool hasEditablePolyline = entity?.HasDescriptor<MapOverlayDescriptor>() == true;
                    bool hasRoute            = entity?.TkbType == TkbEntityTypes.TacGraphic_Route;

                    SharedContextMenuPopulator.PopulateEntityMenu(
                        (long)entityId,
                        entity?.TkbType ?? 0L,
                        hasEditablePolyline,
                        hasRoute,
                        builder,
                        actions);

                    menuItems        = new List<ContextMenuItem>(builder.Build());
                    _activeCallbacks = builder.GetCallbackRegistry();
                }
                else
                {
                    // ── Legacy: strategy-based hardcoded item list ────────────
                    menuItems        = BuildEntityMenu(_currentStrategy, entity);
                    _activeCallbacks = new Dictionary<int, Action>();
                }
            }
        }

        string menuJson = JsonConvert.SerializeObject(menuItems);

        _egressWriters.PushContextActions(evt.MapId, evt.SelectedEntityIds, menuJson);
    }

    /// <inheritdoc/>
    public void OnActionInvoked(ContextActionInvokedDto evt)
    {
        // Dispatch via Phase 6 callback registry when available.
        if (_activeCallbacks.TryGetValue(evt.ActionId, out var callback))
            callback.Invoke();

        // Always fire the event for legacy subscribers (e.g. ExConLogic).
        ActionInvoked?.Invoke(evt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Returns the map-canvas context menu shown when no entity is selected.</summary>
    private static List<ContextMenuItem> BuildMapCanvasMenu()
        => new()
        {
            new() { Id = GlobalActionIds.Measure, Label = "Measure...", Icon = "measure" }
        };

    /// <summary>
    /// Legacy strategy-based menu builder.  Used when no <see cref="IExConLogic"/>
    /// was supplied to the constructor (e.g. in unit tests).
    /// </summary>
    private static List<ContextMenuItem> BuildEntityMenu(MenuStrategy strategy, IDerEntity? entity)
    {
        var items = strategy switch
        {
            MenuStrategy.Standard => new List<ContextMenuItem>
            {
                new() { Id = GlobalActionIds.CenterOnEntity, Label = "Center on Entity",  Icon = "center"     },
                new() { Id = GlobalActionIds.Properties,     Label = "Properties...",      Icon = "properties" },
                new() { Id = GlobalActionIds.Delete,         Label = "DELETE",             Icon = "delete",   Style = "destructive" },
            },
            MenuStrategy.Admin => new List<ContextMenuItem>
            {
                new() { Id = GlobalActionIds.Teleport, Label = "Teleport...", Icon = "teleport" }
            },
            MenuStrategy.DamageControl => new List<ContextMenuItem>
            {
                new() { Id = GlobalActionIds.Repair,    Label = "Repair",    Icon = "repair"    },
                new() { Id = GlobalActionIds.Reinforce, Label = "Reinforce", Icon = "reinforce" }
            },
            MenuStrategy.Logistics => new List<ContextMenuItem>
            {
                new() { Id = GlobalActionIds.Resupply, Label = "Resupply", Icon = "resupply" },
                new() { Id = GlobalActionIds.Transfer, Label = "Transfer", Icon = "transfer" }
            },
            _ => new List<ContextMenuItem>()
        };

        // "Edit Shape" — editable tactical overlay.
        if (entity != null && entity.HasDescriptor<MapOverlayDescriptor>())
        {
            var overlay = entity.GetDescriptor<MapOverlayDescriptor>()!;
            if (overlay.IsEditable)
                items.Add(new ContextMenuItem { Id = GlobalActionIds.EditOverlay, Label = "Edit Shape", Icon = "edit" });
        }

        // "Edit Route" — standalone route entity.
        if (entity != null && entity.TkbType == TkbEntityTypes.TacGraphic_Route)
            items.Add(new ContextMenuItem { Id = GlobalActionIds.EditRoute, Label = "Edit Route", Icon = "edit" });

        // "Edit Personal Route" — non-TacGraphic unit/vehicle.
        if (entity != null
         && entity.TkbType != TkbEntityTypes.TacGraphic_Route
         && entity.TkbType != TkbEntityTypes.TacGraphic_Area
         && entity.TkbType != TkbEntityTypes.TacGraphic_FireLine)
        {
            items.Add(new ContextMenuItem
            {
                Id    = GlobalActionIds.EditPersonalRoute,
                Label = "Edit Personal Route",
                Icon  = "edit-route"
            });
        }

        return items;
    }
}
