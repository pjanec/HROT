using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Q#14 slice 2c — one "Publish: {Event}" Add-Node palette entry per discovered custom event
/// (C# <c>[BlueprintEvent]</c> (2a) + editor-authored defs (2b), via <see cref="UnifiedEventDiscovery"/>).
/// Each descriptor drops a <see cref="PublishEventNode"/> pre-baked with the event's <c>EventTypeFqn</c> +
/// fields + target, so the compiler's baked path (2a) resolves it with no catalog entry. Mirrors
/// <see cref="BlueprintCallablePaletteEntries"/>.
/// </summary>
public static class BlueprintEventPaletteEntries
{
    public static IEnumerable<NodeKindDescriptor> PublishEntries(BlueprintEventCatalog? editorCatalog = null)
    {
        foreach (var ev in UnifiedEventDiscovery.All(editorCatalog))
            yield return PublishDescriptor(ev);
    }

    /// <summary>
    /// ⭐ CE-2015 (<c>DESIGN_Typed_Event_Nodes</c> E4, I8) — one "On: {Event}" entry per discovered event, the mirror of
    /// "Publish: {Event}": it drops an <see cref="EventEntryNode"/> pre-baked with the event's FQN, payload
    /// <see cref="EventEntryNode.Fields"/> and recipient field, so its pins come from the event (T-1) and any number of
    /// them can sit in one Event graph.
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> SubscribeEntries(BlueprintEventCatalog? editorCatalog = null)
    {
        foreach (var ev in UnifiedEventDiscovery.All(editorCatalog))
            yield return SubscribeDescriptor(ev);
    }

    private static NodeKindDescriptor SubscribeDescriptor(DiscoveredBlueprintEvent ev) => new()
    {
        Kind        = $"Event.On.{ev.EventTypeFqn}",
        DisplayName = $"On: {ev.DisplayName}",
        Category    = string.IsNullOrEmpty(ev.Category) ? "Events" : $"Events/{ev.Category}",
        Tooltip     = $"Runs when the {ev.DisplayName} event arrives (in an Event graph; any number per graph).",
        Icon        = "bp/event",
        CreateInstance = () => new EventEntryNode
        {
            Id              = Guid.NewGuid(),
            EventTypeId     = ev.EventTypeFqn,
            TargetFieldName = ev.TargetFieldName,
            Fields          = ev.Fields
                .Select(f => new PublishEventFieldDecl { Name = f.Name, TypeId = f.TypeId })
                .ToList(),
        },
    };

    private static NodeKindDescriptor PublishDescriptor(DiscoveredBlueprintEvent ev) => new()
    {
        Kind        = $"Event.Publish.{ev.EventTypeFqn}",
        DisplayName = $"Publish: {ev.DisplayName}",
        Category    = string.IsNullOrEmpty(ev.Category) ? "Events" : $"Events/{ev.Category}",
        Tooltip     = $"Publish the {ev.DisplayName} event on the world bus.",
        Icon        = "bp/function",
        CreateInstance = () => new PublishEventNode
        {
            Id              = Guid.NewGuid(),
            EventId         = ev.EventTypeFqn,
            EventTypeFqn    = ev.EventTypeFqn,
            TargetFieldName = ev.TargetFieldName,
            PayloadFields   = ev.Fields
                .Select(f => new PublishEventFieldDecl { Name = f.Name, TypeId = f.TypeId })
                .ToList(),
        },
    };
}
