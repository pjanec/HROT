using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// CE-367 -- author-facing diagnostics for hand-written BTree hosting sites.
    /// See DESIGN_Occurrence_Scoped_Storage.md §33.6 / §33.9 item 6.
    ///
    /// <para>
    /// WHY THIS EXISTS. A hosted child's tree-state slot key is
    /// ComputeTreeStateKey(host, SITE, child), and the SITE comes from the visualId passed to
    /// .Subtree(...). BTreeBuilder.Subtree declares `Guid visualId = default`, so omitting it is
    /// silent and legal -- and then BTreeHostedSites falls back to an ORDINAL derived from the
    /// node's index.
    /// </para>
    ///
    /// <para>
    /// ComputeSiteId's own documentation says the site is "from the author's stable node id -- NOT
    /// an ordinal". Inserting another Subtree node ABOVE one shifts that ordinal, which changes the
    /// slot key, which resets that child's cursor once. Bounded and recoverable -- a recompile, one
    /// lost cursor, never a wrong child -- but the fix is free and belongs to the author: pass
    /// visualId:. This warning is the whole mitigation, so it has to actually fire.
    /// </para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SubtreeHostingAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor MissingVisualId = new DiagnosticDescriptor(
            id: "BEH010",
            title: "Hosting .Subtree() has no stable site id",
            messageFormat: "Subtree('{0}') has no visualId, so its hosted child's slot key falls back to the node ORDINAL — inserting a Subtree node above it will reset that child's cursor. Pass visualId: to make the site stable.",
            category: "BehaviorAuthoring",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor EmptyChildName = new DiagnosticDescriptor(
            id: "BEH011",
            title: "Hosting .Subtree() names no child",
            messageFormat: "Subtree(...) was given an empty child name, so no hosting site is planned for it and the node will fail at runtime",
            category: "BehaviorAuthoring",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MissingVisualId, EmptyChildName);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        }

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            if (invocation.Expression is not MemberAccessExpressionSyntax member) return;
            if (member.Name.Identifier.ValueText != "Subtree") return;

            // Confirm it really is BTreeBuilder.Subtree rather than any same-named method.
            var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol == null) return;
            if (symbol.ContainingType?.Name != "BTreeBuilder") return;

            var args = invocation.ArgumentList.Arguments;

            // The child name is the first positional argument (or named treeName).
            var nameArg = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "treeName")
                          ?? args.FirstOrDefault(a => a.NameColon == null);
            if (nameArg != null)
            {
                var constant = context.SemanticModel.GetConstantValue(nameArg.Expression);
                if (constant.HasValue && constant.Value is string s && string.IsNullOrWhiteSpace(s))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        EmptyChildName, nameArg.GetLocation()));
                    return;
                }
            }

            // visualId is either named, or the second positional argument.
            bool hasVisualId =
                args.Any(a => a.NameColon?.Name.Identifier.ValueText == "visualId")
                || args.Count(a => a.NameColon == null) >= 2;

            if (hasVisualId) return;

            string childName = "?";
            if (nameArg != null)
            {
                var constant = context.SemanticModel.GetConstantValue(nameArg.Expression);
                if (constant.HasValue && constant.Value is string s) childName = s;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                MissingVisualId, invocation.GetLocation(), childName));
        }
    }
}
