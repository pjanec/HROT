using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Events;
using Hrot.IG.Components;

namespace Hrot.Editor.UI;

/// <summary>
/// Populates the entity right-click context menu with domain actions derived from
/// the JSON menu definition stored in <see cref="ContextMenuState.MenuJson"/>.
///
/// <para>Each parsed action item publishes a <see cref="ContextActionTriggered"/>
/// event on the local ECS bus so that domain systems can execute the corresponding
/// action without this handler needing to know which system owns each action.</para>
///
/// <para>If the entity does not have a <see cref="ContextMenuState"/> component,
/// or if <see cref="ContextMenuState.MenuJson"/> is empty, this handler adds
/// nothing to the menu.</para>
/// </summary>
public sealed class JsonEntityContextMenuHandler : IEntityContextMenuHandler
{
    private readonly EntityRepository _repo;
    private readonly FdpEventBus      _bus;

    /// <param name="repo">Entity repository used to read components.</param>
    /// <param name="bus">Event bus used to publish triggered actions.</param>
    public JsonEntityContextMenuHandler(EntityRepository repo, FdpEventBus bus)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _bus  = bus  ?? throw new ArgumentNullException(nameof(bus));
    }

    // ── IEntityContextMenuHandler ─────────────────────────────────────────────

    /// <inheritdoc/>
    public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
    {
        if (!_repo.IsAlive(entity)) return;
        if (!_repo.HasManagedComponent<ContextMenuState>(entity)) return;

        var state = ((ISimulationView)_repo).GetManagedComponentRO<ContextMenuState>(entity);
        if (string.IsNullOrEmpty(state.MenuJson)) return;

        long networkId = _repo.HasComponent<NetworkIdentity>(entity)
            ? _repo.GetComponent<NetworkIdentity>(entity).Value
            : 0L;

        try
        {
            using var doc = JsonDocument.Parse(state.MenuJson);
            foreach (var element in doc.RootElement.EnumerateArray())
                AddElement(builder, element, networkId);
        }
        catch (JsonException)
        {
            // Malformed JSON — silently skip.
        }
    }

    /// <inheritdoc/>
    public void PopulateMenu(IReadOnlyCollection<Entity> entities, IContextMenuBuilder builder)
    {
        // Multi-entity JSON-driven menus are not supported.
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void AddElement(IContextMenuBuilder builder, JsonElement element, long networkId)
    {
        // Separator row.
        if (element.TryGetProperty("separator", out var sepProp) && sepProp.GetBoolean())
        {
            builder.AddSeparator();
            return;
        }

        // Submenu with nested children.
        if (element.TryGetProperty("children", out var childrenProp))
        {
            string subLabel = element.TryGetProperty("label", out var subLblProp)
                ? subLblProp.GetString() ?? string.Empty
                : string.Empty;
            var sub = builder.BeginSubmenu(subLabel);
            foreach (var child in childrenProp.EnumerateArray())
                AddElement(sub, child, networkId);
            sub.EndSubmenu();
            return;
        }

        // Regular action item.
        if (!element.TryGetProperty("id", out var idProp)) return;
        int actionId = idProp.GetInt32();
        if (actionId == 0) return;

        string label = element.TryGetProperty("label", out var lblProp)
            ? lblProp.GetString() ?? string.Empty
            : string.Empty;

        bool enabled = !element.TryGetProperty("enabled", out var enabledProp)
            || enabledProp.GetBoolean();

        // Capture loop variables for the closure.
        int  capturedActionId  = actionId;
        long capturedNetworkId = networkId;

        builder.AddItem(label, () =>
        {
            _bus.PublishManaged(new ContextActionTriggered
            {
                EntityNetworkId = (int)capturedNetworkId,
                ActionName      = capturedActionId.ToString(CultureInfo.InvariantCulture),
            });
        }, enabled);
    }
}
