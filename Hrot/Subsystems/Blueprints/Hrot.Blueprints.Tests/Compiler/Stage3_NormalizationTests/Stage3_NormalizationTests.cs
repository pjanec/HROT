using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class Stage3_NormalizationTests
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

    // ---- BP3010: Orphan node elimination --------------------------------

    [Fact]
    [CoversDiagnosticCode("BP3010")]
    public void Normalize_OrphanNode_EmitsBP2001AndRemovesNode()
    {
        // Build a Library with an entry -> return graph PLUS an orphan ReturnNode
        // (not wired to anything).
        var assetId = SyntheticGuidHelper.Compute(Guid.Empty, Guid.Empty, "OrphanTest");
        var graphId = SyntheticGuidHelper.Compute(assetId, Guid.Empty, "Graph", "Main");

        // Build the main graph using the builder (entry -> return, properly wired).
        var asset = BlueprintAssetBuilder
            .Library("OrphanLib")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        // Manually inject a second (orphan) ReturnNode with no wires.
        var graph = asset.Graphs[0];
        var orphanId = Guid.NewGuid();
        graph.Nodes.Add(new ReturnNode
        {
            Id     = orphanId,
            Status = NodeStatus.Failure,
            Pins   = new List<Pin>
            {
                new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
            },
        });

        var sink = new DiagnosticSink();
        var opts = DefaultOptions();
        var ctx  = new ValidationContext(sink, opts);

        var normalized = Stage3_Normalize.Run(asset, ctx);

        // BP3010 warning emitted for the orphan node.
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP3010);
        // Orphan node removed from the output.
        Assert.DoesNotContain(normalized.Graphs[0].Nodes, n => n.Id == orphanId);
    }

    [Fact]
    public void Normalize_NoOrphans_NoWarnings()
    {
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var sink = new DiagnosticSink();
        var normalized = Stage3_Normalize.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP3010);
        Assert.Equal(asset.Graphs[0].Nodes.Count, normalized.Graphs[0].Nodes.Count);
    }

    /// <summary>
    /// ⭐ Batch 29 — the pass-order defect behind 6 of the tree's 16 <c>BP3010</c>s.
    ///
    /// <para>
    /// An orphan node carrying pin defaults used to produce <b>two</b> warnings: one for itself, and
    /// one for the <c>LiteralNode</c> the compiler synthesized for its unconnected defaulted pin
    /// moments before deleting both. The second named a GUID present in no asset file, so a designer
    /// had nothing to act on. Elimination now runs before materialization.
    /// </para>
    ///
    /// <para>
    /// Locked as a COUNT, deliberately: "at least one BP3010" passed before the fix too.
    /// </para>
    /// </summary>
    [Fact]
    public void Normalize_OrphanWithPinDefaults_WarnsOnce_NotAlsoForItsSynthesizedLiteral()
    {
        var asset = BlueprintAssetBuilder
            .Library("OrphanDefaultsLib")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var graph    = asset.Graphs[0];
        var orphanId = Guid.NewGuid();

        // One orphan carrying THREE defaulted, unconnected data-IN pins. Before the reorder this
        // produced 1 + 3 = 4 BP3010s; the three extras were the compiler's own scaffolding.
        graph.Nodes.Add(new PrintStringNode
        {
            Id   = orphanId,
            Pins = new List<Pin>
            {
                new Pin { Id = Guid.NewGuid(), Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                new Pin { Id = Guid.NewGuid(), Name = "A", Direction = "In", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValue = "1" },
                new Pin { Id = Guid.NewGuid(), Name = "B", Direction = "In", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" }, DefaultValue = "2" },
                new Pin { Id = Guid.NewGuid(), Name = "C", Direction = "In", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Single" }, DefaultValue = "3.5" },
            },
        });

        var sink       = new DiagnosticSink();
        var normalized = Stage3_Normalize.Run(asset, new ValidationContext(sink, DefaultOptions()));

        var orphanWarnings = sink.All.Where(d => d.Code == DiagnosticCodes.BP3010).ToList();

        Assert.Single(orphanWarnings);

        // And the one warning names the AUTHORED node — the thing the designer can actually find.
        Assert.Equal(orphanId, orphanWarnings[0].NodeId);

        // No synthesized literal survives for the deleted node either.
        Assert.DoesNotContain(normalized.Graphs[0].Nodes, n => n.Id == orphanId);
        Assert.Empty(normalized.Graphs[0].Nodes.OfType<LiteralNode>());
    }

    // ---- BP-220: the Graph copy shape ------------------------------------

    /// <summary>
    /// ⭐ <b>The guard the copy could not have without reflection.</b> <c>Graph.WithNodesAndLinks</c>
    /// must preserve EVERY member except the two it replaces. A hand-written copy cannot be checked by
    /// the compiler, which is how <c>Comments</c> came to be dropped at both Stage 3 sites and how
    /// <c>ExecOutputs</c> came to need hand-adding at both in Batch 29.
    ///
    /// <para>
    /// This walks the type's properties, so it fails on the NEXT member added without being handled —
    /// at the moment the knowledge exists, rather than several batches later when a value has already
    /// gone missing somewhere downstream.
    /// </para>
    /// </summary>
    [Fact]
    public void Graph_CopyShape_PreservesEveryMember()
    {
        var source = new Graph
        {
            Id             = Guid.NewGuid(),
            Name           = "Original",
            Kind           = GraphKind.Macro,
            Inputs         = { new ParameterDecl { Id = Guid.NewGuid(), Name = "In0" } },
            Outputs        = { new ParameterDecl { Id = Guid.NewGuid(), Name = "Out0" } },
            ExecOutputs    = { new ExecOutDecl { Id = Guid.NewGuid(), Name = "Then" } },
            Comments       = { new GraphComment { Id = Guid.NewGuid(), Text = "keep me" } },
            EditorMetadata = new GraphMetadata(),
            Nodes          = { new ReturnNode { Id = Guid.NewGuid() } },
            Links          = { new Link { FromNodeId = Guid.NewGuid() } },
        };

        var copy = source.WithNodesAndLinks(new List<Node>(), new List<Link>());

        var replaced = new HashSet<string> { nameof(Graph.Nodes), nameof(Graph.Links) };
        var dropped  = new List<string>();

        foreach (var prop in typeof(Graph).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (replaced.Contains(prop.Name)) continue;

            var before = prop.GetValue(source);
            var after  = prop.GetValue(copy);

            // Reference equality for the collections, value equality for the scalars — either way the
            // question is "did the copy carry it across at all".
            bool preserved = ReferenceEquals(before, after) || Equals(before, after);
            if (!preserved) dropped.Add(prop.Name);
        }

        Assert.True(dropped.Count == 0,
            "Graph.WithNodesAndLinks dropped these members:\n  " + string.Join("\n  ", dropped)
            + "\n\nAdd them to the copy. A Graph member that is not copied is silently lost every "
            + "time Stage3_Normalize rebuilds a graph — which is exactly how Comments was lost.");

        // And the two it IS meant to replace really were replaced.
        Assert.Empty(copy.Nodes);
        Assert.Empty(copy.Links);
    }

    /// <summary>The end-to-end statement of the same thing: comments survive Stage 3.</summary>
    [Fact]
    public void Normalize_PreservesGraphComments()
    {
        var asset = BlueprintAssetBuilder
            .Library("CommentsLib")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        asset.Graphs[0].Comments.Add(new GraphComment { Id = Guid.NewGuid(), Text = "designer note" });

        // Force BOTH reconstruction sites to run: an orphan (elimination) with a defaulted pin
        // (materialization).
        asset.Graphs[0].Nodes.Add(new PrintStringNode
        {
            Id   = Guid.NewGuid(),
            Pins = { new Pin { Id = Guid.NewGuid(), Name = "A", Direction = "In", IsExec = false,
                               TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" },
                               DefaultValue = "1" } },
        });

        var sink       = new DiagnosticSink();
        var normalized = Stage3_Normalize.Run(asset, new ValidationContext(sink, DefaultOptions()));

        var comment = Assert.Single(normalized.Graphs[0].Comments);
        Assert.Equal("designer note", comment.Text);
    }

    // ---- Implicit cast insertion (BP3011 RETIRED -- Batch 29) ------------

    [Fact]
    public void Normalize_ImplicitCastNeeded_InsertsCastNode_AndDoesNotWarn()
    {
        // Create a graph where an int output is wired to a float input.
        // StaticTypeRegistry must support int->float coercion for this to emit BP3011.
        var assetId = SyntheticGuidHelper.Compute(Guid.Empty, Guid.Empty, "CastTest");
        var graphId = SyntheticGuidHelper.Compute(assetId, Guid.Empty, "Graph", "CastGraph");
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var outPin  = Guid.NewGuid();
        var inPin   = Guid.NewGuid();
        var execOut = Guid.NewGuid();
        var execIn  = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "CastGraph",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new Pin { Id = execOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                        // Data output typed as int
                        new Pin { Id = outPin, Name = "Value", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins   = new List<Pin>
                    {
                        new Pin { Id = execIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        // Data input typed as float (coercible from int)
                        new Pin { Id = inPin, Name = "Result", Direction = "In", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } },
                    },
                },
            },
            Links = new List<Link>
            {
                new Link { FromNodeId = entryId, FromPinId = execOut, ToNodeId = retId, ToPinId = execIn },
                // Data link: int -> float (requires implicit cast)
                new Link { FromNodeId = entryId, FromPinId = outPin, ToNodeId = retId, ToPinId = inPin },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "CastLib",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new List<Graph> { graph },
            Header = new Header(),
        };

        var sink = new DiagnosticSink();
        var normalized = Stage3_Normalize.Run(asset, new ValidationContext(sink, DefaultOptions()));

        // int -> float is a rung of StaticTypeRegistry.CoercionTable, so a CastNode IS inserted:
        // 2 original + 1 cast = 3 nodes. Asserted directly rather than conditionally -- the old
        // form branched on whether the diagnostic fired and so would have passed even if the pass
        // had stopped inserting casts altogether.
        Assert.Single(normalized.Graphs[0].Nodes.OfType<CastNode>());
        Assert.Equal(3, normalized.Graphs[0].Nodes.Count);

        // ⭐ Batch 29: and it does so SILENTLY. BP3011 is retired -- every rung of the coercion
        // table is a lossless C# implicit conversion, so there was never anything for the designer
        // to act on. See CoercionTable_ContainsOnlyLosslessWidenings for the invariant this rests on.
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP3011);
    }

    /// <summary>
    /// ⭐ The invariant BP3011's retirement rests on: <c>StaticTypeRegistry.CoercionTable</c> carries
    /// ONLY lossless widening conversions — exactly C#'s implicit-numeric-conversion set. If a lossy
    /// rung is ever added, silent insertion stops being safe and the retired diagnostic must come
    /// back; this test is what forces that decision to be made rather than skipped.
    /// </summary>
    [Fact]
    public void CoercionTable_ContainsOnlyLosslessWidenings()
    {
        // Rank within each conversion family. A conversion is lossless iff C# defines it as an
        // IMPLICIT numeric conversion; the pairs below are that set, transcribed from the C#
        // specification (§10.2.3) minus decimal, which the registry does not carry.
        var implicitNumeric = new HashSet<(string From, string To)>
        {
            ("System.SByte",  "System.Int16"),  ("System.SByte",  "System.Int32"),
            ("System.SByte",  "System.Int64"),  ("System.SByte",  "System.Single"),
            ("System.SByte",  "System.Double"),
            ("System.Byte",   "System.Int16"),  ("System.Byte",   "System.UInt16"),
            ("System.Byte",   "System.Int32"),  ("System.Byte",   "System.UInt32"),
            ("System.Byte",   "System.Int64"),  ("System.Byte",   "System.UInt64"),
            ("System.Byte",   "System.Single"), ("System.Byte",   "System.Double"),
            ("System.Int16",  "System.Int32"),  ("System.Int16",  "System.Int64"),
            ("System.Int16",  "System.Single"), ("System.Int16",  "System.Double"),
            ("System.UInt16", "System.Int32"),  ("System.UInt16", "System.UInt32"),
            ("System.UInt16", "System.Int64"),  ("System.UInt16", "System.UInt64"),
            ("System.UInt16", "System.Single"), ("System.UInt16", "System.Double"),
            ("System.Int32",  "System.Int64"),  ("System.Int32",  "System.Single"),
            ("System.Int32",  "System.Double"),
            ("System.UInt32", "System.Int64"),  ("System.UInt32", "System.UInt64"),
            ("System.UInt32", "System.Single"), ("System.UInt32", "System.Double"),
            ("System.Int64",  "System.Single"), ("System.Int64",  "System.Double"),
            ("System.UInt64", "System.Single"), ("System.UInt64", "System.Double"),
            ("System.Single", "System.Double"),
        };

        var table = typeof(StaticTypeRegistry)
            .GetField("CoercionTable", System.Reflection.BindingFlags.NonPublic
                                     | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IEnumerable;

        Assert.NotNull(table);

        var offenders = new List<string>();
        foreach (var entry in table!)
        {
            var keyProp = entry.GetType().GetProperty("Key")!;
            var key     = keyProp.GetValue(entry)!;
            var from    = (string)key.GetType().GetField("Item1")!.GetValue(key)!;
            var to      = (string)key.GetType().GetField("Item2")!.GetValue(key)!;
            if (!implicitNumeric.Contains((from, to)))
                offenders.Add($"{from} -> {to}");
        }

        Assert.True(offenders.Count == 0,
            "CoercionTable gained a rung that is NOT a lossless C# implicit numeric conversion:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nStage3 inserts these casts SILENTLY (BP3011 was retired in Batch 29 precisely "
            + "because every rung was lossless). A lossy rung means a wrong-VALUES change the "
            + "designer cannot see. Either drop the rung, or restore a diagnostic for it.");
    }

    // ---- Return value preservation -------------------------------------

    [Fact]
    public void Normalize_PreservesNodeCountForCleanAsset()
    {
        // An asset with no orphans and no type mismatches should come out identical.
        var asset = BlueprintAssetBuilder
            .Library("L")
            .WithGraph("G", g => g.Entry().Return())
            .Build();
        var originalNodeCount = asset.Graphs[0].Nodes.Count;
        var originalLinkCount = asset.Graphs[0].Links.Count;

        var sink       = new DiagnosticSink();
        var normalized = Stage3_Normalize.Run(asset, new ValidationContext(sink, DefaultOptions()));

        Assert.Equal(originalNodeCount, normalized.Graphs[0].Nodes.Count);
        Assert.Equal(originalLinkCount, normalized.Graphs[0].Links.Count);
        Assert.Empty(sink.All.Where(d => d.IsError));
    }
}
