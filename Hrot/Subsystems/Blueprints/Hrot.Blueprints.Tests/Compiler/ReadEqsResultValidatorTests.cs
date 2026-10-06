using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class ReadEqsResultValidatorTests
{
    // ---- helpers --------------------------------------------------------

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> Validate(
        BlueprintAsset asset,
        CompileOptions? opts = null)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts ?? DefaultOptions()));
        return sink.All;
    }

    // ---- BP2020: unsupported dispatch ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2020")]
    public void Validate_LibraryDispatch_BP2020()
    {
        var asset = BlueprintAssetBuilder
            .Library("LibTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new ReadEqsResultNode
        {
            Id = Guid.NewGuid(),
            SensorVariableName = "MySensor",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2020);
    }

    [Fact]
    [CoversDiagnosticCode("BP2020")]
    public void Validate_AiPrimitiveDispatch_BP2020()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("AiTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new ReadEqsResultNode
        {
            Id = Guid.NewGuid(),
            SensorVariableName = "MySensor",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2020);
    }

    // ---- BP2021: sensor variable not declared --------------------------

    [Fact]
    [CoversDiagnosticCode("BP2021")]
    public void Validate_SensorVariableNotDeclared_BP2021()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new ReadEqsResultNode
        {
            Id = Guid.NewGuid(),
            SensorVariableName = "UndeclaredSensor",   // not in asset.Variables
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2021);
    }

    // ⭐ CE-3054 D — a unit sensor kind needs no variable; a value that is not a sensor kind is refused.
    [Fact]
    [CoversDiagnosticCode("BP2021")]
    public void CE3054_Validate_UnitSensorKind_NoVariableNeeded_BadKindRefused()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        var node = new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "", UnitSensorKind = 1 };
        asset.Graphs[0].Nodes.Add(node);
        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP2021);

        node.UnitSensorKind = 3;   // not one SensorModality bit
        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP2021);
    }

    /// <summary>🔴 <c>CE-3078</c> H3 (§10.4 guard) — the RANKED read refuses a kind of another result family (16 DangerArea)
    /// at compile time, naming the node to use instead; it can never sit at <c>IsReady = false</c> for ever.</summary>
    [Fact]
    [CoversDiagnosticCode("BP2021")]
    public void CE3078_Validate_ADangerAreaKind_OnTheRankedRead_IsRefused()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new ReadEqsResultNode { Id = Guid.NewGuid(), SensorVariableName = "", UnitSensorKind = 16 });
        var d = Assert.Single(Validate(asset), x => x.Code == DiagnosticCodes.BP2021);
        Assert.Contains("ReadSensorResult", d.Message);
    }

    // ---- Happy path: valid Instance ReadEqsResultNode ------------------

    [Fact]
    public void Validate_ValidInstance_NoErrors()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        // Declare matching sensor variable
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "MySensor",
            Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
        });
        asset.Graphs[0].Nodes.Add(new ReadEqsResultNode
        {
            Id = Guid.NewGuid(),
            SensorVariableName = "MySensor",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.IsError);
    }
}
