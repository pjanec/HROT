using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>One field of an editor-authored (2b) custom event. Blittable pin TypeId (enums carry "global::").</summary>
public sealed class BlueprintEventFieldDef
{
    public string Name { get; set; } = "";
    public string TypeId { get; set; } = "";
    /// <summary>The single recipient <c>Entity</c> field (drives the entry node's Self/Any filter). At most one.</summary>
    public bool IsTarget { get; set; }
}

/// <summary>
/// An editor-authored (2b) custom event definition — a designer-defined named event with typed fields, NO C#
/// type (runtime uses the generic blittable carrier keyed by a type-id hashed from <see cref="Name"/>).
/// Q#14 Option 2b: authored via the editor (reusing the blackboard field-definition UX), persisted as JSON.
/// </summary>
public sealed class BlueprintEventDef
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Custom";
    public List<BlueprintEventFieldDef> Fields { get; set; } = new();

    /// <summary>
    /// Projects to the unified discovery shape so the picker treats editor-authored and C# events identically.
    /// Identity = <see cref="Name"/> (the runtime derives the carrier type-id by hashing it).
    /// </summary>
    public DiscoveredBlueprintEvent ToDiscovered() => new(
        EventTypeFqn:    Name,
        DisplayName:     Name,
        Category:        Category,
        Fields:          Fields.Select(f => new DiscoveredEventField(f.Name, f.TypeId)).ToList(),
        TargetFieldName: Fields.FirstOrDefault(f => f.IsTarget)?.Name);
}

/// <summary>Project-level catalog of editor-authored (2b) custom events; persisted as JSON alongside the project.</summary>
public sealed class BlueprintEventCatalog
{
    public List<BlueprintEventDef> Events { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static BlueprintEventCatalog FromJson(string json)
        => JsonSerializer.Deserialize<BlueprintEventCatalog>(json, Opts) ?? new BlueprintEventCatalog();
}

/// <summary>
/// Q#14 slice 1e — unified event discovery: C# <c>[BlueprintEvent]</c> structs (2a) + editor-authored defs (2b),
/// both projected to the single <see cref="DiscoveredBlueprintEvent"/> shape the picker + node-baking consume.
/// </summary>
public static class UnifiedEventDiscovery
{
    public static IEnumerable<DiscoveredBlueprintEvent> All(BlueprintEventCatalog? editorCatalog = null)
    {
        foreach (var e in BlueprintEventDiscovery.Discover())
            yield return e;

        if (editorCatalog != null)
            foreach (var d in editorCatalog.Events)
                yield return d.ToDiscovered();
    }
}
