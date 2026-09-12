using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class Stage4_TypeResolveTests
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

    // ---- BP1500: Unresolvable type ref ---------------------------------

    /// <summary>
    /// BP1500 fires for a variable type the registry cannot resolve AND the AN2 "trust the dotted
    /// project FQN" fallback cannot claim (see <c>Stage4_TypeResolve.TryResolveFieldType</c>) --
    /// i.e. a DOTLESS unknown id. This test originally patched in a DOTTED unknown
    /// ("Not.A.Real.Type") and went stale when the deliberate Q#14 Option B fallback started
    /// resolving any dotted FQN by trust (the reflection-less compiler cannot verify a project
    /// struct; a truly nonexistent type fails later at the Roslyn compile with CS0246). The dotted
    /// case is pinned by <see cref="TypeResolve_DottedUnknownFieldType_ResolvesByTrust_NoBP1500"/>.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1500")]
    public void TypeResolve_UnknownFieldType_EmitsBP1500()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("bad", typeof(int))  // we'll patch the type ID after build
            .Build();

        // Dotless => neither the registry nor the AN2 dotted-FQN fallback resolves it.
        asset.Variables[0].Type.TypeId = "NotARealType";

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1500);
    }

    /// <summary>
    /// The AN2 fallback contract (Q#14 Option B): a DOTTED unknown FQN is trusted as a project
    /// struct -- it resolves (no BP1500) with <c>SizeReliable = false</c>, so State layout must use
    /// runtime offsets instead of baked size sums. A genuinely nonexistent type is caught later by
    /// the Roslyn compile (CS0246), never silently executed.
    /// </summary>
    [Fact]
    public void TypeResolve_DottedUnknownFieldType_ResolvesByTrust_NoBP1500()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("maybeProjectStruct", typeof(int))
            .Build();
        asset.Variables[0].Type.TypeId = "Not.A.Real.Type";

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1500);
    }

    // ---- BP1501: Link type mismatch ------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1501")]
    public void TypeResolve_IncompatiblePinTypes_EmitsBP1501()
    {
        // Create a graph with a data link where source and dest types are
        // incompatible (int -> string, no coercion exists).
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execOut = Guid.NewGuid();
        var execIn  = Guid.NewGuid();
        var intOut  = Guid.NewGuid();
        var strIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = intOut, Name = "IntVal", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        new Pin { Id = strIn, Name = "StrVal", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.String" } },
                    }},
            },
            Links = new List<Link>
            {
                new Link { FromNodeId = entryId, FromPinId = execOut, ToNodeId = retId,   ToPinId = execIn },
                // Incompatible: int -> string
                new Link { FromNodeId = entryId, FromPinId = intOut,  ToNodeId = retId,   ToPinId = strIn  },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "L",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs  = new List<Graph> { graph },
            Header  = new Header(),
        };

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1501);
    }

    /// <summary>
    /// CA-07c regression: <c>StaticTypeRegistry.TryResolve</c> wraps an array's FullName as
    /// "ElementFullName[]" (e.g. "System.Object[]"), so the OLD exact-string wildcard check
    /// (<c>fromType.FullName == "System.Object"</c>) never matched an ARRAY of the placeholder type
    /// -- a real component collection (e.g. "System.Int32[]") wired into
    /// <see cref="ComponentItemCountNode"/>'s "Collection" pin (ALWAYS "System.Object[]", no
    /// <c>ElementTypeFqn</c> on this node) was WRONGLY flagged BP1501, even though the scalar
    /// "System.Int32" -&gt; "System.Object" case was already explicitly fine. Verified against the
    /// real repro before the fix (build failed with exactly this message); this asserts the fix at
    /// the Stage4 unit level too.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1501")]
    public void TypeResolve_IntArrayIntoObjectArray_DoesNotEmitBP1501()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execOut = Guid.NewGuid();
        var execIn  = Guid.NewGuid();
        var arrOut  = Guid.NewGuid();
        var arrIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = arrOut, Name = "IntArray", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        // Mirrors ComponentItemCountNode's "Collection" pin: always System.Object, IsArray.
                        new Pin { Id = arrIn, Name = "ObjArray", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Object", IsArray = true } },
                    }},
            },
            Links = new List<Link>
            {
                new Link { FromNodeId = entryId, FromPinId = execOut, ToNodeId = retId, ToPinId = execIn },
                new Link { FromNodeId = entryId, FromPinId = arrOut,  ToNodeId = retId, ToPinId = arrIn  },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "L",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs  = new List<Graph> { graph },
            Header  = new Header(),
        };

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1501);
    }

    /// <summary>
    /// Companion negative case: the array-wildcard fix must NOT become a blanket "any array is
    /// compatible with any array" hole -- only an array of literal System.Object is a wildcard.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1501")]
    public void TypeResolve_IntArrayIntoStringArray_StillEmitsBP1501()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execOut = Guid.NewGuid();
        var execIn  = Guid.NewGuid();
        var arrOut  = Guid.NewGuid();
        var arrIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = arrOut, Name = "IntArray", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32", IsArray = true } },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        new Pin { Id = arrIn, Name = "StrArray", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.String", IsArray = true } },
                    }},
            },
            Links = new List<Link>
            {
                new Link { FromNodeId = entryId, FromPinId = execOut, ToNodeId = retId, ToPinId = execIn },
                new Link { FromNodeId = entryId, FromPinId = arrOut,  ToNodeId = retId, ToPinId = arrIn  },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "L",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs  = new List<Graph> { graph },
            Header  = new Header(),
        };

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1501);
    }

    // ---- BP1503: Managed type in state ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1503")]
    public void TypeResolve_ManagedTypeInInstanceVariables_EmitsBP1503()
    {
        // System.String is a managed (reference) type -- not allowed in state structs.
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("name", typeof(string))
            .Build();

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1503);
    }

    // ---- Happy path: resolved pin types populate PinTypes map ----------

    [Fact]
    public void TypeResolve_KnownType_PopulatesPinTypesMap()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("hp", typeof(float))
            .Build();

        var sink   = new DiagnosticSink();
        var result = Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        // Variable id should be in the FieldTypes map.
        var varId = asset.Variables[0].Id;
        Assert.True(result.FieldTypes.ContainsKey(varId),
            "Expected float variable to resolve in FieldTypes map.");
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1500);
    }

    // ---- AN2: Enum TypeRef (global:: prefix) resolves as unmanaged ------

    /// <summary>
    /// AN2: A variable typed with a "global::" enum FQN must resolve in the FieldTypes map
    /// (no BP1500) because StaticTypeRegistry accepts "global::" TypeIds as enum/project types
    /// (unmanaged, size 4).
    /// </summary>
    [Fact]
    public void TypeResolve_EnumTypeRef_GlobalPrefix_Resolves()
    {
        var asset = BlueprintAssetBuilder
            .Instance("EnumTest")
            .Build();

        // Manually add a variable with a global:: enum TypeId (the editor-stamped convention).
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Mode",
            Type = new BlueprintTypeRef { TypeId = "global::Hrot.Game.CombatMode" },
        });

        var sink   = new DiagnosticSink();
        var result = Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        var varId = asset.Variables[0].Id;
        Assert.True(result.FieldTypes.ContainsKey(varId),
            "Expected enum variable (global:: prefix) to resolve in FieldTypes map.");
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1500);
    }

    /// <summary>
    /// AN2: A resolved enum IrTypeRef must be unmanaged with SizeBytes = 4
    /// (default Int32 underlying type).
    /// </summary>
    [Fact]
    public void TypeResolve_EnumTypeRef_GlobalPrefix_IsUnmanagedSize4()
    {
        var asset = BlueprintAssetBuilder
            .Instance("EnumTest2")
            .Build();

        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "State",
            Type = new BlueprintTypeRef { TypeId = "global::Hrot.Game.PatrolState" },
        });

        var sink   = new DiagnosticSink();
        var result = Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        var varId = asset.Variables[0].Id;
        Assert.True(result.FieldTypes.TryGetValue(varId, out var irType),
            "Enum variable should have resolved IrTypeRef.");
        Assert.True(irType.IsUnmanaged, "Enum IrTypeRef must be unmanaged.");
        Assert.Equal(4, irType.SizeBytes);
    }

    /// <summary>
    /// AN2: An Instance blueprint variable typed as a "global::" enum must NOT emit BP1503
    /// (managed-type-in-state constraint). Enums are unmanaged blittable types.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1503")]
    public void TypeResolve_EnumVariable_DoesNotEmitBP1503()
    {
        var asset = BlueprintAssetBuilder
            .Instance("EnumBP1503Test")
            .Build();

        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Stance",
            Type = new BlueprintTypeRef { TypeId = "global::Hrot.Game.CombatStance" },
        });

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1503);
    }

    // ---- BP1502: Wildcard node pin unresolvable  -----------------------
    // Note: BP1502 is emitted only if an ArrayMakeNode/ArrayGetNode's
    // element type cannot be propagated. Validated via coverage attribute.
    [Fact]
    [CoversDiagnosticCode("BP1502")]
    public void TypeResolve_ArrayMakeNodeWithUnresolvableElement_EmitsBP1502()
    {
        // Build a Library asset with an ArrayMakeNode whose input pin has
        // an unresolvable type (so wildcard propagation fails).
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var arrayId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var execOut = Guid.NewGuid();
        var execIn  = Guid.NewGuid();
        var execOut2 = Guid.NewGuid();
        var execIn2  = Guid.NewGuid();
        var elemIn  = Guid.NewGuid();
        var arrOut  = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "G",
            Kind    = GraphKind.Function,
            Inputs  = new(), Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new ArrayMakeNode { Id = arrayId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = execOut2,Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        // Input element has unresolvable type -> wildcard propagation fails
                        new Pin { Id = elemIn, Name = "Element", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "Not.Resolvable.Elem" } },
                        new Pin { Id = arrOut, Name = "Result", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "Not.Resolvable.Elem[]" } },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execIn2, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    }},
            },
            Links = new List<Link>
            {
                new Link { FromNodeId = entryId, FromPinId = execOut,  ToNodeId = arrayId, ToPinId = execIn  },
                new Link { FromNodeId = arrayId, FromPinId = execOut2, ToNodeId = retId,   ToPinId = execIn2 },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "ArrayLib",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs  = new List<Graph> { graph },
            Header  = new Header(),
        };

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1502);
    }
}
