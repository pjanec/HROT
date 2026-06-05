using System;
using System.Collections.Generic;
using System.IO;
using Hrot.AiEditor.Persistence;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// PU-502: AssetBaseNameCollisionGuard
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for <see cref="AssetBaseNameCollisionGuard"/> (design §3 D5).
/// Covers GetLogicalBaseName, CheckCollision (both directions), and
/// CheckCollisionOnDisk with an injected lister.
/// </summary>
public sealed class AssetBaseNameCollisionGuardTests
{
    // ── GetLogicalBaseName ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Foo.btree.json", "Foo")]
    [InlineData("Foo.hsm.json",   "Foo")]
    [InlineData("Foo.bp.json",    "Foo")]
    [InlineData("Foo.cs",         "Foo")]
    [InlineData("Foo.txt",        "Foo")]
    [InlineData("Foo.bar.txt",    "Foo.bar")]    // only final extension stripped for unknowns
    public void GetLogicalBaseName_ReturnsExpected(string fileName, string expected)
    {
        Assert.Equal(expected, AssetBaseNameCollisionGuard.GetLogicalBaseName(fileName));
    }

    [Fact]
    public void GetLogicalBaseName_PreservesBaseCasing()
    {
        // The base name casing must be preserved regardless of suffix casing.
        Assert.Equal("MyTree", AssetBaseNameCollisionGuard.GetLogicalBaseName("MyTree.btree.json"));
        Assert.Equal("SampleGuard", AssetBaseNameCollisionGuard.GetLogicalBaseName("SampleGuard.hsm.json"));
        Assert.Equal("SampleBlueprint", AssetBaseNameCollisionGuard.GetLogicalBaseName("SampleBlueprint.bp.json"));
        Assert.Equal("MyBrain", AssetBaseNameCollisionGuard.GetLogicalBaseName("MyBrain.cs"));
    }

    [Fact]
    public void GetLogicalBaseName_CaseInsensitiveSuffixMatch()
    {
        // Compound suffix must be recognized case-insensitively.
        Assert.Equal("Foo", AssetBaseNameCollisionGuard.GetLogicalBaseName("Foo.BTree.JSON"));
        Assert.Equal("Foo", AssetBaseNameCollisionGuard.GetLogicalBaseName("Foo.HSM.JSON"));
        Assert.Equal("Foo", AssetBaseNameCollisionGuard.GetLogicalBaseName("Foo.BP.JSON"));
        Assert.Equal("Foo", AssetBaseNameCollisionGuard.GetLogicalBaseName("Foo.CS"));
    }

    [Fact]
    public void GetLogicalBaseName_LongestMatchWins()
    {
        // .btree.json is longer than .json — must strip the full compound suffix.
        // If we only stripped .json we would get "Foo.btree" — that's wrong.
        Assert.Equal("Foo", AssetBaseNameCollisionGuard.GetLogicalBaseName("Foo.btree.json"));
    }

