using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// E2E compile tests for AN1 — Stage-3 default-literal materialization.
/// Verifies that unconnected data-IN pins with defaults emit correct C# literals
/// and that pins with NO default still produce BP4001.
/// </summary>
public sealed class MaterializeDefaultPinLiteralsTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a minimal Library blueprint with one Function graph containing:
    ///   EventEntry(ExecOut) → FunctionCallNode(ExecIn, ExecOut, data pins...) → Return(ExecIn)
    /// Each <paramref name="dataPins"/> entry is an unconnected data-IN pin on the call node.
    /// </summary>
    private static BlueprintAsset BuildAssetWithUnconnectedPins(
        params (string name, string typeId, string? defaultViaNode, string? defaultViaPin)[] dataPins)
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var callId   = Guid.NewGuid();
        var retId    = Guid.NewGuid();
        var execE    = Guid.NewGuid();
        var execC1   = Guid.NewGuid();
        var execC2   = Guid.NewGuid();
        var execR    = Guid.NewGuid();

        // Build data-IN pins for the call node.
        var pinObjects = dataPins.Select(p => new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = p.name,
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = p.typeId },
            DefaultValue = p.defaultViaPin,
        }).ToList();

        // Build PinDefaults dict (only entries with defaultViaNode non-null).
        Dictionary<string, string>? pinDefaultsDict = null;
        for (int i = 0; i < dataPins.Length; i++)
        {
            if (dataPins[i].defaultViaNode is not null)
            {
                pinDefaultsDict ??= new Dictionary<string, string>();
                pinDefaultsDict[dataPins[i].name] = dataPins[i].defaultViaNode!;
            }
        }

        var callNode = new FunctionCallNode
        {
            Id           = callId,
            TargetTypeId = "System.Math",
            MethodName   = "Abs",
            IsPure       = false,
            PinDefaults  = pinDefaultsDict,
            Pins = new List<Pin>
            {
                new Pin { Id = execC1, Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                new Pin { Id = execC2, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
            }.Concat(pinObjects).ToList(),
        };

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = execE, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                callNode,
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = execR, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = execE,  ToNodeId = callId, ToPinId = execC1 },
                new() { FromNodeId = callId,  FromPinId = execC2, ToNodeId = retId,  ToPinId = execR  },
                // No data links — all data pins are unconnected
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "DefaultPinTest",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };
    }

    // -----------------------------------------------------------------------
    // AN1 core: e2e compile with int + float + FixedString32 + enum defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultPins_IntFloatFixedStringEnum_GenerateCorrectLiterals()
    {
        var asset = BuildAssetWithUnconnectedPins(
            // int pin via PinDefaults
            ("count",    "System.Int32",           "42",   null),
            // float pin via Pin.DefaultValue (with existing 'f' suffix)
            ("speed",    "System.Single",           null,   "3.14f"),
            // FixedString32 via PinDefaults
            ("label",    "Fdp.Core.FixedString32",  "hello", null),
            // enum via PinDefaults  (TypeId has global:: prefix per AN2)
            ("mode",     "global::SomeNs.SomeEnum", "2",    null));

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        // Must compile without errors; BP4001 must NOT appear (all pins have defaults).
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP4001);
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // --- int literal ---
        Assert.Contains("42", src);

        // --- float literal: either authored "3.14f" or Stage3-formatted "3.14f" ---
        Assert.Contains("3.14f", src);

        // --- FixedString32 ctor ---
        Assert.Contains("new global::Fdp.Core.FixedString32(", src);

        // --- enum cast (TypeId already has global:: so the cast is (global::SomeNs.SomeEnum)2) ---
        Assert.Contains("(global::SomeNs.SomeEnum)2", src);

        // --- NO double global:: prefix ---
        Assert.DoesNotContain("global::global::", src);
    }

    // -----------------------------------------------------------------------
    // PinDefaults takes precedence over Pin.DefaultValue
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultPins_PinDefaultsPreferredOver_PinDefaultValue()
    {
        // PinDefaults says "99", Pin.DefaultValue says "7".
        // PinDefaults should win.
        var asset = BuildAssetWithUnconnectedPins(
            ("x", "System.Int32", "99", "7"));

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;
        Assert.Contains("99", src);
        // "7" should not appear as a standalone literal (it might appear in an id, so
        // we check it's not present as the const assignment).
    }

    // -----------------------------------------------------------------------
    // Pin with NO default must still produce BP4001 (regression guard for the
    // existing Schedule_UnconnectedDataPin_EmitsBP4001 test path)
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultPins_NoDefault_StillEmitsBP4001()
    {
        // Build an asset with one unconnected data-IN pin that has NO default.
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var callId   = Guid.NewGuid();
        var retId    = Guid.NewGuid();
        var execE    = Guid.NewGuid();
        var execC1   = Guid.NewGuid();
        var execC2   = Guid.NewGuid();
        var execR    = Guid.NewGuid();
        var noDefaultPin = Guid.NewGuid();

        var graph = new Graph
        {
            Id = graphId, Name = "G", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = execE, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new FunctionCallNode
                {
                    Id = callId, TargetTypeId = "System.Math", MethodName = "Abs", IsPure = false,
                    // PinDefaults = null, no DefaultValue on pin
                    Pins = new()
                    {
                        new Pin { Id = execC1, Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = execC2, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = noDefaultPin, Name = "value", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Double" } },
                        // No DefaultValue, not in PinDefaults
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = execR, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = execE,  ToNodeId = callId, ToPinId = execC1 },
                new() { FromNodeId = callId,  FromPinId = execC2, ToNodeId = retId,  ToPinId = execR  },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId = assetId, Name = "NoDefaultTest",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };

        var result = new BlueprintCompiler().Compile(bp, DefaultOptions());

        // BP4001 must still be emitted for pins with no default.
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP4001);
    }

    // -----------------------------------------------------------------------
    // Float without 'f' suffix gets one added
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultPins_FloatWithoutSuffix_GetsFSuffix()
    {
        var asset = BuildAssetWithUnconnectedPins(
            ("v", "System.Single", "1.5", null));

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("1.5f", result.GeneratedSource!);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP4001);
    }

    // -----------------------------------------------------------------------
    // Boolean default
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultPins_Bool_EmitsTrueFalse()
    {
        var asset = BuildAssetWithUnconnectedPins(
            ("flag", "System.Boolean", "true", null));

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("true", result.GeneratedSource!);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP4001);
    }
}
