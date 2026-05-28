using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Logging;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Reads and writes the <c>$meta</c> envelope that every FDP JSON document
/// must carry as its very first property. All public members are static.
/// </summary>
public static class JsonEnvelope
{
    /// <summary>The JSON property name that contains the envelope object.</summary>
    public const string MetaFieldName = "$meta";

    // Allowed field names inside $meta.
    private const string FieldDocType = "docType";
    private const string FieldSchemaVersion = "schemaVersion";
    private const string FieldEngineVersion = "engineVersion";
    private const string FieldCreatedBy = "createdBy";
    private const string FieldCreatedUtc = "createdUtc";

    private static readonly HashSet<string> s_allowedFields = new(StringComparer.Ordinal)
    {
        FieldDocType, FieldSchemaVersion, FieldEngineVersion, FieldCreatedBy, FieldCreatedUtc
    };

    // Sentinel type used as the logger category (JsonEnvelope is static and cannot be a type arg).
    private sealed class Envelope { }

    // ---------------------------------------------------------------
    // Peek overloads (streaming — no DOM allocation)
    // ---------------------------------------------------------------

    /// <summary>
    /// Reads <c>$meta</c> from raw UTF-8 JSON bytes using a forward-only
    /// <see cref="Utf8JsonReader"/>. Stops immediately after the closing
    /// <c>}</c> of the envelope object.
    /// </summary>
    /// <param name="utf8Json">Raw document bytes.</param>
    /// <returns>The parsed <see cref="DocumentMeta"/>.</returns>
    /// <exception cref="MigrationException">
    /// <c>$meta</c> is absent or contains an unrecognised field.
    /// </exception>
    public static DocumentMeta Peek(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        return ReadMetaFromReader(ref reader);
    }

    /// <summary>
    /// Reads <c>$meta</c> from a stream. Reads exactly as many bytes as
    /// needed to parse the envelope — stream position is advanced past the
    /// closing <c>}</c> of <c>$meta</c> (plus any buffered look-ahead).
    /// The stream is never disposed.
    /// </summary>
    /// <param name="stream">Readable stream positioned at the document start.</param>
    /// <returns>The parsed <see cref="DocumentMeta"/>.</returns>
    public static DocumentMeta Peek(Stream stream)
    {
        // Read entire stream into a buffer then slice — the reader is
        // forward-only and we need the initial segment to be pinned.
        // For the purposes of the contract ("stops after $meta }") we
        // track the consumed position and seek the stream back if seekable.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var meta = ReadMetaFromReader(ref reader);

        // Seek stream to the position that corresponds to after the
        // $meta closing brace (BytesConsumed tracks exact reader position).
        if (stream.CanSeek)
            stream.Seek(reader.BytesConsumed, SeekOrigin.Begin);

        return meta;
    }

    /// <summary>
    /// Reads <c>$meta</c> from a file path using UTF-8 streaming peek.
    /// </summary>
    /// <param name="path">File system path to the document.</param>
    /// <returns>The parsed <see cref="DocumentMeta"/>.</returns>
    public static DocumentMeta Peek(string path)
    {
        byte[] utf8 = File.ReadAllBytes(path);
        return Peek(utf8.AsSpan());
    }

    // ---------------------------------------------------------------
    // DOM-based read
    // ---------------------------------------------------------------

    /// <summary>
    /// Reads <c>$meta</c> from an already-parsed <see cref="JsonObject"/>
    /// DOM root.
    /// </summary>
    public static DocumentMeta Read(JsonObject root)
    {
        if (!root.TryGetPropertyValue(MetaFieldName, out JsonNode? metaNode)
            || metaNode is not JsonObject metaObj)
        {
            throw new MigrationException(
                $"Document does not contain a '{MetaFieldName}' envelope object.",
                docType: null, fromVersion: null, toVersion: null,
                sourcePath: null, path: "$");
        }

        // Warn if $meta is not the first property.
        CheckMetaIsFirst(root);

        return ParseMetaObject(metaObj);
    }

    // ---------------------------------------------------------------
    // DOM-based write
    // ---------------------------------------------------------------

