using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

public class MigrationRegistryTests
{
    private static MigrationRegistry NewRegistry() => new MigrationRegistry();

    // ---------------------------------------------------------------
    // RegisterDocType happy paths
    // ---------------------------------------------------------------

    // T1-050: Register at version 1 with empty migrators list.
    [Fact]
    public void RegisterDocType_ValidSingleVersion_Succeeds()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>());

        Assert.True(reg.IsRegistered("Test.Doc"));
        Assert.Equal(1, reg.GetCurrentVersion("Test.Doc"));
    }

    // T1-051: Register at version 2 with one Up + one Down migrator.
    [Fact]
    public void RegisterDocType_TwoVersionsWithBothMigrators_Succeeds()
    {
        var reg = NewRegistry();
        var migrators = MigratorFactory.MakePair("Test.Doc", 1);
        reg.RegisterDocType("Test.Doc", 2, migrators);

        Assert.True(reg.IsRegistered("Test.Doc"));
        Assert.Equal(2, reg.GetCurrentVersion("Test.Doc"));
    }

    // ---------------------------------------------------------------
    // RegisterDocType validation failures
    // ---------------------------------------------------------------

    // T1-052: Version 2 registered but no v1->v2 migrator throws.
    [Fact]
    public void RegisterDocType_MissingUpMigrator_Throws()
    {
        var reg = NewRegistry();
        // Only the down migrator — missing up.
        var onlyDown = new[] { new StubMigrator("Test.Doc", 2, 1) };
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 2, onlyDown));
    }

    // T1-053: Version 2 registered but no v2->v1 migrator throws.
    [Fact]
    public void RegisterDocType_MissingDownMigrator_Throws()
    {
        var reg = NewRegistry();
        var onlyUp = new[] { new StubMigrator("Test.Doc", 1, 2) };
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 2, onlyUp));
    }

    // T1-054: Two migrators with same (From, To) pair throws.
    [Fact]
    public void RegisterDocType_DuplicateMigrator_Throws()
    {
        var reg = NewRegistry();
        var migrators = new[]
        {
            new StubMigrator("Test.Doc", 1, 2),
            new StubMigrator("Test.Doc", 1, 2), // duplicate
            new StubMigrator("Test.Doc", 2, 1)
        };
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 2, migrators));
    }

    // T1-055: Migrator with To = From + 2 throws.
    [Fact]
    public void RegisterDocType_NonAdjacentVersions_Throws()
    {
        var reg = NewRegistry();
        var bad = new[] { new StubMigrator("Test.Doc", 1, 3) };
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 3, bad));
    }

    // T1-056: Migrator DocType doesn't match registration throws.
    [Fact]
    public void RegisterDocType_MigratorDocTypeMismatch_Throws()
    {
        var reg = NewRegistry();
        // Migrator says "Other.Doc" but we're registering "Test.Doc".
        var wrong = new[]
        {
            new StubMigrator("Other.Doc", 1, 2),
            new StubMigrator("Other.Doc", 2, 1)
        };
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 2, wrong));
    }

    // T1-057: Re-registering same docType throws.
    [Fact]
    public void RegisterDocType_AlreadyRegistered_Throws()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>());
        Assert.Throws<MigrationException>(() =>
            reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>()));
    }

    // T1-058: Versions 1 and 3 registered but no v2 throws.
    [Fact]
    public void RegisterDocType_GapInChain_Throws()
    {
        var reg = NewRegistry();
        // currentVersion=3 but only provide v1<->v2 pairs, not v2<->v3.
        var incomplete = MigratorFactory.MakePair("Test.Doc", 1).ToList();
        Assert.Throws<MigrationException>(() => reg.RegisterDocType("Test.Doc", 3, incomplete));
    }

    // ---------------------------------------------------------------
    // RegisterPassthroughDocType
    // ---------------------------------------------------------------

    // T1-060: Passthrough registration accepts any version.
    [Fact]
    public void RegisterPassthroughDocType_ValidVersion_Succeeds()
    {
        var reg = NewRegistry();
        reg.RegisterPassthroughDocType("Test.Pass", 1);
        Assert.True(reg.IsRegistered("Test.Pass"));
        Assert.True(reg.IsPassthrough("Test.Pass"));
    }

    // T1-061: Re-registering throws.
    [Fact]
    public void RegisterPassthroughDocType_AlreadyRegistered_Throws()
    {
        var reg = NewRegistry();
        reg.RegisterPassthroughDocType("Test.Pass", 1);
        Assert.Throws<MigrationException>(() => reg.RegisterPassthroughDocType("Test.Pass", 1));
    }

    // ---------------------------------------------------------------
    // IsRegistered / IsPassthrough
    // ---------------------------------------------------------------

    // T1-062: After RegisterDocType, IsRegistered returns true.
    [Fact]
    public void IsRegistered_RegisteredType_ReturnsTrue()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>());
        Assert.True(reg.IsRegistered("Test.Doc"));
    }

    // T1-063: Unregistered docType returns false.
    [Fact]
    public void IsRegistered_UnregisteredType_ReturnsFalse()
    {
        var reg = NewRegistry();
        Assert.False(reg.IsRegistered("Nope.Doc"));
    }

    // T1-064: Distinguishes passthrough from migration-enabled.
    [Fact]
    public void IsPassthrough_PassthroughType_ReturnsTrue()
    {
        var reg = NewRegistry();
        reg.RegisterPassthroughDocType("Test.Pass", 1);
        Assert.True(reg.IsPassthrough("Test.Pass"));
    }

    // T1-065: Distinguishes the other direction.
    [Fact]
    public void IsPassthrough_MigrationEnabledType_ReturnsFalse()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>());
        Assert.False(reg.IsPassthrough("Test.Doc"));
    }

    // ---------------------------------------------------------------
    // GetCurrentVersion
    // ---------------------------------------------------------------

    // T1-066: Returns registered current version.
    [Fact]
    public void GetCurrentVersion_RegisteredType_ReturnsVersion()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 3, MigratorFactory.MakeAllPairs("Test.Doc", 3));
        Assert.Equal(3, reg.GetCurrentVersion("Test.Doc"));
    }

    // T1-067: Throws MigrationException.
    [Fact]
    public void GetCurrentVersion_UnregisteredType_Throws()
    {
        var reg = NewRegistry();
        Assert.Throws<MigrationException>(() => reg.GetCurrentVersion("Nope.Doc"));
    }

    // ---------------------------------------------------------------
    // GetPath
    // ---------------------------------------------------------------

    // T1-068: v1 -> v1 returns no migrators.
    [Fact]
    public void GetPath_SameFromAndTo_ReturnsEmptyList()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 2, MigratorFactory.MakeAllPairs("Test.Doc", 2));
        var path = reg.GetPath("Test.Doc", 1, 1);
        Assert.Empty(path);
    }

    // T1-069: v1 -> v2 returns the v1->v2 up-migrator.
    [Fact]
    public void GetPath_AdjacentUp_ReturnsOneMigrator()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 2, MigratorFactory.MakeAllPairs("Test.Doc", 2));
        var path = reg.GetPath("Test.Doc", 1, 2);
        Assert.Single(path);
        Assert.Equal(1, path[0].FromVersion);
        Assert.Equal(2, path[0].ToVersion);
    }

    // T1-070: v2 -> v1 returns the v2->v1 down-migrator.
    [Fact]
    public void GetPath_AdjacentDown_ReturnsOneMigrator()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 2, MigratorFactory.MakeAllPairs("Test.Doc", 2));
        var path = reg.GetPath("Test.Doc", 2, 1);
        Assert.Single(path);
        Assert.Equal(2, path[0].FromVersion);
        Assert.Equal(1, path[0].ToVersion);
    }

    // T1-071: v1 -> v3 returns [v1->v2, v2->v3].
    [Fact]
    public void GetPath_MultiStepUp_ReturnsMigratorsInOrder()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 3, MigratorFactory.MakeAllPairs("Test.Doc", 3));
        var path = reg.GetPath("Test.Doc", 1, 3);
        Assert.Equal(2, path.Count);
        Assert.Equal(1, path[0].FromVersion);
        Assert.Equal(2, path[0].ToVersion);
        Assert.Equal(2, path[1].FromVersion);
        Assert.Equal(3, path[1].ToVersion);
    }

    // T1-072: v3 -> v1 returns [v3->v2, v2->v1].
    [Fact]
    public void GetPath_MultiStepDown_ReturnsMigratorsInOrder()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 3, MigratorFactory.MakeAllPairs("Test.Doc", 3));
        var path = reg.GetPath("Test.Doc", 3, 1);
        Assert.Equal(2, path.Count);
        Assert.Equal(3, path[0].FromVersion);
        Assert.Equal(2, path[0].ToVersion);
        Assert.Equal(2, path[1].FromVersion);
        Assert.Equal(1, path[1].ToVersion);
    }

    // T1-073: Throws MigrationException for unregistered type.
    [Fact]
    public void GetPath_UnregisteredType_Throws()
    {
        var reg = NewRegistry();
        Assert.Throws<MigrationException>(() => reg.GetPath("Nope.Doc", 1, 2));
    }

    // T1-074: Passthrough docTypes have no migrators; throws.
    [Fact]
    public void GetPath_PassthroughType_Throws()
    {
        var reg = NewRegistry();
        reg.RegisterPassthroughDocType("Test.Pass", 1);
        Assert.Throws<MigrationException>(() => reg.GetPath("Test.Pass", 1, 2));
    }

    // ---------------------------------------------------------------
    // CanMigrate
    // ---------------------------------------------------------------

    // T1-075: Verifies the existence check works.
    [Fact]
    public void CanMigrate_RegisteredPath_ReturnsTrue()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 2, MigratorFactory.MakeAllPairs("Test.Doc", 2));
        Assert.True(reg.CanMigrate("Test.Doc", 1, 2));
    }

    // T1-076: False instead of throwing.
    [Fact]
    public void CanMigrate_UnregisteredType_ReturnsFalse()
    {
        var reg = NewRegistry();
        Assert.False(reg.CanMigrate("Nope.Doc", 1, 2));
    }

    // ---------------------------------------------------------------
    // RegisteredDocTypes
    // ---------------------------------------------------------------

    // T1-077: Enumeration includes both migration and passthrough types.
    [Fact]
    public void RegisteredDocTypes_ReturnsAllRegistered()
    {
        var reg = NewRegistry();
        reg.RegisterDocType("Test.Doc", 1, Enumerable.Empty<IJsonDocumentMigrator>());
        reg.RegisterPassthroughDocType("Test.Pass", 1);

        var types = reg.RegisteredDocTypes.ToList();
        Assert.Contains("Test.Doc", types);
        Assert.Contains("Test.Pass", types);
    }
}
