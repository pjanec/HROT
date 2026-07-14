using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using Fbt.Kernel;
using Fhsm.Kernel.Attributes;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---------------------------------------------------------------------------
// Fixture types used by the tests
// ---------------------------------------------------------------------------

public struct TestBTreeDto  { public int Value; }
public struct TestHsmDto    { public float X; }
public struct TestSharedDto { public bool Flag; }
public struct TestHeavyDto  { public double D; }
public struct TestHeavyContainer { public byte[] Data; }

/// <summary>A DTO with two known public fields, used for S1-1 DtoFields tests.</summary>
public struct FooDto { public int Health; public float Speed; }

/// <summary>Stands in for a Blueprint-compiled AiPrimitive's generated Params struct (I4).</summary>
public struct AiPrimParamsDto { public int RunsNeeded; }
/// <summary>Stands in for a Blueprint-compiled AiPrimitive's generated WorkingState struct (I4).</summary>
public struct AiPrimWorkingDto { public int Ticks; }

/// <summary>
/// Static fixture class whose methods are decorated with the various AI action/condition
/// attributes so the schema exporter can reflect over them.
/// </summary>
public static class ActionFixtures
{
    [BTreeAction]
    public static void BTreeActionMethod(ref TestBTreeDto dto) { }

    [BTreeCondition]
    public static void BTreeConditionMethod(ref TestBTreeDto dto) { }

    [HsmAction]
    public static void HsmActionMethod(ref TestHsmDto dto) { }

    [HsmGuard]
    public static void HsmGuardMethod(ref TestHsmDto dto) { }

    [SharedAiAction(typeof(TestSharedDto), "Flag")]
    public static void SharedActionMethod(ref TestSharedDto dto) { }

    [SharedAiCondition(typeof(TestSharedDto), "Flag")]
    public static void SharedConditionMethod(ref TestSharedDto dto) { }

    [SharedAiHeavyAction(
        typeof(TestSharedDto), "Flag",
        typeof(TestHeavyContainer), "Data",
        typeof(TestHeavyDto))]
    public static void SharedHeavyActionMethod(ref TestSharedDto dto) { }

    // Access annotation fixtures
    [BTreeAction]
    public static void ReadOnlyParamMethod([BlackboardReadOnly] ref TestBTreeDto dto) { }

    [BTreeAction]
    public static void ReadWriteParamMethod([BlackboardReadWrite] ref TestBTreeDto dto) { }

    [BTreeAction]
    public static void UnannotatedParamMethod(ref TestBTreeDto dto) { }

    // S1-1 fixture: BTree action with FooDto (used to verify DtoFields reflection)
    [BTreeAction]
    public static void FooDtoAction(ref FooDto dto) { }

    // DEBT-01 fixtures: void* parameters with DtoType on the attribute
    [HsmAction(DtoType = typeof(TestHsmDto))]
    public static unsafe void HsmVoidPtrAction_WithDtoType(void* instance, void* context, ushort eventId) { }

    [HsmAction]
    public static unsafe void HsmVoidPtrAction_NullDtoType(void* instance, void* context, ushort eventId) { }

    [HsmGuard(DtoType = typeof(TestHsmDto))]
    public static unsafe bool HsmVoidPtrGuard_WithDtoType(void* instance, void* context, ushort eventId) { return false; }

    // I4: mirror a Blueprint-compiled AiPrimitive's generated TickCore — the first ref param is the
    // generated Params struct, and the [GeneratedAiPrimitiveAction] flags mirror the compiler output.
    [GeneratedAiPrimitiveAction(bTreeAction: true, blueprintCall: true)]
    public static NodeStatus AiPrimitiveActionTickCore(
        ref AiPrimParamsDto p, ref AiPrimWorkingDto ws,
        Fdp.Core.Entity self, Fdp.Core.EntityRepository world, float time) => NodeStatus.Success;

    [GeneratedAiPrimitiveAction(bTreeCondition: true)]
    public static NodeStatus AiPrimitiveConditionTickCore(
        ref AiPrimParamsDto p, ref AiPrimWorkingDto ws,
        Fdp.Core.Entity self, Fdp.Core.EntityRepository world, float time) => NodeStatus.Success;
}