    [Fact]
    public void GetLogicalBaseName_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => AssetBaseNameCollisionGuard.GetLogicalBaseName(null!));
    }

    // ── CheckCollision — both directions (D5 success condition) ──────────────

    /// <summary>
    /// Creating Foo.btree.json when Foo.cs already exists → collision (JSON→CS direction).
    /// </summary>
    [Fact]
    public void CheckCollision_BTreeJson_BlockedBy_ExistingCs()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.btree.json",
            siblingFileNames: new[] { "Foo.cs", "Bar.cs" },
            directoryForMessage: "/trees");

        Assert.NotNull(error);
        Assert.Contains("Foo.btree.json", error);
        Assert.Contains("Foo.cs",         error);
        Assert.Contains("/trees",          error);
    }

    /// <summary>
    /// Creating Foo.hsm.json when Foo.cs already exists → collision.
    /// </summary>
    [Fact]
    public void CheckCollision_HsmJson_BlockedBy_ExistingCs()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.hsm.json",
            siblingFileNames: new[] { "Foo.cs" });

        Assert.NotNull(error);
        Assert.Contains("Foo.hsm.json", error);
        Assert.Contains("Foo.cs",       error);
    }

    /// <summary>
    /// Creating Foo.bp.json when Foo.cs already exists → collision.
    /// </summary>
    [Fact]
    public void CheckCollision_BpJson_BlockedBy_ExistingCs()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.bp.json",
            siblingFileNames: new[] { "Foo.cs" });

        Assert.NotNull(error);
        Assert.Contains("Foo.bp.json", error);
        Assert.Contains("Foo.cs",      error);
    }

    /// <summary>
    /// Creating Foo.cs when Foo.btree.json already exists → collision (CS→JSON direction).
    /// This is the "both directions" explicit success condition.
    /// </summary>
    [Fact]
    public void CheckCollision_Cs_BlockedBy_ExistingBTreeJson()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.cs",
            siblingFileNames: new[] { "Foo.btree.json", "Other.cs" });

        Assert.NotNull(error);
        Assert.Contains("Foo.cs",       error);
        Assert.Contains("Foo.btree.json", error);
    }

    /// <summary>
    /// Creating Foo.cs when Foo.hsm.json already exists → collision (CS→JSON direction).
    /// </summary>
    [Fact]
    public void CheckCollision_Cs_BlockedBy_ExistingHsmJson()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.cs",
            siblingFileNames: new[] { "Foo.hsm.json" });

        Assert.NotNull(error);
        Assert.Contains("Foo.cs",      error);
        Assert.Contains("Foo.hsm.json", error);
    }

    /// <summary>
    /// Creating Foo.cs when Foo.bp.json already exists → collision (CS→JSON direction).
    /// </summary>
    [Fact]
    public void CheckCollision_Cs_BlockedBy_ExistingBpJson()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.cs",
            siblingFileNames: new[] { "Foo.bp.json" });

        Assert.NotNull(error);
        Assert.Contains("Foo.cs",    error);
        Assert.Contains("Foo.bp.json", error);
    }

    // ── CheckCollision — non-collision cases ─────────────────────────────────

    [Fact]
    public void CheckCollision_DifferentBaseName_NoCollision()
    {
        // Foo.btree.json and Bar.cs → different base names → no D5 collision.
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.btree.json",
            siblingFileNames: new[] { "Bar.cs" });

        Assert.Null(error);
    }

    [Fact]
    public void CheckCollision_TwoJsonsSameBase_NotACollision()
    {
        // Foo.btree.json + Foo.hsm.json → same class (both JSON) → not a D5 collision.
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.btree.json",
            siblingFileNames: new[] { "Foo.hsm.json" });

        Assert.Null(error);
    }

    [Fact]
    public void CheckCollision_EmptySiblingList_NoCollision()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.btree.json",
            siblingFileNames: Array.Empty<string>());

        Assert.Null(error);
    }

    [Fact]
    public void CheckCollision_SelfInSiblingList_Ignored()
    {
        // The target file appears in the sibling list (common when the file already exists).
        // It must be ignored — self-conflict is not a D5 collision.
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "Foo.btree.json",
            siblingFileNames: new[] { "Foo.btree.json" });

        Assert.Null(error);
    }

    [Fact]
    public void CheckCollision_ErrorMessageContainsBothFileNamesAndDir()
    {
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:      "SampleScout.btree.json",
            siblingFileNames:    new[] { "SampleScout.cs" },
            directoryForMessage: @"C:\Hrot\Trees");

        Assert.NotNull(error);
        Assert.Contains("SampleScout.btree.json", error);
        Assert.Contains("SampleScout.cs",         error);
        Assert.Contains(@"C:\Hrot\Trees",          error);
    }

    [Fact]
    public void CheckCollision_CaseInsensitiveBaseNameComparison()
    {
        // Base-name comparison is case-insensitive: "foo.btree.json" collides with "FOO.cs".
        var error = AssetBaseNameCollisionGuard.CheckCollision(
            targetFileName:   "foo.btree.json",
            siblingFileNames: new[] { "FOO.cs" });

        Assert.NotNull(error);
    }

    // ── CheckCollisionOnDisk ─────────────────────────────────────────────────

    [Fact]
    public void CheckCollisionOnDisk_ConsultsOnlyTargetDirectory()
    {
        // The lister should be called with the directory of the target, not some other dir.
        string? queriedDir = null;

        var result = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
            targetFilePath: @"C:\Trees\Foo.btree.json",
            listFilesInDir: dir =>
            {
                queriedDir = dir;
                return new[] { @"C:\Trees\Bar.cs" }; // no collision (different base name)
            });

        Assert.Equal(@"C:\Trees", queriedDir);
        Assert.Null(result); // no collision — Bar ≠ Foo
    }

    [Fact]
    public void CheckCollisionOnDisk_DetectsCollisionViaInjectedLister()
    {
        // Simulate: Trees/ contains Foo.cs → Foo.btree.json would collide.
        var error = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
            targetFilePath: @"C:\Trees\Foo.btree.json",
            listFilesInDir: _ => new[] { @"C:\Trees\Foo.cs", @"C:\Trees\Bar.cs" });

        Assert.NotNull(error);
        Assert.Contains("Foo.btree.json", error);
        Assert.Contains("Foo.cs",         error);
    }

    [Fact]
    public void CheckCollisionOnDisk_NoCollisionWhenDirDoesNotExist()
    {
        // If the lister throws (dir absent), there are no siblings → no collision.
        var error = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
            targetFilePath: @"C:\NonExistent\Foo.btree.json",
            listFilesInDir: _ => throw new DirectoryNotFoundException("does not exist"));

        Assert.Null(error);
    }

    [Fact]
    public void CheckCollisionOnDisk_CsDirectionDetected()
    {
        // CS→JSON direction via disk: creating Foo.cs when Foo.btree.json exists.
        var error = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
            targetFilePath: @"C:\Trees\Foo.cs",
            listFilesInDir: _ => new[] { @"C:\Trees\Foo.btree.json" });

        Assert.NotNull(error);
        Assert.Contains("Foo.cs",         error);
        Assert.Contains("Foo.btree.json", error);
    }
}
