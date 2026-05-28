using System.Reflection;
using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

/// <summary>
/// Smoke and integration tests for <see cref="ComparisonErrorMessages"/> constants.
/// </summary>
public sealed class ComparisonErrorMessagesTests : IDisposable
{
    private readonly string _tempDir;

    public ComparisonErrorMessagesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ErrorMsgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ---- constant health checks ------------------------------------------------

    [Fact]
    public void AllPublicConstantsAreNonEmpty()
    {
        var fields = typeof(ComparisonErrorMessages)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetRawConstantValue();
            Assert.False(string.IsNullOrEmpty(value),
                $"ComparisonErrorMessages.{field.Name} must not be null or empty");
        }
    }

    // ---- integration: validator uses AssetIdMismatch constant -----------------

    [Fact]
    public void AssetIdMismatch_Validator_UsesCorrectMessage()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        // Write two valid BTree files with different AssetIds.
        var pathA = Path.Combine(_tempDir, "AssetA_BT.cs");
        var pathB = Path.Combine(_tempDir, "AssetB_BT.cs");
        File.WriteAllText(pathA, $"// AssetId: {idA:D}\npublic class AssetA {{ }}\n");
        File.WriteAllText(pathB, $"// AssetId: {idB:D}\npublic class AssetB {{ }}\n");

        var result = AssetSelectionValidator.Validate(
            new DiscoveredAsset(pathA, Array.Empty<DiscoveredCompanion>()),
            new DiscoveredAsset(pathB, Array.Empty<DiscoveredCompanion>()),
            AssetKind.BTree);

        Assert.True(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Contains(ComparisonErrorMessages.AssetIdMismatch, result.Issues[0].Message,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---- integration: parser warning uses TruncatedResponse constant ----------

    [Fact]
    public void TruncatedResponse_Parser_UsesCorrectMessage()
    {
        // JSON cut mid-structure; parser should produce the TruncatedResponse warning.
        const string truncatedInput =
            "----- HUMAN SUMMARY -----\nSummary.\n" +
            "----- STRUCTURED CHANGES (JSON) -----\n" +
            "{ \"summary\": \"x\", \"changes\": [{ \"kind\":";

        var result = LlmResponseParser.Parse(truncatedInput);

        Assert.NotEmpty(result.Warnings);
        Assert.Contains(ComparisonErrorMessages.TruncatedResponse, result.Warnings[0],
            StringComparison.OrdinalIgnoreCase);
    }
}

