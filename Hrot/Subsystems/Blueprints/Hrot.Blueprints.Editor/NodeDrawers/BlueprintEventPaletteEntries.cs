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
