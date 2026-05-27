namespace Hrot.Blueprints.Core.Assets;

public sealed class VariableDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public BlueprintTypeRef Type { get; set; } = new();
    public string? DefaultValueJson { get; set; }
    public bool IsEditable { get; set; }
    public bool IsExposedOnSpawn { get; set; }
    public string? Category { get; set; }
    public string? Tooltip { get; set; }
    public string? Comment { get; set; }
}

public sealed class ParameterDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public BlueprintTypeRef Type { get; set; } = new();
    public string? DefaultValueJson { get; set; }
    public string? Tooltip { get; set; }
    public string? Comment { get; set; }
}

public sealed class EventDispatcherDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<ParameterDecl> Parameters { get; set; } = new();
}

public sealed class CustomEventDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<ParameterDecl> Parameters { get; set; } = new();
}

public sealed class BlueprintTypeRef
{
    public string TypeId { get; set; } = "";
    public bool IsArray { get; set; }
    public List<BlueprintTypeRef> GenericArgs { get; set; } = new();
}
