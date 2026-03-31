using System;
using System.Collections.Generic;
using Hrot.NED.Messages;

namespace FDP.Toolkit.Replication.Patching;

/// <summary>
/// Maps a pre-computed FNV-1a path hash to the attribute ID and expected value type
/// used by <see cref="JsonToRecordCompiler"/> to emit <see cref="AttributeRecord"/>s.
/// </summary>
internal readonly struct EdgeSchemaEntry
{
    /// <summary>The well-known attribute ID written into <see cref="AttributeRecord.AttributeId"/>.</summary>
    public readonly ushort AttributeId;

    /// <summary>The expected value type for this path, used to extract the correct
    /// branch from the JSON token.</summary>
    public readonly AttributeValueType ExpectedType;

    internal EdgeSchemaEntry(ushort attributeId, AttributeValueType expectedType)
    {
        AttributeId = attributeId;
        ExpectedType = expectedType;
    }
}

/// <summary>
/// Fluent builder that registers JSON attribute paths and their binary attribute IDs,
/// then produces an immutable <see cref="JsonToRecordCompiler"/>.
///
/// <para>
/// Paths are hashed at registration time using the same FNV-1a algorithm as
/// <see cref="JsonAttributeCompiler"/> so the two compilers stay in sync.
/// </para>
///
/// <para>
/// Usage pattern — mirrors <see cref="AttributeCompilerBuilder"/>:
/// <code>
/// JsonToRecordCompiler compiler = new JsonToRecordCompilerBuilder()
///     .Register("Name",                 AttributeIds.Name,    AttributeValueType.KindString)
///     .Register("GeoPosition.Latitude", AttributeIds.GeoLat,  AttributeValueType.KindFloat64)
///     .Build();
/// </code>
/// </para>
/// </summary>
public sealed class JsonToRecordCompilerBuilder
{
    private readonly Dictionary<ulong, EdgeSchemaEntry> _routes = new();

    /// <summary>
    /// Registers a JSON attribute path mapping it to a binary <see cref="AttributeRecord.AttributeId"/>.
    /// </summary>
    /// <param name="path">
    /// Dot-separated JSON path, e.g. <c>"Name"</c> or <c>"GeoPosition.Latitude"</c>.
    /// Numeric path segments are normalised to <c>*</c> (wildcard) by the FNV-1a hasher.
    /// </param>
    /// <param name="attributeId">
    /// The <see cref="AttributeIds"/> constant to write into the emitted
    /// <see cref="AttributeRecord.AttributeId"/>.
    /// </param>
    /// <param name="expectedType">
    /// Expected JSON value type for this path. Used by
    /// <see cref="JsonToRecordCompiler.Compile"/> to extract the correct typed branch.
    /// </param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="path"/> collides with an already-registered path.</exception>
    public JsonToRecordCompilerBuilder Register(string path, ushort attributeId, AttributeValueType expectedType)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path), "Path must not be null or empty.");

        ulong hash = JsonAttributeCompiler.HashPath(path);
        if (_routes.ContainsKey(hash))
            throw new InvalidOperationException(
                $"A route for path '{path}' (hash {hash}) is already registered.");

        _routes[hash] = new EdgeSchemaEntry(attributeId, expectedType);
        return this;
    }

    /// <summary>
    /// Builds an immutable <see cref="JsonToRecordCompiler"/> from all registered routes.
    /// The returned compiler is thread-safe and should be reused across calls.
    /// </summary>
    public JsonToRecordCompiler Build() => new JsonToRecordCompiler(_routes);
}
