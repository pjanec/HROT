using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Hsm.Editor.Catalog;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// Tests for AIE-ENABLE-2: verifies that HsmAssetContributor discovers SampleGuard
/// from the Hrot.AI.Behaviors assembly, and that the HSM layout method resolves
/// against the contracts assembly.
///
/// PU-402: SampleGuard.cs is decommitted; SampleGuard.Compile() is now generated from
/// Machines/SampleGuard.hsm.json. The [HsmLayout] method no longer exists in the
/// assembly. Layout coverage migrated to HsmJsonAssetContributor reading the live JSON.
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

    // PU-402 MIGRATED: Layout is now in Machines/SampleGuard.hsm.json (not in the assembly).
    // Assert layout via HsmJsonAssetContributor reading the live committed JSON file.
    [Fact]
    public void HsmAssetContributor_LoadFrom_SampleGuard_LayoutIsApplied()
    {
        // Locate the live JSON — it lives at Machines/SampleGuard.hsm.json under
        // Hrot/Subsystems/Hrot.AI.Behaviors/ relative to the repo root.
        // Walk up from test assembly output dir (net8.0 → Debug → bin →
        // Hrot.Hsm.Editor.Tests → AI → Subsystems → Hrot → repo root).
        var asmDir = Path.GetDirectoryName(typeof(SampleGuardDiscoveryTests).Assembly.Location)!;
        var repoRoot = asmDir;
        for (int i = 0; i < 7; i++)
            repoRoot = Path.GetDirectoryName(repoRoot)!;
        var jsonPath = Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors",
            "Machines", "SampleGuard.hsm.json");

        jsonPath.Should().NotBeNullOrEmpty();
        File.Exists(jsonPath).Should().BeTrue(
            $"live SampleGuard.hsm.json must exist at {jsonPath} (PU-402 decommit)");

        var contrib = new HsmJsonAssetContributor();
        // Trigger full load via Refresh with explicit paths
        contrib.Refresh(jsonPaths: new[] { jsonPath });

        var assets = contrib.Enumerate();
        assets.Should().Contain(a => a.Name == "SampleGuard",
            "JSON contributor must discover SampleGuard from the live .hsm.json");

        var guard = assets.FirstOrDefault(a => a.Name == "SampleGuard")
                    as Hrot.Hsm.Editor.Model.HsmAsset;
        guard.Should().NotBeNull("JSON contributor must return an HsmAsset");

        // At least one state must have non-zero X or Y — proves layout from JSON was applied.
        // (SampleGuard layout: Idle at (100,100), Scanning at (400,100))
        guard!.AllStates.Should().Contain(s => s.Position.X != 0f || s.Position.Y != 0f,
            "layout positions must be applied from Machines/SampleGuard.hsm.json (PU-402)");
    }

    // PU-402 DELETED: SampleGuard_Layout_ReturnsNonNullWithExpectedStates
    // The [HsmLayout] method no longer exists in the generated assembly. Layout lives
    // in Machines/SampleGuard.hsm.json; use HsmJsonAssetContributor to read it (above).

    [Fact]
    public void SampleGuard_Compile_ReturnsValidBlob()
    {
        var blob = Hrot.AI.Behaviors.Machines.SampleGuard.Compile();

        blob.Should().NotBeNull();
    }

    // PU-402 NOTE: SampleGuard_LayoutNamespace_IsResolvableFromBehaviorsAssembly was removed.
    // After decommit, the generated SampleGuard.g.cs no longer has [HsmLayout] and the
    // Hrot.AI.Behaviors assembly no longer needs a reference to Hrot.Editor.AiContracts
    // purely for layout resolution. Layout lives in Machines/SampleGuard.hsm.json.
    // The test LayoutIsApplied above (via HsmJsonAssetContributor) covers layout correctness.
}
