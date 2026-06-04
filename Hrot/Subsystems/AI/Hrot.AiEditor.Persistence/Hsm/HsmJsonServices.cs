using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hrot.AiEditor.Persistence.Hsm;

/// <summary>
/// JSON serialization services for HSM assets (*.hsm.json).
/// Mirrors BlueprintJsonServices and BTreeJsonServices exactly per design §5.1/§2.6.
/// </summary>
public static class HsmJsonServices
{
    /// <summary>Document type identifier for *.hsm.json files.</summary>
    public const string DocType = "Hrot.Hsm";
    /// <summary>Schema version for this batch (Phase 1).</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions _options;

    static HsmJsonServices()
    {
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            WriteIndented               = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        _options = opts;
    }

    /// <summary>
    /// Serializes an HSM asset DTO to JSON with $meta first.
    /// Mirrors BlueprintJsonServices.Serialize exactly.
    /// </summary>
    public static string Serialize(HsmAssetDto asset)
    {
        var dom = JsonSerializer.SerializeToNode(asset, _options)!.AsObject();
        StampMeta(dom, DocType, SchemaVersion);
        return dom.ToJsonString(_options);
    }

    /// <summary>
    /// Deserializes an HSM asset DTO from JSON.
    /// Tolerates unknown properties and missing $meta (legacy-safe).
    /// </summary>
    public static HsmAssetDto? Deserialize(string json)
        => JsonSerializer.Deserialize<HsmAssetDto>(json, _options);

    /// <summary>
    /// Header-lazy discovery: reads only AssetId+Name from a *.hsm.json file.
    /// Never throws on malformed files — returns null on any parse error.
    /// </summary>
    public static (Guid AssetId, string Name)? ReadHeader(string json)
    {
        try
        {
            Guid assetId = default;
            string? name = null;
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json),
                new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling     = JsonCommentHandling.Skip,
                });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            int depth = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) { depth++; continue; }
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (depth == 0) break;
                    depth--;
                    continue;
                }
                if (depth > 0) { reader.TrySkip(); continue; }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propName = reader.GetString();
                    if (!reader.Read()) break;

                    if (propName != null && string.Equals(propName, "AssetId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var s = reader.GetString();
                            if (s != null) Guid.TryParse(s, out assetId);
                        }
                    }
                    else if (propName != null && string.Equals(propName, "Name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader.TokenType == JsonTokenType.String)
                            name = reader.GetString();
                    }
                    else
                    {
                        reader.TrySkip();
                    }

                    if (assetId != default && name != null)
                        return (assetId, name);
                }
            }

            return (assetId != default && name != null)
                ? (assetId, name)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Enumerates *.hsm.json files in a directory tree, returning header info.
    /// Skips malformed files silently.
    /// </summary>
    public static IEnumerable<(string FilePath, Guid AssetId, string Name)> DiscoverHeaders(
        string rootDirectory,
        SearchOption searchOption = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(rootDirectory)) yield break;
        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*.hsm.json", searchOption))
        {
            string? json = null;
            try { json = File.ReadAllText(file); }
            catch { continue; }

            var header = ReadHeader(json);
            if (header.HasValue)
                yield return (file, header.Value.AssetId, header.Value.Name);
        }
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    internal static void StampMeta(JsonObject dom, string docType, int schemaVersion)
    {
        dom.Remove("$meta");

        var meta = new JsonObject
        {
            ["docType"]       = JsonValue.Create(docType),
            ["schemaVersion"] = JsonValue.Create(schemaVersion),
        };

        var copy = new JsonObject { ["$meta"] = meta };
        foreach (var kv in dom)
            copy[kv.Key] = kv.Value?.DeepClone();

        var keys = new List<string>();
        foreach (var kv in dom) keys.Add(kv.Key);
        foreach (var k in keys) dom.Remove(k);
        foreach (var kv in copy) dom[kv.Key] = kv.Value?.DeepClone();
    }
}
