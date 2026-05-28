namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Centralizes user-facing error and warning strings for the comparison feature.
/// Use these constants in validators and UI rather than inline string literals.
/// See design sections 5.3 and 7.3 for the full message catalogue.
/// </summary>
public static class ComparisonErrorMessages
{
    /// <summary>
    /// Emitted when an LLM response JSON appears cut off before completion.
    /// Used by <see cref="LlmResponseParser"/> and checked by the paste-response modal.
    /// </summary>
    public const string TruncatedResponse =
        "LLM response appears truncated. Re-run with a more capable model or smaller asset.";

    /// <summary>
    /// Prefix emitted when a required asset file is absent on disk.
    /// Append the full file path to form the final message.
    /// </summary>
    public const string FileNotFound = "File not found: ";

    /// <summary>
    /// Prefix emitted when Version A and Version B belong to different asset kinds.
    /// Append kind names and punctuation to form the final message.
    /// </summary>
    public const string AssetKindMismatch = "Cannot compare across asset kinds";

    /// <summary>
    /// Prefix emitted when the two selected files have different AssetId GUIDs.
    /// Append the GUID pair to form the final message.
    /// </summary>
    public const string AssetIdMismatch = "The two assets have different AssetIds";

    /// <summary>
    /// Prefix emitted when a file cannot be parsed to extract its AssetId.
    /// Append version label and file path to form the final message.
    /// </summary>
    public const string CannotParseMetadata = "Cannot parse";
}
