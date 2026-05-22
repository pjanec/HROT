using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Fake My Blueprint model — shows Functions, Variables, and Events sections.</summary>
public sealed class FakeMyBlueprintModel : IMyBlueprintModel
{
    private readonly Dictionary<string, List<MyBlueprintItem>> _sections = new();

    public event System.Action? Changed;

    /// <summary>Fire <see cref="Changed"/> to notify listeners.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public FakeMyBlueprintModel()
    {
        // Pre-populate with demo data
        _sections["graphs"] = new List<MyBlueprintItem>
        {
            new("graph.eventgraph", "graphs", "EventGraph", null, null, null, null, null, false, false, true, "The main event graph"),
            new("graph.update",     "graphs", "Update",     null, null, null, null, null, false, false, true, null),
        };

        _sections["functions"] = new List<MyBlueprintItem>
        {
            new("fn.init",     "functions", "Initialize",     null, null, null, null, null, true, true, false, null),
            new("fn.compute",  "functions", "ComputeDamage",   null, null, null, null, null, true, true, false, null),
        };

        _sections["variables"] = new List<MyBlueprintItem>
        {
            new("var.health",  "variables", "Health",   null, null, null, new Vector4(0.15f,0.63f,0.90f,1f), null, true, true, false, "Player health (0..100)"),
            new("var.speed",   "variables", "Speed",    null, null, null, new Vector4(0.15f,0.63f,0.90f,1f), null, true, true, false, "Movement speed"),
            new("var.name",    "variables", "PlayerName", null, null, null, new Vector4(0.87f,0.35f,0.11f,1f), null, true, true, false, "Display name"),
            new("var.active",  "variables", "IsActive", null, null, null, new Vector4(0.60f,0.00f,0.00f,1f), null, true, true, false, "Active state flag"),
        };

        _sections["events"] = new List<MyBlueprintItem>
        {
            new("evt.begin",   "events", "BeginPlay", null, null, null, null, null, false, false, true, null),
            new("evt.tick",    "events", "Tick",       null, null, null, null, null, false, false, true, null),
        };
    }

    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; } = new List<MyBlueprintSectionDescriptor>
    {
        new("graphs",    "Graphs",    0, null, true,  false, null),
        new("functions", "Functions", 1, null, true,  true,  null),
        new("variables", "Variables", 2, null, true,  true,  null),
        new("events",    "Events",    3, null, false, false, null),
    };

    public IReadOnlyList<MyBlueprintItem> GetItems(string sectionId)
        => _sections.TryGetValue(sectionId, out var items) ? items : Array.Empty<MyBlueprintItem>();
}
