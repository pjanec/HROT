using System.Text;
using System.Text.RegularExpressions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.BTree.Editor.Comparison;

/// <summary>
/// Sanitizes BTree asset C# files for LLM-based comparison (design §3.3).
/// Operates on raw file text; does NOT use reflection or runtime layout discovery.
/// Steps performed:
///   1. Locate <c>[BTreeLayout(...)]</c>.
///   2. Parse the layout method body for per-node comment, expressionTarget, and sync bindings.
///   3. Walk the CreateBuilder() chain; inject <c>//</c> comment and sync-binding lines
///      above each matching builder call, and humanize cross-asset GUID references inline.
///   4. Truncate everything from <c>[BTreeLayout(...)]</c> onward; add closing brace.
///   5. Strip the <c>; manual edits...</c> suffix from the HROT_EDITOR_GENERATED header.
///   6. Normalize line endings to <c>\n</c>.
/// </summary>
public sealed class BTreeComparisonSanitizer : IAssetComparisonSanitizer
{
    private readonly IAssetCatalog _catalog;

    /// <summary>Initializes a new instance with the given asset catalog for GUID humanization.</summary>
    public BTreeComparisonSanitizer(IAssetCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc/>
    public AssetKind TargetKind => AssetKind.BTree;

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
        // Normalize to \n for platform-independent processing.
        string normalizedText = fileText.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalizedText.Split('\n');

        var warnings = new List<SanitizationWarning>();

        int layoutLineIndex = FindLayoutAttributeLineIndex(lines);
        if (layoutLineIndex < 0)
        {
            warnings.Add(new SanitizationWarning(
                "Layout method not found; comments/sync may be missing."));
            return new SanitizationResult(
                NormalizeEndings(fileText),
                BuildMetadata(request, lines),
                warnings);
        }

        // Parse layout body for per-node metadata and sync bindings.
        var (nodeMeta, syncBindings) = ParseLayoutSection(lines, layoutLineIndex, warnings);

        // Rebuild the pre-layout section with comments/sync injected and humanization applied.
        string sanitizedText = RebuildPreLayout(lines, layoutLineIndex, nodeMeta, syncBindings, warnings);

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
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("[BTreeLayout(", StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    // ---- Layout section parsing ----

    private record NodeMeta(string? Comment, string? ExpressionTarget);

    private record SyncBinding(string FieldName, string? MasterVar, bool SyncIn, bool SyncOut);

    private static (Dictionary<string, NodeMeta> nodes, Dictionary<string, List<SyncBinding>> sync)
        ParseLayoutSection(string[] lines, int layoutLineIndex,
                           List<SanitizationWarning> warnings)
    {
        var nodes   = new Dictionary<string, NodeMeta>(StringComparer.OrdinalIgnoreCase);
        var sync    = new Dictionary<string, List<SyncBinding>>(StringComparer.OrdinalIgnoreCase);

        // Walk lines inside the layout method body.
        for (int i = layoutLineIndex + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(".Node(", StringComparison.Ordinal) ||
                trimmed.StartsWith(".Pill(", StringComparison.Ordinal))
            {
                // Collect this call (may span multiple lines) and parse it.
                string callText = CollectCallText(lines, i, out int endLine);
                ParseNodeCall(callText, nodes);
                i = endLine;
            }
            else if (trimmed.StartsWith(".SubtreeSyncField(", StringComparison.Ordinal))
            {
                string callText = CollectCallText(lines, i, out int endLine);
                ParseSyncFieldCall(callText, sync);
                i = endLine;
            }
            else if (trimmed.StartsWith(".Build(", StringComparison.Ordinal) ||
                     trimmed == "}")
            {
                break; // end of layout method body
            }
        }

        return (nodes, sync);
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
            sb.Append(' '); // normalize whitespace between lines

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

    // Regex patterns for layout parsing.
    private static readonly Regex NodeGuidPattern =
        new Regex(@"\.(?:Node|Pill)\(\s*""([0-9a-fA-F\-]{36})""", RegexOptions.Compiled);
    private static readonly Regex CommentPattern =
        new Regex(@"comment:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex ExprTargetPattern =
        new Regex(@"expressionTarget:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static readonly Regex SyncFieldPattern =
        new Regex(
            @"\.SubtreeSyncField\(\s*""([0-9a-fA-F\-]{36})""\s*,\s*""([^""]*)""\s*,\s*" +
            @"masterVar:\s*(?:""([^""]*)""|null)\s*,\s*syncIn:\s*(true|false)\s*,\s*syncOut:\s*(true|false)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void ParseNodeCall(string callText, Dictionary<string, NodeMeta> nodes)
    {
        var guidMatch = NodeGuidPattern.Match(callText);
        if (!guidMatch.Success) return;

        string guid = guidMatch.Groups[1].Value;

        string? comment = null;
        var cm = CommentPattern.Match(callText);
        if (cm.Success)
            comment = cm.Groups[1].Value.Replace("\\\"", "\"");

        string? exprTarget = null;
        var em = ExprTargetPattern.Match(callText);
        if (em.Success)
            exprTarget = em.Groups[1].Value;

        nodes[guid] = new NodeMeta(comment, exprTarget);
    }

    private static void ParseSyncFieldCall(string callText, Dictionary<string, List<SyncBinding>> sync)
    {
        var m = SyncFieldPattern.Match(callText);
        if (!m.Success) return;

        string guid      = m.Groups[1].Value;
        string fieldName = m.Groups[2].Value;
        string? masterVar = m.Groups[3].Success && m.Groups[3].Value.Length > 0
            ? m.Groups[3].Value : null;
        bool syncIn  = string.Equals(m.Groups[4].Value, "true", StringComparison.OrdinalIgnoreCase);
        bool syncOut = string.Equals(m.Groups[5].Value, "true", StringComparison.OrdinalIgnoreCase);

        if (!sync.TryGetValue(guid, out var list))
        {
            list = new List<SyncBinding>();
            sync[guid] = list;
        }
        list.Add(new SyncBinding(fieldName, masterVar, syncIn, syncOut));
    }

    // ---- Pre-layout rebuilding ----

    private static readonly Regex VisualIdPattern =
        new Regex(@"visualId:\s*new\s+Guid\(""([0-9a-fA-F\-]{36})""\)", RegexOptions.Compiled);
    private static readonly Regex SubtreeAssetGuidPattern =
        new Regex(@"\.Subtree\(""([0-9a-fA-F\-]{36})""", RegexOptions.Compiled);

    private string RebuildPreLayout(
        string[] lines,
        int layoutLineIndex,
        Dictionary<string, NodeMeta> nodeMeta,
        Dictionary<string, List<SyncBinding>> syncBindings,
        List<SanitizationWarning> warnings)
    {
        // Build a map: lineIndex -> lines to insert BEFORE that line.
        var insertionsBefore = new Dictionary<int, List<string>>();
        // Build a map: lineIndex -> suffix to append at end of that line.
        var lineSuffixes = new Dictionary<int, string>();

        // Scan pre-layout lines for visualId occurrences and subtree asset GUIDs.
        for (int i = 0; i < layoutLineIndex; i++)
        {
            string line = lines[i];

            // Check for visualId: new Guid("...") → hoist comment/sync bindings.
            var vidMatch = VisualIdPattern.Match(line);
            if (vidMatch.Success)
            {
                string guid = vidMatch.Groups[1].Value;
                bool hasMeta   = nodeMeta.TryGetValue(guid, out var meta);
                bool hasSync   = syncBindings.TryGetValue(guid, out var bindings);

                if ((hasMeta && meta!.Comment != null) || hasSync)
                {
                    int callStart = FindCallStartLine(lines, i, GetLeadingSpaceCount(line));
                    if (callStart >= 0)
                    {
                        string indent = GetLeadingSpaces(lines[callStart]);
                        var toInsert = new List<string>();

                        if (hasMeta && meta!.Comment != null)
                            toInsert.Add($"{indent}// {meta.Comment}");

                        if (hasSync)
                        {
                            foreach (var b in bindings!)
                            {
                                string master = b.MasterVar ?? "(unmapped)";
                                if (b.SyncIn && b.SyncOut)
                                    toInsert.Add($"{indent}// sync (both): {b.FieldName} <--> {master}");
                                else if (b.SyncIn)
                                    toInsert.Add($"{indent}// sync (in): {b.FieldName} <-- {master}");
                                else if (b.SyncOut)
                                    toInsert.Add($"{indent}// sync (out): {b.FieldName} --> {master}");
                            }
                        }

                        if (toInsert.Count > 0)
                        {
                            if (!insertionsBefore.TryGetValue(callStart, out var existing))
                            {
                                existing = new List<string>();
                                insertionsBefore[callStart] = existing;
                            }
                            // Only add if not already added (prevents duplicates when visualId
                            // is detected multiple times for same line).
                            if (existing.Count == 0)
                                existing.AddRange(toInsert);
                        }
                    }
                }
            }

            // Check for .Subtree("assetGuid", → humanize the cross-asset GUID reference.
            var subMatch = SubtreeAssetGuidPattern.Match(line);
            if (subMatch.Success)
            {
                string assetGuidStr = subMatch.Groups[1].Value;
                if (Guid.TryParse(assetGuidStr, out var assetGuid))
                {
                    var asset = _catalog.FindByAssetId(assetGuid);
                    string suffix = asset != null
                        ? $"  // -> {asset.Name} ({asset.Kind})"
                        : "  // -> (asset not found in catalog)";
                    lineSuffixes[i] = suffix;
                }
            }
        }

        // Find last non-blank line in pre-layout section to strip trailing blanks.
        int lastNonBlank = layoutLineIndex - 1;
        while (lastNonBlank >= 0 && lines[lastNonBlank].Trim().Length == 0)
            lastNonBlank--;

        // Reconstruct output.
        var sb = new StringBuilder();

        for (int i = 0; i <= lastNonBlank; i++)
        {
            // Insert accumulated comment/sync lines before this line.
            if (insertionsBefore.TryGetValue(i, out var inserts))
            {
                foreach (var insertedLine in inserts)
                    sb.Append(insertedLine).Append('\n');
            }

            // Transform the line (header stripping, suffix appending).
            string line = TransformLine(lines[i]);
            if (lineSuffixes.TryGetValue(i, out var suffix))
                line = line + suffix;

            sb.Append(line).Append('\n');
        }

        // Close the class (the layout method was the last thing before the closing brace).
        sb.Append('}').Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// Going backward from <paramref name="fromLine"/> - 1, finds the first line that:
    ///   (a) has strictly fewer leading spaces than <paramref name="visualIdIndent"/>, AND
    ///   (b) when trimmed, starts with '.'.
    /// Returns the line index, or -1 if not found.
    /// </summary>
    private static int FindCallStartLine(string[] lines, int fromLine, int visualIdIndent)
    {
        for (int i = fromLine - 1; i >= 0; i--)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue; // skip blank lines

            int spaces = GetLeadingSpaceCount(lines[i]);
            if (spaces < visualIdIndent && trimmed.StartsWith(".", StringComparison.Ordinal))
                return i;
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
            // Strip everything from the first "; " onwards and add a full-stop.
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
            AssetKind.BTree,
            assetId,
            request.AssetMainFilePath,
            Array.Empty<string>(), // companion discovery is TASK-C-11
            timestamp);
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request)
    {
        return new AssetMetadataBlock(
            "(unknown)",
            AssetKind.BTree,
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
        // Try [BTreeDefinition("Name", ...)] attribute first.
        const string defAttr = "[BTreeDefinition(\"";
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith(defAttr, StringComparison.Ordinal))
            {
                int nameStart = defAttr.Length;
                int nameEnd   = trimmed.IndexOf('"', nameStart);
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

    private static int GetLeadingSpaceCount(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4; // treat tab as 4 spaces
            else break;
        }
        return count;
    }

    private static string GetLeadingSpaces(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }
}
