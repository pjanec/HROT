using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Captures the "unknowns" from a down-migration as a flat list of
/// JSONPath-indexed operations that can be replayed at save-back time
/// to restore the higher-version exclusive content.
/// </summary>
internal sealed class UnknownsJournal
{
    private static readonly JsonSerializerOptions s_writeOptions =
        new JsonSerializerOptions { WriteIndented = true };

    public DocumentMeta JournalMeta { get; }
    public string SourceDocType { get; }
    public int SourceFileVersion { get; }
    public int DownMigratedToVersion { get; }
    public string SourceContentHash { get; }
    public IReadOnlyList<JournalOperation> Operations { get; }

    private UnknownsJournal(
        DocumentMeta journalMeta,
        string sourceDocType,
        int sourceFileVersion,
        int downMigratedToVersion,
        string sourceContentHash,
        IReadOnlyList<JournalOperation> operations)
    {
        JournalMeta = journalMeta;
        SourceDocType = sourceDocType;
        SourceFileVersion = sourceFileVersion;
        DownMigratedToVersion = downMigratedToVersion;
        SourceContentHash = sourceContentHash;
        Operations = operations;
    }

    // ---------------------------------------------------------------
    // Compute
    // ---------------------------------------------------------------

    /// <summary>
    /// Computes the journal by diffing pre- and post-down-migration DOMs.
    /// </summary>
    public static UnknownsJournal Compute(
        JsonObject preMigration,
        JsonObject postMigration,
        string sourceDocType,
        int sourceVersion,
        int targetVersion,
        string sourceContentHash,
        string engineVersion,
        string createdBy)
    {
        var diffRoot = DomDiffer.Diff(preMigration, postMigration, compareArraysElementWise: true);
        var ops = DiffToJournalConverter.Convert(diffRoot, preMigration);

        var meta = new DocumentMeta(
            docType: FdpDocumentTypes.MigrationJournal,
            schemaVersion: 1,
            engineVersion: engineVersion,
            createdBy: createdBy,
            createdUtc: DateTime.UtcNow);

        return new UnknownsJournal(meta, sourceDocType, sourceVersion, targetVersion,
            sourceContentHash, ops);
    }

    // ---------------------------------------------------------------
    // Serialize
    // ---------------------------------------------------------------

    /// <summary>
    /// Serializes the journal to indented JSON per wire-format spec §5.
    /// </summary>
    public string Serialize()
    {
        var root = new JsonObject();

        // $meta first (per §8.4)
        var metaObj = new JsonObject();
        metaObj["docType"] = JournalMeta.DocType;
        metaObj["schemaVersion"] = JournalMeta.SchemaVersion;
        if (JournalMeta.EngineVersion is not null)
            metaObj["engineVersion"] = JournalMeta.EngineVersion;
        if (JournalMeta.CreatedBy is not null)
            metaObj["createdBy"] = JournalMeta.CreatedBy;
        if (JournalMeta.CreatedUtc.HasValue)
            metaObj["createdUtc"] = JournalMeta.CreatedUtc.Value.ToString("O");
        root["$meta"] = metaObj;

        root["sourceDocType"] = SourceDocType;
        root["sourceFileVersion"] = SourceFileVersion;
        root["downMigratedToVersion"] = DownMigratedToVersion;
        root["sourceContentHash"] = SourceContentHash;

        var opsArray = new JsonArray();
        foreach (var op in Operations)
        {
            var opObj = new JsonObject();
            opObj["kind"] = op.Kind == JournalOpKind.Set ? "Set" : "Remove";
            opObj["path"] = op.Path;
            if (op.Kind == JournalOpKind.Set && op.Value is not null)
                opObj["value"] = op.Value.DeepClone();
            opsArray.Add(opObj);
        }
        root["operations"] = opsArray;

        // Produce indented JSON with \n newlines.
        var json = root.ToJsonString(s_writeOptions);
        // Normalize to \n (System.Text.Json on Windows may emit \r\n).
        if (json.Contains('\r'))
            json = json.Replace("\r\n", "\n");
        return json;
    }

    // ---------------------------------------------------------------
    // Deserialize
    // ---------------------------------------------------------------

    /// <summary>
    /// Deserializes a journal from JSON. Validates the envelope.
    /// Throws <see cref="MigrationException"/> on validation failure.
    /// </summary>
    public static UnknownsJournal Deserialize(string json)
    {
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new MigrationException($"UnknownsJournal: failed to parse JSON. {ex.Message}", ex);
        }

