using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Builders;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Q#14 Option B — struct-typed Variables: a blueprint Variable whose type is a blittable struct can hold a
/// whole struct value. Tick: Make{A=5,B=6} → SetVariable(S) → then GetVariable(S) → Break → mirror A/B into
/// int variables. Asserts the struct round-trips through a struct-typed variable slot (OutA=5, OutB=6).
/// </summary>
[Collection("DebugProbe")]
public sealed class StructTypedVariableTests
{
    [Fact]
    public void StructTypedVariable_StoresAndReadsBackAStructValue()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        string structFqn = typeof(MultiPinShared).FullName!;
        var asset = BlueprintAssetBuilder.Instance("StructVarBp")
            .WithVariable("S", typeof(MultiPinShared))     // ← the struct-typed variable
            .WithVariable("OutA", typeof(int), "0")
            .WithVariable("OutB", typeof(int), "0")
            .Build();

        asset.Graphs.Add(BuildGraph(structFqn,
            asset.Variables[0].Id, asset.Variables[1].Id, asset.Variables[2].Id));

        fixture.CompileAndLoad(asset);
        var harness = new BlueprintRunHarness(fixture);
        var entity = harness.SpawnAndAttach(asset);
        harness.Pump(1);

        Assert.Equal(5, harness.ReadIntField(entity, asset, "OutA"));
        Assert.Equal(6, harness.ReadIntField(entity, asset, "OutB"));
    }

    private static Graph BuildGraph(string structFqn, Guid sId, Guid outAId, Guid outBId)
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

        var l5 = Lit(5, out var l5Out);
        var l6 = Lit(6, out var l6Out);

        // Make{A=5,B=6} → Value(struct)
        var mA = P("A", "In", false, intT); var mB = P("B", "In", false, intT); var mVal = P("Value", "Out", false, structT);
        var make = new MakeStructNode { Id = Guid.NewGuid(), StructTypeId = structFqn, Fields = AB() };
        make.Pins.AddRange(new[] { mA, mB, mVal });

        // SetVariable S (struct) ← Make.Value
        var ssIn = P("In", "In", true, new BlueprintTypeRef()); var ssOut = P("Out", "Out", true, new BlueprintTypeRef()); var ssVal = P("Value", "In", false, structT);
        var setS = new SetVariableNode { Id = Guid.NewGuid(), VariableId = sId.ToString() }; setS.Pins.AddRange(new[] { ssIn, ssOut, ssVal });

        // GetVariable S (struct) → data-out "Value"
        var gsVal = P("Value", "Out", false, structT);
        var getS = new GetVariableNode { Id = Guid.NewGuid(), VariableId = sId.ToString() }; getS.Pins.Add(gsVal);

        // Break(Value ← GetVariable S) → A,B
        var bVal = P("Value", "In", false, structT); var bA = P("A", "Out", false, intT); var bB = P("B", "Out", false, intT);
        var brk = new BreakStructNode { Id = Guid.NewGuid(), StructTypeId = structFqn, Fields = AB() };
        brk.Pins.AddRange(new[] { bVal, bA, bB });

        var aIn = P("In", "In", true, new BlueprintTypeRef()); var aOut = P("Out", "Out", true, new BlueprintTypeRef()); var aVal = P("Value", "In", false, intT);
        var setA = new SetVariableNode { Id = Guid.NewGuid(), VariableId = outAId.ToString() }; setA.Pins.AddRange(new[] { aIn, aOut, aVal });
        var bIn = P("In", "In", true, new BlueprintTypeRef()); var bOut = P("Out", "Out", true, new BlueprintTypeRef()); var bValIn = P("Value", "In", false, intT);
        var setB = new SetVariableNode { Id = Guid.NewGuid(), VariableId = outBId.ToString() }; setB.Pins.AddRange(new[] { bIn, bOut, bValIn });
        var rIn = P("In", "In", true, new BlueprintTypeRef()); var ret = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success }; ret.Pins.Add(rIn);

        Link Lk(Node fn, Pin fp, Node tn, Pin tp) => new Link { FromNodeId = fn.Id, FromPinId = fp.Id, ToNodeId = tn.Id, ToPinId = tp.Id };
        var links = new List<Link>
        {
            Lk(entry, eOut, setS, ssIn), Lk(setS, ssOut, setA, aIn), Lk(setA, aOut, setB, bIn), Lk(setB, bOut, ret, rIn), // exec
            Lk(l5, l5Out, make, mA), Lk(l6, l6Out, make, mB),      // Make{5,6}
            Lk(make, mVal, setS, ssVal),                            // S = struct
            Lk(getS, gsVal, brk, bVal),                             // Break(GetVariable S)
            Lk(brk, bA, setA, aVal), Lk(brk, bB, setB, bValIn),     // → OutA/OutB
        };

        return new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = new List<Node> { entry, l5, l6, make, setS, getS, brk, setA, setB, ret },
            Links = links,
            Inputs = new List<ParameterDecl>(), Outputs = new List<ParameterDecl>(),
        };
    }
}
