using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Golden;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐ <c>CE-471</c> — bit/shift operators on the native <see cref="BinaryOpNode"/>
/// (<c>BitAnd/BitOr/BitXor/ShiftLeft/ShiftRight</c>). 📄
/// <c>docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md</c> §7 row 6 ·
/// <c>docs/blueprints/BinaryOp_And_Boolean_Nodes_Design.md</c>.
/// </summary>
public class CE471_BitOperatorTests
{
    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>EventEntry → SetVariable(Out) → Return, fed by <c>BinaryOp(litA, litB)</c> — the
    /// <c>NodeCoverageTests.BuildBinaryOpMinimalAsset</c> shape, parameterised by operator and types.</summary>
    private static BlueprintAsset Build(ArithmeticOperator op, string aType, string aJson, string bType, string bJson)
    {
        var litAValue = DataPin("Value", "Out", aType);
        var litA = new LiteralNode { Id = Guid.NewGuid(), TypeId = aType, ValueJson = aJson };
        litA.Pins.Add(litAValue);
        var litBValue = DataPin("Value", "Out", bType);
        var litB = new LiteralNode { Id = Guid.NewGuid(), TypeId = bType, ValueJson = bJson };
        litB.Pins.Add(litBValue);

        var aPin = DataPin("A", "In", aType);
        var bPin = DataPin("B", "In", bType);
        var rPin = DataPin("Result", "Out", aType);
        var bin = new BinaryOpNode { Id = Guid.NewGuid(), Operator = op };
        bin.Pins.AddRange(new[] { aPin, bPin, rPin });

        var outVarId = Guid.NewGuid();
        var outVar = new VariableDecl { Id = outVarId, Name = "Out", Type = new BlueprintTypeRef { TypeId = aType } };

        var setIn = ExecPin("ExecIn", "In");
        var setOut = ExecPin("ExecOut", "Out");
        var setValue = DataPin("Value", "In", aType);
        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = outVarId.ToString() };
        set.Pins.AddRange(new[] { setIn, setOut, setValue });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, litA, litB, bin, set, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,  ToNodeId = set.Id, ToPinId = setIn.Id },
                new Link { FromNodeId = set.Id,   FromPinId = setOut.Id,    ToNodeId = ret.Id, ToPinId = retIn.Id },
                new Link { FromNodeId = litA.Id,  FromPinId = litAValue.Id, ToNodeId = bin.Id, ToPinId = aPin.Id },
                new Link { FromNodeId = litB.Id,  FromPinId = litBValue.Id, ToNodeId = bin.Id, ToPinId = bPin.Id },
                new Link { FromNodeId = bin.Id,   FromPinId = rPin.Id,      ToNodeId = set.Id, ToPinId = setValue.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = $"BitOp{op}", Dispatch = BlueprintDispatchKind.Instance,
            Variables = { outVar }, Graphs = { graph },
        };
    }

    private static string Diags(CompileResult r) => string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    [Theory]
    [InlineData(ArithmeticOperator.BitAnd,     "System.Int32",  "12", "System.Int32", "10", "__t", " & ")]
    [InlineData(ArithmeticOperator.BitOr,      "System.UInt32", "12", "System.UInt32", "3", "__t", " | ")]
    [InlineData(ArithmeticOperator.BitXor,     "System.Int64",  "12", "System.Int64", "-1", "__t", " ^ ")]
    [InlineData(ArithmeticOperator.ShiftLeft,  "System.Int32",  "1",  "System.Int32", "4",  "(int)__t", " << ")]
    [InlineData(ArithmeticOperator.ShiftRight, "System.UInt64", "256", "System.Int64", "4", "(int)__t", " >> ")]
    public void BitOperator_EmitsTheInfix_AndSurvivesTheRealCSharpCompiler(
        ArithmeticOperator op, string aType, string aJson, string bType, string bJson, string rightPrefix, string infix)
    {
        var asset = Build(op, aType, aJson, bType, bJson);
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded, Diags(result));
        Assert.Contains(infix + rightPrefix, result.GeneratedSource!);

        // 🔴 the real C# compiler — a shift with a long count only compiles because of the (int) cast.
        using var fixture = new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.CompileAndLoad(asset, GoldenCorpus.Options());
    }

    [Theory]
    [CoversDiagnosticCode("BP1678")]
    [InlineData(ArithmeticOperator.BitAnd,    "System.Single",  "1.5",  "System.Single", "2.5")]
    [InlineData(ArithmeticOperator.BitXor,    "System.Double",  "1.5",  "System.Double", "2.5")]
    [InlineData(ArithmeticOperator.ShiftLeft, "System.Boolean", "true", "System.Int32",  "1")]
    public void BitOperator_OnANonIntegerOperand_IsRefusedByTheCompilerNotByRoslyn(
        ArithmeticOperator op, string aType, string aJson, string bType, string bJson)
    {
        var result = new BlueprintCompiler().Compile(Build(op, aType, aJson, bType, bJson), GoldenCorpus.Options());
        Assert.Contains(result.Diagnostics, d => d.Code == "BP1678");
    }

    [Fact]
    public void BoolBitAnd_IsAllowed()
    {
        var result = new BlueprintCompiler().Compile(
            Build(ArithmeticOperator.BitAnd, "System.Boolean", "true", "System.Boolean", "false"), GoldenCorpus.Options());
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "BP1678");
    }
}
