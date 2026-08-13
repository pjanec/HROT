using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;


namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null);
}

public sealed class BlueprintCompiler : IBlueprintCompiler
{
    // Injected by Hrot.Blueprints.Core at module initialization when Roslyn is available.
    // Signature: (source, virtualFilePath, assemblyName, sink) -> (pe, pdb)
    internal static Func<string, string, string, DiagnosticSink, (byte[] Pe, byte[] Pdb)>?
        RoslynFinalizer;

    public CompileResult Compile(BlueprintAsset asset, CompileOptions options)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, options);

        // Stage 0 -- Rehydrate pin-less nodes (saved projection-only assets have Pins:[]).
        // NOTE: Stage0 mutates node.Pins in place; later stages (Stage3) may also replace
        // asset.Graphs.  To prevent mutations from leaking back to the caller we work on a
        // shallow copy of the asset that owns a new Graphs list (graphs themselves are shared
        // and stage0 pin mutation IS visible to caller — that is intentional rehydration).
        asset = new BlueprintAsset
        {
            Header             = asset.Header,
            AssetId            = asset.AssetId,
            Name               = asset.Name,
            Dispatch           = asset.Dispatch,
            TierHint           = asset.TierHint,
            IsWorldSingleton   = asset.IsWorldSingleton,
            Primitive          = asset.Primitive,
            Parameters         = asset.Parameters,
            ParameterOrder     = asset.ParameterOrder,
            WorkingState       = asset.WorkingState,
            WorkingStateOrder  = asset.WorkingStateOrder,
            Variables          = asset.Variables,
            VariableOrder      = asset.VariableOrder,
            EventDispatchers   = asset.EventDispatchers,
            CustomEvents       = asset.CustomEvents,
            CallablePeers      = asset.CallablePeers,
            Graphs             = new List<Graph>(asset.Graphs),  // new list, same graph objects
            EditorMetadata     = asset.EditorMetadata,
        };

        Stage0_Rehydrate.Run(asset, options);

        // U-2 / BP-229 -- the compiler takes ownership of the graphs it is about to rewrite.
        //
        // ⛔ The defect: the shallow copy above gives this method its own Graphs LIST but the SAME
        // Graph objects, and Stage2_5_ExpandMacros then edits them in place — it removes the caller's
        // MacroCallNode from host.Nodes and rewires host Link objects (MacroExpander:205, :258).
        // ⇒ compiling an asset EDITED it: the designer's macro call node vanished from the graph they
        // were looking at and the macro body appeared spliced into it.
        //
        // ⭐ Placed HERE, after Stage 0 and before the first mutating stage, because Stage 0's pin
        // rehydration is INTENTIONALLY visible to the caller (see the note above) — a copy taken any
        // earlier would hide it and change documented behaviour. Stage 2 in between is a pure
        // validator.
        //
        // ⚠ Node OBJECTS stay shared on purpose. Nothing mutates a node in place after Stage 0
        // (verified across Stage 2.5/3/4; MacroExpander's only node write is to a literal node it just
        // created), and their one intended mutation is exactly the rehydration above. Cloning them
        // would also have to preserve node ids — the DebugMap and every diagnostic are keyed by them —
        // so it would buy nothing and risk that.
        // (`asset` is already this method's private shallow copy, so replacing entries in its own
        // Graphs list touches nothing the caller can see.)
        for (int i = 0; i < asset.Graphs.Count; i++)
            asset.Graphs[i] = CloneGraphForCompilation(asset.Graphs[i]);

        // Stage 2 -- Validate
        Stage2_Validate.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, asset);

        // Stage 2.5 -- Expand macros (BP-81).
        //
        // ⭐ Deliberately AFTER Stage 2's error gate above. That ordering is what lets the splice
        // rules assume a resolvable, acyclic macro target: BP1660 (unresolvable) and BP1662 (cycle)
        // are Stage 2 validators, so a graph that fails either never reaches expansion at all. Moving
        // this above the gate would turn both into defensive null-checks on every rule.
        asset = Stage2_5_ExpandMacros.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, asset);

        // Stage 3 -- Normalize
        asset = Stage3_Normalize.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, asset);

        // Stage 4 -- Type resolve
        var typed = Stage4_TypeResolve.Run(asset, ctx);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 5 -- Schedule
        var ir = Stage5_Schedule.Run(typed, ctx);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 6 -- Lower
        var lowered = Stage6_Lower.Run(ir, options.Mode, sink);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        // Stage 7 -- Emit C# source
        var (generatedSource, debugMap) = Stage7_Emit.Run(
            lowered, options.Mode, sink, options.SiblingSignatures);
        if (sink.HasErrors) return FailResult(sink, typed.Asset);

        byte[]? pe = null;
        byte[]? pdb = null;

        if (options.EmitPdbWithEmbeddedSource && RoslynFinalizer is not null)
        {
            var fileName = $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}_Bp.g.cs";
            var (compiledPe, compiledPdb) = RoslynFinalizer(
                generatedSource, fileName, $"Blueprint.{lowered.SanitizedName}", sink);
            if (!sink.HasErrors)
            {
                pe  = compiledPe;
                pdb = compiledPdb;
            }
        }

        return new CompileResult(
            Succeeded:         true,
            GeneratedSource:   generatedSource,
            GeneratedFileName: $"{lowered.SanitizedName}_{lowered.BlueprintId:X8}_Bp.g.cs",
            BlueprintId:       lowered.BlueprintId,
            StructureHash:     lowered.StructureHash,
            DebugMap:          debugMap,
            Diagnostics:       sink.All,
            CanonicalAsset:    typed.Asset,
            PortablePdb:       pdb,
            PortablePe:        pe);
    }

    /// <summary>
    /// U-2 / BP-229 — the per-graph copy the compiler works on.
    ///
    /// <para>
    /// ⭐ <b>Fresh containers and fresh <see cref="Link"/> objects; shared <see cref="Node"/>
    /// objects.</b> The lists because Stage 2.5 adds and removes entries, and the links because it
    /// <b>rewires them in place</b> — <c>MacroExpander</c> assigns <c>link.ToNodeId</c>/<c>ToPinId</c>
    /// when it stitches a spliced body onto the call site's continuation, which is the write the
    /// caller must not see.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Built on <see cref="Graph.WithNodesAndLinks"/> rather than field-by-field</b> — BP-220
    /// exists because two hand-written copies both dropped a member, and <c>LocalVariables</c> (BP-57)
    /// is the newest member that a hand-written copy here would have had to remember.
    /// </para>
    /// </summary>
    private static Graph CloneGraphForCompilation(Graph g)
        => g.WithNodesAndLinks(
            new List<Node>(g.Nodes),
            g.Links.Select(l => new Link
            {
                FromNodeId = l.FromNodeId,
                FromPinId  = l.FromPinId,
                ToNodeId   = l.ToNodeId,
                ToPinId    = l.ToPinId,
                // Waypoints are editor-only and never rewritten by a stage; the list may be shared.
                Waypoints  = l.Waypoints,
            }).ToList());

    public ValidationResult Validate(BlueprintAsset asset, ValidationOptions? options = null)
    {
        var sink = new DiagnosticSink();
        var compileOptions = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var ctx = new ValidationContext(sink, compileOptions);
        Stage2_Validate.Run(asset, ctx);
        return new ValidationResult(sink.All);
    }

    private static CompileResult FailResult(DiagnosticSink sink, BlueprintAsset? asset) =>
        new CompileResult(
            Succeeded:       false,
            GeneratedSource: null,
            GeneratedFileName: null,
            BlueprintId:     0,
            StructureHash:   0,
            DebugMap:        null,
            Diagnostics:     sink.All,
            CanonicalAsset:  asset,
            PortablePdb:     null,
            PortablePe:      null);
}
