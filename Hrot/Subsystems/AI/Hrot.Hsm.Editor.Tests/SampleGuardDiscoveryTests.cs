using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Layout;
using Hrot.Hsm.Editor.Catalog;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Tests for AIE-ENABLE-2: verifies that HsmAssetContributor discovers SampleGuard
/// from the Hrot.AI.Behaviors assembly, and that the HSM layout method resolves
/// against the contracts assembly.
/// </summary>
public sealed class SampleGuardDiscoveryTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Machines.SampleGuard).Assembly;

    private const string ExpectedAssetId = "979df4a4-0000-0000-0000-000000000000";

    // ── contributor discovery ─────────────────────────────────────────────────

    [Fact]
    public void HsmAssetContributor_LoadFrom_DiscoversSampleGuard()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var assets = contributor.Enumerate();
        assets.Should().Contain(a => a.Name == "SampleGuard",
            "SampleGuard should be found by name");
    }

    [Fact]
    public void HsmAssetContributor_LoadFrom_SampleGuard_HasCorrectAssetId()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var guard = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleGuard");
        guard.Should().NotBeNull();

        guard!.AssetId.Should().Be(Guid.Parse(ExpectedAssetId),
            "asset ID must match the explicit AssetId set on [HsmDefinition]");
    }

    [Fact]
    public void HsmAssetContributor_LoadFrom_SampleGuard_KindIsHsm()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var guard = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleGuard");
        guard.Should().NotBeNull();
        guard!.Kind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void HsmAssetContributor_LoadFrom_SampleGuard_LayoutIsApplied()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);

        var guard = contributor.Enumerate().FirstOrDefault(a => a.Name == "SampleGuard")
                    as Hrot.Hsm.Editor.Model.HsmAsset;
        guard.Should().NotBeNull("contributor must return an HsmAsset");

        // At least one state must have a non-zero canvas position — proves layout was applied.
        guard!.AllStates.Should().Contain(s => s.Position.X != 0f || s.Position.Y != 0f,
            "layout positions must be applied from the [HsmLayout] method");
    }

    // ── layout method round-trip ─────────────────────────────────────────────

    [Fact]
    public void SampleGuard_Layout_ReturnsNonNullWithExpectedStates()
    {
        var layout = Hrot.AI.Behaviors.Machines.SampleGuard.Layout();

        layout.Should().NotBeNull();
        layout.States.Should().HaveCount(2,
            "SampleGuard declares Idle and Scanning");
        layout.Transitions.Should().HaveCount(2,
            "SampleGuard declares Alert and Clear transitions");
    }

    [Fact]
    public void SampleGuard_Compile_ReturnsValidBlob()
    {
        var blob = Hrot.AI.Behaviors.Machines.SampleGuard.Compile();

        blob.Should().NotBeNull();
    }

    [Fact]
    public void SampleGuard_LayoutNamespace_IsResolvableFromBehaviorsAssembly()
    {
        // The [HsmLayout] attribute lives in Hrot.Editor.AiShared.Layout (via AiContracts).
        // Verify the behaviors assembly carries a reference to Hrot.Editor.AiContracts.
        var referencedAssemblyNames = BehaviorsAssembly
            .GetReferencedAssemblies()
            .Select(n => n.Name)
            .ToHashSet();

        referencedAssemblyNames.Should().Contain("Hrot.Editor.AiContracts",
            "Hrot.AI.Behaviors must reference Hrot.Editor.AiContracts to resolve HsmLayoutAttribute");
    }
}
