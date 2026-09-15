using System.Collections.Immutable;
using System.Numerics;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NodeEditor.Core.Commands;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>The authoring path, driven from a test.</b> Composes blueprint assets <b>through the editor's own
/// APIs</b> and compiles them <b>through the real source generator</b>.
///
/// <para>
/// ⭐ <b>Why this exists, and why it is not a builder.</b> Four consecutive batches shipped defects that
/// a human found by clicking the UI and that no headless test caught. Every one was the same shape:
/// <i>a blueprint shape nobody happened to author by hand</i>.
/// <list type="bullet">
///   <item><b>BP-116</b> — the editor never wrote <c>CallablePeers</c>, so every editor-authored peer
///     call failed <c>BP1300</c>. <b>Invisible to every test that writes <c>CallablePeers</c> itself</b>
///     — and every fixture in the repo does, including <c>SmokePatrol.bp.json</c>, which carries one
///     literally. <c>BP109_SmokeTestEndToEndTests</c> composed a whole multi-asset set and still missed
///     it, because it composed the <i>JSON</i>, not the authoring path.</item>
///   <item><b>BP-117</b> — a Library graph with declared outputs and no <c>Return</c> emitted a bare
///     <c>return;</c> ⇒ <c>CS0126</c>. A generator-path failure: nothing in the in-memory suites runs
///     Roslyn over the generated text with the real generator's sibling wiring.</item>
///   <item><b>BP-112</b> — <c>CS9191</c> in a Library adapter. In-memory compiles do not treat warnings
///     as errors, so only the real build could see it.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠ <b>The rule that makes this worth anything:</b> nothing here may set asset state that the editor
/// itself sets. Dispatch comes from <see cref="BlueprintNewAssetService.AvailableRecipes"/>; nodes come
/// from <see cref="BlueprintCommandSink"/>; a peer comes from the picker session. The moment a test
/// assigns <c>asset.CallablePeers</c> (or any other field the editor owns) it stops testing the
/// authoring path and starts testing itself.
/// </para>
/// </summary>
internal static class AuthoringPath
{
    // ── Composition: create an asset exactly as the "New Blueprint" flow does ─────────────────────

    /// <summary>
    /// Creates a blueprint via <see cref="BlueprintNewAssetService"/>, selecting dispatch by picking the
    /// matching <b>blank template</b> from <see cref="BlueprintNewAssetService.AvailableRecipes"/> —
    /// the same choice the New-Asset dialog offers. ⚠ Deliberately NOT <c>asset.Dispatch = …</c>: that
    /// would be the test configuring the asset rather than the editor.
    /// </summary>
    public static BlueprintAsset NewAsset(string name, BlueprintDispatchKind dispatch)
    {
        var service = new BlueprintNewAssetService();

        Hrot.Editor.AiShared.IEditableAsset? recipe = null;
        var wanted = dispatch == BlueprintDispatchKind.Library ? "Library" : "Empty";
        foreach (var candidate in service.AvailableRecipes())
        {
            if (!service.IsBlankTemplate(candidate)) continue;
            if (candidate.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                recipe = candidate;
                break;
            }
        }

        var created = service.CreateNew(recipe, name, string.Empty);
        var adapter = created as BlueprintEditableAssetAdapter
            ?? throw new InvalidOperationException(
                $"BlueprintNewAssetService returned {created.GetType().Name}, not a "
                + "BlueprintEditableAssetAdapter — the authoring path changed shape.");

        var asset = adapter.Asset;
        if (asset.Dispatch != dispatch)
        {
            throw new InvalidOperationException(
                $"Asked the New-Asset service for {dispatch} and got {asset.Dispatch}. The blank-template "
                + "table no longer offers that dispatch — fix the lookup rather than assigning Dispatch "
                + "directly, or this harness stops testing the authoring path.");
        }
        return asset;
    }

    /// <summary>
    /// An open document: every collaborator the editor host wires up, kept together so a test can
    /// author a wire, count undo entries, or re-project pins without rebuilding the world.
    ///
    /// <para>
    /// ⭐ <b>Batch 27, axes 2 and 3.</b> The Batch-25 matrix could only reach <see cref="Sink"/>, which
    /// is enough to place nodes and therefore enough to test <i>final states</i>. Wiring acceptance and
    /// edit sequences both need the model (to name a pin), the history (to undo) and the edit service
    /// (to drive a Details session) as well — and every defect in Batch 27 lived in one of those two.
    /// </para>
    /// </summary>
    public sealed record Document(
        BlueprintAsset        Asset,
        Graph                 Graph,
        BlueprintGraphModel   Model,
        BlueprintCommandSink  Sink,
        CommandHistory        History,
        EditService           Edit,
        BlueprintTypeSystem   TypeSystem);

