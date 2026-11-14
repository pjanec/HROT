using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.Params;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐ <c>CE-2136</c> (B4a, docs/DESIGN_Orders_Params_Entity_Classes.md §12) — consumes <c>*.params.json</c> AdditionalTexts and emits each
/// params DTO asset as a <c>[ParamsDto]</c> struct (<see cref="ParamsDtoEmitCore"/>). Per-asset failure → a diagnostic on that asset,
/// never a throw and never a failed sibling (the <c>BTreeJsonGenerator</c> rule).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ParamsDtoJsonGenerator : IIncrementalGenerator
{
    /// <summary>Diagnostic code for a params DTO asset that does not parse or validate.</summary>
    public const string DiagnosticId = "PARAMS0001";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(static at => at.Path.EndsWith(ParamsDtoJson.Extension, StringComparison.OrdinalIgnoreCase))
            .Select(static (at, ct) => (at.Path, Text: at.GetText(ct)?.ToString() ?? string.Empty));

        context.RegisterSourceOutput(files, static (spc, file) =>
        {
            var (source, hint, errors) = Generate(file.Path, file.Text);
            foreach (var e in errors)
                // Descriptor created inline to avoid RS2008 (release tracking required for static fields) — the sibling generators' rule.
                spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                    DiagnosticId, "Invalid params DTO asset", "{0}: {1}", "ParamsDto", DiagnosticSeverity.Error, isEnabledByDefault: true),
                    Location.None, System.IO.Path.GetFileName(file.Path), e));
            if (source != null) spc.AddSource(hint!, source);
        });
    }

    /// <summary>The generated source and hint name for one asset, or its errors — the generator's whole decision, testable without
    /// Roslyn.</summary>
    public static (string? Source, string? HintName, string[] Errors) Generate(string path, string text)
    {
        ParamsDtoAssetDto dto;
        try { dto = ParamsDtoJson.Deserialize(text); }
        catch (Exception ex) { return (null, null, new[] { "does not parse: " + ex.Message }); }

        var errors = ParamsDtoEmitCore.Validate(dto)
            .Concat(FileNameErrors(path, dto)).ToArray();
        if (errors.Length > 0) return (null, null, errors);
        return (ParamsDtoEmitCore.Emit(dto, path), ParamsDtoEmitCore.TypeIdOf(dto) + ".params.g.cs", Array.Empty<string>());
    }

    /// <summary>⭐ <c>CE-3185</c> (F1) — the asset's file name IS its DTO's name (<c>&lt;Name&gt;.params.json</c>), so the type a behaviour
    /// names can be found by eye; a mismatch is an error, never a silent rename.</summary>
    private static string[] FileNameErrors(string path, ParamsDtoAssetDto dto)
    {
        string file = System.IO.Path.GetFileName(path);
        string stem = file.EndsWith(ParamsDtoJson.Extension, StringComparison.OrdinalIgnoreCase)
            ? file.Substring(0, file.Length - ParamsDtoJson.Extension.Length) : file;
        return string.Equals(stem, dto.Name?.Trim(), StringComparison.Ordinal)
            ? Array.Empty<string>()
            : new[] { $"the file is named '{stem}' but the DTO is '{dto.Name}' — a params DTO asset's file name is its Name (CE-3185)" };
    }
}
