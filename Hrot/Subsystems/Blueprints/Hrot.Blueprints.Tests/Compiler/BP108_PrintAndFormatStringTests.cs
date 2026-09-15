using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Format;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-108 — <c>Print String</c> / <c>Format String</c> (Unreal's <c>Print String</c> + <c>Format
/// Text</c>). Covers the shared parser (<see cref="BlueprintFormatString"/>), pin derivation
/// (<see cref="BuiltInNodeRegistry.GetStaticPins"/>), the Stage 2 malformed-format diagnostic
/// (BP2072), and — most importantly — a real end-to-end Roslyn compile of both node kinds,
/// including the documented composition (a Format String result wired into a Print String
/// placeholder).
/// </summary>
public sealed class BP108_PrintAndFormatStringTests
{
    // ════════════════════════════════════════════════════════════════════
    // 1. Parser (BlueprintFormatString)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_PlaceholdersAppearInFirstAppearanceOrder()
    {
        var result = BlueprintFormatString.Parse("squad={Squad} threat={Threat} squad again={Squad}");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "Squad", "Threat" }, result.Names);
    }

    [Fact]
    public void Parse_RepeatedPlaceholder_YieldsOneName()
    {
        var result = BlueprintFormatString.Parse("{X} and {X} and {X} again");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "X" }, result.Names);
    }

    [Fact]
    public void Parse_EscapedBraces_AreLiteral_NotPlaceholders()
    {
        var result = BlueprintFormatString.Parse("{{literal}} {Name} }} trailing {{");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "Name" }, result.Names);
    }

    [Fact]
    public void Parse_EmptyOrNullFormat_IsValid_WithNoNames()
    {
        Assert.True(BlueprintFormatString.Parse("").IsValid);
        Assert.Empty(BlueprintFormatString.Parse("").Names);
        Assert.True(BlueprintFormatString.Parse(null).IsValid);
    }

    [Theory]
    [InlineData("unclosed {brace")]
    [InlineData("empty {} placeholder")]
    [InlineData("invalid {1StartsWithDigit} name")]
    [InlineData("stray } with no opener")]
    public void Parse_MalformedFormats_AreInvalid_WithNonEmptyError(string format)
    {
        var result = BlueprintFormatString.Parse(format);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Empty(result.Names);
    }

    // ════════════════════════════════════════════════════════════════════
    // 2. Pin derivation (BuiltInNodeRegistry.GetStaticPins)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PrintString_DerivesExecPlusDataInPins_InFirstAppearanceOrder()
    {
        var node = new PrintStringNode { Id = Guid.NewGuid(), Format = "threat={Threat} squad={Squad}" };

        var pins = BuiltInNodeRegistry.Instance.GetStaticPins(node);

        Assert.Equal(4, pins.Count);
        Assert.Equal("In",  pins[0].Name); Assert.True(pins[0].IsExec); Assert.Equal("In",  pins[0].Direction);
        Assert.Equal("Out", pins[1].Name); Assert.True(pins[1].IsExec); Assert.Equal("Out", pins[1].Direction);
        Assert.Equal("Threat", pins[2].Name); Assert.False(pins[2].IsExec); Assert.Equal("In", pins[2].Direction);
        Assert.Equal("Squad",  pins[3].Name); Assert.False(pins[3].IsExec); Assert.Equal("In", pins[3].Direction);
    }

    [Fact]
    public void PrintString_RepeatedPlaceholder_YieldsOneDataInPin()
    {
        var node = new PrintStringNode { Id = Guid.NewGuid(), Format = "{X} then {X} again" };

        var pins = BuiltInNodeRegistry.Instance.GetStaticPins(node);

        // exec In + exec Out + one data-in "X".
        Assert.Equal(3, pins.Count);
        Assert.Equal("X", pins[2].Name);
    }

    [Fact]
    public void FormatString_HasNoExecPins_PlusResultTypedFromResultTypeId()
    {
        var node = new FormatStringNode
        {
            Id           = Guid.NewGuid(),
            Format       = "n={N}",
            ResultTypeId = "Fdp.Core.FixedString64",
        };

        var pins = BuiltInNodeRegistry.Instance.GetStaticPins(node);

        Assert.DoesNotContain(pins, p => p.IsExec);
        Assert.Equal(2, pins.Count); // "N" data-in + "Result" data-out.
        Assert.Equal("N", pins[0].Name);
        Assert.Equal("In", pins[0].Direction);
        Assert.Equal("Result", pins[1].Name);
        Assert.Equal("Out", pins[1].Direction);
        Assert.Equal("Fdp.Core.FixedString64", pins[1].TypeId);
    }

    [Fact]
    public void ArgTypes_TypeTheDerivedPins_AndAMissingEntryFallsBackToSystemObject()
    {
        var node = new PrintStringNode
        {
            Id       = Guid.NewGuid(),
            Format   = "{A} {B}",
            ArgTypes = new Dictionary<string, string> { ["A"] = "System.Single" },
            // "B" deliberately has no ArgTypes entry.
        };

        var pins = BuiltInNodeRegistry.Instance.GetStaticPins(node);

        Assert.Equal("System.Single", pins.Single(p => p.Name == "A").TypeId);
        Assert.Equal("System.Object", pins.Single(p => p.Name == "B").TypeId);
    }

    // ════════════════════════════════════════════════════════════════════
    // 3. Stage 2 — malformed Format emits BP2072
    // ════════════════════════════════════════════════════════════════════

    private static CompileOptions DefaultOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    [Fact]
    [CoversDiagnosticCode("BP2072")]
    public void Stage2_MalformedPrintStringFormat_EmitsBP2072Error()
    {
        var asset = BlueprintAssetBuilder
            .Instance("PrintStringMalformedTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new PrintStringNode
        {
            Id     = Guid.NewGuid(),
            Format = "unclosed {brace",
        });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2072 && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [CoversDiagnosticCode("BP2072")]
    public void Stage2_MalformedFormatStringFormat_EmitsBP2072Error()
    {
        var asset = BlueprintAssetBuilder
            .Instance("FormatStringMalformedTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new FormatStringNode
        {
            Id     = Guid.NewGuid(),
            Format = "stray } here",
        });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2072 && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Stage2_WellFormedFormats_EmitNoBP2072()
    {
        var asset = BlueprintAssetBuilder
            .Instance("PrintStringWellFormedTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new PrintStringNode { Id = Guid.NewGuid(), Format = "threat={Threat}" });
        asset.Graphs[0].Nodes.Add(new FormatStringNode { Id = Guid.NewGuid(), Format = "n={N}" });

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2072);
    }

    // ════════════════════════════════════════════════════════════════════
    // 4. End-to-end — real Roslyn compile (the tests that actually prove the
    //    emitted stackalloc/TryWrite/interpolated-string code is valid C#).
    // ════════════════════════════════════════════════════════════════════

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>EventEntry -&gt; PrintString(Format="threat={Threat}") -&gt; Return, fed by a Literal.</summary>
    private static BlueprintAsset BuildPrintStringOnlyAsset()
    {
        var litOut = DataPin("Value", "Out", "System.Single");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single", ValueJson = "1.5" };
        lit.Pins.Add(litOut);

        var printIn      = ExecPin("In",  "In");
        var printOut     = ExecPin("Out", "Out");
        var printThreat  = DataPin("Threat", "In", "System.Single");
        var print = new PrintStringNode
        {
            Id     = Guid.NewGuid(),
            Format = "threat={Threat}",
            Level  = BlueprintLogLevel.Info,
        };
        print.Pins.AddRange(new[] { printIn, printOut, printThreat });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, lit, print, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id,   ToNodeId = print.Id, ToPinId = printIn.Id },
                new Link { FromNodeId = print.Id, FromPinId = printOut.Id,   ToNodeId = ret.Id,   ToPinId = retIn.Id },
                new Link { FromNodeId = lit.Id,   FromPinId = litOut.Id,     ToNodeId = print.Id, ToPinId = printThreat.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "PrintStringOnly",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    /// <summary>
    /// EventEntry -&gt; PrintString(Format="hello {Msg}") -&gt; Return, where "Msg" is fed by a
    /// FormatString(Format="n={N}") pure node — the documented BP-108 composition (a FixedString
    /// result is a legal Print String argument).
    /// </summary>
    private static BlueprintAsset BuildFormatStringIntoPrintStringAsset()
    {
        var litOut = DataPin("Value", "Out", "System.Int32");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "3" };
        lit.Pins.Add(litOut);

        var fmtN   = DataPin("N",      "In",  "System.Int32");
        var fmtOut = DataPin("Result", "Out", "Fdp.Core.FixedString32");
        var fmt = new FormatStringNode
        {
            Id           = Guid.NewGuid(),
            Format       = "n={N}",
            ResultTypeId = "Fdp.Core.FixedString32",
        };
        fmt.Pins.AddRange(new[] { fmtN, fmtOut });

        var printIn  = ExecPin("In",  "In");
        var printOut = ExecPin("Out", "Out");
        var printMsg = DataPin("Msg", "In", "Fdp.Core.FixedString32");
        var print = new PrintStringNode
        {
            Id     = Guid.NewGuid(),
            Format = "hello {Msg}",
            Level  = BlueprintLogLevel.Warn,
        };
        print.Pins.AddRange(new[] { printIn, printOut, printMsg });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, lit, fmt, print, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = print.Id, ToPinId = printIn.Id },
                new Link { FromNodeId = print.Id, FromPinId = printOut.Id, ToNodeId = ret.Id,   ToPinId = retIn.Id },
                new Link { FromNodeId = lit.Id,   FromPinId = litOut.Id,   ToNodeId = fmt.Id,    ToPinId = fmtN.Id },
                new Link { FromNodeId = fmt.Id,   FromPinId = fmtOut.Id,   ToNodeId = print.Id,  ToPinId = printMsg.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "FormatStringIntoPrintString",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    [Fact]
    public void PrintString_CompilesAndLoads_ThroughRealRoslyn()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = BuildPrintStringOnlyAsset();

        // ⭐ The real proof: a real Roslyn compile of the generated C#. CompileAndLoad throws
        // (with the Roslyn diagnostics attached) if the generated source is not valid C# —
        // never assert on CompileResult.Succeeded alone for this.
        var ex = Record.Exception(() => fixture.CompileAndLoad(asset));

        Assert.Null(ex);
    }

    [Fact]
    public void FormatStringIntoPrintString_CompilesAndLoads_ThroughRealRoslyn()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = BuildFormatStringIntoPrintStringAsset();

        // ⭐ Proves the emitted `stackalloc char[...]` + `TryWrite` + `ReadOnlySpan<char>`
        // FixedString constructor for Format String, AND the level-guarded log call for Print
        // String consuming its Result, are both valid C# together in one real compile.
        var ex = Record.Exception(() => fixture.CompileAndLoad(asset));

        Assert.Null(ex);
    }

    // ════════════════════════════════════════════════════════════════════
    // 5. Emitted-source shape (verbatim generated C# assertions)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void PrintString_EmitsLevelProbeGuard()
    {
        var result = new BlueprintCompiler().Compile(BuildPrintStringOnlyAsset(), DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        // The level probe is the whole performance story: the call is guarded by the matching
        // Is<Level>Enabled probe so a disabled level never constructs the interpolated string.
        Assert.Contains("IsInfoEnabled", src);
        Assert.Contains("BlueprintLog.Info(", src);
    }

    [Fact]
    public void FormatString_EmitsStackallocAndTryWrite()
    {
        var result = new BlueprintCompiler().Compile(BuildFormatStringIntoPrintStringAsset(), DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        // Zero-alloc path: format straight into a stack buffer, never materialize a managed string.
        Assert.Contains("stackalloc char[", src);
        Assert.Contains(".TryWrite(", src);
        Assert.Contains("Fdp.Core.FixedString32(", src);
    }
}
