using System;
using System.Reflection;
using System.Reflection.Emit;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="MigrationBootstrap"/> (T2-100..T2-103).
/// </summary>
public sealed class MigrationBootstrapTests
{
    // T2-100: Journal doc type is always registered, even with an empty callback.
    [Fact]
    public void Build_RegistersJournalDocType()
    {
        var storage = new InMemoryMigrationStorage();
        MigrationServices services = MigrationBootstrap.Build(
            reg => { },
            storage,
            () => "1.0",
            "Test.Writer");

        Assert.True(services.Registry.IsRegistered(FdpDocumentTypes.MigrationJournal));
    }

    // T2-101: The registerFormats callback is invoked during Build.
    [Fact]
    public void Build_InvokesRegisterFormatsCallback()
    {
        bool callbackInvoked = false;
        var storage = new InMemoryMigrationStorage();

        MigrationBootstrap.Build(
            reg =>
            {
                callbackInvoked = true;
            },
            storage,
            () => "1.0",
            "Test.Writer");

        Assert.True(callbackInvoked);
    }

    // T2-102: After Build returns, the registry is sealed and further registration throws.
    [Fact]
    public void Build_SealsRegistry()
    {
        var storage = new InMemoryMigrationStorage();
        MigrationServices services = MigrationBootstrap.Build(
            reg => { },
            storage,
            () => "1.0",
            "Test.Writer");

        Assert.Throws<MigrationException>(
            () => services.Registry.RegisterPassthroughDocType("Any", 1));
    }

    // T2-103: BuildForProduction reads AssemblyInformationalVersionAttribute and returns
    //         non-null services.
    [Fact]
    public void Build_ProductionOverload_ReadsAssemblyInformationalVersion()
    {
        MigrationServices services = MigrationBootstrap.BuildForProduction(
            reg => { },
            "Test");

        Assert.NotNull(services.Registry);
        Assert.NotNull(services.Pipeline);
        Assert.NotNull(services.ReadOnly);
        Assert.NotNull(services.Persistent);
    }

    // T2-104: null registerFormats throws.
    [Fact]
    public void Build_NullRegisterFormats_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            MigrationBootstrap.Build(null!, storage, () => "1.0", "Test.Writer"));
    }

    // T2-105: null storage throws.
    [Fact]
    public void Build_NullStorage_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MigrationBootstrap.Build(reg => { }, null!, () => "1.0", "Test.Writer"));
    }

    // T2-106: null engineVersionProvider throws.
    [Fact]
    public void Build_NullEngineVersionProvider_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            MigrationBootstrap.Build(reg => { }, storage, null!, "Test.Writer"));
    }

    // T2-107: null writerIdentifier throws.
    [Fact]
    public void Build_NullWriterIdentifier_ThrowsArgumentNullException()
    {
        var storage = new InMemoryMigrationStorage();
        Assert.Throws<ArgumentNullException>(() =>
            MigrationBootstrap.Build(reg => { }, storage, () => "1.0", null!));
    }

    // T2-108: ReadEngineVersion returns the actual version for an assembly that has
    //         AssemblyInformationalVersionAttribute.
    [Fact]
    public void ReadEngineVersion_AssemblyWithAttribute_ReturnsVersionString()
    {
        // typeof(MigrationBootstrap).Assembly has the attribute set by the build.
        string version = MigrationBootstrap.ReadEngineVersion(typeof(MigrationBootstrap).Assembly);
        Assert.False(string.IsNullOrEmpty(version));
    }

    // T2-109: ReadEngineVersion returns "unknown" for a dynamic assembly that has
    //         no AssemblyInformationalVersionAttribute.
    //         Covers the null-coalesce fallback branch.
    [Fact]
    public void ReadEngineVersion_AssemblyWithoutAttribute_ReturnsUnknown()
    {
        var asmName = new AssemblyName("MigrationBootstrapTestHelper");
        var asm = AssemblyBuilder.DefineDynamicAssembly(
            asmName, AssemblyBuilderAccess.Run);

        string version = MigrationBootstrap.ReadEngineVersion(asm);
        Assert.Equal("unknown", version);
    }
}
