using System.Reflection;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Hsm.Editor.Catalog;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmAssetContributorTests
{
    // Real [HsmDefinition]-decorated method used by LoadFrom_with_definition_method_produces_asset.
    [HsmDefinition("TestMachine")]
    public static HsmDefinitionBlob CompileTestMachine()
    {
        var builder = new HsmBuilder("TestMachine");
        builder.State("Idle");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        return HsmEmitter.Emit(flatData);
    }

    [Fact]
    public void LoadFrom_assembly_with_no_definitions_enumerates_empty()
    {
        // The main editor assembly contains no [HsmDefinition] methods.
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(typeof(HsmAssetContributor).Assembly);
        contributor.Enumerate().Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_before_load_from_returns_empty()
    {
        var contributor = new HsmAssetContributor();
        contributor.Enumerate().Should().BeEmpty();
    }

    [Fact]
    public void Kind_property_returns_Hsm()
    {
        new HsmAssetContributor().Kind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void ContributorChanged_fires_after_LoadFrom()
    {
        var contributor = new HsmAssetContributor();
        var fired = false;
        contributor.ContributorChanged += () => fired = true;
        contributor.LoadFrom(Assembly.GetExecutingAssembly());
        fired.Should().BeTrue();
    }

    [Fact]
    public void LoadFrom_with_definition_method_produces_asset()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(typeof(HsmAssetContributorTests).Assembly);
        var assets = contributor.Enumerate();
        assets.Should().HaveCount(1);
        assets[0].Name.Should().Be("TestMachine");
        assets[0].Kind.Should().Be(AssetKind.Hsm);
    }
}