    /// <summary>
    /// Opens <paramref name="asset"/>'s graph with the collaborators the editor host wires up.
    ///
    /// <para>
    /// ⚠ <c>OnStructureChanged</c> is wired to <c>Model.RebuildAndNotify</c> exactly as
    /// <c>BlueprintDocumentFactory</c> does. Without it a Details edit changes the model and the
    /// projection never re-derives — which is BP-125, and a harness that omitted it could not see it.
    /// </para>
    /// </summary>
    public static Document Open(BlueprintAsset asset, Graph? graph = null)
    {
        var g          = graph ?? asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, g);
        // ⭐ The REAL palette registry, not an empty one. With `new NodeKindRegistry()` the sink's
        // CreateAssetNode falls through to a generic FunctionCallNode for every unknown kind id, so the
        // matrix would silently author the wrong nodes and prove nothing.
        var catalog    = new BlueprintNodeCatalog(BlueprintEditorBootstrap.CreatePaletteRegistry());
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var edit       = new EditService
        {
            Context = new EditServiceContext(
                history,
                markDirty: _ => { },
                onStructureChanged: _ => model.RebuildAndNotify()),
        };

        var sink = new BlueprintCommandSink(
            asset, g, model, catalog, validator, history, edit, markDirty: _ => { });

        return new Document(asset, g, model, sink, history, edit, typeSystem);
    }

    /// <summary>
    /// Builds a <see cref="BlueprintCommandSink"/> over <paramref name="asset"/>/<paramref name="graph"/>
    /// with the same collaborators the editor host wires up. Mirrors
    /// <c>BlueprintCommandSinkTests.MakeSut</c>.
    /// </summary>
    public static BlueprintCommandSink Sink(BlueprintAsset asset, Graph graph)
        => Open(asset, graph).Sink;

    // ── Axis 2: wiring acceptance ─────────────────────────────────────────────────────────────────

    /// <summary>The projected pin named <paramref name="pinName"/> on <paramref name="node"/>.</summary>
    /// <remarks>
    /// ⚠ Reads the <b>model's</b> pins, not <c>node.Pins</c>. The asset node is projection-only for
    /// most kinds, so its own list is usually empty; the model is where a pin acquires the GUID a link
    /// must name.
    /// </remarks>
    public static NodeEditor.Core.Interfaces.IPinModel Pin(Document doc, Node node, string pinName)
    {
        var model = doc.Model.FindNode(new NodeId(node.Id))
            ?? throw new InvalidOperationException($"Node {node.Id} is not in the graph model.");

        foreach (var pin in model.Pins)
            if (string.Equals(pin.Label, pinName, StringComparison.OrdinalIgnoreCase))
                return pin;

        throw new InvalidOperationException(
            $"No pin '{pinName}' on {node.GetType().Name}. Projected: "
            + string.Join(", ", model.Pins.Select(p => $"{p.Label}({p.Direction})")));
    }

    /// <summary>
    /// Attempts a wire <b>through the editor's own command path</b> — the validator, the sink, the
    /// undo history and the wire-time bakes, exactly as dropping a wire on the canvas does. Returns
    /// the sink's verdict rather than throwing: whether the editor <i>accepts</i> a wire is the thing
    /// under test.
    /// </summary>
    public static NodeEditor.Core.Interfaces.GraphCommandResult TryLink(
        Document doc, Node fromNode, string fromPin, Node toNode, string toPin)
        => doc.Sink.Apply(new GraphCommand.AddLink(
            IdGenerator.NewLinkId(), Pin(doc, fromNode, fromPin).Id, Pin(doc, toNode, toPin).Id));

    /// <summary>As <see cref="TryLink"/>, but fails the test when the editor refuses the wire.</summary>
    public static void Link(Document doc, Node fromNode, string fromPin, Node toNode, string toPin)
    {
        var result = TryLink(doc, fromNode, fromPin, toNode, toPin);
        if (!result.Success)
            throw new InvalidOperationException(
                $"The editor refused {fromNode.GetType().Name}.{fromPin} -> "
                + $"{toNode.GetType().Name}.{toPin}: {result.Message}");
    }

    // ── Axis 3: edit sequences ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Details session for <paramref name="node"/> through the <b>real drawer registry</b> —
    /// the same resolution <c>BlueprintDetailsWindow</c> performs when the designer selects a node.
    ///
    /// <para>
    /// ⚠ Constructing a drawer directly would skip the registry, and a node kind whose drawer was never
    /// registered would then be testable while being unreachable in the editor.
    /// </para>
    /// </summary>
    public static INodeEditSession Details(Document doc, Node node)
    {
        var registry = BlueprintEditorBootstrap.CreateNodeDrawerRegistry(
            BuiltInChannelCommandCatalog.Instance,
            BuiltInEngineEventCatalog.Instance,
            doc.Edit,
            new NoopPredicateCompiler(),
            new EqsTemplateRegistry());

        var drawer = registry.GetDrawerFor(node)
            ?? throw new InvalidOperationException(
                $"No Details drawer is registered for {node.GetType().Name} — the designer cannot edit it.");
        return drawer.CreateSession(node, doc.Asset);
    }

    /// <summary>
    /// Stands in for the replay-browser predicate compiler, which only the <c>When</c> drawer consults
    /// and which no test here exercises. Present solely so the <b>real</b> registry factory can be used
    /// rather than a hand-assembled subset of it.
    /// </summary>
    private sealed class NoopPredicateCompiler : Fdp.Toolkit.ReplayBrowser.Search.IPredicateCompiler
    {
        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileComponentPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public Func<Fdp.Core.EntityRepository, Fdp.Core.Entity, bool> CompileEntityPredicate(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => (_, _) => true;

        public IReadOnlyList<Type> ExtractMandatoryComponents(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto predicate) => Array.Empty<Type>();
    }

    /// <summary>
    /// Declares a graph output through <see cref="GraphSignatureEditModel"/> — the same model the
    /// Graph Signature window and the Return node's Details table drive. ⚠ Not <c>graph.Outputs.Add</c>.
    /// </summary>
    public static void AddOutput(Graph graph, string name, string typeId)
        => new GraphSignatureEditModel(graph, isOutputs: true, onChanged: () => { })
            .AddParameter(name, typeId);

    /// <summary>Adds a node of <paramref name="kindId"/> through the sink's command path.</summary>
    public static Node AddNode(
        BlueprintCommandSink sink, Graph graph, string kindId,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        var id = Guid.NewGuid();
        sink.Apply(new GraphCommand.AddNode(
            new NodeId(id), new NodeKindKey(kindId), Vector2.Zero, properties));

        return graph.Nodes.FirstOrDefault(n => n.Id == id)
            ?? throw new InvalidOperationException(
                $"AddNode('{kindId}') did not produce a node — the sink rejected the kind id.");
    }

    // ── Generation: run the REAL generator, then Roslyn ───────────────────────────────────────────

    /// <summary>The outcome of pushing authored assets through the real generator and then Roslyn.</summary>
    public sealed record MatrixResult(
        IReadOnlyList<Diagnostic> GeneratorDiagnostics,
        IReadOnlyList<Diagnostic> RoslynDiagnostics,
        IReadOnlyList<string>     GeneratedSources)
    {
        public IEnumerable<Diagnostic> GeneratorErrors =>
            GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

        public IEnumerable<Diagnostic> RoslynErrors =>
            RoslynDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

        public bool Clean => !GeneratorErrors.Any() && !RoslynErrors.Any();

        /// <summary>Every error, formatted for an assertion message that is actually diagnosable.</summary>
        public string Report()
        {
            var parts = GeneratorErrors.Select(d => $"  [generator] {d.Id}: {d.GetMessage()}")
                .Concat(RoslynErrors.Select(d => $"  [roslyn] {d.Id}: {d.GetMessage()}"))
                .ToList();
            return parts.Count == 0 ? "  (no errors)" : string.Join(Environment.NewLine, parts);
        }
    }

    /// <summary>
    /// Serializes each asset exactly as the editor's save path does
    /// (<see cref="BlueprintJsonServices.Serialize"/>), feeds them to
    /// <c>BlueprintIncrementalGenerator</c> as <c>AdditionalTexts</c>, then compiles the generated trees
    /// with real Roslyn.
    ///
    /// <para>
    /// ⭐ Both halves are load-bearing. The generator step catches compiler diagnostics
    /// (<c>BP1300</c> …); the Roslyn step catches <c>CS…</c> failures in the emitted C# — which is how
    /// BP-117's <c>CS0126</c> and BP-112's <c>CS9191</c> escaped every suite that only checked
    /// <c>Succeeded</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠ All assets are passed together, so sibling resolution (peer calls) works exactly as it does in
    /// a real build.
    /// </para>
    /// </summary>
    public static MatrixResult Generate(params BlueprintAsset[] assets)
    {
        var texts = assets
            .Select(a => (AdditionalText)new InMemoryAdditionalText(
                $"/authoring/{a.Name}.bp.json", SaveToText(a)))
            .ToImmutableArray();

        var references = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver
            .ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies())
            .Resolve();

        var seed = CSharpCompilation.Create(
            "AuthoringMatrix.Seed",
            Array.Empty<SyntaxTree>(),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
            .Create(new Hrot.Blueprints.Generators.BlueprintIncrementalGenerator())
            .AddAdditionalTexts(texts)
            .RunGenerators(seed);

        var run     = driver.GetRunResult();
        var sources = run.GeneratedTrees.Select(t => t.ToString()).ToList();

        // Compile the generated C# for real. Without this step a generator that emits invalid code
        // still "passes" — the gap that hid BP-117 (CS0126) and BP-112 (CS9191).
        var roslyn = new List<Diagnostic>();
        if (sources.Count > 0)
        {
            var trees = sources.Select((src, i) => CSharpSyntaxTree.ParseText(
                Microsoft.CodeAnalysis.Text.SourceText.From(src, System.Text.Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.Latest),
                path: $"AuthoringMatrix_{i}.g.cs")).ToArray();

            var compilation = CSharpCompilation.Create(
                "AuthoringMatrix.Generated",
                trees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    deterministic: true,
                    allowUnsafe: true));

            using var peStream = new MemoryStream();
            roslyn.AddRange(compilation.Emit(peStream).Diagnostics);
        }

        return new MatrixResult(run.Diagnostics, roslyn, sources);
    }

    /// <summary>
    /// Serializes through the editor's <b>real save command</b> and reads the bytes back.
    ///
    /// <para>
    /// ⭐ <b>Batch 27 — this used to call <c>BlueprintJsonServices.Serialize</c> directly, and that was
    /// a hole in the harness.</b> The editor never saves that way:
    /// <see cref="SaveActiveBlueprintCommand.Save"/> first <b>canonicalizes every link endpoint</b> to
    /// its pin's deterministic GUID and only then strips the projection-only pin lists. A test that
    /// skips that step compiles a JSON shape the editor never writes — so a defect in the
    /// canonicalization, or in what it can and cannot rewrite, is invisible. BP-202's dangling link is
    /// exactly such a defect: a link whose pin no longer exists is not in the rewrite map and survives
    /// verbatim into the file.
    /// </para>
    ///
    /// <para>
    /// ⚠ Round-trips through a real temp file because <c>Save</c> is the entry point the editor uses;
    /// exposing an in-memory sibling would be a second save path, which is the shape of defect this
    /// whole harness exists to catch.
    /// </para>
    /// </summary>
    private static string SaveToText(BlueprintAsset asset)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"authoring-path-{Guid.NewGuid():N}.bp.json");
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);
            return File.ReadAllText(path);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { /* best effort */ }
        }
    }

    // ── Axis 1: run the result and assert VALUES ──────────────────────────────────────────────────

    /// <summary>
    /// Round-trips <paramref name="asset"/> through the editor's <b>save</b> and the loader's
    /// <b>deserialize</b>, returning the asset a designer would actually have after saving and
    /// reopening.
    ///
    /// <para>
    /// ⭐ <b>Required before ticking, not a nicety.</b> An in-memory authored asset can carry state the
    /// saved file never does — most importantly a populated <c>Node.Pins</c> list (BP-208). Ticking the
    /// in-memory object would run a shape that never reaches disk, and would hide exactly the
    /// save-shaped defects this harness exists for.
    /// </para>
    /// </summary>
    public static BlueprintAsset SaveAndReload(BlueprintAsset asset)
        => BlueprintJsonServices.Deserialize(SaveToText(asset))
           ?? throw new InvalidOperationException(
               $"The editor's own save output for '{asset.Name}' did not deserialize.");

    /// <summary><see cref="AdditionalText"/> over an in-memory string — the generator's only input channel.</summary>
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _text;
        public InMemoryAdditionalText(string path, string text) { Path = path; _text = text; }
        public override string Path { get; }
        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken ct = default)
            => Microsoft.CodeAnalysis.Text.SourceText.From(_text, System.Text.Encoding.UTF8);
    }
}
