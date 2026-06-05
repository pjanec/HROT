namespace Hrot.Blueprints.Core.Assets;

public sealed class Graph
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GraphKind Kind { get; set; }
    public List<ParameterDecl> Inputs { get; set; } = new();
    public List<ParameterDecl> Outputs { get; set; } = new();
    public List<Node> Nodes { get; set; } = new();
    public List<Link> Links { get; set; } = new();
    public GraphMetadata EditorMetadata { get; set; } = new();
}

public enum GraphKind { Function, Event, Construction }

public sealed class Pin
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Direction { get; set; } = "";
    public BlueprintTypeRef TypeRef { get; set; } = new();
    public bool IsExec { get; set; }
    public List<Guid> LinkedToIds { get; set; } = new();

    /// <summary>
    /// Inline default value for this pin, stored as a JSON-compatible string
    /// (e.g. "42" for int, "3.14" for float, "true" for bool, "hello" for string).
    /// Null when no default has been set. Written to disk only when non-null.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValue { get; set; }
}

public sealed class Link
{
    public Guid FromNodeId { get; set; }
    public Guid FromPinId { get; set; }
    public Guid ToNodeId { get; set; }
    public Guid ToPinId { get; set; }
}

public sealed class AssetMetadata
{
    public string? Description { get; set; }
    public string? Category { get; set; }
    public RecipeMetadata? Recipe { get; set; }
}

public sealed class RecipeMetadata
{
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Difficulty { get; set; } = "Beginner";
    public List<string> ConceptsTaught { get; set; } = new();
}

public sealed class GraphMetadata
{
    public float ViewportX { get; set; }
    public float ViewportY { get; set; }
    public float ViewportZoom { get; set; }
}

public sealed class NodeMetadata
{
    public float X { get; set; }
    public float Y { get; set; }
    public string? Comment { get; set; }
}

public sealed class Header
{
    // SubsystemType and SchemaVersion removed -- $meta envelope carries this since Phase 2 (D-021).
}

public enum NodeStatus { Success, Failure, Running }
