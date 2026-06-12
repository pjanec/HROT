using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Catalog;
using Hrot.Editor.AiShared.Identity;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Catalog;

/// <summary>
/// BATCH-09: Tests for <see cref="BTreeAssetContributor"/> AssetId attribute handling.
///
/// Verifies:
/// 1. When a [BTreeDefinition] carries AssetId, the contributor uses it.
/// 2. When AssetId is absent/missing, the contributor falls back to FromName(treeName).
/// </summary>
public sealed class BTreeAssetContributorTests
{
    private static readonly Assembly TestAssembly = typeof(BTreeAssetContributorTests).Assembly;

    [Fact]
    public void LoadFrom_UsesAttributeAssetId_WhenPresent()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(TestAssembly);

        var assets = contributor.Enumerate();
        var fixture = assets.FirstOrDefault(a => a.Name == "Bt09Fixture");
        fixture.Should().NotBeNull("Bt09Fixture must be discovered from test assembly");

        var expectedId = new Guid("12345678-0000-0000-0000-0000000000aa");
        fixture!.AssetId.Should().Be(expectedId,
            "contributor must use the AssetId from [BTreeDefinition(..., AssetId = ...)] " +
            "when present, NOT FromName(treeName)");
    }

    [Fact]
    public void LoadFrom_FallsBackToFromName_WhenAssetIdAbsent()
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(TestAssembly);

        var assets = contributor.Enumerate();
        var fixture = assets.FirstOrDefault(a => a.Name == "Bt09NoAssetIdFixture");
        fixture.Should().NotBeNull("Bt09NoAssetIdFixture must be discovered from test assembly");

        var expectedId = AssetIdHasher.FromName("Bt09NoAssetIdFixture");
        fixture!.AssetId.Should().Be(expectedId,
            "contributor must fall back to FromName(treeName) when AssetId is absent");
    }
}

// ── Test fixtures: static methods in the test assembly decorated with [BTreeDefinition] ──

/// <summary>
/// BATCH-09 test fixture: carries an explicit AssetId.
/// </summary>
public static class Bt09FixtureHost
{
    [BTreeDefinition("Bt09Fixture", AssetId = "12345678-0000-0000-0000-0000000000aa")]
    public static BehaviorTreeBlob Build() => new()
    {
        TreeName = "Bt09Fixture",
        Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(),
        FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };
}

/// <summary>
/// BATCH-09 test fixture: NO AssetId — the contributor must fall back to FromName.
/// </summary>
public static class Bt09NoAssetIdFixtureHost
{
    [BTreeDefinition("Bt09NoAssetIdFixture")]
    public static BehaviorTreeBlob Build() => new()
    {
        TreeName = "Bt09NoAssetIdFixture",
        Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(),
        FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(),
        SubtreeAssetIds = Array.Empty<string>(),
    };
}
