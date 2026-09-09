using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Assets;
using BpDiagnostic = Hrot.Blueprints.Core.Compiler.Diagnostics.Diagnostic;
// Aliased because `Microsoft.CodeAnalysis` is also imported and declares DiagnosticSeverity too;
// an unqualified reference in ToRoslynDiagnostic would be CS0104.
using BpDiagnosticSeverity = Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity;
using BpCompiler = Hrot.Blueprints.Core.Compiler.BlueprintCompiler;

namespace Hrot.Blueprints.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class BlueprintIncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider 1 -- raw file text from .bp.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(at => at.Path.EndsWith(".bp.json", System.StringComparison.OrdinalIgnoreCase))
                .Select((at, ct) =>
                {
                    var text = at.GetText(ct)?.ToString() ?? "";
                    return (at.Path, text);
                });

        // Provider 2 -- per-asset signature (lightweight parse)
        IncrementalValuesProvider<BlueprintSignature> signatures =
            rawFiles.Select((rf, ct) => BlueprintSignatureParser.Parse(rf.Path, rf.Text));

        // Provider 3 -- collected sibling catalog
        IncrementalValueProvider<ImmutableArray<BlueprintSignature>> siblingCatalog =
            signatures.Collect();

        // Provider 4 -- per-asset full compile combined with sibling catalog + the Compilation.
        // The Compilation is threaded through so Stage0 can resolve same-assembly FunctionCall target
        // signatures via the semantic model (RoslynClrSignatureResolver) — reflection cannot see the
        // curated helper types in the assembly currently being compiled. (Combining CompilationProvider
        // costs fine-grained incrementality, but this generator already collapses on any sibling change
        // via siblingCatalog.Collect(), so the tradeoff is consistent with the existing design.)
        IncrementalValuesProvider<CompileResult> compileResults =
            rawFiles.Combine(siblingCatalog)
                    .Combine(context.CompilationProvider)
                    .Select((pair, ct) =>
                    {
                        var ((rawFile, siblings), compilation) = pair;
                        return CompileOneAsset(rawFile.Path, rawFile.Text, siblings, compilation, ct);
                    });

        // Register source output
        context.RegisterSourceOutput(compileResults, static (spc, result) =>
        {
            // BP-121: drain the diagnostic sink UNCONDITIONALLY, before deciding success/failure.
            //
            // ⚠ This used to sit inside the failure branch, so a compile that SUCCEEDED reported
            // nothing at all. Every warning the blueprint compiler produced -- BP1657 (implicit
            // default return), BP4001 (unwired data pin), BP3010 (orphan node eliminated) -- was
            // computed, mapped to a Roslyn severity by ToRoslynDiagnostic, and then thrown away.
            // A designer authoring in the editor got NO warnings from a real build, ever.
            //
            // ⭐ It also silently neutered BP-117: BP1657 was deliberately downgraded to a Warning so
            // that it would warn instead of blocking, and in the real build it then did neither.
            //
            // ⚠ Hrot.AI.Behaviors sets TreatWarningsAsErrors, so making these visible would turn every
            // pre-existing BP4001/BP3010 into a hard build error. The BP ids are therefore listed in
            // that project's <WarningsNotAsErrors>, which is the correct lever: the warnings stay
            // VISIBLE in build output (the entire point) while TreatWarningsAsErrors keeps protecting
            // against C# warnings. Lowering our severities to Info instead would hide them in normal
            // builds and re-create this very bug one level down.
            foreach (var diag in result.Diagnostics)
                spc.ReportDiagnostic(ToRoslynDiagnostic(diag));

            if (result.GeneratedSource == null || !result.Succeeded)
                return;

            spc.AddSource(result.GeneratedFileName ?? "Blueprint.g.cs", result.GeneratedSource);
        });
    }

    private static CompileResult CompileOneAsset(
        string path,
        string text,
        ImmutableArray<BlueprintSignature> siblings,
        Compilation compilation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        BlueprintAsset? asset;
        try
        {
            asset = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(text);
        }
        catch (Exception ex)
        {
            // BP-1: never swallow — surface the full exception (type + message + stack + inner)
            // so the real deserialization failure (e.g. System.Text.Json polymorphism in the
            // analyzer host) is visible in the build output instead of a generic message.
            return FailedParse(path, ex.ToString());
        }

        if (asset is null)
            return FailedParse(path, "BlueprintJsonServices.Deserialize returned null (no exception thrown).");

        var compiler = new BpCompiler();
        var options = new CompileOptions(
            Mode:              CompilerMode.Release,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings.ToList(),
            EmitPdbWithEmbeddedSource: false,
            ClrSignatureResolver: new RoslynClrSignatureResolver(compilation),
            // ⭐⭐ S2 — the struct-size oracle. Same semantic model, same shipped algorithm as the
            //     BTree blackboard packer; injected as Func<string,int?> per the design mandate.
            StructSizeOracle: Hrot.AiEditor.Generators.StructSizeResolver
                .MakeFieldSizeDelegate(compilation));

        try
        {
            var result = compiler.Compile(asset, options);

            // BP-206: resolve the ids every diagnostic already carries into names, HERE, where the
            // asset is in hand. The alternative -- threading blueprint/graph/node names through every
            // Diagnostic.Error(...) call site -- is a hundred edits that a new call site can forget;
            // this one cannot drift.
            return result with
            {
                Diagnostics = DiagnosticIdentity.Attribute(result.Diagnostics, asset),
            };
        }
        catch (Exception ex)
        {
            return new CompileResult(
                Succeeded:         false,
                GeneratedSource:   null,
                GeneratedFileName: null,
                BlueprintId:       0,
                StructureHash:     0UL,
                DebugMap:          null,
                Diagnostics:       new[]
                {
                    BpDiagnostic.Error(DiagnosticCodes.BP0002_JsonParseError,
                        $"Blueprint '{path}' threw during compile: {ex}")
                },
                CanonicalAsset:    null,
                PortablePdb:       null,
                PortablePe:        null);
        }
    }

    private static CompileResult FailedParse(string path, string? detail = null) =>
        new CompileResult(
            Succeeded:         false,
            GeneratedSource:   null,
            GeneratedFileName: null,
            BlueprintId:       0,
            StructureHash:     0UL,
            DebugMap:          null,
            Diagnostics:       new[]
            {
                BpDiagnostic.Error(DiagnosticCodes.BP0002_JsonParseError,
                    $"Blueprint file '{path}' could not be parsed."
                    + (string.IsNullOrEmpty(detail) ? "" : $" Detail: {detail}"))
            },
            CanonicalAsset:    null,
            PortablePdb:       null,
            PortablePe:        null);

    private static Microsoft.CodeAnalysis.Diagnostic ToRoslynDiagnostic(
        BpDiagnostic diag)
    {
        // BP-206: "SmokePatrol ▸ Tick ▸ Print String: Orphan node … was eliminated." A designer reading
        // the build output can now go straight to the node; before this the message named a GUID and
        // finding it meant grepping every asset file.
        var message = string.IsNullOrEmpty(diag.Origin)
            ? diag.Message
            : diag.Origin + ": " + diag.Message;

        var descriptor = new DiagnosticDescriptor(
            id:                 diag.Code,
            title:              diag.Code,
            messageFormat:      message,
            category:           "Blueprints",
            // ⭐ BP-219. This was `diag.IsError ? Error : Warning` — a TWO-way branch over a
            // THREE-member enum, so a BpDiagnosticSeverity.Info would have surfaced as a build
            // WARNING, and under Hrot.AI.Behaviors' TreatWarningsAsErrors as a build BREAK. Latent
            // (nothing emits Info today) but it is the same missing-arm shape as BP-215/BP-216, one
            // layer out.
            //
            // ⚠⚠ Adding this arm makes something newly dangerous, and BP-121's ruling above still
            // stands: do NOT lower a designer-actionable warning (BP1657/BP4001/BP3010) to Info to
            // quieten it. Before this change that would merely have been ineffective; now it would
            // genuinely hide the diagnostic in normal builds — which is the exact bug BP-121 fixed.
            // Info is for diagnostics with nothing for the designer to act on, and BP-218 shows the
            // preferred treatment for those: retire the code, do not demote it.
            //
            // No catch-all: a future fourth member must fail loudly rather than be mis-mapped
            // (the Stage5_Schedule.MapGraphKind idiom).
            defaultSeverity:    diag.Severity switch
                                {
                                    BpDiagnosticSeverity.Error   => Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
                                    BpDiagnosticSeverity.Warning => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
                                    BpDiagnosticSeverity.Info    => Microsoft.CodeAnalysis.DiagnosticSeverity.Info,
                                    _ => throw new NotSupportedException(
                                             $"BpDiagnosticSeverity.{diag.Severity} has no Roslyn mapping. "
                                             + "Add one rather than letting it default -- a mis-mapped "
                                             + "severity either hides a real problem or breaks a build."),
                                },
            isEnabledByDefault: true);
        return Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, Location.None);
    }
}
