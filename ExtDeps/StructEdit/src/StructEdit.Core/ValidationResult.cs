namespace StructEdit.Core;

/// <summary>
/// The result of a component validation pass.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    private ValidationResult(bool isValid, IReadOnlyList<ValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>Returns a successful validation result with no errors.</summary>
    public static ValidationResult Ok() =>
        new(true, Array.Empty<ValidationError>());

    /// <summary>Returns a failed validation result with the provided errors.
    /// If <paramref name="errors"/> is empty, returns <see cref="Ok"/> instead.</summary>
    public static ValidationResult Fail(IEnumerable<ValidationError> errors)
    {
        var list = errors.ToList();
        if (list.Count == 0) return Ok();
        return new(false, list.AsReadOnly());
    }
}
