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
    /// Builds a <see cref="BlueprintCommandSink"/> over <paramref name="asset"/>/<paramref name="graph"/>
    /// with the same collaborators the editor host wires up. Mirrors
    /// <c>BlueprintCommandSinkTests.MakeSut</c>.
    /// </summary>
    public static BlueprintCommandSink Sink(BlueprintAsset asset, Graph graph)
    {
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        // ⭐ The REAL palette registry, not an empty one. With `new NodeKindRegistry()` the sink's
        // CreateAssetNode falls through to a generic FunctionCallNode for every unknown kind id, so the
        // matrix would silently author the wrong nodes and prove nothing.
        var catalog    = new BlueprintNodeCatalog(BlueprintEditorBootstrap.CreatePaletteRegistry());
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var edit       = new EditService { Context = new EditServiceContext(history, _ => { }) };

        return new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, edit, markDirty: _ => { });
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
                $"/authoring/{a.Name}.bp.json", BlueprintJsonServices.Serialize(a)))
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
