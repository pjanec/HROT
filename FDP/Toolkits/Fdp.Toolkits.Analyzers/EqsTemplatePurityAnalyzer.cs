using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// Enforces EQS template purity constraints at compile time.
    ///
    /// EQS_001: The [EqsTemplate] class must contain a static Build(IEqsTemplateBuilder)
    ///          overload. Any Build() method that is not a valid generator overload and the
    ///          class has no valid generator overload emits EQS_001.
    /// EQS_002: Build(IEqsTemplateBuilder) on an [EqsTemplate] class must not read static
    ///          non-constant fields declared in the same class (must be structurally pure).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EqsTemplatePurityAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor EQS001_BadSignature = new DiagnosticDescriptor(
            id: "EQS_001",
            title: "EqsTemplate Build() must be static with single IEqsTemplateBuilder parameter",
            messageFormat: "Method '{0}' in [EqsTemplate] class must be static with a single IEqsTemplateBuilder parameter",
            category: "Fdp.Eqs",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor EQS002_ImpureAccess = new DiagnosticDescriptor(
            id: "EQS_002",
            title: "EqsTemplate Build() must be pure",
            messageFormat: "Method '{0}' in [EqsTemplate] class reads non-constant state. Build() must be pure.",
            category: "Fdp.Eqs",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(EQS001_BadSignature, EQS002_ImpureAccess);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            // Only analyze classes with [EqsTemplateAttribute].
            bool hasEqsTemplate = type.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "EqsTemplateAttribute");

            if (!hasEqsTemplate) return;

            var buildMethods = type.GetMembers("Build")
                .OfType<IMethodSymbol>()
                .ToList();

            if (buildMethods.Count == 0) return;

            // Check if a valid generator-compatible overload exists:
            // must be static with exactly one IEqsTemplateBuilder parameter.
            bool hasValidGeneratorOverload = buildMethods.Any(m =>
                m.IsStatic
                && m.Parameters.Length == 1
                && m.Parameters[0].Type.Name == "IEqsTemplateBuilder");

            if (!hasValidGeneratorOverload)
            {
                // No valid generator overload: flag every Build() that does not satisfy
                // the required signature so the developer knows what to fix.
                foreach (var method in buildMethods)
                {
                    bool isValid = method.IsStatic
                        && method.Parameters.Length == 1
                        && method.Parameters[0].Type.Name == "IEqsTemplateBuilder";

                    if (!isValid)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            EQS001_BadSignature,
                            method.Locations.FirstOrDefault(),
                            method.Name));
                    }
                }
            }

            // EQS_002: check the valid generator overload for static non-const field reads.
            var generatorOverload = buildMethods.FirstOrDefault(m =>
                m.IsStatic
                && m.Parameters.Length == 1
                && m.Parameters[0].Type.Name == "IEqsTemplateBuilder");

            if (generatorOverload == null) return;

            var staticMutableFields = new HashSet<string>(
                type.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => f.IsStatic && !f.IsConst)
                    .Select(f => f.Name));

            if (staticMutableFields.Count == 0) return;

            // Inspect method body for IdentifierNameSyntax references to those fields.
            var syntax = generatorOverload.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .FirstOrDefault();

            if (syntax == null) return;

            var identifiers = syntax.DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                if (staticMutableFields.Contains(id.Identifier.Text))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        EQS002_ImpureAccess,
                        generatorOverload.Locations.FirstOrDefault(),
                        generatorOverload.Name));
                    return;
                }
            }
        }
    }
}
