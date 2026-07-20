using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>Top-level blittable shared struct for the multi-pin SetShared test (3 int fields).</summary>
public struct MultiPinShared
{
    public int A;
    public int B;
    public int C;
}

/// <summary>
/// Q#14 multi-pin <c>SetShared</c> — proves the per-field write through the real runtime: a blueprint whose
/// SetShared node bakes three fields (A/B/C) but wires only A and B writes those two into the entity's shared
/// slot and leaves C untouched. Asserts A==7, B==8 (written) and C==99 (PRESERVED — never reset), validating
/// "unwired pins are not written" and that the write is a true per-field write, not a whole-struct overwrite.
/// </summary>
[Collection("DebugProbe")]
public sealed class MultiPinSetSharedTests
{
    private const string SlotName = "myshared";

    private static uint ExpectedHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private static int SlotKey()
        => StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, SlotName);

    /// <summary>Attaches the shared slot to an already-initialised partition (blueprint attached first).</summary>
    private static unsafe void AttachSharedSlot(EntityRepository world, Entity entity)
    {
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            bool ok = BlueprintBlackboardPartitions.TryAttach(
                mem, SlotKey(), Marshal.SizeOf<MultiPinShared>(), ExpectedHash<MultiPinShared>(), out _);
            Assert.True(ok, "TryAttach for the shared slot must succeed");
        }
    }

    [Fact]
    public void MultiPinSetShared_WritesWiredFields_PreservesUnwired()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string sharedFqn = typeof(MultiPinShared).FullName!;
        var asset = BuildAsset(sharedFqn);

        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);   // initialises the partition + adds the blueprint slot
        AttachSharedSlot(fixture.World, entity);   // then attach the shared slot alongside it

        // Seed the slot: C=99 is the field the blueprint never writes — it must survive the tick.
        Assert.True(BlueprintSharedState.TrySetShared(
            fixture.World, entity, SlotName, new MultiPinShared { A = 0, B = 0, C = 99 }));

        fixture.TickFrame(0.016f);   // runs SetShared: A=7, B=8; C untouched

        Assert.True(BlueprintSharedState.TryGetShared<MultiPinShared>(
            fixture.World, entity, SlotName, out var result));
        Assert.Equal(7, result.A);
        Assert.Equal(8, result.B);
        Assert.Equal(99, result.C);   // unwired field preserved — the whole point
    }

    [Fact]
    public void MultiPinGetShared_ReadsField_IntoVariable()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string sharedFqn = typeof(MultiPinShared).FullName!;
        var asset = BuildReadAsset(sharedFqn, out var lastAVarId);

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        AttachSharedSlot(fixture.World, entity);

        Assert.True(BlueprintSharedState.TrySetShared(
            fixture.World, entity, SlotName, new MultiPinShared { A = 42, B = 0, C = 0 }));

        fixture.TickFrame(0.016f);   // GetShared multi-pin reads field A → SetVariable(LastA)

        Assert.Equal(42, harness.ReadIntField(entity, asset, "LastA"));   // field A read via the multi-pin GetShared
    }

    /// <summary>
    /// Instance blueprint, Tick graph: EventEntry → SetShared(myshared, fields A/B/C; A←7, B←8, C unwired) → Return.
    /// Explicit pins so wiring is deterministic (no positional rehydration).
    /// </summary>
    private static BlueprintAsset BuildAsset(string sharedFqn)
    {
        var asset = BlueprintAssetBuilder.Instance("MultiPinSetSharedBp").Build();

        int offA = (int)Marshal.OffsetOf<MultiPinShared>(nameof(MultiPinShared.A));
        int offB = (int)Marshal.OffsetOf<MultiPinShared>(nameof(MultiPinShared.B));
        int offC = (int)Marshal.OffsetOf<MultiPinShared>(nameof(MultiPinShared.C));
        var intT = new BlueprintTypeRef { TypeId = "System.Int32" };

        // Nodes
        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        entry.Pins.Add(entryExecOut);

        var litAOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intT };
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        litA.Pins.Add(litAOut);

        var litBOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intT };
        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "8" };
        litB.Pins.Add(litBOut);

        var sExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new BlueprintTypeRef() };
        var sExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var sA = new Pin { Id = Guid.NewGuid(), Name = "A", Direction = "In", IsExec = false, TypeRef = intT };
        var sB = new Pin { Id = Guid.NewGuid(), Name = "B", Direction = "In", IsExec = false, TypeRef = intT };
        var sC = new Pin { Id = Guid.NewGuid(), Name = "C", Direction = "In", IsExec = false, TypeRef = intT };
        var setShared = new SetSharedNode
        {
            Id = Guid.NewGuid(),
            VariableId = SlotName,
            SharedTypeId = sharedFqn,
            Fields = new List<SharedFieldDecl>
            {
                new SharedFieldDecl { Name = "A", TypeId = "System.Int32", Offset = offA },
                new SharedFieldDecl { Name = "B", TypeId = "System.Int32", Offset = offB },
                new SharedFieldDecl { Name = "C", TypeId = "System.Int32", Offset = offC },
            },
        };
        setShared.Pins.AddRange(new[] { sExecIn, sExecOut, sA, sB, sC });

        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(),
            Name = "Tick",
            Kind = GraphKind.Function,
            Nodes = new List<Node> { entry, litA, litB, setShared, ret },
            Links = new List<Link>
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryExecOut.Id, ToNodeId = setShared.Id, ToPinId = sExecIn.Id },
                new Link { FromNodeId = litA.Id,      FromPinId = litAOut.Id,      ToNodeId = setShared.Id, ToPinId = sA.Id },
                new Link { FromNodeId = litB.Id,      FromPinId = litBOut.Id,      ToNodeId = setShared.Id, ToPinId = sB.Id },
                new Link { FromNodeId = setShared.Id, FromPinId = sExecOut.Id,     ToNodeId = ret.Id,       ToPinId = retExecIn.Id },
            },
            Inputs = new List<ParameterDecl>(),
            Outputs = new List<ParameterDecl>(),
        };

        asset.Graphs.Add(graph);
        return asset;
    }

    /// <summary>
    /// Instance blueprint, Tick graph: EventEntry → SetVariable(LastA ← GetShared(myshared).A) → Return.
    /// GetShared is a multi-pin pure node (fields A/B/C); the A data-out feeds SetVariable's value pin.
    /// </summary>
    private static BlueprintAsset BuildReadAsset(string sharedFqn, out Guid lastAVarId)
    {
        var asset = BlueprintAssetBuilder.Instance("MultiPinGetSharedBp")
            .WithVariable("LastA", typeof(int), "0")
            .Build();
        lastAVarId = asset.Variables[0].Id;

        var intT = new BlueprintTypeRef { TypeId = "System.Int32" };
        SharedFieldDecl F(string n) => new SharedFieldDecl
        {
            Name = n, TypeId = "System.Int32",
            Offset = (int)Marshal.OffsetOf<MultiPinShared>(n),
        };

        var entryExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        entry.Pins.Add(entryExecOut);

        // GetShared: pure node (no exec) — Target(in) + per-field data-outs + Found.
        var gTarget = new Pin { Id = Guid.NewGuid(), Name = "Target", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var gA = new Pin { Id = Guid.NewGuid(), Name = "A", Direction = "Out", IsExec = false, TypeRef = intT };
        var gB = new Pin { Id = Guid.NewGuid(), Name = "B", Direction = "Out", IsExec = false, TypeRef = intT };
        var gC = new Pin { Id = Guid.NewGuid(), Name = "C", Direction = "Out", IsExec = false, TypeRef = intT };
        var gFound = new Pin { Id = Guid.NewGuid(), Name = "Found", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var getShared = new GetSharedNode
        {
            Id = Guid.NewGuid(), VariableId = SlotName, SharedTypeId = sharedFqn,
            Fields = new List<SharedFieldDecl> { F("A"), F("B"), F("C") },
        };
        getShared.Pins.AddRange(new[] { gTarget, gA, gB, gC, gFound });

        var sExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new BlueprintTypeRef() };
        var sExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var sValIn   = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In",  IsExec = false, TypeRef = intT };
        var sValOut  = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intT };
        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = lastAVarId.ToString() };
        setVar.Pins.AddRange(new[] { sExecIn, sExecOut, sValIn, sValOut });

        var retExecIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retExecIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { entry, getShared, setVar, ret },
            Links = new List<Link>
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryExecOut.Id, ToNodeId = setVar.Id, ToPinId = sExecIn.Id },
                new Link { FromNodeId = getShared.Id, FromPinId = gA.Id,           ToNodeId = setVar.Id, ToPinId = sValIn.Id },
                new Link { FromNodeId = setVar.Id,    FromPinId = sExecOut.Id,     ToNodeId = ret.Id,    ToPinId = retExecIn.Id },
            },
            Inputs = new List<ParameterDecl>(), Outputs = new List<ParameterDecl>(),
        };

        asset.Graphs.Add(graph);
        return asset;
    }
}
