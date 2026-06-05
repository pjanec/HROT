using System;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Roslyn IncrementalGenerator that consumes <c>*.hsm.json</c> AdditionalTexts
/// and emits <c>CreateBuilder()</c> + <c>[HsmDefinition]</c> thunk (NO <c>[HsmLayout]</c>)
/// to <c>obj/GeneratedFiles/{Name}.g.cs</c>.
///
/// Design §6.2 (PU-202): JSON-owned assets generate topology core only; layout lives in JSON.
/// Per-asset deserialization failure → Roslyn diagnostic (never throws, never fails siblings).
/// Mirrors <see cref="Hrot.Blueprints.Generators.BlueprintIncrementalGenerator"/> control flow.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HsmJsonGenerator : IIncrementalGenerator
{
    /// <summary>Diagnostic code for HSM JSON parse/emit errors.</summary>
    public const string DiagnosticId = "HSM0001";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider: raw file text from *.hsm.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".hsm.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                });

        // Per-asset: deserialize → emit topology core → register source output
        context.RegisterSourceOutput(rawFiles, static (spc, item) =>
        {
            GenerateOneAsset(spc, item.Path, item.Text);
        });
    }

    private static void GenerateOneAsset(SourceProductionContext spc, string path, string text)
    {
        // Deserialize — failure becomes a diagnostic, never throws, never fails siblings.
        HsmAssetDto? dto;
        try
        {
            dto = HsmJsonServices.Deserialize(text);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during deserialization: " + ex.Message));
            return;
        }

        if (dto is null)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Deserialization returned null (empty or invalid JSON)."));
            return;
        }

        // Emit topology core (CreateBuilder + [HsmDefinition] thunk, NO [HsmLayout]).
        string source;
        try
        {
            source = HsmEmitCore.EmitTopologyCore(dto);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during code generation: " + ex.Message));
            return;
        }

        string baseName = System.IO.Path.GetFileNameWithoutExtension(
                              System.IO.Path.GetFileNameWithoutExtension(path));

        // Topology core: {Name}.g.cs
        spc.AddSource(baseName + ".g.cs", source);

        // Bridge: {Name}.Registrar.g.cs  (additive, separate hint name — PU-203, §14 item 3)
        string bridge;
        try
        {
            bridge = HsmBridgeEmitCore.EmitBridge(dto);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during bridge code generation: " + ex.Message));
            return;
        }

        spc.AddSource(baseName + ".Registrar.g.cs", bridge);
    }

    /// <summary>Creates a Roslyn diagnostic for an HSM JSON parse/emit error.</summary>
    internal static Diagnostic MakeParseErrorDiagnostic(string path, string detail)
    {
        // Descriptor created inline to avoid RS2008 (release tracking required for static fields).
        // Mirrors BlueprintIncrementalGenerator.ToRoslynDiagnostic pattern.
        var descriptor = new DiagnosticDescriptor(
            id:                 DiagnosticId,
            title:              "HSM JSON parse error",
            messageFormat:      "Failed to process '{0}': {1}",
            category:           "HsmJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, detail);
    }
}