// ---------------------------------------------------------------------------
// Fake IAssetCatalog for watcher tests
// ---------------------------------------------------------------------------

public sealed class FakeCatalog : IAssetCatalog
{
    public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
    public IEditableAsset? FindByAssetId(Guid assetId) => null;
    public IEditableAsset? FindByName(string name) => null;
    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
    public event Action<AssetKind>? Changed;
    public void RaiseChanged(AssetKind kind = AssetKind.Blueprint) => Changed?.Invoke(kind);
}

// ---------------------------------------------------------------------------
// Counting wrapper for Rebuild() call counting
// ---------------------------------------------------------------------------

public sealed class CountingExporter : IActionSchemaExporter
{
    private readonly ActionSchemaExporter _inner = new();
    public int RebuildCallCount { get; private set; }

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _inner.All;

    public ActionSchemaEntry? Lookup(string fqn) => _inner.Lookup(fqn);

    public void Rebuild()
    {
        RebuildCallCount++;
        _inner.Rebuild();
        Changed?.Invoke();
    }

    public event Action? Changed;
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

public sealed class ActionSchemaExporterTests
{
    private static string Fqn(string methodName) =>
        $"{typeof(ActionFixtures).FullName}.{methodName}";

    // -------------------------------------------------------------------------
    // TASK-BB-1a-01: Reflection-based population
    // -------------------------------------------------------------------------

    [Fact]
    public void Rebuild_DiscoversBTreeAction_ByFqn()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        Assert.True(exporter.All.ContainsKey(Fqn(nameof(ActionFixtures.BTreeActionMethod))));
    }

