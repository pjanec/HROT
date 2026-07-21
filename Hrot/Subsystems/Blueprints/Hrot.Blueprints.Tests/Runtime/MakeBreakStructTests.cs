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

    /// <summary>
    /// Q#14 Option B — SetMembers copy-with-changes: Make{A=1,B=2} → SetMembers(overwrite B=20) → Break.
    /// Asserts A stays 1 (copied from the source, member unwired) and B becomes 20 (overwritten).
    /// </summary>
    [Fact]
    public void SetMembers_CopiesSourceAndOverwritesOnlyWiredMembers()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string structFqn = typeof(MultiPinShared).FullName!;
        var asset = BlueprintAssetBuilder.Instance("SetMembersBp")
            .WithVariable("ResultA", typeof(int), "0")
            .WithVariable("ResultB", typeof(int), "0")
            .Build();
        asset.Graphs.Add(BuildSetMembersGraph(structFqn, asset.Variables[0].Id, asset.Variables[1].Id));

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(1);

        Assert.Equal(1,  harness.ReadIntField(entity, asset, "ResultA")); // source A, member unwired → preserved
        Assert.Equal(20, harness.ReadIntField(entity, asset, "ResultB")); // member B overwritten
    }

    private static Graph BuildSetMembersGraph(string structFqn, Guid resultAId, Guid resultBId)
    {
        var intT    = new BlueprintTypeRef { TypeId = "System.Int32" };
        var structT = new BlueprintTypeRef { TypeId = "global::" + structFqn };
        Pin P(string name, string dir, bool exec, BlueprintTypeRef t) =>
            new Pin { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = exec, TypeRef = t };
        LiteralNode Lit(int v, out Pin outPin)
        {
            var n = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = v.ToString() };
            outPin = P("Value", "Out", false, intT); n.Pins.Add(outPin); return n;
        }
        List<StructFieldDecl> AB() => new()
        {
            new StructFieldDecl { Name = "A", TypeId = "System.Int32" },
            new StructFieldDecl { Name = "B", TypeId = "System.Int32" },
        };

        var eOut = P("Out", "Out", true, new BlueprintTypeRef());
        var entry = new EventEntryNode { Id = Guid.NewGuid(), EventTypeId = "" }; entry.Pins.Add(eOut);

        var l1 = Lit(1, out var l1Out);
        var l2 = Lit(2, out var l2Out);
        var l20 = Lit(20, out var l20Out);

        // Make{A,B} → Value(struct)
        var mA = P("A", "In", false, intT); var mB = P("B", "In", false, intT); var mVal = P("Value", "Out", false, structT);
        var make = new MakeStructNode { Id = Guid.NewGuid(), StructTypeId = structFqn, Fields = AB() };
        make.Pins.AddRange(new[] { mA, mB, mVal });

        // SetMembers(Source, A, B) → Result(struct); wire Source + B only (A member unwired)
        var smSrc = P("Source", "In", false, structT); var smA = P("A", "In", false, intT);
        var smB = P("B", "In", false, intT); var smRes = P("Result", "Out", false, structT);
        var setmem = new SetMembersNode { Id = Guid.NewGuid(), StructTypeId = structFqn, Fields = AB() };
        setmem.Pins.AddRange(new[] { smSrc, smA, smB, smRes });

        // Break(Value) → A,B
        var bVal = P("Value", "In", false, structT); var bA = P("A", "Out", false, intT); var bB = P("B", "Out", false, intT);
        var brk = new BreakStructNode { Id = Guid.NewGuid(), StructTypeId = structFqn, Fields = AB() };
        brk.Pins.AddRange(new[] { bVal, bA, bB });

        var saIn = P("In", "In", true, new BlueprintTypeRef()); var saOut = P("Out", "Out", true, new BlueprintTypeRef()); var saVal = P("Value", "In", false, intT);
        var setA = new SetVariableNode { Id = Guid.NewGuid(), VariableId = resultAId.ToString() }; setA.Pins.AddRange(new[] { saIn, saOut, saVal });
        var sbIn = P("In", "In", true, new BlueprintTypeRef()); var sbOut = P("Out", "Out", true, new BlueprintTypeRef()); var sbVal = P("Value", "In", false, intT);
        var setB = new SetVariableNode { Id = Guid.NewGuid(), VariableId = resultBId.ToString() }; setB.Pins.AddRange(new[] { sbIn, sbOut, sbVal });
        var rIn = P("In", "In", true, new BlueprintTypeRef()); var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success }; ret.Pins.Add(rIn);

        Link Lk(Node fn, Pin fp, Node tn, Pin tp) => new Link { FromNodeId = fn.Id, FromPinId = fp.Id, ToNodeId = tn.Id, ToPinId = tp.Id };
        var links = new List<Link>
        {
            Lk(entry, eOut, setA, saIn), Lk(setA, saOut, setB, sbIn), Lk(setB, sbOut, ret, rIn),   // exec
            Lk(l1, l1Out, make, mA), Lk(l2, l2Out, make, mB),                                        // Make{1,2}
            Lk(make, mVal, setmem, smSrc), Lk(l20, l20Out, setmem, smB),                             // SetMembers(Source, B=20)
            Lk(setmem, smRes, brk, bVal),                                                            // Break(Result)
            Lk(brk, bA, setA, saVal), Lk(brk, bB, setB, sbVal),                                      // → variables
        };

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { entry, l1, l2, l20, make, setmem, brk, setA, setB, ret },
            Links = links,
            Inputs = new List<ParameterDecl>(), Outputs = new List<ParameterDecl>(),
        };
    }
}
