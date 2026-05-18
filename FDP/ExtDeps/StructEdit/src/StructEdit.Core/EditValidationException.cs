namespace StructEdit.Core;

/// <summary>
/// Exception thrown when component validation fails.
/// </summary>
public sealed class EditValidationException : Exception
{
    public ValidationResult Result { get; }

    public EditValidationException(ValidationResult result)
        : base("Component validation failed.")
    {
        Result = result;
    }

    public EditValidationException(ValidationResult result, string message)
        : base(message)
    {
        Result = result;
    }
}
