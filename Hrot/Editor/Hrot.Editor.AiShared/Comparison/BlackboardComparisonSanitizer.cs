using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Sanitizes Blackboard DTO asset C# files for LLM-based comparison (design §3.4).
/// Operates on raw file text. Steps performed:
///   1. Read the inline <c>{Name}.Blackboard.cs</c> file.
///   2. Discover the optional companion <c>{Name}.HeavyBlackboard.cs</c> in the same directory.
///   3. Emit both files as a labeled concatenation with <c>// === ... ===</c> section headers.
///   4. No comment hoisting needed; XML <c>///</c> comments are already canonical.
/// </summary>
public sealed class BlackboardComparisonSanitizer : IAssetComparisonSanitizer
{
    /// <inheritdoc/>
    public AssetKind TargetKind => AssetKind.Blackboard;

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

        // Read the inline file.
        if (!File.Exists(request.AssetMainFilePath))
        {
            return new SanitizationResult(
                string.Empty,
                BuildFallbackMetadata(request),
                new[] { new SanitizationWarning($"File not found: {request.AssetMainFilePath}") });
        }

        string inlineText = NormalizeEndings(File.ReadAllText(request.AssetMainFilePath));
        string[] inlineLines = inlineText.Split('\n');

        // Discover optional companion (.HeavyBlackboard.cs).
        string? heavyPath = DiscoverHeavyCompanion(request.AssetMainFilePath);
        string? heavyText = null;
        if (heavyPath != null && File.Exists(heavyPath))
            heavyText = NormalizeEndings(File.ReadAllText(heavyPath));

        // Assemble labeled concatenation.
        var sb = new System.Text.StringBuilder();
        sb.Append("// === Inline blackboard ===\n");
        sb.Append(inlineText);

        if (heavyText != null)
        {
            sb.Append("\n// === Heavy blackboard (overflow) ===\n");
            sb.Append(heavyText);
        }

        string sanitizedText = sb.ToString();

        // Build companion list for metadata.
        var companions = new List<string>();
        if (heavyText != null)
            companions.Add(heavyPath!);

        var metadata = BuildMetadata(request, inlineLines, companions);

        return new SanitizationResult(sanitizedText, metadata, warnings);
    }

    // ---- Companion file discovery ----

    /// <summary>
    /// Derives the heavy companion path from the main file path.
    /// If the main file is <c>Foo.Blackboard.cs</c>, the companion is
    /// <c>Foo.HeavyBlackboard.cs</c> in the same directory.
    /// Returns null when the naming convention does not apply.
    /// </summary>
    private static string? DiscoverHeavyCompanion(string mainFilePath)
    {
        const string BlackboardSuffix = ".Blackboard.cs";

        string fileName = Path.GetFileName(mainFilePath);
        if (!fileName.EndsWith(BlackboardSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        string baseName = fileName[..^BlackboardSuffix.Length];
        string dir = Path.GetDirectoryName(mainFilePath) ?? string.Empty;
        return Path.Combine(dir, baseName + ".HeavyBlackboard.cs");
    }

    // ---- Metadata extraction ----

    private static AssetMetadataBlock BuildMetadata(
        AssetExportRequest request,
        string[] inlineLines,
        IReadOnlyList<string> companionFiles)
    {
        // The Blackboard emitter writes // OwningAssetId: and // OwningAssetName:
        Guid assetId   = ExtractAssetId(inlineLines);
        string assetName = ExtractAssetName(inlineLines);
        DateTime? timestamp = TryGetLastWriteTime(request.AssetMainFilePath);

        return new AssetMetadataBlock(
            assetName,
            AssetKind.Blackboard,
            assetId,
            request.AssetMainFilePath,
            companionFiles,
            timestamp);
    }

    private static AssetMetadataBlock BuildFallbackMetadata(AssetExportRequest request)
    {
        return new AssetMetadataBlock(
            "(unknown)",
            AssetKind.Blackboard,
            Guid.Empty,
            request.AssetMainFilePath,
            Array.Empty<string>(),
            TryGetLastWriteTime(request.AssetMainFilePath));
    }

    private static Guid ExtractAssetId(string[] lines)
    {
        // Support both // OwningAssetId: (actual emitter) and // AssetId: (design doc notation).
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            string? raw = null;
            if (trimmed.StartsWith("// OwningAssetId:", StringComparison.Ordinal))
                raw = trimmed["// OwningAssetId:".Length..].Trim();
            else if (trimmed.StartsWith("// AssetId:", StringComparison.Ordinal))
                raw = trimmed["// AssetId:".Length..].Trim();

            if (raw != null && Guid.TryParse(raw, out var guid))
                return guid;
        }
        return Guid.Empty;
    }

    private static string ExtractAssetName(string[] lines)
    {
        // Support both // OwningAssetName: (actual emitter) and // AssetName: (design doc notation).
        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();
            string? name = null;
            if (trimmed.StartsWith("// OwningAssetName:", StringComparison.Ordinal))
                name = trimmed["// OwningAssetName:".Length..].Trim();
            else if (trimmed.StartsWith("// AssetName:", StringComparison.Ordinal))
                name = trimmed["// AssetName:".Length..].Trim();

            if (!string.IsNullOrEmpty(name))
                return name;
        }
        return "(unknown)";
    }

    // ---- Utilities ----

    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static DateTime? TryGetLastWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }
}
