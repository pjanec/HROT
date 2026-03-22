using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.Map.Common;
using Bagira.Map.Common.Dds;
using FDP.Toolkit.DER;
using Newtonsoft.Json;

namespace Bagira.IOS.Logic;

/// <summary>
/// Strategy-based context menu generator.
///
/// <para>
/// On each <see cref="OnSelectionChanged"/> call the logic:
/// <list type="number">
///   <item>Builds a menu item list according to <see cref="CurrentStrategy"/>.</item>
///   <item>Serialises the list to the JSON schema expected by the IG.</item>
///   <item>Pushes a <see cref="ContextActionsUpdate"/> via the injected writer.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ContextMenuLogic : IContextMenuLogic
{
    // â”€â”€ Dependencies â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly IDerRepo _repo;
    private readonly IDdsWriter<ContextActionsUpdate> _menuWriter;

    // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private MenuStrategy _currentStrategy = MenuStrategy.Standard;

    // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public event Action<ContextActionInvoked>? ActionInvoked;

    // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public ContextMenuLogic(IDerRepo repo, IDdsWriter<ContextActionsUpdate> menuWriter)
    {
        _repo       = repo       ?? throw new ArgumentNullException(nameof(repo));
        _menuWriter = menuWriter ?? throw new ArgumentNullException(nameof(menuWriter));
    }

    // â”€â”€ IContextMenuLogic â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public MenuStrategy CurrentStrategy => _currentStrategy;

    /// <inheritdoc/>
    public void SetStrategy(MenuStrategy strategy)
    {
        _currentStrategy = strategy;
    }

    /// <inheritdoc/>
    public void OnSelectionChanged(SelectionChangedEvent evt, Func<int, bool>? isEntityPending = null)
    {
        List<ContextMenuItem> menuItems;

        if (evt.SelectedEntityIds is not { Count: > 0 })
        {
            // No entity selected â€” this is a map-canvas right-click.
            menuItems = BuildMapCanvasMenu();
        }
        else
        {
            int entityId = evt.SelectedEntityIds[0];

            // Guard: pending entity means Phase 1 received but ELM not yet confirmed.
            // Return an empty menu so the operator cannot act on a half-baked entity.
            if (isEntityPending != null && isEntityPending(entityId))
            {
                menuItems = new List<ContextMenuItem>();
            }
            else
            {
                var entity = _repo.GetEntity(entityId);
                menuItems = BuildEntityMenu(_currentStrategy, entity);
            }
        }

        string menuJson = JsonConvert.SerializeObject(menuItems);

        _menuWriter.Write(new ContextActionsUpdate
        {
            MapGroupId         = evt.MapId,
            ForSelection       = evt.SelectedEntityIds,
            MenuDefinitionJson = menuJson
        });
    }

    /// <inheritdoc/>
    public void OnActionInvoked(ContextActionInvoked evt)
    {
        ActionInvoked?.Invoke(evt);
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Returns the map-canvas context menu shown when no entity is selected.
    /// </summary>
    private static List<ContextMenuItem> BuildMapCanvasMenu()
        => new()
        {
            new() { Id = ContextMenuActions.Measure, Label = "Measure...", Icon = "measure" }
        };

    /// <summary>
    /// Builds the item list for the given strategy, optionally appending entity-specific
    /// actions (e.g. "Edit Drawing" for editable overlays).
    /// <paramref name="entity"/> may be <c>null</c> when the entity is not yet in the
    /// DER repo; the strategy menu is returned without dynamic additions in that case.
    /// </summary>
    private static List<ContextMenuItem> BuildEntityMenu(MenuStrategy strategy, IDerEntity? entity)
    {
        var items = strategy switch
        {
            MenuStrategy.Standard => new List<ContextMenuItem>
            {
                new() { Id = ContextMenuActions.CenterOnEntity, Label = "Center on Entity",  Icon = "center"     },
                new() { Id = ContextMenuActions.Properties,     Label = "Properties...",      Icon = "properties" },
                new() { Id = ContextMenuActions.Delete,   Label = "DELETE",       Icon = "delete",   Style = "destructive" },
            },

            MenuStrategy.Admin => new List<ContextMenuItem>
            {
                //new() { Id = ContextMenuActions.Delete,   Label = "DELETE",       Icon = "delete",   Style = "destructive" },
                new() { Id = ContextMenuActions.Teleport, Label = "Teleport...",  Icon = "teleport"  }
            },

            MenuStrategy.DamageControl => new List<ContextMenuItem>
            {
                new() { Id = ContextMenuActions.Repair,    Label = "Repair",    Icon = "repair"    },
                new() { Id = ContextMenuActions.Reinforce, Label = "Reinforce", Icon = "reinforce" }
            },

            MenuStrategy.Logistics => new List<ContextMenuItem>
            {
                new() { Id = ContextMenuActions.Resupply, Label = "Resupply", Icon = "resupply" },
                new() { Id = ContextMenuActions.Transfer, Label = "Transfer", Icon = "transfer" }
            },

            _ => new List<ContextMenuItem>()
        };

        // Dynamically append "Edit Drawing" when the entity has an editable tactical overlay.
        if (entity != null && entity.HasDescriptor<MapVisualOverlay>())
        {
            // HasDescriptor confirmed the overlay exists; GetDescriptor is safe to call.
            var overlay = entity.GetDescriptor<MapVisualOverlay>()!;
            if (overlay.IsEditable)
            {
                items.Add(new ContextMenuItem
                {
                    Id    = ContextMenuActions.EditOverlay,
                    Label = "Edit Drawing",
                    Icon  = "edit"
                });
            }
        }

        // "Edit Route" — shown for standalone route entities so the operator can reshape them.
        if (entity != null && entity.TkbType == TkbEntityTypes.TacGraphic_Route)
        {
            items.Add(new ContextMenuItem
            {
                Id    = ContextMenuActions.EditRoute,
                Label = "Edit Route",
                Icon  = "edit"
            });
        }

        // "Edit Personal Route" — shown for non-TacGraphic unit/vehicle entities so the operator
        // can reshape the vehicle's assigned personal route on the IG canvas.
        if (entity != null
         && entity.TkbType != TkbEntityTypes.TacGraphic_Route
         && entity.TkbType != TkbEntityTypes.TacGraphic_Area
         && entity.TkbType != TkbEntityTypes.TacGraphic_FireLine)
        {
            items.Add(new ContextMenuItem
            {
                Id    = ContextMenuActions.EditPersonalRoute,
                Label = "Edit Personal Route",
                Icon  = "edit-route"
            });
        }

        return items;
    }
}
