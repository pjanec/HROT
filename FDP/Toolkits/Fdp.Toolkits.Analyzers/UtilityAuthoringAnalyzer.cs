using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// Enforces Utility AI authoring constraints at compile time (UT0120/UT0130/UT0131/UT0143).
    ///
    /// UT0120: consideration references an unknown input name (not in the cross-assembly catalog).
    /// UT0130: Build reads disallowed runtime state (purity violation, mirrors EqsTemplatePurityAnalyzer).
    /// UT0131: weight literal outside [0, 1] in a Consider call.
    /// UT0143: PostureSelect decision defines zero options in Build.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UtilityAuthoringAnalyzer : DiagnosticAnalyzer
    {
        // Disallowed type simple-names for the UT0130 purity check (syntactic name check).
        private static readonly HashSet<string> DisallowedTypeNames = new HashSet<string>
        {
            "EntityRepository",
            "ISimulationView",
            "DateTime",
            "Random"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                SharedUtilityDiagnostics.UT0120_UnknownInput,
                SharedUtilityDiagnostics.UT0130_ImpureBuild,
                SharedUtilityDiagnostics.UT0131_WeightOutOfRange,
                SharedUtilityDiagnostics.UT0143_ZeroOptions,
                SharedUtilityDiagnostics.UT0151_ManeuverSelectInvalidContext);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Build the cross-assembly input catalog once per compilation, then analyse each type.
            context.RegisterCompilationStartAction(compilationCtx =>
            {
                var inputCatalog = BuildInputCatalog(compilationCtx.Compilation);

                // UT0130 part 1 (static mutable field reads) and UT0143 (zero options):
                // purely structural, no semantic model required.
                compilationCtx.RegisterSymbolAction(
                    sym => AnalyzeNamedTypeStructural(sym),
                    SymbolKind.NamedType);

                // UT0130 part 2 (disallowed type names), UT0131 (weight range), UT0120 (unknown input):
                // these use the SemanticModel provided by the SyntaxNodeAnalysisContext.
                compilationCtx.RegisterSyntaxNodeAction(
                    nodeCtx => AnalyzeBuildMethodNode(nodeCtx, inputCatalog),
                    SyntaxKind.MethodDeclaration);
            });
        }

        // ---- Catalog builder ----------------------------------------------------

        /// <summary>
        /// Walks the current assembly's GlobalNamespace plus all referenced assemblies and
        /// collects the Name value from every [UtilityInput]-attributed method.
        /// Satisfies SC-P2-03-3: cross-assembly resolution.
        /// Using compilation.Assembly.GlobalNamespace (rather than the merged
        /// compilation.GlobalNamespace) ensures source-defined types are always found,
        /// including those in in-memory test compilations.
        /// </summary>
        private static HashSet<string> BuildInputCatalog(Compilation compilation)
        {
            var catalog = new HashSet<string>(StringComparer.Ordinal);
            // Current compilation's own source/assembly.
            CollectInputNames(compilation.Assembly.GlobalNamespace, catalog);
            // Referenced assemblies (cross-assembly inputs such as Fdp.Toolkits.dll).
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
                    CollectInputNames(asm.GlobalNamespace, catalog);
            }
            return catalog;
        }

        private static void CollectInputNames(INamespaceSymbol ns, HashSet<string> catalog)
        {
            foreach (var member in ns.GetMembers())
            {
                var childNs = member as INamespaceSymbol;
                if (childNs != null)
                {
                    CollectInputNames(childNs, catalog);
                    continue;
                }

                var type = member as INamedTypeSymbol;
                if (type == null) continue;

                foreach (var typeMember in type.GetMembers())
                {
                    var method = typeMember as IMethodSymbol;
                    if (method == null) continue;

                    foreach (var attr in method.GetAttributes())
                    {
                        if (attr.AttributeClass == null) continue;
                        if (attr.AttributeClass.Name != "UtilityInputAttribute") continue;
                        if (attr.ConstructorArguments.Length == 0) continue;

                        var name = attr.ConstructorArguments[0].Value as string;
                        if (!string.IsNullOrEmpty(name))
                            catalog.Add(name);
                    }
                }
            }
        }

        // ---- SymbolAction: structural checks (no SemanticModel) -----------------

        private static void AnalyzeNamedTypeStructural(SymbolAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            if (!HasUtilityDecisionAttribute(type)) return;

            var buildMethod = FindBuildMethod(type);
            if (buildMethod == null) return;

            var buildSyntax = buildMethod.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .FirstOrDefault();

            if (buildSyntax == null) return;

            // UT0130 part 1: static non-const field reads (EqsTemplatePurityAnalyzer EQS_002 pattern).
            CheckStaticFieldReads(context, type, buildMethod, buildSyntax);

            // UT0143: PostureSelect with zero options.
            CheckZeroOptions(context, type, buildMethod, buildSyntax);

            // UT0151: ManeuverSelect must not bind Candidate or Target context.
            CheckManeuverSelectContextBinding(context, type, buildMethod, buildSyntax);
        }

        // ---- SyntaxNodeAction: semantic checks (SemanticModel available) ---------

        private static void AnalyzeBuildMethodNode(
            SyntaxNodeAnalysisContext context,
            HashSet<string> inputCatalog)
        {
            var methodDecl = (MethodDeclarationSyntax)context.Node;

            // Filter: must be named "Build".
            if (methodDecl.Identifier.Text != "Build") return;

            // Must be static.
            bool isStatic = methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            if (!isStatic) return;

            // Must have exactly one parameter named IUtilityDecisionBuilder.
            if (methodDecl.ParameterList.Parameters.Count != 1) return;
            var paramTypeSyntax = methodDecl.ParameterList.Parameters[0].Type;
            if (paramTypeSyntax == null) return;
            var paramTypeIdent = paramTypeSyntax as IdentifierNameSyntax;
            if (paramTypeIdent == null || paramTypeIdent.Identifier.Text != "IUtilityDecisionBuilder") return;

            // Containing type must have [UtilityDecision].
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            if (methodSymbol == null) return;
            var containingType = methodSymbol.ContainingType;
            if (containingType == null) return;
            if (!HasUtilityDecisionAttribute(containingType)) return;

            // UT0130 part 2: disallowed type names (syntactic name check on identifiers).
            CheckDisallowedTypeNames(context, methodSymbol, methodDecl);

            // UT0131: weight out of range.
            CheckWeights(context, containingType, methodDecl);

            // UT0120: unknown input name.
            CheckUnknownInputs(context, containingType, methodDecl, inputCatalog);
        }

        // ---- UT0130 part 1: static mutable field reads --------------------------
        // Mirrors EqsTemplatePurityAnalyzer.EQS_002 logic verbatim.

        private static void CheckStaticFieldReads(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            IMethodSymbol buildMethod,
            SyntaxNode buildSyntax)
        {
            var staticMutableFields = new HashSet<string>(
                type.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => f.IsStatic && !f.IsConst)
                    .Select(f => f.Name));

            if (staticMutableFields.Count == 0) return;

            var identifiers = buildSyntax.DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var id in identifiers)
            {
                if (staticMutableFields.Contains(id.Identifier.Text))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SharedUtilityDiagnostics.UT0130_ImpureBuild,
                        buildMethod.Locations.FirstOrDefault(),
                        buildMethod.Name));
                    return;
                }
            }
        }

        // ---- UT0130 part 2: disallowed type name check ---------------------------
        // Syntactic name check: if an identifier in the Build body matches a disallowed
        // runtime type name, emit UT0130.

        private static void CheckDisallowedTypeNames(
            SyntaxNodeAnalysisContext context,
            IMethodSymbol methodSymbol,
            MethodDeclarationSyntax methodDecl)
        {
            SyntaxNode bodyRoot = (SyntaxNode)methodDecl.Body ?? methodDecl.ExpressionBody;
            if (bodyRoot == null) return;

            foreach (var identifier in bodyRoot.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (DisallowedTypeNames.Contains(identifier.Identifier.Text))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SharedUtilityDiagnostics.UT0130_ImpureBuild,
                        methodSymbol.Locations.FirstOrDefault(),
                        methodSymbol.Name));
                    return;
                }
            }
        }

        // ---- UT0131: weight out of range -----------------------------------------

        private static void CheckWeights(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol containingType,
            MethodDeclarationSyntax methodDecl)
        {
            SyntaxNode bodyRoot = (SyntaxNode)methodDecl.Body ?? methodDecl.ExpressionBody;
            if (bodyRoot == null) return;

            var invocations = bodyRoot.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(IsConsiderInvocation);

            foreach (var invocation in invocations)
            {
                var args = invocation.ArgumentList.Arguments;
                // Consider(InputRef input, float weight, ResponseCurve curve) -- weight is arg index 1.
                if (args.Count < 2) continue;

                var weightArg = args[1].Expression;
                var constant = context.SemanticModel.GetConstantValue(weightArg);

                if (!constant.HasValue) continue;

                float weightValue;
                try
                {
                    weightValue = Convert.ToSingle(constant.Value);
                }
                catch
                {
                    continue;
                }

                if (weightValue < 0f || weightValue > 1f)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SharedUtilityDiagnostics.UT0131_WeightOutOfRange,
                        weightArg.GetLocation(),
                        weightValue,
                        containingType.Name));
                }
            }
        }

        // ---- UT0120: unknown input name ------------------------------------------

        private static void CheckUnknownInputs(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol containingType,
            MethodDeclarationSyntax methodDecl,
            HashSet<string> inputCatalog)
        {
            SyntaxNode bodyRoot = (SyntaxNode)methodDecl.Body ?? methodDecl.ExpressionBody;
            if (bodyRoot == null) return;

            var invocations = bodyRoot.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(IsConsiderInvocation);

            foreach (var considerInvocation in invocations)
            {
                var args = considerInvocation.ArgumentList.Arguments;
                if (args.Count == 0) continue;

                var firstArg = args[0].Expression;
                string inputName = TryExtractInAccessorName(firstArg, context.SemanticModel);
                if (inputName == null) continue;

                if (!inputCatalog.Contains(inputName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SharedUtilityDiagnostics.UT0120_UnknownInput,
                        firstArg.GetLocation(),
                        containingType.Name,
                        inputName));
                }
            }
        }

        /// <summary>
        /// Attempts to extract the input name from an expression such as
        /// In.AmmoFraction(...) or In.EqsTopScore("t").
        ///
        /// Strategy:
        /// 1. If the expression resolves via SemanticModel to a method with [UtilityInput],
        ///    return the Name from the attribute.
        /// 2. Fall back to the method identifier text.
        /// Returns null if the expression is not an In.Name pattern.
        /// </summary>
        private static string TryExtractInAccessorName(ExpressionSyntax expr, SemanticModel semanticModel)
        {
            InvocationExpressionSyntax invocation = expr as InvocationExpressionSyntax;
            MemberAccessExpressionSyntax memberAccess = null;

            if (invocation != null)
                memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            else
                memberAccess = expr as MemberAccessExpressionSyntax;

            if (memberAccess == null) return null;

            // Check that the receiver is "In".
            var receiver = memberAccess.Expression as IdentifierNameSyntax;
            if (receiver == null || receiver.Identifier.Text != "In") return null;

            // Try to resolve via semantic model to get the [UtilityInput] Name attribute.
            SymbolInfo symbolInfo = invocation != null
                ? semanticModel.GetSymbolInfo(invocation)
                : semanticModel.GetSymbolInfo(memberAccess);

            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol != null)
            {
                foreach (var attr in methodSymbol.GetAttributes())
                {
                    if (attr.AttributeClass == null) continue;
                    if (attr.AttributeClass.Name != "UtilityInputAttribute") continue;
                    if (attr.ConstructorArguments.Length == 0) continue;
                    var nameArg = attr.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrEmpty(nameArg)) return nameArg;
                }
            }

            // Fall back: use the identifier text as the input name.
            return memberAccess.Name.Identifier.Text;
        }

        // ---- UT0143: PostureSelect zero options ----------------------------------

        private static void CheckZeroOptions(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            IMethodSymbol buildMethod,
            SyntaxNode buildSyntax)
        {
            if (!IsPostureSelectDecision(type)) return;

            int optionCount = buildSyntax.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(inv =>
                {
                    var name = inv.Expression as IdentifierNameSyntax;
                    if (name != null)
                        return name.Identifier.Text == "Option"
                            || name.Identifier.Text == "CandidateOption";
                    var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
                    if (memberAccess != null)
                        return memberAccess.Name.Identifier.Text == "Option"
                            || memberAccess.Name.Identifier.Text == "CandidateOption";
                    return false;
                });

            if (optionCount == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SharedUtilityDiagnostics.UT0143_ZeroOptions,
                    buildMethod.Locations.FirstOrDefault(),
                    type.Name));
            }
        }

        // ---- Helpers ------------------------------------------------------------

        private static bool HasUtilityDecisionAttribute(INamedTypeSymbol type)
        {
            return type.GetAttributes()
                .Any(a => a.AttributeClass != null
                       && a.AttributeClass.Name == "UtilityDecisionAttribute");
        }

        private static IMethodSymbol FindBuildMethod(INamedTypeSymbol type)
        {
            return type.GetMembers("Build")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m =>
                    m.IsStatic
                    && m.Parameters.Length == 1
                    && m.Parameters[0].Type.Name == "IUtilityDecisionBuilder");
        }

        private static bool IsPostureSelectDecision(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                if (attr.AttributeClass.Name != "UtilityDecisionAttribute") continue;

                // Kind is the third constructor argument (DecisionKind.PostureSelect == 1).
                if (attr.ConstructorArguments.Length >= 3)
                {
                    var kindArg = attr.ConstructorArguments[2];
                    if (kindArg.Value is byte bv && bv == 1) return true;
                    if (kindArg.Value is int iv && iv == 1) return true;
                    if (kindArg.Value is short sv && sv == 1) return true;
                    if (kindArg.Value != null && kindArg.Value.ToString() == "1") return true;
                }
            }
            return false;
        }

        private static bool IsConsiderInvocation(InvocationExpressionSyntax inv)
        {
            var name = inv.Expression as IdentifierNameSyntax;
            if (name != null) return name.Identifier.Text == "Consider";
            var memberAccess = inv.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null) return memberAccess.Name.Identifier.Text == "Consider";
            return false;
        }

        // ---- UT0151: ManeuverSelect must not bind Candidate or Target context ----

        private static bool IsManeuverSelectDecision(INamedTypeSymbol type)
        {
            foreach (var attr in type.GetAttributes())
            {
                if (attr.AttributeClass == null) continue;
                if (attr.AttributeClass.Name != "UtilityDecisionAttribute") continue;

                // Kind is the third constructor argument (DecisionKind.ManeuverSelect == 3).
                if (attr.ConstructorArguments.Length >= 3)
                {
                    var kindArg = attr.ConstructorArguments[2];
                    if (kindArg.Value is byte bv && bv == 3) return true;
                    if (kindArg.Value is int iv && iv == 3) return true;
                    if (kindArg.Value is short sv && sv == 3) return true;
                    if (kindArg.Value != null && kindArg.Value.ToString() == "3") return true;
                }
            }
            return false;
        }

        private static void CheckManeuverSelectContextBinding(
            SymbolAnalysisContext context,
            INamedTypeSymbol type,
            IMethodSymbol buildMethod,
            SyntaxNode buildSyntax)
        {
            if (!IsManeuverSelectDecision(type)) return;

            // Look for any member-access expression whose receiver is "Candidate" or "Target".
            var memberAccesses = buildSyntax.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>();

            foreach (var ma in memberAccesses)
            {
                var receiver = ma.Expression as IdentifierNameSyntax;
                if (receiver == null) continue;

                string receiverName = receiver.Identifier.Text;
                if (receiverName == "Candidate" || receiverName == "Target")
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SharedUtilityDiagnostics.UT0151_ManeuverSelectInvalidContext,
                        ma.GetLocation(),
                        type.Name,
                        receiverName + "." + ma.Name.Identifier.Text));
                    return;
                }
            }
        }
    }
}