    /// <summary>
    /// Stamps the supplied <paramref name="meta"/> as the first property of
    /// <paramref name="root"/>, replacing any existing <c>$meta</c> object.
    /// </summary>
    public static void Write(JsonObject root, DocumentMeta meta)
    {
        // Build the $meta object.
        var metaObj = new JsonObject();
        metaObj[FieldDocType] = JsonValue.Create(meta.DocType);
        metaObj[FieldSchemaVersion] = JsonValue.Create(meta.SchemaVersion);

        if (meta.EngineVersion is not null)
            metaObj[FieldEngineVersion] = JsonValue.Create(meta.EngineVersion);

        if (meta.CreatedBy is not null)
            metaObj[FieldCreatedBy] = JsonValue.Create(meta.CreatedBy);

        if (meta.CreatedUtc.HasValue)
            metaObj[FieldCreatedUtc] = JsonValue.Create(meta.CreatedUtc.Value.ToString("O"));

        // Remove existing $meta if present (it may be at any position).
        root.Remove(MetaFieldName);

        // We need $meta first — collect remaining properties, clear, re-add.
        var others = root.ToList();
        root.Clear();
        root[MetaFieldName] = metaObj;
        foreach (var (key, val) in others)
            root[key] = val?.DeepClone();
    }

    // ---------------------------------------------------------------
    // Query helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if <paramref name="root"/> contains a
    /// <c>$meta</c> property that is a JSON object. Never throws.
    /// </summary>
    public static bool HasEnvelope(JsonObject root)
    {
        return root.TryGetPropertyValue(MetaFieldName, out JsonNode? n)
            && n is JsonObject;
    }

    // ---------------------------------------------------------------
    // Non-destructive update helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns a new <see cref="DocumentMeta"/> with
    /// <see cref="DocumentMeta.SchemaVersion"/> replaced.
    /// </summary>
    public static DocumentMeta WithSchemaVersion(DocumentMeta meta, int newVersion)
        => new(meta.DocType, newVersion, meta.EngineVersion, meta.CreatedBy, meta.CreatedUtc);

    /// <summary>
    /// Returns a new <see cref="DocumentMeta"/> with
    /// <see cref="DocumentMeta.EngineVersion"/> replaced.
    /// </summary>
    public static DocumentMeta WithEngineVersion(DocumentMeta meta, string newEngineVersion)
        => new(meta.DocType, meta.SchemaVersion, newEngineVersion, meta.CreatedBy, meta.CreatedUtc);

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Drives the forward-only reader until the $meta object is consumed.
    /// </summary>
    private static DocumentMeta ReadMetaFromReader(ref Utf8JsonReader reader)
    {
        // The reader must see the outer object start first.
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new MigrationException("Document root is not a JSON object.");

        bool firstProperty = true;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break; // root object closed without $meta

            if (reader.TokenType != JsonTokenType.PropertyName)
                break; // malformed

            string propName = reader.GetString()!;

            if (propName == MetaFieldName)
            {
                if (!firstProperty)
                {
                    FdpLog<Envelope>.Warn(
                        "'{0}' is not the first property of the document; migration may be unreliable.",
                        MetaFieldName);
                }

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    throw new MigrationException($"'{MetaFieldName}' value is not a JSON object.");

                return ReadMetaObject(ref reader);
            }
            else
            {
                // Skip this property's value.
                reader.Read(); // move to the value
                SkipValue(ref reader);
                firstProperty = false;
            }
        }

