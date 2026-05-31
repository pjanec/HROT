using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Blueprints;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Unit tests for BlueprintRegistry - TASK-RT-001.
/// Covers success criteria SC1-SC7.
/// </summary>
public sealed class BlueprintRegistryTests : IDisposable
{
    private readonly BlueprintRegistry _registry = new();

    public void Dispose() { }

    // ---- SC1: Direct registration round-trips --------------------------------

    [Fact]
    public void SC1_DirectRegistration_ByIdAndByName()
    {
        int idLib  = 1001;
        int idPrim = 1002;
        int idInst = 1003;

        var defPrim = new BlueprintDefinition
        {
            Name = "AiPrim", Kind = BlueprintDispatchKind.AiPrimitive,
            StructureHash = 0x100, StateSize = 64
        };
        var defInst = new BlueprintDefinition
        {
            Name = "Inst", Kind = BlueprintDispatchKind.Instance,
            StructureHash = 0x200, StateSize = 128
        };

        _registry.RegisterLibrary(idLib, "Lib");
        _registry.RegisterAiPrimitive(idPrim, defPrim);
        _registry.RegisterInstance(idInst, defInst);

        Assert.True(_registry.TryGetById(idLib, out var defLibOut));
        Assert.Equal("Lib", defLibOut!.Name);
        Assert.Equal(BlueprintDispatchKind.Library, defLibOut.Kind);

        Assert.True(_registry.TryGetById(idPrim, out var defPrimOut));
        Assert.Equal("AiPrim", defPrimOut!.Name);

        Assert.True(_registry.TryGetById(idInst, out var defInstOut));
        Assert.Equal("Inst", defInstOut!.Name);

        Assert.True(_registry.TryGetByName("Lib", out var byNameDef));
        Assert.Equal("Lib", byNameDef!.Name);

        Assert.Equal(3, _registry.GetAll().Count);
    }

    [Fact]
    public void SC1_TryGetById_ReturnsFalseForUnknownId()
    {
        Assert.False(_registry.TryGetById(9999, out _));
    }

    // ---- SC2: Staging commit replaces registry content ----------------------

    [Fact]
    public void SC2_CommitStaging_Makes_Entries_Retrievable()
    {
        var staging = _registry.BeginStaging();
        var def1 = new BlueprintDefinition { Name = "S1", Kind = BlueprintDispatchKind.Instance, StructureHash = 1, StateSize = 16 };
        var def2 = new BlueprintDefinition { Name = "S2", Kind = BlueprintDispatchKind.Instance, StructureHash = 2, StateSize = 16 };
        int id1 = 2001, id2 = 2002;
        staging.Add(id1, def1);
        staging.Add(id2, def2);

        _registry.CommitStaging(staging);

        Assert.True(_registry.TryGetById(id1, out _));
        Assert.True(_registry.TryGetById(id2, out _));
        Assert.False(_registry.TryGetById(9999, out _));
    }

