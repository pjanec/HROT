namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Strips diagnostic-only fields from a Blueprint JSON document's <c>$meta</c> envelope
/// (e.g., <c>engineVersion</c>, <c>createdBy</c>, <c>createdUtc</c>) while preserving
/// load-bearing fields (<c>docType</c>, <c>schemaVersion</c>).
/// See design §3.5 step 1 and §8.1.
/// A no-op implementation is provided in TASK-C-08 until the <c>$meta</c> envelope
/// is widely deployed.
/// </summary>
public interface IMetaEnvelopeSanitizer
{
    /// <summary>
    /// Strips diagnostic-only fields from the <c>$meta</c> envelope JSON text.
    /// Returns the sanitized envelope. Must never throw; return the input unchanged on error.
    /// </summary>
    string Sanitize(string metaEnvelopeJson);
}