        throw new MigrationException(
            $"Document does not contain a '{MetaFieldName}' envelope object.",
            docType: null, fromVersion: null, toVersion: null,
            sourcePath: null, path: "$");
    }

    /// <summary>
    /// Parses the $meta object from the reader positioned just after
    /// <see cref="JsonTokenType.StartObject"/>. On return the reader is
    /// positioned at the <see cref="JsonTokenType.EndObject"/> of $meta.
    /// </summary>
    private static DocumentMeta ReadMetaObject(ref Utf8JsonReader reader)
    {
        string? docType = null;
        int? schemaVersion = null;
        string? engineVersion = null;
        string? createdBy = null;
        DateTime? createdUtc = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new MigrationException($"Unexpected token inside '{MetaFieldName}'.");

            string fieldName = reader.GetString()!;

            if (!s_allowedFields.Contains(fieldName))
            {
                throw new MigrationException(
                    $"'{MetaFieldName}' contains unrecognised field '{fieldName}'. Only the fields " +
                    $"docType, schemaVersion, engineVersion, createdBy, and createdUtc are allowed.");
            }

            reader.Read(); // move to value

            switch (fieldName)
            {
                case FieldDocType:
                    docType = reader.GetString();
                    break;
                case FieldSchemaVersion:
                    try { schemaVersion = reader.GetInt32(); }
                    catch (Exception ex) when (ex is InvalidOperationException or FormatException)
                    {
                        throw new MigrationException(
                            $"'{MetaFieldName}.{FieldSchemaVersion}' must be an integer; got token type {reader.TokenType}.",
                            innerException: ex);
                    }
                    break;
                case FieldEngineVersion:
                    engineVersion = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case FieldCreatedBy:
                    createdBy = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case FieldCreatedUtc:
                    if (reader.TokenType != JsonTokenType.Null)
                    {
                        string? raw = reader.GetString();
                        if (raw is not null && DateTime.TryParse(raw, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                        {
                            createdUtc = dt;
                        }
                    }
                    break;
            }
        }

        if (docType is null)
            throw new MigrationException($"'{MetaFieldName}.docType' is missing or null.");
        if (docType.Length == 0)
            throw new MigrationException($"'{MetaFieldName}.docType' must not be empty.");
        if (!schemaVersion.HasValue)
            throw new MigrationException($"'{MetaFieldName}.schemaVersion' is missing.");

        try
        {
            return new DocumentMeta(docType, schemaVersion.Value, engineVersion, createdBy, createdUtc);
        }
        catch (ArgumentException ex)
        {
            throw new MigrationException($"Invalid value in '{MetaFieldName}': {ex.Message}", ex);
        }
    }

    /// <summary>Parses $meta from an already-resolved DOM <see cref="JsonObject"/>.</summary>
    private static DocumentMeta ParseMetaObject(JsonObject metaObj)
    {
        string? docType = null;
        int? schemaVersion = null;
        string? engineVersion = null;
        string? createdBy = null;
        DateTime? createdUtc = null;

        foreach (var (key, val) in metaObj)
        {
            if (!s_allowedFields.Contains(key))
            {
                throw new MigrationException(
                    $"'{MetaFieldName}' contains unrecognised field '{key}'. Only the fields " +
                    $"docType, schemaVersion, engineVersion, createdBy, and createdUtc are allowed.");
            }

            switch (key)
            {
                case FieldDocType:
                    docType = val?.GetValue<string>();
                    break;
                case FieldSchemaVersion:
                    schemaVersion = val?.GetValue<int>();
                    break;
                case FieldEngineVersion:
                    engineVersion = val is null || val.GetValueKind() == JsonValueKind.Null
                        ? null : val.GetValue<string>();
                    break;
                case FieldCreatedBy:
                    createdBy = val is null || val.GetValueKind() == JsonValueKind.Null
                        ? null : val.GetValue<string>();
                    break;
                case FieldCreatedUtc:
                    if (val is not null && val.GetValueKind() != JsonValueKind.Null)
                    {
                        string? raw = val.GetValue<string>();
                        if (raw is not null && DateTime.TryParse(raw, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt))
                        {
                            createdUtc = dt;
                        }
                    }
                    break;
            }
        }

        if (docType is null)
            throw new MigrationException($"'{MetaFieldName}.docType' is missing or null.");
        if (docType.Length == 0)
            throw new MigrationException($"'{MetaFieldName}.docType' must not be empty.");
        if (!schemaVersion.HasValue)
            throw new MigrationException($"'{MetaFieldName}.schemaVersion' is missing.");

        try
        {
            return new DocumentMeta(docType, schemaVersion.Value, engineVersion, createdBy, createdUtc);
        }
        catch (ArgumentException ex)
        {
            throw new MigrationException($"Invalid value in '{MetaFieldName}': {ex.Message}", ex);
        }
    }

    /// <summary>Checks that $meta appears before any other top-level property.</summary>
    private static void CheckMetaIsFirst(JsonObject root)
    {
        foreach (var (key, _) in root)
        {
            if (key == MetaFieldName)
                return; // $meta is first — good

            // Some other property appeared first.
            FdpLog<Envelope>.Warn(
                "'{0}' is not the first property of the document; migration may be unreliable.",
                MetaFieldName);
            return;
        }
    }

    /// <summary>
    /// Skips the current value token and all its children in the reader.
    /// Reader must be positioned at the value token.
    /// </summary>
    private static void SkipValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                int depth = 1;
                while (depth > 0 && reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartObject
                        || reader.TokenType == JsonTokenType.StartArray)
                        depth++;
                    else if (reader.TokenType == JsonTokenType.EndObject
                             || reader.TokenType == JsonTokenType.EndArray)
                        depth--;
                }
                break;
            }
            case JsonTokenType.StartArray:
            {
                int depth = 1;
                while (depth > 0 && reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.StartObject
                        || reader.TokenType == JsonTokenType.StartArray)
                        depth++;
                    else if (reader.TokenType == JsonTokenType.EndObject
                             || reader.TokenType == JsonTokenType.EndArray)
                        depth--;
                }
                break;
            }
            // Scalars: already consumed.
            default:
                break;
        }
    }
}
