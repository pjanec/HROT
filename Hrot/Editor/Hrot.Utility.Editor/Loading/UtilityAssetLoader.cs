using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Loading;

/// <summary>
/// Result returned by <see cref="UtilityAssetLoader.Load"/>.
/// </summary>
public sealed record UtilityLoadResult(
    UtilityDecisionAsset Asset,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Reads a .cs file produced by the editor (or a hand-authored partial-manifest) and
/// returns a <see cref="UtilityDecisionAsset"/> with extracted metadata.
/// Text-based extraction only; no Roslyn, no assembly loading.
/// </summary>
public static class UtilityAssetLoader
{
    private const string GeneratedMarker = "HROT_EDITOR_GENERATED";

    /// <summary>
    /// Loads a <see cref="UtilityDecisionAsset"/> from <paramref name="filePath"/>.
    /// Returns a default read-only asset with a warning when the file does not exist.
    /// Options/considerations are not populated (deferred to a later batch).
    /// </summary>
    public static UtilityLoadResult Load(string filePath)
    {
        var warnings = new List<string>();
        var asset    = new UtilityDecisionAsset();

        if (!File.Exists(filePath))
        {
            asset.IsEditorOwned = false;
            warnings.Add($"File not found: {filePath}");
            return new UtilityLoadResult(asset, warnings);
        }

        string text  = File.ReadAllText(filePath)
                           .Replace("\r\n", "\n")
                           .Replace("\r",   "\n");
        string[] lines = text.Split('\n');

        // Check for the editor-generated marker in the first 5 lines.
        bool hasMarker  = false;
        int  checkCount = Math.Min(5, lines.Length);
        for (int i = 0; i < checkCount; i++)
        {
            if (lines[i].Contains(GeneratedMarker, StringComparison.Ordinal))
            {
                hasMarker = true;
                break;
            }
        }

        if (!hasMarker)
        {
            asset.IsEditorOwned = false;
            warnings.Add("File is not editor-generated; opened read-only.");
        }

        // Parse [UtilityDecision(...)] attribute fields permissively — any order.
        foreach (string line in lines)
        {
            if (line.Contains("assetId:", StringComparison.Ordinal))
            {
                Guid g = ParseGuid(line);
                if (g != Guid.Empty) asset.AssetId = g;
            }
            else if (line.Contains("displayName:", StringComparison.Ordinal))
            {
                string? s = ParseString(line);
                if (s != null) asset.DisplayName = s;
            }
            else if (line.Contains("kind:", StringComparison.Ordinal) &&
                     line.Contains("DecisionKind.", StringComparison.Ordinal))
            {
                DecisionKind? k = ParseDecisionKind(line);
                if (k.HasValue) asset.DecisionKind = k.Value;
            }
            else if (line.Contains("category:", StringComparison.Ordinal))
            {
                string? s = ParseString(line);
                if (s != null) asset.Category = s;
            }
            else if (line.Contains("hysteresisBonus:", StringComparison.Ordinal))
            {
                float? f = ParseFloat(line, "hysteresisBonus");
                if (f.HasValue) asset.HysteresisBonus = f.Value;
            }
        }

        asset.SourceFilePath = filePath;
        return new UtilityLoadResult(asset, warnings);
    }

    // ---- Parsing helpers -----------------------------------------------

    private static Guid ParseGuid(string line)
    {
        int start = line.IndexOf('"');
        if (start < 0) return Guid.Empty;
        int end = line.IndexOf('"', start + 1);
        if (end <= start) return Guid.Empty;
        string candidate = line.Substring(start + 1, end - start - 1);
        return Guid.TryParse(candidate, out Guid g) ? g : Guid.Empty;
    }

    private static string? ParseString(string line)
    {
        int start = line.IndexOf('"');
        if (start < 0) return null;
        int end = line.IndexOf('"', start + 1);
        if (end <= start) return null;
        return line.Substring(start + 1, end - start - 1);
    }

    private static DecisionKind? ParseDecisionKind(string line)
    {
        const string prefix = "DecisionKind.";
        int idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        int nameStart = idx + prefix.Length;
        int nameEnd   = nameStart;
        while (nameEnd < line.Length &&
               (char.IsLetterOrDigit(line[nameEnd]) || line[nameEnd] == '_'))
        {
            nameEnd++;
        }
        string name = line.Substring(nameStart, nameEnd - nameStart);
        return Enum.TryParse<DecisionKind>(name, out DecisionKind k) ? k : null;
    }

    private static float? ParseFloat(string line, string label)
    {
        string search = label + ":";
        int idx = line.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        int valueStart = idx + search.Length;
        // Skip whitespace.
        while (valueStart < line.Length && line[valueStart] == ' ')
            valueStart++;
        // Advance until 'f' or end of line.
        int valueEnd = valueStart;
        while (valueEnd < line.Length && line[valueEnd] != 'f' && line[valueEnd] != '\n')
            valueEnd++;
        string token = line.Substring(valueStart, valueEnd - valueStart).Trim();
        return float.TryParse(token, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float f) ? f : null;
    }
}
