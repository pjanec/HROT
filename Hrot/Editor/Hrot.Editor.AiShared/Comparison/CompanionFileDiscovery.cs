namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Discovers companion files for a visually-authored asset given its main file or a folder.
/// See design section 3.6.
/// </summary>
public static class CompanionFileDiscovery
{
    /// <summary>
    /// Returns a DiscoveredAsset with the main file and companion paths (found/not-found).
    /// </summary>
    public static DiscoveredAsset DiscoverFromMainFile(string mainFilePath, AssetKind expectedKind)
    {
        var dir = Path.GetDirectoryName(mainFilePath) ?? ".";
        var fileName = Path.GetFileName(mainFilePath);
        var companionPaths = GetCompanionPaths(dir, fileName, expectedKind);
        var companions = companionPaths
            .Select(p => new DiscoveredCompanion(p, File.Exists(p)))
            .ToList();
        return new DiscoveredAsset(mainFilePath, companions);
    }

    /// <summary>
    /// Scans a folder for a file whose AssetId matches targetAssetId; then resolves companions.
    /// Prefers files with <c>AssetId:</c> (main asset file) over files with <c>OwningAssetId:</c>
    /// (companion files) when multiple matches exist.
    /// Skips directories whose name starts with '.'.
    /// Returns null when no matching file is found.
    /// </summary>
    public static DiscoveredAsset? DiscoverFromFolder(string folderPath, Guid targetAssetId, AssetKind expectedKind)
    {
        string? bestFile = null;
        int bestScore = 0;

        foreach (var file in EnumerateFiles(folderPath))
        {
            int score = ScoreFileForAssetId(file, targetAssetId);
            if (score > bestScore)
            {
                bestScore = score;
                bestFile = file;
            }
        }

        return bestFile != null ? DiscoverFromMainFile(bestFile, expectedKind) : null;
    }

    // Returns 2 for AssetId match (main file), 1 for OwningAssetId match (companion), 0 for no match.
    private static int ScoreFileForAssetId(string filePath, Guid targetAssetId)
    {
        try
        {
            if (filePath.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase))
            {
                var id = ExtractAssetIdFromJson(filePath);
                return id != Guid.Empty && id == targetAssetId ? 2 : 0;
            }

            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("// AssetId:", StringComparison.Ordinal))
                {
                    var value = trimmed["// AssetId:".Length..].Trim();
                    return Guid.TryParse(value, out var id) && id == targetAssetId ? 2 : 0;
                }
                if (trimmed.StartsWith("// OwningAssetId:", StringComparison.Ordinal))
                {
                    var value = trimmed["// OwningAssetId:".Length..].Trim();
                    return Guid.TryParse(value, out var id) && id == targetAssetId ? 1 : 0;
                }
                // Stop reading after the header comment block (first non-comment non-empty line)
                if (!trimmed.StartsWith("//") && trimmed.Length > 0)
                    break;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> EnumerateFiles(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            yield break;

        foreach (var file in Directory.GetFiles(folderPath))
        {
            if (IsAssetFile(file))
                yield return file;
        }

        foreach (var subDir in Directory.GetDirectories(folderPath))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.'))
                continue;

            foreach (var file in EnumerateFiles(subDir))
                yield return file;
        }
    }

    private static bool IsAssetFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.EndsWith(".cs", StringComparison.Ordinal)
               || name.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase);
    }

    private static Guid TryExtractAssetId(string filePath)
    {
        try
        {
            return filePath.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase)
                ? ExtractAssetIdFromJson(filePath)
                : ExtractAssetIdFromCs(filePath);
        }
        catch
        {
            return Guid.Empty;
        }
    }

    private static Guid ExtractAssetIdFromCs(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("// AssetId:", StringComparison.Ordinal))
            {
                var value = trimmed["// AssetId:".Length..].Trim();
                return Guid.TryParse(value, out var id) ? id : Guid.Empty;
            }
            if (trimmed.StartsWith("// OwningAssetId:", StringComparison.Ordinal))
            {
                var value = trimmed["// OwningAssetId:".Length..].Trim();
                return Guid.TryParse(value, out var id) ? id : Guid.Empty;
            }
            // Stop reading after the header region (first non-comment non-empty line)
            if (!trimmed.StartsWith("//") && trimmed.Length > 0)
                break;
        }
        return Guid.Empty;
    }

    private static Guid ExtractAssetIdFromJson(string filePath)
    {
        var text = File.ReadAllText(filePath);
        const string marker = "\"AssetId\"";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return Guid.Empty;

        idx += marker.Length;
        while (idx < text.Length && (text[idx] == ':' || text[idx] == ' ' || text[idx] == '\t' || text[idx] == '\r' || text[idx] == '\n'))
            idx++;

        if (idx >= text.Length || text[idx] != '"')
            return Guid.Empty;

        idx++;
        var end = text.IndexOf('"', idx);
        if (end < 0)
            return Guid.Empty;

        var guidStr = text[idx..end];
        return Guid.TryParse(guidStr, out var id) ? id : Guid.Empty;
    }

    private static IReadOnlyList<string> GetCompanionPaths(string dir, string fileName, AssetKind kind)
    {
        return kind switch
        {
            AssetKind.BTree => GetBTreeCompanions(dir, fileName),
            AssetKind.Hsm => GetHsmCompanions(dir, fileName),
            AssetKind.Blackboard => GetBlackboardCompanions(dir, fileName),
            _ => Array.Empty<string>(),
        };
    }

    private static string[] GetBTreeCompanions(string dir, string fileName)
    {
        if (!fileName.EndsWith("_BT.cs", StringComparison.Ordinal))
            return Array.Empty<string>();

        var baseName = fileName[..^"_BT.cs".Length] + "_BT";
        return
        [
            Path.Combine(dir, baseName + ".Blackboard.cs"),
            Path.Combine(dir, baseName + ".HeavyBlackboard.cs"),
            Path.Combine(dir, baseName + ".Orchestrators.g.cs"),
        ];
    }

    private static string[] GetHsmCompanions(string dir, string fileName)
    {
        if (!fileName.EndsWith("_HSM.cs", StringComparison.Ordinal))
            return Array.Empty<string>();

        var baseName = fileName[..^"_HSM.cs".Length] + "_HSM";
        return
        [
            Path.Combine(dir, baseName + ".Blackboard.cs"),
            Path.Combine(dir, baseName + ".HeavyBlackboard.cs"),
            Path.Combine(dir, baseName + ".Orchestrators.g.cs"),
        ];
    }

    private static string[] GetBlackboardCompanions(string dir, string fileName)
    {
        if (!fileName.EndsWith(".Blackboard.cs", StringComparison.Ordinal))
            return Array.Empty<string>();

        var baseName = fileName[..^".Blackboard.cs".Length];
        return
        [
            Path.Combine(dir, baseName + ".HeavyBlackboard.cs"),
        ];
    }
}

/// <summary>An asset discovered on disk, with its main file and companion file presence.</summary>
public sealed record DiscoveredAsset(
    string MainFilePath,
    IReadOnlyList<DiscoveredCompanion> Companions);

/// <summary>A companion file path and whether it was found on disk.</summary>
public sealed record DiscoveredCompanion(
    string Path,
    bool IsPresent);
