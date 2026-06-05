namespace Hrot.Editor.AiShared.Tests.Identity;

/// <summary>
/// Tests for <see cref="AssetKindExtensions.ToPerspectiveName"/> (BATCH-10 Bug #3).
/// Verifies that the canonical Kind→perspective-name map is stable and that
/// Hsm maps to "HSM" (not the raw enum string "Hsm").
/// </summary>
public sealed class AssetKindExtensionsTests
{
    [Theory]
    [InlineData(AssetKind.BTree,     "BTree")]
    [InlineData(AssetKind.Hsm,       "HSM")]
    [InlineData(AssetKind.Blueprint, "Blueprint")]
    public void ToPerspectiveName_Returns_CanonicalName(AssetKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToPerspectiveName());
    }

    /// <summary>
    /// Hsm must map to "HSM", NOT "Hsm" (the raw enum ToString).
    /// This is the exact bug that caused HSM perspective switching to silently no-op.
    /// </summary>
    [Fact]
    public void Hsm_ToPerspectiveName_IsUppercaseHSM_NotEnumToString()
    {
        Assert.NotEqual(AssetKind.Hsm.ToString(), AssetKind.Hsm.ToPerspectiveName());
        Assert.Equal("HSM", AssetKind.Hsm.ToPerspectiveName());
    }

    /// <summary>
    /// BTree and Blueprint happen to match their enum ToString, but we still verify
    /// that ToPerspectiveName is the authoritative source rather than ToString.
    /// </summary>
    [Fact]
    public void BTree_ToPerspectiveName_MatchesRegisteredName()
    {
        Assert.Equal("BTree", AssetKind.BTree.ToPerspectiveName());
    }

    [Fact]
    public void Blueprint_ToPerspectiveName_MatchesRegisteredName()
    {
        Assert.Equal("Blueprint", AssetKind.Blueprint.ToPerspectiveName());
    }
}
