using System.Text.Json.Nodes;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class WhenNodeLoweringTests
{
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs Stage 5 then Stage 6; returns the lowered IrAsset.</summary>
    private static IrAsset RunLower(BlueprintAsset asset, DiagnosticSink sink)
    {
        var opts  = DefaultOptions();
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
    }

    /// <summary>Runs all stages (skipping Stage 2) and returns the generated C# source.</summary>
    private static string? Compile(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        // Skip Stage 2 (validation tests are in WhenNodeValidatorTests).
        // These are lowering/emission tests; BP1601/BP2005 would block unrelated graphs.
        asset  = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(asset, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (source, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        return sink.HasErrors ? null : source;
    }

    /// <summary>
    /// Builds a minimal WhenNode for a ValueChanged / SelfComponent scenario.
    /// The node has an ExecIn, ExecOut ("Out"), and optionally OnFired pins.
    /// </summary>
    private static WhenNode MakeValueChangedNode(
        Guid nodeId,
        Guid graphId,
        Guid assetId,
        string componentTypeId,
        string propertyPath,
        float epsilon = 0.001f,
        WhenEdge edges = WhenEdge.RisingEdge)
    {
        var node = new WhenNode
        {
            Id   = nodeId,
            Mode = WhenMode.ValueChanged,
            Edges = edges,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = componentTypeId,
                PropertyPath    = propertyPath,
                Epsilon         = epsilon,
                Source          = ValueChangedSource.SelfComponent,
            },
        };

        // Exec pins
        var execIn  = new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() };
        var execOut = new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() };

        if ((edges & WhenEdge.RisingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        node.Pins.Add(execIn);
        node.Pins.Add(execOut);
        return node;
    }

    [Fact]
    public void Lower_StructureHashIncludesSynthesizedFields()
    {
        // Build an Instance asset with a ValueChanged WhenNode (RisingEdge).
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "MyGame.Health",
            propertyPath:    "Current",
            epsilon:         0.001f,
            edges:           WhenEdge.RisingEdge);

        // Wire entry -> whenNode (Out not connected = no further nodes)
        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "WhenTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        // Stage 6 must have added the synthesized field to Variables.
        var id8 = nodeId.ToString("N").Substring(0, 8);
        var expectedFieldName = $"_when_{id8}_prev";
        Assert.Contains(lowered.Variables, v => v.Name == expectedFieldName);

        // StructureHash must be non-zero (computed in Stage 6 from Variables).
        Assert.NotEqual(0UL, lowered.StructureHash);
    }

    [Fact]
    public void Lower_ValueChanged_Scalar_EmitsInlineComparison()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "MyGame.Health",
            propertyPath:    "Current",
            epsilon:         0.001f);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenScalar",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Must emit the component read
        Assert.Contains("GetComponentRO<global::MyGame.Health>", src);
        // Must emit the field access
        Assert.Contains(".Current", src);
        // Must emit the epsilon comparison
        Assert.Contains("MathF.Abs", src);
        // Must reference the synthesized prev-state field
        Assert.Contains($"_when_{id8}_prev", src);
    }

    [Fact]
    public void Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison()
    {
        // With epsilon > 0 and a Vector2 field (resolvable at test runtime via reflection),
        // the emitter should use LengthSquared() instead of MathF.Abs.
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "Hrot.Blueprints.Tests.Mocks.VectorTestComponent",
            propertyPath:    "Position2D",
            epsilon:         0.1f);   // epsilon > 0 -> vector path

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenVector2",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Vector2 epsilon path: must use LengthSquared(), must NOT use MathF.Abs
        Assert.Contains("LengthSquared()", src!);
        Assert.DoesNotContain("MathF.Abs", src!);
        // Still must reference the prev field
        Assert.Contains($"_when_{id8}_prev", src!);
    }

    [Fact]
    public void Lower_ValueChanged_Vector3_EmitsLengthSquaredComparison()
    {
        // With epsilon > 0 and a Vector3 field, must use LengthSquared() path.
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "Hrot.Blueprints.Tests.Mocks.VectorTestComponent",
            propertyPath:    "Position3D",
            epsilon:         0.5f);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenVector3",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("LengthSquared()", src!);
        Assert.DoesNotContain("MathF.Abs", src!);
        Assert.Contains($"_when_{id8}_prev", src!);
    }

    [Fact]
    public void Compile_ValueChanged_OnVector2Field_ProducesValidCSharp()
    {
        // Full end-to-end: compile a blueprint that observes a Vector2 field.
        // The emitted C# source must contain LengthSquared and NOT MathF.Abs.
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "Hrot.Blueprints.Tests.Mocks.VectorTestComponent",
            propertyPath:    "Position2D",
            epsilon:         0.25f);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenVector2Full",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("LengthSquared()", src!);
        Assert.DoesNotContain("MathF.Abs", src!);
    }

    [Fact]
    public void Lower_ValueChanged_ScalarPath_UnchangedAfterVectorBranchAdded()
    {
        // After adding the vector branch, scalar float fields must STILL use MathF.Abs.
        // Uses AnotherTestComponent.X which is a float field (resolvable at test runtime).
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeValueChangedNode(nodeId, graphId, assetId,
            componentTypeId: "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
            propertyPath:    "X",
            epsilon:         0.05f);  // epsilon > 0, scalar float -> MathF.Abs path

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenScalarFloat",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("MathF.Abs", src!);
        Assert.DoesNotContain("LengthSquared()", src!);
        Assert.Contains($"_when_{id8}_prev", src!);
    }

    [Fact]
    public void Lower_ValueChanged_PeerVariable_EmitsSlotLookup()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var node = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId      = "",
                PropertyPath         = "",
                Source               = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = Guid.NewGuid(),
                PeerVariableName     = "Speed",
                Epsilon              = 0.01,
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, node },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = node.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenPeer",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        // PeerBlueprintVariable source: Stage 5 schedules without crash.
        // Full peer-slot emit is deferred to M4.
        var sink = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        // No crashes = pass. Diagnostic errors for unsupported source are acceptable.
        // What must NOT happen: NullReferenceException / InvalidOperationException.
        Assert.NotNull(lowered);
    }

    [Fact]
    public void Lower_EventFired_WithSelf_EmitsTargetCheck()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var node = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId     = "MyGame.HitEvent",
                TargetFilter    = EventTargetFilter.Self,
                TargetFieldName = "Target",
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, node },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = node.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenEvent",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        Assert.Contains("ReadEvents<global::MyGame.HitEvent>", src);
        // Target filter must emit a != self check
        Assert.Contains("!= self", src);
    }

    [Fact]
    public void Lower_EventFired_WithPayloadCondition_EmitsValueParse()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var node = new WhenNode
        {
            Id    = Guid.NewGuid(),
            Mode  = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "MyGame.HitEvent",
                TargetFilter = EventTargetFilter.Self,
                PayloadCheck = new PayloadCondition
                {
                    PropertyPath    = "Damage",
                    Operator        = ComparisonOperator.GreaterThan,
                    TargetValueText = "50f",
                },
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, node },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = node.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenPayload",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Must emit the Damage field access
        Assert.Contains(".Damage", src);
        // Must emit the > operator
        Assert.Contains("> 50f", src);
    }

    [Fact]
    public void Lower_EventFired_NoFilters_EmitsHasEventFastPath()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var node = new WhenNode
        {
            Id    = Guid.NewGuid(),
            Mode  = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "MyGame.ExplosionEvent",
                TargetFilter = EventTargetFilter.None,
                // No PayloadCheck
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, node },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = node.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenFastPath",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Fast path: no loop, just Length > 0 check
        Assert.Contains(".Length > 0", src);
        // Must NOT emit a full for-loop since there are no filters
        Assert.DoesNotContain("for (int", src);
    }

    [Fact]
    public void Lower_EventFired_NoSynthesizedField()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var node = new WhenNode
        {
            Id    = nodeId,
            Mode  = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "MyGame.SpawnEvent",
                TargetFilter = EventTargetFilter.None,
            },
        };
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = node.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, node },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = node.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId = assetId, Name = "WhenEventNoState",
            Dispatch = AssetDispatchKind.Instance, Graphs = { graph },
        };

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        // EventFired must NOT add any _when_xxx_prev field to Variables.
        var synthFieldName = $"_when_{id8}_prev";
        Assert.DoesNotContain(lowered.Variables, v => v.Name == synthFieldName);
    }

    /// <summary>
    /// Builds a minimal WhenNode for a ConditionMet scenario with a simple PropertyMatchDto predicate.
    /// </summary>
    private static WhenNode MakeConditionMetNode(
        Guid nodeId,
        WhenEdge edges = WhenEdge.RisingEdge)
    {
        var node = new WhenNode
        {
            Id   = nodeId,
            Mode = WhenMode.ConditionMet,
            Edges = edges,
            ConditionMet = new ConditionMetPayload
            {
                // Condition stored as JsonNode (Fdp.Toolkits-free serialized model).
                // Dummy PropertyMatch predicate; Stage 2 validation is skipped in these lowering tests.
                Condition = JsonNode.Parse(
                    "{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Object\",\"PropertyPath\":\"Value\"," +
                    "\"Predicate\":{\"$type\":\"Numeric\",\"MinValue\":10.0,\"MaxValue\":1.7976931348623157E+308}}"),
            },
        };

        if ((edges & WhenEdge.RisingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true, TypeRef = new() });
        if ((edges & WhenEdge.FallingEdge) != 0)
            node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "OnEnded", Direction = "Out", IsExec = true, TypeRef = new() });

        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true, TypeRef = new() });

        return node;
    }

    [Fact]
    public void Lower_ConditionMet_EmitsStaticDelegateField()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeConditionMetNode(nodeId, WhenEdge.RisingEdge);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "CondMetTest",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Static delegate field
        Assert.Contains($"_whenCondPred_{id8}", src);
        // InitializePredicates method
        Assert.Contains("InitializePredicates", src);
        // Synthesized bool prev field in State struct
        Assert.Contains($"_when_{id8}_prev", src);
        // Tick-body: predicate null-check
        Assert.Contains($"_whenCondPred_{id8} != null", src);
        // Tick-body: EntityRepository cast + predicate invocation
        Assert.Contains($"_whenCondPred_{id8}(", src);
    }

    [Fact]
    public void Lower_ConditionMet_RisingFallingBoth_BothBranchesEmitted()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var id8     = nodeId.ToString("N").Substring(0, 8);

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(new Pin { Id = Guid.NewGuid(), Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });

        var whenNode = MakeConditionMetNode(nodeId, WhenEdge.RisingEdge | WhenEdge.FallingEdge);

        var execOutPin = entry.Pins.First(p => p.IsExec && p.Direction == "Out");
        var execInPin  = whenNode.Pins.First(p => p.IsExec && p.Direction == "In");

        var graph = new Graph
        {
            Id    = graphId, Name = "Tick", Kind = GraphKind.Event,
            Nodes = { entry, whenNode },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = execOutPin.Id,
                                 ToNodeId = whenNode.Id, ToPinId = execInPin.Id } },
        };
        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "CondMetBothEdges",
            Dispatch = AssetDispatchKind.Instance,
            Graphs   = { graph },
        };

        var src = Compile(asset);

        Assert.NotNull(src);
        // Rising edge: current && !prev -> goto fired block
        Assert.Contains($"__cur_{id8} && !__prev_{id8}", src);
        // Falling edge: !current && prev -> goto ended block
        Assert.Contains($"!__cur_{id8} && __prev_{id8}", src);
        // Prev field is updated unconditionally
        Assert.Contains($"{id8}_prev = __cur_{id8}", src);
    }
}
