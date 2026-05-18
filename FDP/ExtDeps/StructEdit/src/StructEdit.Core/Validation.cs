namespace StructEdit.Core;

/// <summary>
/// Interface for validators that check a component's edit buffer state.
/// </summary>
public interface IComponentValidator
{
    ValidationResult Validate(EditValidationContext context);
}

/// <summary>
/// Context passed to a component validator during a validation pass.
/// Validation always runs against the entire edit buffer, not only the visible scoped nodes.
/// </summary>
public sealed class EditValidationContext
{
    public required Type ComponentType { get; init; }
    public required IEditBuffer Buffer { get; init; }
    public required EditScope Scope { get; init; }
}
