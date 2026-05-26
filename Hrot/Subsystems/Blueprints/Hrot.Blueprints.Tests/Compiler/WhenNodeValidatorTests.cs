using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class WhenNodeValidatorTests
{
    // ---- helpers --------------------------------------------------------

    private static CompileOptions DefaultOptions(
        IReadOnlyList<BlueprintSignature>? siblings = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Builds an Instance asset with a Function graph containing an EventEntryNode,
    /// appends <paramref name="node"/> to that graph, then runs Stage 2 validation.
    /// </summary>
    private static IReadOnlyList<Diagnostic> ValidateInstance(
        WhenNode node,
        CompileOptions? opts = null,
        Action<BlueprintAsset>? configure = null)
    {
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        configure?.Invoke(asset);

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts ?? DefaultOptions()));
        return sink.All;
    }

    // ---- BP2001: unsupported dispatch -----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2001")]
    public void Validate_LibraryDispatch_BP2001()
    {
        var asset = BlueprintAssetBuilder
            .Library("LibTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(MakeValidValueChangedNode());

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP2001);
    }

    [Fact]
    [CoversDiagnosticCode("BP2001")]
    public void Validate_AiPrimitiveDispatch_BP2001()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("AiTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(MakeValidValueChangedNode());

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP2001);
    }

    // ---- BP2002: missing required payload ------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2002")]
    public void Validate_MissingPayload_ValueChanged_BP2002()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            // ValueChanged intentionally null
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2002);
    }

    [Fact]
    [CoversDiagnosticCode("BP2002")]
    public void Validate_MissingPayload_EventFired_BP2002()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            // EventFired intentionally null
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2002);
    }

    [Fact]
    [CoversDiagnosticCode("BP2002")]
    public void Validate_MissingPayload_ConditionMet_BP2002()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            // ConditionMet intentionally null
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2002);
    }

    [Fact]
    [CoversDiagnosticCode("BP2002")]
    public void Validate_MissingPayload_EqsResult_BP2002()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            // EqsResult intentionally null
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2002);
    }

    // ---- BP2003: invalid property path ---------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2003")]
    public void Validate_InvalidPropertyPath_BP2003()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "",   // empty -- invalid
                PropertyPath    = "SomeField",
                Source          = ValueChangedSource.SelfComponent,
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2003);
    }

    // ---- BP2004: peer BP variable not declared -------------------------

    [Fact]
    [CoversDiagnosticCode("BP2004")]
    public void Validate_PeerVariableNotDeclared_BP2004()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId      = "SomeComponent",
                PropertyPath         = "SomeField",
                Source               = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = Guid.NewGuid(), // not in sibling signatures
            },
        };
        var diags = ValidateInstance(node, DefaultOptions(siblings: Array.Empty<BlueprintSignature>()));
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2004);
    }

    // ---- BP2005: event type not in catalog -----------------------------

    [Fact]
    [CoversDiagnosticCode("BP2005")]
    public void Validate_EventTypeNotInCatalog_BP2005()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "UnknownEvent_DoesNotExistInCatalog",
                TargetFilter = EventTargetFilter.None,
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2005);
    }

    // ---- BP2006: Self filter without target field ----------------------

    [Fact]
    [CoversDiagnosticCode("BP2006")]
    public void Validate_SelfFilterWithoutTargetField_BP2006()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "HitEvent", // valid catalog entry
                TargetFilter = EventTargetFilter.Self,
                TargetFieldName = null,    // missing -- triggers BP2006
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2006);
    }

    // ---- BP2007: payload condition invalid -----------------------------

    [Fact]
    [CoversDiagnosticCode("BP2007")]
    public void Validate_PayloadConditionInvalid_BP2007()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.RisingEdge,
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "HitEvent", // valid
                TargetFilter = EventTargetFilter.None,
                PayloadCheck = new PayloadCondition
                {
                    PropertyPath    = "",  // empty -- invalid
                    TargetValueText = "42",
                },
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2007);
    }

    // ---- BP2008: predicate tree null or empty --------------------------

    [Fact]
    [CoversDiagnosticCode("BP2008")]
    public void Validate_ConditionNull_BP2008()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = null,   // null predicate
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2008);
    }

    [Fact]
    [CoversDiagnosticCode("BP2008")]
    public void Validate_ConditionEmptyCompound_BP2008()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new CompoundPredicateDto
                {
                    Conditions = new List<SearchPredicateDto>(),   // empty compound
                },
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2008);
    }

    // ---- BP2009: predicate DTO references unknown type -----------------

    [Fact]
    [CoversDiagnosticCode("BP2009")]
    public void Validate_UnresolvableComponentType_BP2009()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ConditionMet,
            Edges = WhenEdge.RisingEdge,
            ConditionMet = new ConditionMetPayload
            {
                Condition = new PropertyMatchDto
                {
                    ComponentType = null!,        // null simulates failed type resolution
                    PropertyPath  = "SomeField",
                    Predicate     = new NumericPredicateDto(),
                },
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2009);
    }

    // ---- BP2010: EQS sensor variable not declared ----------------------

    [Fact]
    [CoversDiagnosticCode("BP2010")]
    public void Validate_SensorVariableNotDeclared_EqsResult_BP2010()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = "MySensor",  // not declared in asset
                Trigger            = EqsTrigger.FirstReady,
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2010);
    }

    // ---- BP2011: trigger requires threshold/max-age -------------------

    [Fact]
    [CoversDiagnosticCode("BP2011")]
    public void Validate_ScoreCrossedWithZeroThreshold_BP2011()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = "MySensor",
                Trigger            = EqsTrigger.ScoreCrossed,
                ScoreThreshold     = 0,   // zero threshold -- invalid
            },
        };
        var diags = ValidateInstance(node, configure: asset =>
        {
            asset.Variables.Add(new VariableDecl
            {
                Id   = Guid.NewGuid(),
                Name = "MySensor",
                Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
            });
        });
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2011);
    }

    [Fact]
    [CoversDiagnosticCode("BP2011")]
    public void Validate_BecomesStaleWithZeroMaxAge_BP2011()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EqsResult,
            Edges = WhenEdge.RisingEdge,
            EqsResult = new EqsResultPayload
            {
                SensorVariableName = "MySensor",
                Trigger            = EqsTrigger.BecomesStale,
                MaxAgeSeconds      = 0,   // zero max-age -- invalid
            },
        };
        var diags = ValidateInstance(node, configure: asset =>
        {
            asset.Variables.Add(new VariableDecl
            {
                Id   = Guid.NewGuid(),
                Name = "MySensor",
                Type = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" },
            });
        });
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2011);
    }

    // ---- BP2012: Edges set to None -------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2012")]
    public void Validate_EdgesNone_BP2012()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.None,   // forbidden
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "SomeComponent",
                PropertyPath    = "SomeField",
                Source          = ValueChangedSource.SelfComponent,
            },
        };
        var diags = ValidateInstance(node);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2012);
    }

    // ---- BP2013: EventFired FallingEdge (warning) ----------------------

    [Fact]
    [CoversDiagnosticCode("BP2013")]
    public void Validate_EventFiredFallingEdge_BP2013Warning()
    {
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.EventFired,
            Edges = WhenEdge.FallingEdge,   // falling edge on an event is meaningless
            EventFired = new EventFiredPayload
            {
                EventTypeId  = "HitEvent",
                TargetFilter = EventTargetFilter.None,
            },
        };
        var sink = new DiagnosticSink();
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest_BP2013")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.False(sink.HasErrors);
        Assert.Contains(sink.All, d =>
            d.Code == DiagnosticCodes.BP2013
            && d.Severity == DiagnosticSeverity.Warning);
    }

    // ---- BP2014: epsilon on non-float field (warning) ------------------

    [Fact]
    [CoversDiagnosticCode("BP2014")]
    public void Validate_EpsilonNonZero_ValueChanged_BP2014Warning()
    {
        // TestComponent.Value is an int field (not floating-point).
        // BP2014 must fire when epsilon is non-zero on a non-float resolvable field.
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.TestComponent",
                PropertyPath    = "Value",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0.001,   // non-zero epsilon on int field -> BP2014
            },
        };
        var sink = new DiagnosticSink();
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest_BP2014")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.False(sink.HasErrors);
        Assert.Contains(sink.All, d =>
            d.Code == DiagnosticCodes.BP2014
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Validate_EpsilonNonZero_OnFloatField_NoBP2014()
    {
        // AnotherTestComponent.X is a float field. BP2014 must NOT fire.
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.AnotherTestComponent",
                PropertyPath    = "X",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0.05,
            },
        };
        var sink = new DiagnosticSink();
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest_FloatNoBP2014")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP2014);
    }

    [Fact]
    public void Validate_EpsilonNonZero_OnDoubleField_NoBP2014()
    {
        // VectorTestComponent.DoubleValue is a double field. BP2014 must NOT fire.
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.VectorTestComponent",
                PropertyPath    = "DoubleValue",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0.001,
            },
        };
        var sink = new DiagnosticSink();
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest_DoubleNoBP2014")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP2014);
    }

    [Fact]
    public void Validate_EpsilonNonZero_OnVector2Field_NoBP2014()
    {
        // VectorTestComponent.Position2D is Vector2. BP2014 must NOT fire.
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "Hrot.Blueprints.Tests.Mocks.VectorTestComponent",
                PropertyPath    = "Position2D",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0.1,
            },
        };
        var sink = new DiagnosticSink();
        var asset = BlueprintAssetBuilder
            .Instance("WhenTest_Vector2NoBP2014")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP2014);
    }

    // ---- Happy path: valid Instance WhenNode ---------------------------

    [Fact]
    public void Validate_ValidInstance_NoErrors()
    {
        var node = MakeValidValueChangedNode();
        var diags = ValidateInstance(node);
        Assert.DoesNotContain(diags, d => d.IsError);
    }

    [Fact]
    public void Validate_PeerVariableSource_EmptyComponentFields_NoBP2003()
    {
        // PeerBlueprintVariable does NOT require ComponentTypeId/PropertyPath.
        var peerId = Guid.NewGuid();
        var sig = new BlueprintSignature(
            Path:                  "",
            AssetId:               peerId,
            Name:                  "SquadState",
            SanitizedName:         "SquadState",
            BlueprintId:           1,
            Dispatch:              Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            ExportedFunctionNames: Array.Empty<string>(),
            Hostings:              Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());
        var node = new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId      = "",            // intentionally empty
                PropertyPath         = "",            // intentionally empty
                Source               = ValueChangedSource.PeerBlueprintVariable,
                PeerBlueprintAssetId = peerId,
                PeerVariableName     = "ThreatLevel",
            },
        };
        var diags = ValidateInstance(node, DefaultOptions(siblings: new[] { sig }));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2003);
        // BP2004 should also not fire (peer is in siblings)
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2004);
    }

    // ---- Private helpers -----------------------------------------------

    private static WhenNode MakeValidValueChangedNode() =>
        new WhenNode
        {
            Id   = Guid.NewGuid(),
            Mode = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            ValueChanged = new ValueChangedPayload
            {
                ComponentTypeId = "SomeComponent",
                PropertyPath    = "SomeField",
                Source          = ValueChangedSource.SelfComponent,
                Epsilon         = 0,
            },
        };
}
