using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Q#14 Option B — Make/Break struct values. A Tick graph constructs a <see cref="MultiPinShared"/> via a
/// MakeStruct node (wiring only field A = 7; B left at the struct default), flows the whole-struct value
/// into a BreakStruct node, and mirrors the broken-out fields into variables. Asserts ResultA == 7 (built)
/// and ResultB == 0 (unwired Make field → struct default), proving the struct value round-trips
/// Make → (struct-typed pin) → Break → fields through the real compiler + runtime.
/// </summary>
[Collection("DebugProbe")]
public sealed class MakeBreakStructTests
{
    [Fact]
    public void MakeStruct_ThenBreakStruct_RoundTripsFieldsThroughAStructValue()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string structFqn = typeof(MultiPinShared).FullName!;

        var asset = BlueprintAssetBuilder.Instance("MakeBreakBp")
            .WithVariable("ResultA", typeof(int), "0")
            .WithVariable("ResultB", typeof(int), "0")
            .Build();
        var resultAId = asset.Variables[0].Id;
        var resultBId = asset.Variables[1].Id;

        asset.Graphs.Add(BuildTickGraph(structFqn, resultAId, resultBId));

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);

        harness.Pump(1);

        Assert.Equal(7, harness.ReadIntField(entity, asset, "ResultA")); // Make wired A = 7 → Break.A
        Assert.Equal(0, harness.ReadIntField(entity, asset, "ResultB")); // Make left B unset → struct default
    }

    private static Graph BuildTickGraph(string structFqn, Guid resultAId, Guid resultBId)
    {
        var intT    = new BlueprintTypeRef { TypeId = "System.Int32" };
        var structT = new BlueprintTypeRef { TypeId = "global::" + structFqn };

        // EventEntry (exec-Out)
        var eOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" };
        entry.Pins.Add(eOut);

        // Literal 7 (data-Out "Value")
        var litOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = intT };
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        lit.Pins.Add(litOut);

        // MakeStruct (data-In A/B, data-Out Value:struct)
        var mA   = new Pin { Id = Guid.NewGuid(), Name = "A",     Direction = "In",  IsExec = false, TypeRef = intT };
        var mB   = new Pin { Id = Guid.NewGuid(), Name = "B",     Direction = "In",  IsExec = false, TypeRef = intT };
        var mVal = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = structT };
        var make = new MakeStructNode
        {
            Id = Guid.NewGuid(), StructTypeId = structFqn,
            Fields = new List<StructFieldDecl>
            {
                new StructFieldDecl { Name = "A", TypeId = "System.Int32" },
                new StructFieldDecl { Name = "B", TypeId = "System.Int32" },
            },
        };
        make.Pins.AddRange(new[] { mA, mB, mVal });

        // BreakStruct (data-In Value:struct, data-Out A/B)
        var bVal = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In",  IsExec = false, TypeRef = structT };
        var bA   = new Pin { Id = Guid.NewGuid(), Name = "A",     Direction = "Out", IsExec = false, TypeRef = intT };
        var bB   = new Pin { Id = Guid.NewGuid(), Name = "B",     Direction = "Out", IsExec = false, TypeRef = intT };
        var brk = new BreakStructNode
        {
            Id = Guid.NewGuid(), StructTypeId = structFqn,
            Fields = new List<StructFieldDecl>
            {
                new StructFieldDecl { Name = "A", TypeId = "System.Int32" },
                new StructFieldDecl { Name = "B", TypeId = "System.Int32" },
            },
        };
        brk.Pins.AddRange(new[] { bVal, bA, bB });

        // SetVariable ResultA (exec + Value-in)
        var saIn  = new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var saOut = new Pin { Id = Guid.NewGuid(), Name = "Out",   Direction = "Out", IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var saVal = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In",  IsExec = false, TypeRef = intT };
        var setA = new SetVariableNode { Id = Guid.NewGuid(), VariableId = resultAId.ToString() };
        setA.Pins.AddRange(new[] { saIn, saOut, saVal });

        // SetVariable ResultB (exec + Value-in)
        var sbIn  = new Pin { Id = Guid.NewGuid(), Name = "In",    Direction = "In",  IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var sbOut = new Pin { Id = Guid.NewGuid(), Name = "Out",   Direction = "Out", IsExec = true,  TypeRef = new BlueprintTypeRef() };
        var sbVal = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In",  IsExec = false, TypeRef = intT };
        var setB = new SetVariableNode { Id = Guid.NewGuid(), VariableId = resultBId.ToString() };
        setB.Pins.AddRange(new[] { sbIn, sbOut, sbVal });

        var rIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new BlueprintTypeRef() };
        var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(rIn);

        var links = new List<Link>
        {
            // exec chain
            new Link { FromNodeId = entry.Id, FromPinId = eOut.Id,  ToNodeId = setA.Id, ToPinId = saIn.Id },
            new Link { FromNodeId = setA.Id,  FromPinId = saOut.Id, ToNodeId = setB.Id, ToPinId = sbIn.Id },
            new Link { FromNodeId = setB.Id,  FromPinId = sbOut.Id, ToNodeId = ret.Id,  ToPinId = rIn.Id },
            // data: Lit7 → Make.A ; Make.Value → Break.Value ; Break.A → SetA.Value ; Break.B → SetB.Value
            new Link { FromNodeId = lit.Id,  FromPinId = litOut.Id, ToNodeId = make.Id, ToPinId = mA.Id },
            new Link { FromNodeId = make.Id, FromPinId = mVal.Id,   ToNodeId = brk.Id,  ToPinId = bVal.Id },
            new Link { FromNodeId = brk.Id,  FromPinId = bA.Id,     ToNodeId = setA.Id, ToPinId = saVal.Id },
            new Link { FromNodeId = brk.Id,  FromPinId = bB.Id,     ToNodeId = setB.Id, ToPinId = sbVal.Id },
        };

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { entry, lit, make, brk, setA, setB, ret },
            Links = links,
            Inputs = new List<ParameterDecl>(), Outputs = new List<ParameterDecl>(),
        };
    }
}
