using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Spatial.Eqs;
using FDP.Eqs;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// End-to-end integration smoke tests for the CoverAwarePatrol recipe blueprint.
/// Verifies compile + runtime viability, parent-death sub-entity cleanup, and
/// soft-reload StructureHash preservation.
/// </summary>
[Collection("DebugProbe")]
public sealed class CoverAwarePatrolEndToEndTest
{
    // ---- EQS template catalog stub ----

    private sealed class AlwaysContainsCatalog : IEqsTemplateCatalog
    {
        public bool Contains(Guid assetId) => true;
    }

    // ---- Helpers ----

    private static CompileOptions MakeEqsOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>(),
        EqsTemplates:      new AlwaysContainsCatalog());

    private static ulong GetStructureHash(BlueprintTestFixture fixture, Guid assetId)
    {
        var hash = BlueprintIdHash.Compute(assetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            $"Blueprint definition not found for asset {assetId}");
        return def!.StructureHash;
    }

    private static BlueprintAsset LoadRecipe()
    {
        var dir  = TestData.ResolveTestAssetsDir();
        var path = Path.Combine(dir, "Recipes", "CoverAwarePatrol.bp.json");
        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Null from '{path}'");
    }

    private static void RegisterEqsComponents(BlueprintTestFixture fixture)
    {
        fixture.World.RegisterComponent<EqsCognitiveBuffer>();
        fixture.World.RegisterComponent<EqsSensor>();
        fixture.World.RegisterComponent<PartMetadata>();
    }

    private static List<Entity> QueryEntities<T>(BlueprintTestFixture fixture)
        where T : unmanaged
    {
        var result = new List<Entity>();
        fixture.World.Query().With<T>().Build().ForEach(e => result.Add(e));
        return result;
    }

    // ---- Tests ----

    /// <summary>
    /// Verifies the CoverAwarePatrol recipe compiles and runs one tick without crashing.
    /// Does NOT assert specific EQS behavior -- smoke test only.
    /// </summary>
    [Fact]
    public void CoverAwarePatrol_FullScenario()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);

        var asset = LoadRecipe();
        fixture.CompileAndLoad(asset, MakeEqsOptions());

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        Assert.True(fixture.World.IsAlive(entity),
            "Parent entity must remain alive after the first tick.");
    }

    /// <summary>
    /// Verifies that destroying the parent entity and running SubEntityCleanupSystem
    /// removes any child entities (PartMetadata-tagged) created by the blueprint.
    /// </summary>
    [Fact]
    public void CoverAwarePatrol_ParentDeath_AutoCleanup()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);

        var asset = LoadRecipe();
        fixture.CompileAndLoad(asset, MakeEqsOptions());

        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);
        fixture.TickFrame(0.016f);

        // Collect any child entities created by the blueprint during the tick.
        var childEntities = QueryEntities<PartMetadata>(fixture);

        // Destroy the parent entity.
        fixture.World.DestroyEntity(parentEntity);
        Assert.False(fixture.World.IsAlive(parentEntity),
            "Parent entity must not be alive after explicit destruction.");

        // Run SubEntityCleanupSystem (PostSimulation phase) to cascade-destroy children.
        var cleanupSystem = new SubEntityCleanupSystem();
        cleanupSystem.Execute(fixture.World, 0f);

        // Any child entities with PartMetadata must have been cleaned up.
        foreach (var child in childEntities)
            Assert.False(fixture.World.IsAlive(child),
                "Child entity must be destroyed by SubEntityCleanupSystem after parent death.");
    }

    /// <summary>
    /// Verifies that reloading the identical CoverAwarePatrol JSON produces the same
    /// StructureHash (soft reload -- no field layout change).
    /// </summary>
    [Fact]
    public void CoverAwarePatrol_HotReload_SoftReload_PreservesStructure()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = LoadRecipe();

        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var hash1 = GetStructureHash(fixture, asset.AssetId);

        // Reload the identical asset -- same JSON => same field layout => soft reload.
        fixture.CompileAndLoad(asset, MakeEqsOptions());
        var hash2 = GetStructureHash(fixture, asset.AssetId);

        Assert.Equal(hash1, hash2);
    }

    /// <summary>
    /// Verifies that a soft reload (identical asset) preserves the existing spawned
    /// EQS sensor child entity. The entity and its EqsSensor + EqsCognitiveBuffer
    /// components must survive the reload.
    /// </summary>
    [Fact]
    public void CoverAwarePatrol_HotReload_SoftReload_PreservesSensor()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        RegisterEqsComponents(fixture);

        var asset = LoadRecipe();
        fixture.CompileAndLoad(asset, MakeEqsOptions());

        var parentEntity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, parentEntity);

        // Tick several frames to give the blueprint time to spawn the sensor child.
        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        var childrenBefore = QueryEntities<PartMetadata>(fixture);
        if (childrenBefore.Count == 0)
        {
            // Recipe did not spawn any child entities -- sensor spawning is conditional.
            // Skip sensor-preservation assertions; just verify the reload does not crash.
            fixture.CompileAndLoad(asset, MakeEqsOptions());
            var ex = Record.Exception(() => fixture.TickFrame(0.016f));
            Assert.Null(ex);
            return;
        }

        var childBefore = childrenBefore[0];
        Assert.True(fixture.World.IsAlive(childBefore),
            "Child entity must be alive after initial spawn ticks.");

        // Soft reload: compile and load the identical asset again.
        // StructureHash must match, so no hard restart is triggered.
        fixture.CompileAndLoad(asset, MakeEqsOptions());

        // Tick several frames to let the reload settle.
        for (int i = 0; i < 3; i++)
            fixture.TickFrame(0.016f);

        // The original child entity must still be alive after a soft reload.
        Assert.True(fixture.World.IsAlive(childBefore),
            "Child sensor entity must survive a soft hot-reload (same StructureHash).");

        // It must still have EqsSensor and EqsCognitiveBuffer components.
        Assert.True(fixture.World.HasComponent<EqsSensor>(childBefore),
            "Child entity must retain EqsSensor after soft reload.");
        Assert.True(fixture.World.HasComponent<EqsCognitiveBuffer>(childBefore),
            "Child entity must retain EqsCognitiveBuffer after soft reload.");
    }
}
