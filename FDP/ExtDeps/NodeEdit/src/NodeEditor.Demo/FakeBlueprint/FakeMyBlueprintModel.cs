using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Fake My Blueprint model — shows Functions, Variables, Events, Macros, and Dispatchers sections.</summary>
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
            new("fn.init",     "functions", "Initialize",   null, null, null, null, null, true, true, false, null),
            new("fn.compute",  "functions", "ComputeDamage", null, null, null, null, null, true, true, false, null),
        };

        _sections["macros"] = new List<MyBlueprintItem>();

        _sections["variables"] = new List<MyBlueprintItem>
        {
            new("var.health",  "variables", "Health",     null, null, null, new Vector4(0.15f,0.63f,0.90f,1f), null, true, true, false, "Player health (0..100)"),
            new("var.speed",   "variables", "Speed",      null, null, null, new Vector4(0.15f,0.63f,0.90f,1f), null, true, true, false, "Movement speed"),
            new("var.name",    "variables", "PlayerName", null, null, null, new Vector4(0.87f,0.35f,0.11f,1f), null, true, true, false, "Display name"),
            new("var.active",  "variables", "IsActive",   null, null, null, new Vector4(0.60f,0.00f,0.00f,1f), null, true, true, false, "Active state flag"),
        };

        _sections["events"] = new List<MyBlueprintItem>
        {
            new("evt.begin", "events", "BeginPlay", null, null, null, null, null, false, false, true, null),
            new("evt.tick",  "events", "Tick",      null, null, null, null, null, false, false, true, null),
        };

        _sections["dispatchers"] = new List<MyBlueprintItem>();
    }

    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; } = new List<MyBlueprintSectionDescriptor>
    {
        new("graphs",      "Graphs",      0, null, true,  false, null),
        new("functions",   "Functions",   1, null, true,  true,  null),
        new("macros",      "Macros",      2, null, true,  true,  null),
        new("variables",   "Variables",   3, null, true,  true,  null),
        new("events",      "Events",      4, null, true,  false, null),
        new("dispatchers", "Dispatchers", 5, null, true,  true,  null),
    };

    public IReadOnlyList<MyBlueprintItem> GetItems(string sectionId)
        => _sections.TryGetValue(sectionId, out var items) ? items : Array.Empty<MyBlueprintItem>();

    // ── Variables ─────────────────────────────────────────────────────────────

    /// <summary>Add a variable item to the Variables section.</summary>
    public void AddVariable(string id, string name, Vector4 color, string? tooltip = null)
    {
        EnsureSection("variables");
        _sections["variables"].Add(new MyBlueprintItem(id, "variables", name, null, null, null, color, null, true, true, false, tooltip));
        NotifyChanged();
    }

    /// <summary>Remove a variable by item id.</summary>
    public void RemoveVariable(string id)
    {
        if (_sections.TryGetValue("variables", out var list) && list.RemoveAll(x => x.ItemId == id) > 0)
            NotifyChanged();
    }

    /// <summary>Rename a variable by item id.</summary>
    public void RenameVariable(string id, string newName)
    {
        if (!_sections.TryGetValue("variables", out var list)) return;
        var idx = list.FindIndex(x => x.ItemId == id);
        if (idx < 0) return;
        list[idx] = list[idx] with { DisplayName = newName };
        NotifyChanged();
    }

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Add a function item to the Functions section.</summary>
    public void AddFunction(string id, string name)
    {
        EnsureSection("functions");
        _sections["functions"].Add(new MyBlueprintItem(id, "functions", name, null, null, null, null, null, true, true, false, null));
        NotifyChanged();
    }

    /// <summary>Remove a function by item id.</summary>
    public void RemoveFunction(string id)
    {
        if (_sections.TryGetValue("functions", out var list) && list.RemoveAll(x => x.ItemId == id) > 0)
            NotifyChanged();
    }

    // ── Macros ────────────────────────────────────────────────────────────────

    /// <summary>Add a macro item to the Macros section.</summary>
    public void AddMacro(string id, string name)
    {
        EnsureSection("macros");
        _sections["macros"].Add(new MyBlueprintItem(id, "macros", name, null, null, null, null, null, true, true, false, null));
        NotifyChanged();
    }

    // ── Custom Events ─────────────────────────────────────────────────────────

    /// <summary>Add a custom event item to the Events section.</summary>
    public void AddCustomEvent(string id, string name)
    {
        EnsureSection("events");
        _sections["events"].Add(new MyBlueprintItem(id, "events", name, null, null, null, null, null, true, true, false, null));
        NotifyChanged();
    }

    /// <summary>Remove a custom event by item id.</summary>
    public void RemoveCustomEvent(string id)
    {
        if (_sections.TryGetValue("events", out var list) && list.RemoveAll(x => x.ItemId == id) > 0)
            NotifyChanged();
    }

    // ── Event Dispatchers ─────────────────────────────────────────────────────

    /// <summary>Add a dispatcher item to the Dispatchers section.</summary>
    public void AddDispatcher(string id, string name)
    {
        EnsureSection("dispatchers");
        _sections["dispatchers"].Add(new MyBlueprintItem(id, "dispatchers", name, null, null, null, null, null, true, true, false, null));
        NotifyChanged();
    }

    // ── private ───────────────────────────────────────────────────────────────

    private void EnsureSection(string sectionId)
    {
        if (!_sections.ContainsKey(sectionId))
            _sections[sectionId] = new List<MyBlueprintItem>();
    }
}
