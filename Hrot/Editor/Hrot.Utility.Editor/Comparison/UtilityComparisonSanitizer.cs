using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Utility.Editor.Comparison;

/// <summary>
/// Sanitizes Utility AI decision C# files for LLM-based comparison (design SS10).
/// Operates on raw file text. Steps performed:
///   1. Normalize line endings to \n.
///   2. Strip the [UtilityLayout] method block (if present).
///   3. Sanitize the HROT_EDITOR_GENERATED header line (strip suffix after the marker prefix).
/// </summary>
public sealed class UtilityComparisonSanitizer : IAssetComparisonSanitizer
{
    private const string GeneratedMarkerPrefix = "// HROT_EDITOR_GENERATED";
    private const string AssetIdPrefix         = "// AssetId:";
    private const string ClassPrefix           = "public sealed partial class ";

    /// <inheritdoc/>
    public AssetKind TargetKind => AssetKind.Utility;

    /// <inheritdoc/>
    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    // ---- Core pipeline ----

    private static SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        var warnings = new List<SanitizationWarning>();

        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"File not found: {request.AssetMainFilePath}") });
        }

        string rawText        = File.ReadAllText(request.AssetMainFilePath);
        string normalizedText = NormalizeEndings(rawText);
        string[] lines        = normalizedText.Split('\n');

        // Strip [UtilityLayout] block if present (optional; no warning when absent)
        lines = StripLayoutBlock(lines);

        // Sanitize the HROT_EDITOR_GENERATED header line
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(GeneratedMarkerPrefix, StringComparison.Ordinal))
            {
                lines[i] = GeneratedMarkerPrefix;
                break;
            }
        }

        string sanitizedText = string.Join("\n", lines);

        // Extract metadata
        Guid   assetId   = ExtractAssetId(lines);
        string assetName = ExtractAssetName(lines);

        var metadata = new AssetMetadataBlock(
            assetName,
            AssetKind.Utility,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));

        return new SanitizationResult(sanitizedText, metadata, warnings);
    }

    // ---- Layout block stripping ----

    private static string[] StripLayoutBlock(string[] lines)
    {
        int startIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("[UtilityLayout]", StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
            return lines; // no layout block; proceed without stripping

        // Count braces from startIndex to find the matching closing brace
        int braceCount = 0;
        int endIndex   = -1;
        for (int i = startIndex; i < lines.Length; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '{')
                {
                    braceCount++;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        endIndex = i;
                        break;
                    }
                }
            }
            if (endIndex >= 0) break;
        }

        if (endIndex < 0)
            return lines; // malformed block; return as-is

        var result = new List<string>(lines.Length - (endIndex - startIndex + 1));
        for (int i = 0; i < lines.Length; i++)
        {
            if (i >= startIndex && i <= endIndex) continue;
            result.Add(lines[i]);
        }
        return result.ToArray();
    }

    // ---- Metadata extraction ----

    private static Guid ExtractAssetId(string[] lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(AssetIdPrefix, StringComparison.Ordinal))
            {
                string raw = trimmed[AssetIdPrefix.Length..].Trim();
                if (Guid.TryParse(raw, out var guid))
                    return guid;
            }
        }
        return Guid.Empty;
    }

    private static string ExtractAssetName(string[] lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(ClassPrefix, StringComparison.Ordinal))
            {
                string rest          = trimmed[ClassPrefix.Length..];
                int    spaceOrColon  = rest.IndexOfAny(new[] { ' ', ':' });
                if (spaceOrColon > 0)
                    return rest[..spaceOrColon];
                if (rest.Length > 0)
                    return rest;
            }
        }
        return "(unknown)";
    }

    // ---- Utilities ----

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request) =>
        new AssetMetadataBlock(
            "(unknown)",
            AssetKind.Utility,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));

    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }
}
