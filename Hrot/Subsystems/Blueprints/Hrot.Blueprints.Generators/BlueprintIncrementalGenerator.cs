using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Generators;

/// <summary>
/// Roslyn incremental source generator for Blueprint .bp.json assets.
/// Reads AdditionalFiles matching *.bp.json and generates C# thunks.
/// </summary>
[Generator]
public sealed class BlueprintIncrementalGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Placeholder: full implementation in later milestones.
        // Register interest in *.bp.json AdditionalFiles.
        var bpJsonFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".bp.json",
                System.StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(bpJsonFiles, static (spc, file) =>
        {
            var text = file.GetText(spc.CancellationToken);
            if (text == null)
                return;

            // Validate JSON is parseable; report diagnostic if not.
            var content = text.ToString();
            if (string.IsNullOrWhiteSpace(content))
                return;

            // Simple well-formedness check: attempt to find the opening brace.
            var trimmed = content.TrimStart();
            if (!trimmed.StartsWith("{"))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "BP0001",
                        title: "Invalid Blueprint JSON",
                        messageFormat: "Blueprint file '{0}' does not contain valid JSON: expected '{{' at start.",
                        category: "Blueprints",
                        defaultSeverity: DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    System.IO.Path.GetFileName(file.Path)));
                return;
            }

            // Full parse + codegen is implemented in later milestones.
        });
    }
}
