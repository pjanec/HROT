using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// =============================================================================
// UBP-P6T2: Slot-table-aware IL emission for BlueprintVariablePredicateDto
// =============================================================================

/// <summary>
/// Unit tests for the <see cref="BlueprintVariablePredicateDto"/> compilation path
/// in <see cref="PredicateCompiler"/>.
///
/// Test blueprint: one Instance-dispatch blueprint "TestBP" with a single int field
/// "AmmoCount" at payload offset 0, stateSize=4. The asset GUID drives the
/// BlueprintId via BlueprintIdHash.Compute.
///
/// BB16384 is NOT registered (it would require ~16 GB of virtual-address space in
/// test repos). The compiler bakes typeId16384=-1, so HasComponentByTypeId(entity,-1)
/// returns false safely.
/// </summary>
[Collection("ComponentRegistry")]
public sealed unsafe class BlueprintVariableCompilerTests
{
    private static readonly Guid   s_assetGuid  = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly int    s_blueprintId = BlueprintIdHash.Compute(s_assetGuid);
    private const            string s_fieldName  = "AmmoCount";

    // -------------------------------------------------------------------------
    // Shared setup
    // -------------------------------------------------------------------------

    private static (EntityRepository repo, PredicateCompiler compiler) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo = new EntityRepository();
        repo.RegisterComponent<BlueprintBlackboard1024>();
        repo.RegisterComponent<BlueprintBlackboard4096>();
        // BB16384 intentionally omitted -- see class summary.

        var registry = new BlueprintRegistry();
        var def = new BlueprintDefinition
        {
            Name          = "TestBP",
            Kind          = BlueprintDispatchKind.Instance,
            StructureHash = 0,
            StateSize     = sizeof(int),
            AssetId       = s_assetGuid,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                [s_fieldName] = new BlueprintFieldDescriptor(
                    Name:           s_fieldName,
                    ClrType:        typeof(int),
                    OffsetBytes:    0,
                    SizeBytes:      sizeof(int),
                    CategoryOrEmpty: ""),
            },
        };
        registry.RegisterInstance(s_blueprintId, def);

        var compiler = new PredicateCompiler(
            new ComponentEditServiceBuilder().Build(),
            blueprintRegistry: registry);
        return (repo, compiler);
    }

    // -------------------------------------------------------------------------
    // Helper: attach the test blueprint to a BB1024 component on an entity.
    // Returns the payloadOffset written by TryAttach.
    // -------------------------------------------------------------------------
    private static int AttachToBB1024(EntityRepository repo, Entity entity)
    {
        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        BlueprintBlackboardPartitions.Initialize(
            mem,
            BlueprintBlackboard1024.TotalSize,
            (byte)BlueprintBlackboard1024.MaxSlots);

        bool ok = BlueprintBlackboardPartitions.TryAttach(
            mem,
            s_blueprintId,
            requestedSize: sizeof(int),
            structureHash: 0,
            out int payloadOffset);
        Assert.True(ok, "TryAttach must succeed for a freshly initialised BB1024");
        return payloadOffset;
    }

    // =========================================================================
    // P6T2-SC1: No slot present -> false
    // =========================================================================

    /// <summary>
    /// Entity has a BB1024 component that is fully initialised (valid header)
    /// but no blueprint slot has been attached. The predicate must return false
    /// rather than reading uninitialised memory.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse()
    {
        var (repo, compiler) = Setup();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        // Initialise the header so TryGetSlotOffset scans a valid structure,
        // but do not call TryAttach -- SlotCount remains 0.
        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        BlueprintBlackboardPartitions.Initialize(
            mem,
            BlueprintBlackboard1024.TotalSize,
            (byte)BlueprintBlackboard1024.MaxSlots);

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.False(predicate(repo, entity));
    }

    // =========================================================================
    // P6T2-SC2: Slot present with matching value -> true
    // =========================================================================

    /// <summary>
    /// Entity has a BB1024 component with the blueprint attached and AmmoCount=0.
    /// Predicate "AmmoCount == 0" must return true.
    /// Also tests the negative: after writing AmmoCount=99, predicate returns false.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_SlotPresent_EvaluatesField()
    {
        var (repo, compiler) = Setup();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        int payloadOffset = AttachToBB1024(repo, entity);

        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem = (byte*)Unsafe.AsPointer(ref bb);
        *(int*)(mem + payloadOffset) = 0; // AmmoCount = 0

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.True(predicate(repo, entity),  "AmmoCount=0 must satisfy == 0");

        // Mutate field and verify the delegate reads fresh data each call.
        ref var bb2 = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem2 = (byte*)Unsafe.AsPointer(ref bb2);
        *(int*)(mem2 + payloadOffset) = 99; // AmmoCount = 99
        Assert.False(predicate(repo, entity), "AmmoCount=99 must not satisfy == 0");
    }

    // =========================================================================
    // P6T2-SC3: Tier upgrade (BB1024 -> BB4096) -- delegate re-runs slot scan
    // =========================================================================

    /// <summary>
    /// Compile a delegate while the entity uses BB1024.
    /// Simulate a tier upgrade: add BB4096, copy slots via CopyToLargerTier,
    /// remove BB1024.
    /// The same compiled delegate must still find AmmoCount=5 via BB4096 because
    /// it probes all tiers on every evaluation.
    /// </summary>
    [Fact]
    public void Compile_BlueprintVariable_TierUpgrade_StillWorks()
    {
        var (repo, compiler) = Setup();

        // -- Phase 1: entity has BB1024, AmmoCount=5 -------------------------
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

        int payloadOffset1024 = AttachToBB1024(repo, entity);

        ref var bb1024 = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem1024 = (byte*)Unsafe.AsPointer(ref bb1024);
        *(int*)(mem1024 + payloadOffset1024) = 5; // AmmoCount = 5

        var dto = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = s_assetGuid,
            VariableName           = s_fieldName,
            Operator               = SearchOperator.Equals,
            Predicate              = new NumericPredicateDto { MinValue = 5, MaxValue = 5 },
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.True(predicate(repo, entity), "Pre-upgrade: AmmoCount=5 on BB1024 must satisfy == 5");

        // -- Phase 2: upgrade to BB4096 via CopyToLargerTier -----------------
        repo.AddComponent(entity, new BlueprintBlackboard4096());

        // Re-fetch BB1024 pointer -- structural change (AddComponent) may have
        // moved the entity to a new archetype chunk.
        ref var bb1024After = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem1024After  = (byte*)Unsafe.AsPointer(ref bb1024After);

        ref var bb4096 = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
        byte* mem4096  = (byte*)Unsafe.AsPointer(ref bb4096);

        BlueprintBlackboardPartitions.CopyToLargerTier(
            src:         mem1024After,
            srcSize:     BlueprintBlackboard1024.TotalSize,
            dst:         mem4096,
            dstSize:     BlueprintBlackboard4096.TotalSize,
            dstMaxSlots: (byte)BlueprintBlackboard4096.MaxSlots);

        repo.RemoveComponent<BlueprintBlackboard1024>(entity);

        // -- Phase 3: same compiled delegate works on BB4096 -----------------
        Assert.True(predicate(repo, entity), "Post-upgrade: AmmoCount=5 on BB4096 must still satisfy == 5");
    }
}