    [Fact]
    public void Rebuild_BTreeAction_HasBTreeHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeActionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
    }

    [Fact]
    public void Rebuild_BTreeAction_DtoTypeExtractedFromRefParam()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeActionMethod))];
        Assert.Equal(typeof(TestBTreeDto), entry.DtoType);
    }

    [Fact]
    public void Rebuild_BTreeCondition_HasBTreeHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeConditionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
    }

    [Fact]
    public void Rebuild_HsmAction_HasHsmHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmActionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
    }

    [Fact]
    public void Rebuild_HsmGuard_HasHsmHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmGuardMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
    }

    [Fact]
    public void Rebuild_SharedAction_HasBTreeHsmSharedHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.SharedActionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Shared));
    }

    [Fact]
    public void Rebuild_SharedCondition_HasBTreeHsmSharedHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.SharedConditionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Shared));
    }

    [Fact]
    public void Rebuild_HeavyAction_HasHeavyFlag()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.SharedHeavyActionMethod))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Heavy));
    }

    [Fact]
    public void Rebuild_HeavyAction_HeavyDtoTypeNonNull()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.SharedHeavyActionMethod))];
        Assert.NotNull(entry.HeavyDtoType);
        Assert.Equal(typeof(TestHeavyDto), entry.HeavyDtoType);
    }

    [Fact]
    public void Rebuild_ReadOnlyParam_AccessIsReadOnly()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.ReadOnlyParamMethod))];
        Assert.Equal(BlackboardAccess.ReadOnly, entry.Access);
    }

    [Fact]
    public void Rebuild_ReadWriteParam_AccessIsReadWrite()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.ReadWriteParamMethod))];
        Assert.Equal(BlackboardAccess.ReadWrite, entry.Access);
    }

    [Fact]
    public void Rebuild_UnannotatedParam_AccessIsUnknown()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.UnannotatedParamMethod))];
        Assert.Equal(BlackboardAccess.Unknown, entry.Access);
    }

    [Fact]
    public void Lookup_UnknownFqn_ReturnsNull()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        Assert.Null(exporter.Lookup("No.Such.Method"));
    }

    [Fact]
    public void Lookup_KnownFqn_ReturnsEntry()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var fqn   = Fqn(nameof(ActionFixtures.BTreeActionMethod));
        var entry = exporter.Lookup(fqn);
        Assert.NotNull(entry);
        Assert.Equal(fqn, entry.Fqn);
    }

    [Fact]
    public void Rebuild_CalledTwice_NoDuplicateEntries()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();
        int firstCount = exporter.All.Count;

        exporter.Rebuild();
        Assert.Equal(firstCount, exporter.All.Count);
    }

    [Fact]
    public void Rebuild_RaisesChangedEvent()
    {
        var exporter = new ActionSchemaExporter();
        int raised = 0;
        exporter.Changed += () => raised++;

        exporter.Rebuild();
        Assert.Equal(1, raised);
    }

    // -------------------------------------------------------------------------
    // TASK-BB-1a-02: Catalog watcher
    // -------------------------------------------------------------------------

    [Fact]
    public void CatalogChanged_TriggersRebuild_ExactlyOnce()
    {
        var catalog  = new FakeCatalog();
        var counting = new CountingExporter();
        using var watcher = new ActionSchemaExporterCatalogWatcher(counting, catalog);

        catalog.RaiseChanged();

        Assert.Equal(1, counting.RebuildCallCount);
    }

    [Fact]
    public void CatalogChanged_TwiceTriggersRebuild_Twice()
    {
        var catalog  = new FakeCatalog();
        var counting = new CountingExporter();
        using var watcher = new ActionSchemaExporterCatalogWatcher(counting, catalog);

        catalog.RaiseChanged();
        catalog.RaiseChanged();

        Assert.Equal(2, counting.RebuildCallCount);
    }

    [Fact]
    public void CatalogChanged_AfterDispose_DoesNotTriggerRebuild()
    {
        var catalog  = new FakeCatalog();
        var counting = new CountingExporter();
        var watcher  = new ActionSchemaExporterCatalogWatcher(counting, catalog);
        watcher.Dispose();

        catalog.RaiseChanged();

        Assert.Equal(0, counting.RebuildCallCount);
    }

    [Fact]
    public void CatalogChanged_ExporterChangedEventFires_AfterRebuild()
    {
        var catalog  = new FakeCatalog();
        var exporter = new ActionSchemaExporter();
        int exporterChangedCount = 0;
        exporter.Changed += () => exporterChangedCount++;
        using var watcher = new ActionSchemaExporterCatalogWatcher(exporter, catalog);

        catalog.RaiseChanged();

        Assert.Equal(1, exporterChangedCount);
    }

    [Fact]
    public void SingleWatcher_CatalogChanged_NoDuplicateSubscription()
    {
        // Creating a single watcher must subscribe exactly once -- verify by counting
        // Rebuild calls rather than reflection on the delegate list.
        var catalog  = new FakeCatalog();
        var counting = new CountingExporter();
        using var watcher = new ActionSchemaExporterCatalogWatcher(counting, catalog);

        catalog.RaiseChanged();

        // Exactly one rebuild (not two, which would indicate double-subscription).
        Assert.Equal(1, counting.RebuildCallCount);
    }

    // -------------------------------------------------------------------------
    // DEBT-01: HsmAction/HsmGuard DtoType fallback for void* signatures
    // -------------------------------------------------------------------------

    [Fact]
    public void Rebuild_HsmVoidPtrAction_WithDtoType_AppearsInAll()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        Assert.True(exporter.All.ContainsKey(Fqn(nameof(ActionFixtures.HsmVoidPtrAction_WithDtoType))));
    }

    [Fact]
    public void Rebuild_HsmVoidPtrAction_WithDtoType_HasCorrectDtoType()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmVoidPtrAction_WithDtoType))];
        Assert.Equal(typeof(TestHsmDto), entry.DtoType);
    }

    [Fact]
    public void Rebuild_HsmVoidPtrAction_WithDtoType_HasHsmHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmVoidPtrAction_WithDtoType))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
    }

    [Fact]
    public void Rebuild_HsmVoidPtrAction_NullDtoType_NotInAll()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        // Method has [HsmAction] but DtoType is null and params are void* -- must be skipped.
        Assert.False(exporter.All.ContainsKey(Fqn(nameof(ActionFixtures.HsmVoidPtrAction_NullDtoType))));
    }

    [Fact]
    public void Rebuild_HsmVoidPtrGuard_WithDtoType_AppearsInAll()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        Assert.True(exporter.All.ContainsKey(Fqn(nameof(ActionFixtures.HsmVoidPtrGuard_WithDtoType))));
    }

    [Fact]
    public void Rebuild_HsmVoidPtrGuard_WithDtoType_HasCorrectDtoType()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmVoidPtrGuard_WithDtoType))];
        Assert.Equal(typeof(TestHsmDto), entry.DtoType);
    }

    [Fact]
    public void Rebuild_HsmVoidPtrGuard_WithDtoType_HasHsmHosting()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.HsmVoidPtrGuard_WithDtoType))];
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Hsm));
    }

    // -------------------------------------------------------------------------
    // S1-1 — DtoFields reflection (BATCH-01)
    // -------------------------------------------------------------------------

    [Fact]
    public void ActionSchema_ReflectsFirstRefParamDto_DtoType_IsCorrect()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.FooDtoAction))];
        Assert.Equal(typeof(FooDto), entry.DtoType);
    }

    [Fact]
    public void ActionSchema_ReflectsFirstRefParamDto_DtoFields_NotNull()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.FooDtoAction))];
        Assert.NotNull(entry.DtoFields);
    }

    [Fact]
    public void ActionSchema_ReflectsFirstRefParamDto_DtoFields_ContainsHealth()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.FooDtoAction))];
        Assert.NotNull(entry.DtoFields);
        Assert.Contains(entry.DtoFields!, f => f.Name == "Health" && f.FieldType == typeof(int));
    }

    [Fact]
    public void ActionSchema_ReflectsFirstRefParamDto_DtoFields_ContainsSpeed()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.FooDtoAction))];
        Assert.NotNull(entry.DtoFields);
        Assert.Contains(entry.DtoFields!, f => f.Name == "Speed" && f.FieldType == typeof(float));
    }

    [Fact]
    public void ActionSchema_ReflectsFirstRefParamDto_DtoFields_CountIs2()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.FooDtoAction))];
        Assert.NotNull(entry.DtoFields);
        Assert.Equal(2, entry.DtoFields!.Count);
    }

    // -------------------------------------------------------------------------
    // T1 — exporter discriminator (BATCH-01)
    // -------------------------------------------------------------------------

    [Fact]
    public void Rebuild_BTreeAction_IsCondition_False()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeActionMethod))];
        Assert.False(entry.IsCondition);
    }

    [Fact]
    public void Rebuild_BTreeCondition_IsCondition_True()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeConditionMethod))];
        Assert.True(entry.IsCondition);
    }

    // -------------------------------------------------------------------------
    // I4 — Blueprint AiPrimitive discovery via [GeneratedAiPrimitiveAction]
    // -------------------------------------------------------------------------

    [Fact]
    public void Rebuild_GeneratedAiPrimitiveAction_IsAiPrimitiveTrue()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.AiPrimitiveActionTickCore))];
        Assert.True(entry.IsAiPrimitive);
    }

    [Fact]
    public void Rebuild_GeneratedAiPrimitiveAction_MapsHostingFlags()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.AiPrimitiveActionTickCore))];
        // bTreeAction: true → BTree; blueprintCall: true → Shared.
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
        Assert.True(entry.Hosting.HasFlag(ActionHosting.Shared));
        Assert.False(entry.IsCondition);
    }

    [Fact]
    public void Rebuild_GeneratedAiPrimitiveAction_DtoTypeIsParamsStruct()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.AiPrimitiveActionTickCore))];
        // DtoType is read from the first ref param (the generated Params), not the WorkingState.
        Assert.Equal(typeof(AiPrimParamsDto), entry.DtoType);
    }

    [Fact]
    public void Rebuild_GeneratedAiPrimitiveCondition_IsConditionTrue_BTreeHosted()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        var entry = exporter.All[Fqn(nameof(ActionFixtures.AiPrimitiveConditionTickCore))];
        Assert.True(entry.IsAiPrimitive);
        Assert.True(entry.IsCondition);
        Assert.True(entry.Hosting.HasFlag(ActionHosting.BTree));
    }

    [Fact]
    public void Rebuild_HardcodedBTreeAction_IsAiPrimitiveFalse()
    {
        var exporter = new ActionSchemaExporter();
        exporter.Rebuild();

        // Regression: hand-written attributed methods must keep IsAiPrimitive == false.
        var entry = exporter.All[Fqn(nameof(ActionFixtures.BTreeActionMethod))];
        Assert.False(entry.IsAiPrimitive);
    }
}
