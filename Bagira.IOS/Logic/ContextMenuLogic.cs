using Bagira.BDC.SSTM;
using Bagira.Map.Common.Dds;
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
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDdsWriter<ContextActionsUpdate> _menuWriter;

    // ── State ─────────────────────────────────────────────────────────────────

    private MenuStrategy _currentStrategy = MenuStrategy.Standard;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public event Action<ContextActionInvoked>? ActionInvoked;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ContextMenuLogic(IDdsWriter<ContextActionsUpdate> menuWriter)
    {
        _menuWriter = menuWriter ?? throw new ArgumentNullException(nameof(menuWriter));
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
    public void OnSelectionChanged(SelectionChangedEvent evt)
    {
        var menuItems = BuildMenu(_currentStrategy);
        string menuJson = JsonConvert.SerializeObject(menuItems);

        _menuWriter.Write(new ContextActionsUpdate
        {
            MapGroupId       = evt.MapId,
            ForSelection     = evt.SelectedEntityIds,
            MenuDefinitionJson = menuJson
        });
    }

    /// <inheritdoc/>
    public void OnActionInvoked(ContextActionInvoked evt)
    {
        ActionInvoked?.Invoke(evt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the concrete item list for the given strategy.
    /// All action IDs are referenced by name from <see cref="ContextMenuActions"/>
    /// to avoid magic numbers (CODE-STANDARDS §1).
    /// </summary>
    private static List<ContextMenuItem> BuildMenu(MenuStrategy strategy)
        => strategy switch
        {
            MenuStrategy.Standard => new List<ContextMenuItem>
            {
                new() { Id = ContextMenuActions.CenterOnEntity, Label = "Center on Entity",  Icon = "center"     },
                new() { Id = ContextMenuActions.Properties,     Label = "Properties...",      Icon = "properties" }
            },

            MenuStrategy.Admin => new List<ContextMenuItem>
            {
                new() { Id = ContextMenuActions.Delete,   Label = "DELETE",       Icon = "delete",   Style = "destructive" },
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
}
