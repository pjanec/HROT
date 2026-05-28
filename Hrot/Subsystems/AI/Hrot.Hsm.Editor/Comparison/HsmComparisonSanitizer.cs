using System.Text;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Hsm.Editor.Comparison;

/// <summary>
/// Sanitizes HSM asset C# files for LLM-based comparison (design §3.3).
/// Operates on raw file text; does NOT use reflection or runtime layout discovery.
/// Steps performed:
///   1. Locate <c>[HsmLayout(...)]</c>.
///   2. Parse the layout method body for per-element comment metadata:
///      <c>.State</c>, <c>.Transition</c>, and <c>.Region</c> layout entries.
///   3. Walk the CreateBuilder() chain; inject <c>//</c> comment lines
///      above each matching builder call (stableId for states/regions,
///      visualId for transitions and global transitions).
///   4. Truncate everything from <c>[HsmLayout(...)]</c> onward; add closing brace.
///   5. Strip the <c>; manual edits...</c> suffix from the HROT_EDITOR_GENERATED header.
///   6. Normalize line endings to <c>\n</c>.
/// </summary>
public sealed class HsmComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IAssetCatalog _catalog;

    /// <summary>Initializes a new instance with the given asset catalog.</summary>
    /// <remarks>
    /// The catalog is injected for consistency with the BTree pattern and future
    /// cross-asset GUID humanization. HSM has no cross-asset GUIDs in Phase 1.
    /// </remarks>
    public HsmComparisonSanitizer(IAssetCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public AssetKind TargetKind => AssetKind.Hsm;

    /// <inheritdoc/>
    public SanitizationResult Sanitize(AssetExportRequest request)
    {
        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            string rawText = TryReadFile(request.AssetMainFilePath);
            return new SanitizationResult(
                rawText,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"Sanitization failed unexpectedly: {ex.Message}") });
        }
    }

    // ---- Core pipeline ----

    private SanitizationResult SanitizeCore(AssetExportRequest request)
    {
        string fileText = TryReadFile(request.AssetMainFilePath);
        string normalizedText = fileText.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalizedText.Split('\n');

        var warnings = new List<SanitizationWarning>();

        int layoutLineIndex = FindLayoutAttributeLineIndex(lines);
        if (layoutLineIndex < 0)
        {
            warnings.Add(new SanitizationWarning(
                "Layout method not found; comments may be missing."));
            return new SanitizationResult(
                NormalizeEndings(fileText),
                BuildMetadata(request, lines),
                warnings);
        }

        // Parse layout body for per-element comment metadata.
        Dictionary<string, string> elementComments = ParseLayoutSection(lines, layoutLineIndex, warnings);

        // Rebuild the pre-layout section with comments injected.
        string sanitizedText = RebuildPreLayout(lines, layoutLineIndex, elementComments, warnings);

        return new SanitizationResult(
            sanitizedText,
            BuildMetadata(request, lines),
            warnings);
    }

    // ---- Layout attribute detection ----

    private static int FindLayoutAttributeLineIndex(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("[HsmLayout(", StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    // ---- Layout section parsing ----

    // Matches the first GUID string argument of .State, .Transition, or .Region layout calls.
    private static readonly Regex LayoutElementGuidPattern =
        new Regex(
            @"\.(?:State|Transition|Region)\(\s*""([0-9a-fA-F\-]{36})""",
            RegexOptions.Compiled);

    private static readonly Regex CommentPattern =
        new Regex(@"comment:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    /// <summary>
    /// Parses the layout method body and returns a dictionary mapping normalized GUID
    /// string (lowercase, D-format) to the comment extracted from that element's layout
    /// entry. Merges State, Transition, and Region entries into a single dict.
    /// </summary>
    private static Dictionary<string, string> ParseLayoutSection(
        string[] lines,
        int layoutLineIndex,
        List<SanitizationWarning> warnings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = layoutLineIndex + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(".State(", StringComparison.Ordinal) ||
                trimmed.StartsWith(".Transition(", StringComparison.Ordinal) ||
                trimmed.StartsWith(".Region(", StringComparison.Ordinal))
            {
                string callText = CollectCallText(lines, i, out int endLine);

                var guidMatch = LayoutElementGuidPattern.Match(callText);
                if (guidMatch.Success)
                {
                    string rawGuid = guidMatch.Groups[1].Value;
                    if (Guid.TryParse(rawGuid, out var guid))
                    {
                        string normalizedKey = guid.ToString("D");
                        var cm = CommentPattern.Match(callText);
                        if (cm.Success)
                        {
                            string comment = cm.Groups[1].Value.Replace("\\\"", "\"");
                            // Last write wins if the same GUID appears more than once
                            // (should not happen with valid assets).
                            result[normalizedKey] = comment;
                        }
                    }
                }

                i = endLine;
            }
            else if (trimmed.StartsWith(".Build(", StringComparison.Ordinal) ||
                     trimmed == "}")
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Collects a complete method-call text starting at <paramref name="startLine"/>,
    /// tracking parentheses balance to handle multi-line calls.
    /// Sets <paramref name="endLine"/> to the index of the last line consumed.
    /// </summary>
    private static string CollectCallText(string[] lines, int startLine, out int endLine)
    {
        var sb = new StringBuilder();
        int depth = 0;
        endLine = startLine;

        for (int i = startLine; i < lines.Length; i++)
        {
            string line = lines[i];
            sb.Append(line);
            sb.Append(' ');

            foreach (char c in line)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
            }

            if (depth <= 0 && i >= startLine)
            {
                endLine = i;
                break;
            }
        }

        return sb.ToString();
    }

    // ---- Pre-layout rebuilding ----

    // Matches stableId: new Guid("...") in the builder chain.
    private static readonly Regex StableIdPattern =
        new Regex(@"stableId:\s*new\s+Guid\(""([0-9a-fA-F\-]{36})""\)", RegexOptions.Compiled);

    // Matches visualId: new Guid("...") in the builder chain.
    private static readonly Regex VisualIdPattern =
        new Regex(@"visualId:\s*new\s+Guid\(""([0-9a-fA-F\-]{36})""\)", RegexOptions.Compiled);

    private string RebuildPreLayout(
        string[] lines,
        int layoutLineIndex,
        Dictionary<string, string> elementComments,
        List<SanitizationWarning> warnings)
    {
        // Build a map: lineIndex -> lines to insert BEFORE that line.
        var insertionsBefore = new Dictionary<int, List<string>>();

        for (int i = 0; i < layoutLineIndex; i++)
        {
            string line = lines[i];

            // Check for stableId: new Guid("...") → hoist comment above the call.
            var stableMatch = StableIdPattern.Match(line);
            if (stableMatch.Success)
            {
                string rawGuid = stableMatch.Groups[1].Value;
                if (Guid.TryParse(rawGuid, out var guid))
                {
                    string key = guid.ToString("D");
                    if (elementComments.TryGetValue(key, out string? comment))
                    {
                        int callStart = FindCallStartForStableId(lines, i);
                        if (callStart >= 0)
                            AddCommentInsertion(insertionsBefore, callStart,
                                GetLeadingSpaces(lines[callStart]), comment);
                    }
                }
            }

            // Check for visualId: new Guid("...") → hoist comment above the call.
            var visualMatch = VisualIdPattern.Match(line);
            if (visualMatch.Success)
            {
                string rawGuid = visualMatch.Groups[1].Value;
                if (Guid.TryParse(rawGuid, out var guid))
                {
                    string key = guid.ToString("D");
                    if (elementComments.TryGetValue(key, out string? comment))
                    {
                        // Both .On().GoTo() and builder.GlobalTransition() emit their
                        // visualId on the same line as the call start, so callStart = i.
                        AddCommentInsertion(insertionsBefore, i,
                            GetLeadingSpaces(lines[i]), comment);
                    }
                }
            }
        }

        // Find last non-blank line before the layout section.
        int lastNonBlank = layoutLineIndex - 1;
        while (lastNonBlank >= 0 && lines[lastNonBlank].Trim().Length == 0)
            lastNonBlank--;

        var sb = new StringBuilder();

        for (int i = 0; i <= lastNonBlank; i++)
        {
            if (insertionsBefore.TryGetValue(i, out var inserts))
            {
                foreach (var inserted in inserts)
                    sb.Append(inserted).Append('\n');
            }

            sb.Append(TransformLine(lines[i])).Append('\n');
        }

        // Close the class (layout method was the last thing before the closing brace).
        sb.Append('}').Append('\n');

        return sb.ToString();
    }

    private static void AddCommentInsertion(
        Dictionary<int, List<string>> insertionsBefore,
        int lineIndex,
        string indent,
        string comment)
    {
        if (!insertionsBefore.TryGetValue(lineIndex, out var list))
        {
            list = new List<string>();
            insertionsBefore[lineIndex] = list;
        }
        // Only add once (prevents duplicates when stableId appears multiple times for
        // the same logical call — not expected but safe).
        if (list.Count == 0)
            list.Add($"{indent}// {comment}");
    }

    /// <summary>
    /// Determines the line index of the call start for a stableId occurrence.
    /// <para>
    /// If the stableId's line itself starts with <c>}</c> (closing of a multi-line Child
    /// lambda block), performs a backward brace-depth scan to locate the <c>.Child(</c>
    /// call opener. Otherwise the stableId is on the State/one-liner-Child call line
    /// itself and that line is returned directly.
    /// </para>
    /// </summary>
    private static int FindCallStartForStableId(string[] lines, int stableIdLine)
    {
        string trimmed = lines[stableIdLine].TrimStart();

        if (!trimmed.StartsWith("}", StringComparison.Ordinal))
        {
            // stableId is on the same line as builder.State(...) or a one-liner Child.
            return stableIdLine;
        }

        // Multi-line Child block: the stableId is on the closing `}, stableId: ...` line.
        // Scan backward tracking brace depth to find the opening `{` of the lambda block,
        // then return the line before it (the .Child( declaration).
        int depth = 0;
        for (int i = stableIdLine; i >= 0; i--)
        {
            foreach (char c in lines[i])
            {
                if (c == '}') depth++;
                else if (c == '{') depth--;
            }

            if (depth <= 0)
            {
                // Line i contains the opening `{`; the .Child( call is i-1.
                return i > 0 ? i - 1 : -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Applies per-line transformations:
    ///   - Strips the <c>; manual edits...</c> suffix from the HROT_EDITOR_GENERATED header line.
    /// </summary>
    private static string TransformLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("// HROT_EDITOR_GENERATED", StringComparison.Ordinal))
        {
            int semiIdx = line.IndexOf("; ", StringComparison.Ordinal);
            if (semiIdx >= 0)
                return line[..semiIdx] + ".";
        }
        return line;
    }

    // ---- Metadata extraction ----

    private static AssetMetadataBlock BuildMetadata(AssetExportRequest request, string[] lines)
    {
        Guid assetId = ExtractAssetId(lines);
        string assetName = ExtractAssetName(lines);
        DateTime? timestamp = TryGetLastWriteTime(request.AssetMainFilePath);

        return new AssetMetadataBlock(
            assetName,
            AssetKind.Hsm,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            timestamp);
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request)
    {
        return new AssetMetadataBlock(
            "(unknown)",
            AssetKind.Hsm,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));
    }

    private static Guid ExtractAssetId(string[] lines)
    {
        // Look for: // AssetId: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        const string prefix = "// AssetId:";
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                string guidStr = trimmed[prefix.Length..].Trim();
                if (Guid.TryParse(guidStr, out var guid))
                    return guid;
            }
        }
        return Guid.Empty;
    }

    private static string ExtractAssetName(string[] lines)
    {
        // Try [HsmDefinition("Name", ...)] attribute first.
        const string defAttr = "[HsmDefinition(\"";
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(defAttr, StringComparison.Ordinal))
            {
                int nameStart = defAttr.Length;
                int nameEnd = trimmed.IndexOf('"', nameStart);
                if (nameEnd > nameStart)
                    return trimmed[nameStart..nameEnd];
            }
        }

        // Fallback: class name.
        const string classKw = "public static class ";
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(classKw, StringComparison.Ordinal))
            {
                string rest = trimmed[classKw.Length..].Trim();
                int spaceIdx = rest.IndexOf(' ');
                return spaceIdx > 0 ? rest[..spaceIdx] : rest;
            }
        }

        return "(unknown)";
    }

    // ---- Utilities ----

    private static string TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    }

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }

    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string GetLeadingSpaces(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }
}
