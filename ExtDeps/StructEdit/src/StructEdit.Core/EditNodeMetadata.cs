namespace StructEdit.Core;

/// <summary>
/// Carries editor-hint attributes decoded from reflection.
/// </summary>
public sealed record EditNodeMetadata
{
    /// <summary>Minimum value from [EditRange].</summary>
    public double? Min { get; init; }

    /// <summary>Maximum value from [EditRange].</summary>
    public double? Max { get; init; }

    /// <summary>Unit string from [EditUnit].</summary>
    public string? Unit { get; init; }

    /// <summary>Fixed length from [FixedBufferHint] or [InlineArrayHint].</summary>
    public int? FixedLength { get; init; }

    /// <summary>Display name from [EditDisplayName].</summary>
    public string? DisplayName { get; init; }

    public static EditNodeMetadata Empty { get; } = new EditNodeMetadata();
}