        if (rootNode is not JsonObject root)
            throw new MigrationException("UnknownsJournal: JSON root must be an object.");

        // Validate $meta
        if (root["$meta"] is not JsonObject metaObj)
            throw new MigrationException("UnknownsJournal: missing '$meta' object.");

        string? docType = metaObj["docType"]?.GetValue<string>();
        if (docType != FdpDocumentTypes.MigrationJournal)
            throw new MigrationException(
                $"UnknownsJournal: expected docType '{FdpDocumentTypes.MigrationJournal}', got '{docType}'.");

        int schemaVersion = metaObj["schemaVersion"]?.GetValue<int>()
            ?? throw new MigrationException("UnknownsJournal: missing '$meta.schemaVersion'.");
        if (schemaVersion != 1)
            throw new MigrationException(
                $"UnknownsJournal: unsupported schemaVersion {schemaVersion}; expected 1.");

        string? engineVersion = metaObj["engineVersion"]?.GetValue<string>();
        string? createdBy = metaObj["createdBy"]?.GetValue<string>();
        DateTime? createdUtc = null;
        if (metaObj["createdUtc"] is JsonValue createdUtcNode)
        {
            string? createdUtcStr = createdUtcNode.GetValue<string>();
            if (createdUtcStr is not null
                && DateTime.TryParse(createdUtcStr,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsedUtc))
                createdUtc = parsedUtc;
        }

        var journalMeta = new DocumentMeta(FdpDocumentTypes.MigrationJournal, schemaVersion,
            engineVersion, createdBy, createdUtc);

        // Validate required body fields
        string sourceDocType = root["sourceDocType"]?.GetValue<string>()
            ?? throw new MigrationException("UnknownsJournal: missing 'sourceDocType'.");

        int sourceFileVersion = root["sourceFileVersion"]?.GetValue<int>()
            ?? throw new MigrationException("UnknownsJournal: missing 'sourceFileVersion'.");

        int downMigratedToVersion = root["downMigratedToVersion"]?.GetValue<int>()
            ?? throw new MigrationException("UnknownsJournal: missing 'downMigratedToVersion'.");

        string sourceContentHash = root["sourceContentHash"]?.GetValue<string>()
            ?? throw new MigrationException("UnknownsJournal: missing 'sourceContentHash'.");

        // Parse operations
        if (root["operations"] is not JsonArray opsArray)
            throw new MigrationException("UnknownsJournal: missing 'operations' array.");

        var ops = new List<JournalOperation>(opsArray.Count);
        for (int i = 0; i < opsArray.Count; i++)
        {
            if (opsArray[i] is not JsonObject opObj)
                throw new MigrationException($"UnknownsJournal: operation[{i}] must be an object.");

            string? kindStr = opObj["kind"]?.GetValue<string>();
            JournalOpKind kind = kindStr switch
            {
                "Set" => JournalOpKind.Set,
                "Remove" => JournalOpKind.Remove,
                _ => throw new MigrationException(
                    $"UnknownsJournal: operation[{i}].kind must be 'Set' or 'Remove', got '{kindStr}'.")
            };

            string? path = opObj["path"]?.GetValue<string>()
                ?? throw new MigrationException($"UnknownsJournal: operation[{i}].path is missing.");

            JsonNode? value = opObj["value"]?.DeepClone();

            ops.Add(new JournalOperation(kind, path, value));
        }

        return new UnknownsJournal(journalMeta, sourceDocType, sourceFileVersion,
            downMigratedToVersion, sourceContentHash, ops.AsReadOnly());
    }

    // ---------------------------------------------------------------
    // ApplyTo
    // ---------------------------------------------------------------

    /// <summary>
    /// Applies the journal to a DOM, restoring the higher-version shape.
    /// All <c>Set</c> operations are applied first (in journal order),
    /// then all <c>Remove</c> operations (in journal order). See §7.
    /// </summary>
    public void ApplyTo(JsonObject root)
    {
        // Pass 1: all Set operations
        foreach (var op in Operations)
        {
            if (op.Kind != JournalOpKind.Set) continue;
            var path = JsonPathParser.Parse(op.Path);
            path.TryWrite(root, op.Value?.DeepClone());
        }

        // Pass 2: all Remove operations
        foreach (var op in Operations)
        {
            if (op.Kind != JournalOpKind.Remove) continue;
            var path = JsonPathParser.Parse(op.Path);
            path.TryRemove(root);
        }
    }
}
