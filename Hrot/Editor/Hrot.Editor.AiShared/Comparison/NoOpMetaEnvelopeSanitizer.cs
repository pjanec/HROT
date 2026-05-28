namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// No-op implementation of <see cref="IMetaEnvelopeSanitizer"/> for use until the
/// <c>$meta</c> envelope is widely deployed. Returns the input unchanged.
/// See design §3.5 step 1 and §8.1.
/// </summary>
public sealed class NoOpMetaEnvelopeSanitizer : IMetaEnvelopeSanitizer
{
    /// <inheritdoc/>
    public string Sanitize(string metaEnvelopeJson) => metaEnvelopeJson;
}
