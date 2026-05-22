namespace Hrot.Blueprints.Editor.Inspector;

public sealed record DrawContext(
    bool IsReadOnly = false,
    string IdPrefix = "",
    object? TypeRegistry = null);
