using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// =============================================================================
// BP-29 regression: PredicateCompiler's blueprintRegistry wiring
// =============================================================================

/// <summary>
/// Regression guard for <b>BP-29</b> — blueprint conditional breakpoints silently never fired.
///
/// <para>
/// <see cref="PredicateCompiler"/>'s <c>blueprintRegistry</c> constructor argument is optional and
/// defaults to <c>null</c>. When it is null, <c>CompileBlueprintVariablePredicate</c> short-circuits
/// to <c>static (_, _) =&gt; false</c> — so a <see cref="BlueprintVariablePredicateDto"/> (exactly what
/// "Add Conditional Data Breakpoint…" synthesizes from a blueprint node) evaluates false for every
/// entity, whatever the real variable value is. No throw, no diagnostic.
/// </para>
///
/// <para>
/// All three production sites omitted the argument (<c>EditorSubsystem</c>, <c>CgfSubsystem</c>,
/// <c>ReplayBrowserSubsystem</c>), so the feature was dead in the running editor. The existing
/// <see cref="BlueprintVariableCompilerTests"/> could not catch it because every one of its cases
/// passes the registry explicitly — proving the <i>logic</i> correct while the <i>wiring</i> stayed
/// broken.
/// </para>
///
/// <para>
/// These two tests build the <b>same entity and the same predicate</b> and differ only in whether the
/// registry is supplied, so the wiring is the single variable. If someone later changes the null-registry
/// path to throw or warn instead of returning false, <see cref="MissingRegistry_SilentlyReturnsFalse"/>
/// is the test that should be updated — deliberately, not by accident.
/// </para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed unsafe class BlueprintVariableWiringRegressionTests
{
    private static readonly Guid   s_assetGuid   = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly int    s_blueprintId = BlueprintIdHash.Compute(s_assetGuid);
    private const           string s_fieldName   = "AmmoCount";

    /// <summary>Builds the registry describing a one-int-field Instance blueprint.</summary>
    private static BlueprintRegistry BuildRegistry()
    {
        var registry = new BlueprintRegistry();
        registry.RegisterInstance(s_blueprintId, new BlueprintDefinition
        {
            Name          = "TestBP",
            Kind          = BlueprintDispatchKind.Instance,
            StructureHash = 0,
            StateSize     = sizeof(int),
            AssetId       = s_assetGuid,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                [s_fieldName] = new BlueprintFieldDescriptor(
                    Name:            s_fieldName,
                    ClrType:         typeof(int),
                    OffsetBytes:     0,
                    SizeBytes:       sizeof(int),
                    CategoryOrEmpty: ""),
            },
        });
        return registry;
    }

    /// <summary>Creates an entity carrying the blueprint with <c>AmmoCount == 0</c>.</summary>
    private static (EntityRepository repo, Entity entity) BuildEntityWithAmmoZero()
    {
        ComponentTypeRegistry.Clear();
        var repo = new EntityRepository();
        repo.RegisterComponent<BlueprintBlackboard1024>();
        repo.RegisterComponent<BlueprintBlackboard4096>();
        // BB16384 intentionally omitted -- see BlueprintVariableCompilerTests' class summary.

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BlueprintBlackboard1024());

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

        // Slot payload is zero-initialised, so AmmoCount == 0 without an explicit write.
        Assert.Equal(0, *(int*)(mem + payloadOffset));

        return (repo, entity);
    }

    /// <summary>The predicate "AmmoCount == 0" — true for the entity built above.</summary>
    private static BlueprintVariablePredicateDto AmmoEqualsZero() => new()
    {
        TargetBlueprintAssetId = s_assetGuid,
        VariableName           = s_fieldName,
        Operator               = SearchOperator.Equals,
        Predicate              = new NumericPredicateDto { MinValue = 0, MaxValue = 0 },
    };

    // -------------------------------------------------------------------------

    /// <summary>
    /// Constructed the way production now does — with the registry — the predicate evaluates the
    /// real field value and returns true.
    /// </summary>
    [Fact]
    public void WithRegistry_EvaluatesBlueprintVariable()
    {
        var (repo, entity) = BuildEntityWithAmmoZero();

        var compiler = new PredicateCompiler(
            new ComponentEditServiceBuilder().Build(),
            blueprintRegistry: BuildRegistry());

        var predicate = compiler.CompileComponentPredicate(AmmoEqualsZero());

        Assert.True(
            predicate(repo, entity),
            "With the blueprint registry wired, 'AmmoCount == 0' must evaluate the real field value.");
    }

    /// <summary>
    /// The BP-29 failure mode, pinned. Same entity, same predicate, registry omitted — the compiled
    /// delegate is constant-false, so a breakpoint condition that is genuinely satisfied never fires.
    /// This test documents the degradation; it is not an endorsement of it.
    /// </summary>
    [Fact]
    public void MissingRegistry_SilentlyReturnsFalse()
    {
        var (repo, entity) = BuildEntityWithAmmoZero();

        // No blueprintRegistry -- the shape every production site had before BP-29 was fixed.
        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());

        var predicate = compiler.CompileComponentPredicate(AmmoEqualsZero());

        Assert.False(
            predicate(repo, entity),
            "Without the registry the predicate short-circuits to constant-false -- the BP-29 bug. " +
            "If this assertion starts failing, the null-registry path changed; make sure the change " +
            "was intentional and that it is loud rather than silent.");
    }
}
