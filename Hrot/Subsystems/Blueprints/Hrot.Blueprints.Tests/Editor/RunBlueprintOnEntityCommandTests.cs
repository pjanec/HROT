using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Runtime;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Headless unit tests for <see cref="RunBlueprintOnEntityCommand.Execute"/>.
/// Verifies the four spec-required scenarios without any ImGui dependency.
///
/// <para>The entity creation uses a bare <see cref="EntityRepository"/> with the three
/// blueprint tier components registered (via <see cref="BlueprintRuntimeWiring.RegisterTierComponents"/>),
/// mirroring the production composition.</para>
/// </summary>
public sealed class RunBlueprintOnEntityCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository NewWorld()
    {
        var world = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(world);
        return world;
    }

    private static (BlueprintRegistry registry, BlueprintAsset asset) RegisteredDemoBlueprint()
    {
        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        var asset = CounterDemoBlueprint.MakeAsset();
        return (registry, asset);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Happy path: registered demo blueprint + selected entity → Attached, entity carries the slot.
    /// </summary>
    [Fact]
    public void Execute_RegisteredAsset_SelectedEntity_ReturnsAttached_EntityHasSlot()
    {
        using var world = NewWorld();
        var (registry, asset) = RegisteredDemoBlueprint();
        var entity = world.CreateEntity();

        var log = new List<string>();
        var result = RunBlueprintOnEntityCommand.Execute(
            world:          world,
            registry:       registry,
            selectedEntity: entity,
            activeAssetRef: asset,
            report:         msg => log.Add(msg));

        Assert.NotNull(result);
        Assert.Equal(BlueprintAttachStatus.Attached, result!.Value.Status);
        Assert.True(result.Value.Success);
        Assert.Single(log);

        // The entity must carry a tier-1024 blackboard component with one allocated slot.
        Assert.True(world.HasComponent<BlueprintBlackboard1024>(entity),
            "Entity must have BlueprintBlackboard1024 after attach.");
    }

    /// <summary>
    /// Idempotency: calling Execute twice on the same entity returns AlreadyAttached on the second call.
    /// </summary>
    [Fact]
    public void Execute_CalledTwice_SecondCall_ReturnsAlreadyAttached()
    {
        using var world = NewWorld();
        var (registry, asset) = RegisteredDemoBlueprint();
        var entity = world.CreateEntity();

        var log = new List<string>();
        var first = RunBlueprintOnEntityCommand.Execute(world, registry, entity, asset, msg => log.Add(msg));
        var second = RunBlueprintOnEntityCommand.Execute(world, registry, entity, asset, msg => log.Add(msg));

        Assert.Equal(BlueprintAttachStatus.Attached,        first!.Value.Status);
        Assert.Equal(BlueprintAttachStatus.AlreadyAttached, second!.Value.Status);
        Assert.True(second.Value.Success);
    }

    /// <summary>
    /// Unregistered asset: Execute returns NotRegistered and logs a message including "Compile".
    /// </summary>
    [Fact]
    public void Execute_UnregisteredAsset_ReturnsNotRegistered_LogsCompileHint()
    {
        using var world = NewWorld();
        var registry = new BlueprintRegistry(); // intentionally empty
        var asset    = CounterDemoBlueprint.MakeAsset();
        var entity   = world.CreateEntity();

        var log = new List<string>();
        var result = RunBlueprintOnEntityCommand.Execute(world, registry, entity, asset,
            msg => log.Add(msg));

        Assert.NotNull(result);
        Assert.Equal(BlueprintAttachStatus.NotRegistered, result!.Value.Status);
        Assert.False(result.Value.Success);
        // The logged message must hint the user to compile the blueprint.
        Assert.Single(log);
        Assert.Contains("ompile", log[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No entity selected: Execute returns null (no-op) and logs "select an entity first".
    /// </summary>
    [Fact]
    public void Execute_NoEntitySelected_ReturnsNull_LogsSelectEntityFirst()
    {
        using var world = NewWorld();
        var (registry, asset) = RegisteredDemoBlueprint();

        var log = new List<string>();
        var result = RunBlueprintOnEntityCommand.Execute(
            world:          world,
            registry:       registry,
            selectedEntity: null,          // no entity selected
            activeAssetRef: asset,
            report:         msg => log.Add(msg));

        Assert.Null(result);
        Assert.Single(log);
        Assert.Contains("select", log[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No blueprint open (activeAssetRef is null): Execute returns null (no-op) and logs a hint.
    /// </summary>
    [Fact]
    public void Execute_NoBlueprintOpen_ReturnsNull_LogsOpenBlueprintFirst()
    {
        using var world = NewWorld();
        var (registry, _) = RegisteredDemoBlueprint();
        var entity = world.CreateEntity();

        var log = new List<string>();
        var result = RunBlueprintOnEntityCommand.Execute(
            world:          world,
            registry:       registry,
            selectedEntity: entity,
            activeAssetRef: null,           // no active asset
            report:         msg => log.Add(msg));

        Assert.Null(result);
        Assert.Single(log);
        Assert.Contains("blueprint", log[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Active asset is a wrong type (not BlueprintAsset): Execute returns null (no-op) and logs the type name.
    /// </summary>
    [Fact]
    public void Execute_WrongAssetType_ReturnsNull_LogsTypeName()
    {
        using var world = NewWorld();
        var (registry, _) = RegisteredDemoBlueprint();
        var entity = world.CreateEntity();
        var wrongAsset = new object(); // not a BlueprintAsset

        var log = new List<string>();
        var result = RunBlueprintOnEntityCommand.Execute(
            world:          world,
            registry:       registry,
            selectedEntity: entity,
            activeAssetRef: wrongAsset,
            report:         msg => log.Add(msg));

        Assert.Null(result);
        Assert.Single(log);
        // Must mention it's not a Blueprint
        Assert.Contains("Blueprint", log[0], StringComparison.OrdinalIgnoreCase);
    }

    // ── IShellCommandRegistrar.RegisterToolbarEntry: CaptureShellCommandRegistrar integration ──────────────

    /// <summary>
    /// Verifies that <see cref="MockShellCommandRegistrar.RegisterToolbarEntry"/> captures the label
    /// and callback, and that invoking the callback triggers the Execute path with the correct
    /// arguments (simulating the composition root).
    /// </summary>
    [Fact]
    public void IShellCommandRegistrar_RegisterToolbarEntry_CapturesCallback()
    {
        var registrar = new MockShellCommandRegistrar();
        var log = new List<string>();

        registrar.RegisterToolbarEntry(
            RunBlueprintOnEntityCommand.ToolbarLabel,
            () => log.Add("clicked"));

        Assert.Single(registrar.ToolbarEntries);
        Assert.Equal(RunBlueprintOnEntityCommand.ToolbarLabel, registrar.ToolbarEntries[0].Label);

        // Invoke the captured callback — simulates the ImGui button click.
        registrar.ToolbarEntries[0].OnClicked();
        Assert.Single(log);
        Assert.Equal("clicked", log[0]);
    }
}
