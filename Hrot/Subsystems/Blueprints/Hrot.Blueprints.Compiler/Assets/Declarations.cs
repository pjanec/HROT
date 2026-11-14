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

    /// <summary>
    /// FC-2/LV-1 (Q#19-B) -- when &gt; 0, this type is a FIXED-CAPACITY LIST of <see cref="TypeId"/>
    /// elements (the blueprint-variable home of Fixed Collections): storage is a compiler-generated
    /// per-class `[InlineArray(Capacity)]` buffer + `int Count` wrapper inline in State/WorkingState.
    /// `Capacity` -- not <see cref="IsArray"/> -- is the list discriminator (review F7: IsArray is the
    /// PIN-shape flag; conflating them would emit a managed `Elem[]` field). 0 (default, omitted from
    /// JSON) = not a list; existing assets round-trip byte-identically.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Capacity { get; set; }

    /// <summary>
    /// FC-2/LV-1 (Q#19-B) -- declared initial logical length (0…<see cref="Capacity"/>), seeded as
    /// `Count = InitialLength` by the generated `InitDefault` (slots are already-zeroed defaults).
    /// Only meaningful when <see cref="Capacity"/> &gt; 0. Default 0 (omitted from JSON).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int InitialLength { get; set; }
}
