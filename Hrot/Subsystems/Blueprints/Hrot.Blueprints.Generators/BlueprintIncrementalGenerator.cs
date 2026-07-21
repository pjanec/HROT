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
            if (result.GeneratedSource == null || !result.Succeeded)
            {
                foreach (var diag in result.Diagnostics)
                    spc.ReportDiagnostic(ToRoslynDiagnostic(diag));
                return;
            }
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
            ClrSignatureResolver: new RoslynClrSignatureResolver(compilation));

        try
        {
            return compiler.Compile(asset, options);
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
        var descriptor = new DiagnosticDescriptor(
            id:                 diag.Code,
            title:              diag.Code,
            messageFormat:      diag.Message,
            category:           "Blueprints",
            defaultSeverity:    diag.IsError
                                    ? Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                                    : Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        return Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, Location.None);
    }
}