    [Fact]
    public void SC2_CommitStaging_Replaces_PreviousContent()
    {
        // First commit with id1
        var staging1 = _registry.BeginStaging();
        staging1.Add(3001, new BlueprintDefinition { Name = "First", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        _registry.CommitStaging(staging1);

        // Second commit with id2 only -- id1 is no longer present
        var staging2 = _registry.BeginStaging();
        staging2.Add(3002, new BlueprintDefinition { Name = "Second", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        _registry.CommitStaging(staging2);

        Assert.False(_registry.TryGetById(3001, out _));
        Assert.True(_registry.TryGetById(3002, out _));
    }

    // ---- SC3: World singletons -------------------------------------------

    [Fact]
    public void SC3_GetAllWorldSingletons_AfterStagingCommit()
    {
        var staging = _registry.BeginStaging();
        int id1 = 4001;
        staging.Add(id1, new BlueprintDefinition { Name = "Singleton", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        staging.AddWorldSingleton(id1, BlackboardTier.B1024);
        _registry.CommitStaging(staging);

        var singletons = _registry.GetAllWorldSingletons();
        Assert.Single(singletons);
        Assert.Equal(id1, singletons[0].Item1);
        Assert.Equal(BlackboardTier.B1024, singletons[0].Item2);

        // Second call returns same reference (pre-built list)
        var singletons2 = _registry.GetAllWorldSingletons();
        Assert.Same(singletons, singletons2);
    }

    [Fact]
    public void SC3_RegisterWorldSingleton_DirectPath()
    {
        _registry.RegisterLibrary(5001, "SingletonDirect");
        _registry.RegisterWorldSingleton(5001, BlackboardTier.B4096);

        Assert.True(_registry.TryGetWorldSingleton(5001, out var tier));
        Assert.Equal(BlackboardTier.B4096, tier);

        var all = _registry.GetAllWorldSingletons();
        Assert.Contains(all, s => s.Item1 == 5001);
    }

    // ---- SC4: Duplicate detection ----------------------------------------

    [Fact]
    public void SC4_DirectRegistration_Duplicate_ThrowsInvalidOperation()
    {
        _registry.RegisterLibrary(6001, "DupA");
        var ex = Assert.Throws<InvalidOperationException>(
            () => _registry.RegisterLibrary(6001, "DupB"));
        Assert.Contains("DupA", ex.Message);
        Assert.Contains("DupB", ex.Message);
    }

    [Fact]
    public void SC4_StagingAdd_Duplicate_ThrowsInvalidOperation()
    {
        var staging = _registry.BeginStaging();
        staging.Add(7001, new BlueprintDefinition { Name = "DupX", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        var ex = Assert.Throws<InvalidOperationException>(
            () => staging.Add(7001, new BlueprintDefinition { Name = "DupY", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 }));
        Assert.Contains("DupX", ex.Message);
        Assert.Contains("DupY", ex.Message);
    }

    // ---- SC5: RegisterWorldSingleton with unknown ID throws ----------------

    [Fact]
    public void SC5_RegisterWorldSingleton_UnknownId_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => _registry.RegisterWorldSingleton(99999, BlackboardTier.B1024));
    }

    // ---- SC6: CommitStaging replaces previous entries (already in SC2) -----

    [Fact]
    public void SC6_TwoCommitStagingCalls_SecondWins()
    {
        var staging1 = _registry.BeginStaging();
        staging1.Add(8001, new BlueprintDefinition { Name = "Alpha", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        _registry.CommitStaging(staging1);

        var staging2 = _registry.BeginStaging();
        staging2.Add(8002, new BlueprintDefinition { Name = "Beta", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        _registry.CommitStaging(staging2);

        Assert.False(_registry.TryGetById(8001, out _));
        Assert.True(_registry.TryGetById(8002, out _));
        Assert.Equal(1, _registry.GetAll().Count);
    }

    // ---- SC7: OnRegistryChanged fires exactly once per CommitStaging --------

    [Fact]
    public void SC7_OnRegistryChanged_FiresExactlyOnce_PerCommit()
    {
        int count = 0;
        _registry.OnRegistryChanged += () => count++;

        var staging = _registry.BeginStaging();
        staging.Add(9001, new BlueprintDefinition { Name = "X", Kind = BlueprintDispatchKind.Library, StructureHash = 0, StateSize = 0 });
        _registry.CommitStaging(staging);

        Assert.Equal(1, count);
    }

    [Fact]
    public void SC7_OnRegistryChanged_FiresEvenForEmptyStaging()
    {
        int count = 0;
        _registry.OnRegistryChanged += () => count++;

        var staging = _registry.BeginStaging();
        _registry.CommitStaging(staging);

        Assert.Equal(1, count);
    }

    // ---- BPF-007: GetAll returns (Id, Def) tuples ---------------------------

    [Fact]
    public void BPF007_GetAll_Returns_Tuple_With_Correct_Id()
    {
        int id = 10001;
        var def = new BlueprintDefinition
        {
            Name = "TupleTest", Kind = BlueprintDispatchKind.Instance,
            StructureHash = 0x999, StateSize = 32
        };
        _registry.RegisterInstance(id, def);

        var all = _registry.GetAll();

        Assert.Contains(all, t => t.Id == id && t.Def.Name == "TupleTest");
    }
}
