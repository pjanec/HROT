namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Validates a pair of discovered assets before a comparison is started.
/// See design sections 3.7 and 7.3.
/// </summary>
public static class AssetSelectionValidator
{
    /// <summary>
    /// Validates that versionA and versionB are suitable for comparison.
    /// IsValid=false means "do not proceed with comparison".
    /// IsValid=true with Warnings means "proceed with caution".
    /// </summary>
    public static ValidationResult Validate(
        DiscoveredAsset versionA,
        DiscoveredAsset versionB,
        AssetKind expectedKind)
    {
        var issues = new List<ValidationIssue>();

        // Rule 1: Both main files must exist.
        if (!File.Exists(versionA.MainFilePath))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ComparisonErrorMessages.FileNotFound}{versionA.MainFilePath}"));
            return new ValidationResult(false, issues);
        }
        if (!File.Exists(versionB.MainFilePath))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ComparisonErrorMessages.FileNotFound}{versionB.MainFilePath}"));
            return new ValidationResult(false, issues);
        }

        // Rules 2+3: Both files must be readable and parseable enough to extract AssetId.
        var idA = TryParseAssetId(versionA.MainFilePath, "A", out var errorA);
        if (errorA != null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, errorA));
            return new ValidationResult(false, issues);
        }

        var idB = TryParseAssetId(versionB.MainFilePath, "B", out var errorB);
        if (errorB != null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, errorB));
            return new ValidationResult(false, issues);
        }

        // Rule 4: The two files must have the same AssetKind.
        var kindA = DetectAssetKind(versionA.MainFilePath);
        var kindB = DetectAssetKind(versionB.MainFilePath);
        if (kindA != kindB)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"{ComparisonErrorMessages.AssetKindMismatch} -- Version A is {kindA} but Version B is {kindB}."));
            return new ValidationResult(false, issues);
        }

        // Rule 5: If AssetIds differ, warn but allow.
        if (idA != Guid.Empty && idB != Guid.Empty && idA != idB)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                $"{ComparisonErrorMessages.AssetIdMismatch} ({idA:D} vs {idB:D}). " +
                "Phase 1 comparison treats both as the same asset for visualId correlation..."));
        }

        return new ValidationResult(true, issues);
    }

    private static Guid TryParseAssetId(string filePath, string version, out string? error)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            error = $"Cannot read file: {filePath}";
            return Guid.Empty;
        }

        if (filePath.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase))
        {
            var id = ExtractAssetIdFromJson(text);
            if (id != Guid.Empty)
            {
                error = null;
                return id;
            }
            error = $"{ComparisonErrorMessages.CannotParseMetadata} Version {version}'s metadata: {filePath}";
            return Guid.Empty;
        }

        // C# header: scan for // AssetId: or // OwningAssetId:
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("// AssetId:", StringComparison.Ordinal))
            {
                var value = trimmed["// AssetId:".Length..].Trim();
                if (Guid.TryParse(value, out var id) && id != Guid.Empty)
                {
                    error = null;
                    return id;
                }
                break;
            }
            if (trimmed.StartsWith("// OwningAssetId:", StringComparison.Ordinal))
            {
                var value = trimmed["// OwningAssetId:".Length..].Trim();
                if (Guid.TryParse(value, out var id) && id != Guid.Empty)
                {
                    error = null;
                    return id;
                }
                break;
            }
            // Stop after the header comment block
            if (!trimmed.StartsWith("//") && trimmed.Length > 0)
                break;
        }

        error = $"{ComparisonErrorMessages.CannotParseMetadata} Version {version}'s metadata: {filePath}";
        return Guid.Empty;
    }

    private static Guid ExtractAssetIdFromJson(string text)
    {
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

    private static AssetKind DetectAssetKind(string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".bp.json", StringComparison.OrdinalIgnoreCase)) return AssetKind.Blueprint;
        if (name.EndsWith("_BT.cs", StringComparison.Ordinal)) return AssetKind.BTree;
        if (name.EndsWith("_HSM.cs", StringComparison.Ordinal)) return AssetKind.Hsm;
        if (name.EndsWith(".Blackboard.cs", StringComparison.Ordinal)) return AssetKind.Blackboard;
        if (name.EndsWith(".HeavyBlackboard.cs", StringComparison.Ordinal)) return AssetKind.Blackboard;
        // Default: treat as BTree for unknown extensions
        return AssetKind.BTree;
    }
}

/// <summary>The result of validating a pair of assets for comparison.</summary>
public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>A validation issue raised during asset selection.</summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);

/// <summary>Severity of a validation issue.</summary>
public enum ValidationSeverity { Error, Warning }
