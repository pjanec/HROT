using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    [Generator]
    public class UtilityDecisionGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor UT0140 = SharedUtilityDiagnostics.UT0140_MissingInterface;
        private static readonly DiagnosticDescriptor UT0141 = SharedUtilityDiagnostics.UT0141_MissingBuildMethod;
        private static readonly DiagnosticDescriptor UT0150 = SharedUtilityDiagnostics.UT0150_DuplicateAssetId;

        // ---- Initialize --------------------------------------------------------

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetDecisionInfo(ctx))
                .Where(static info => info != null);

            var compilationAndClasses = context.CompilationProvider.Combine(candidateClasses.Collect());

            context.RegisterSourceOutput(
                compilationAndClasses,
                static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        // ---- Collect class information -----------------------------------------

        private static DecisionInfo GetDecisionInfo(GeneratorSyntaxContext context)
        {
            var classSyntax = (ClassDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(classSyntax) as INamedTypeSymbol;
            if (symbol == null) return null;

            // Check for [UtilityDecision] attribute
            AttributeData attr = null;
            foreach (var a in symbol.GetAttributes())
            {
                if (a.AttributeClass?.Name == "UtilityDecisionAttribute" ||
                    a.AttributeClass?.Name == "UtilityDecision")
                {
                    attr = a;
                    break;
                }
            }
            if (attr == null) return null;

            var location = classSyntax.GetLocation();

            // Extract AssetId
            string assetId = null;
            if (attr.ConstructorArguments.Length > 0)
                assetId = attr.ConstructorArguments[0].Value as string;

            if (string.IsNullOrEmpty(assetId))
                return null;

            // Extract DisplayName
            string displayName = null;
            if (attr.ConstructorArguments.Length > 1)
                displayName = attr.ConstructorArguments[1].Value as string;
            if (displayName == null) displayName = assetId;

            // Extract Kind enum member name from the TypedConstant
            string kindName = null;
            if (attr.ConstructorArguments.Length > 2)
            {
                var kindConst = attr.ConstructorArguments[2];
                if (kindConst.Type is INamedTypeSymbol enumType && kindConst.Value != null)
                {
                    foreach (var member in enumType.GetMembers())
                    {
                        if (member is IFieldSymbol field && field.HasConstantValue &&
                            kindConst.Value.Equals(field.ConstantValue))
                        {
                            kindName = field.Name;
                            break;
                        }
                    }
                }
            }

            // Extract Category (optional, 4th arg)
            string category = "";
            if (attr.ConstructorArguments.Length > 3)
                category = attr.ConstructorArguments[3].Value as string ?? "";

            // Extract HysteresisBonus (optional, 5th arg)
            float hysteresisBonus = 0f;
            if (attr.ConstructorArguments.Length > 4 && attr.ConstructorArguments[4].Value is float hb)
                hysteresisBonus = hb;

            // Check that class implements IUtilityDecisionDefinition
            bool implementsInterface = false;
            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.Name == "IUtilityDecisionDefinition")
                {
                    implementsInterface = true;
                    break;
                }
            }
            if (!implementsInterface)
                return new DecisionInfo
                {
                    AssetId         = assetId,
                    Location        = location,
                    ErrorDescriptor = UT0140,
                    ErrorArgs       = new object[] { symbol.Name }
                };

            // Check for public static void Build(IUtilityDecisionBuilder)
            bool hasBuildMethod = false;
            foreach (var member in symbol.GetMembers("Build"))
            {
                if (member is IMethodSymbol method &&
                    method.IsStatic &&
                    method.ReturnsVoid &&
                    method.Parameters.Length == 1 &&
                    method.Parameters[0].Type.Name == "IUtilityDecisionBuilder")
                {
                    hasBuildMethod = true;
                    break;
                }
            }
            if (!hasBuildMethod)
                return new DecisionInfo
                {
                    AssetId         = assetId,
                    Location        = location,
                    ErrorDescriptor = UT0141,
                    ErrorArgs       = new object[] { symbol.Name }
                };

            // Compute blueprint ID via FNV-1a-32 (bit-cast to int)
            uint rawId = Fnv1a32(assetId);

            // Determine containing namespace
            string ns = GetFullNamespace(symbol.ContainingNamespace);

            // Analyse Build method body (syntactic only)
            MethodDeclarationSyntax buildSyntax = null;
            foreach (var m in classSyntax.Members)
            {
                if (m is MethodDeclarationSyntax mds && mds.Identifier.Text == "Build")
                {
                    buildSyntax = mds;
                    break;
                }
            }
            bool manifestFull  = false;
            int  optionCount   = 0;
            int  considerCount = 0;
            if (buildSyntax != null)
                (manifestFull, optionCount, considerCount) = AnalyzeBuildBody(buildSyntax);

            return new DecisionInfo
            {
                AssetId               = assetId,
                DisplayName           = displayName,
                KindName              = kindName,
                Category              = category,
                HysteresisBonus       = hysteresisBonus,
                ClassName             = symbol.Name,
                FullyQualifiedClassName = symbol.ToDisplayString(),
                Namespace             = ns,
                RawBlueprintId        = rawId,
                ManifestIsFull        = manifestFull,
                ManifestOptionCount   = optionCount,
                ManifestConsiderCount = considerCount,
                Location              = location,
                ErrorDescriptor       = null,
                ErrorArgs             = null
            };
        }

        // Syntactic walk of the Build method body to count Option/CandidateOption and Consider calls.
        // Returns (false, 0, 0) when the body contains dynamic constructs (loops, branches, locals).
        private static (bool isFull, int optionCount, int considerCount) AnalyzeBuildBody(
            MethodDeclarationSyntax buildSyntax)
        {
            foreach (var node in buildSyntax.DescendantNodes())
            {
                if (node is ForEachStatementSyntax ||
                    node is ForStatementSyntax ||
                    node is WhileStatementSyntax ||
                    node is DoStatementSyntax ||
                    node is IfStatementSyntax ||
                    node is SwitchStatementSyntax ||
                    node is LocalDeclarationStatementSyntax)
                    return (false, 0, 0);
            }

            int options  = 0;
            int considers = 0;
            foreach (var node in buildSyntax.DescendantNodes())
            {
                if (node is InvocationExpressionSyntax invocation)
                {
                    string name = null;
                    if (invocation.Expression is MemberAccessExpressionSyntax ma)
                        name = ma.Name.Identifier.Text;
                    else if (invocation.Expression is IdentifierNameSyntax idName)
                        name = idName.Identifier.Text;

                    if (name == "Option" || name == "CandidateOption") options++;
                    else if (name == "Consider") considers++;
                }
            }
            return (true, options, considers);
        }

        // ---- Execute -----------------------------------------------------------

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<DecisionInfo> infos)
        {
            // Report errors and collect valid infos
            var valid = new List<DecisionInfo>();
            foreach (var info in infos)
            {
                if (info.ErrorDescriptor != null)
                    context.ReportDiagnostic(Diagnostic.Create(info.ErrorDescriptor, info.Location, info.ErrorArgs));
                else
                    valid.Add(info);
            }

            if (valid.Count == 0) return;

            // Cross-class duplicate AssetId check (UT0150)
            var seen    = new Dictionary<string, DecisionInfo>();
            var deduped = new List<DecisionInfo>();
            foreach (var info in valid)
            {
                if (seen.ContainsKey(info.AssetId))
                    context.ReportDiagnostic(Diagnostic.Create(UT0150, info.Location, info.ClassName, info.AssetId));
                else
                {
                    seen[info.AssetId] = info;
                    deduped.Add(info);
                }
            }

            if (deduped.Count == 0) return;

            // Catalog namespace = first decision's namespace + ".Generated"
            // to avoid naming conflicts with any existing UtilityDecisionCatalog
            // class that may live in the decision classes' own namespace.
            string catalogNamespace = deduped[0].Namespace + ".Generated";

            context.AddSource("UtilityDecisionCatalog.g.cs", GenerateCatalog(deduped, catalogNamespace));
            context.AddSource("UtilityDecisionIds.g.cs",     GenerateIds(deduped));
        }

        // ---- Source emitters ---------------------------------------------------

        private static string GenerateCatalog(List<DecisionInfo> decisions, string catalogNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();
            sb.AppendLine("namespace " + catalogNamespace);
            sb.AppendLine("{");
            sb.AppendLine("    [global::Fdp.Toolkit.Utility.UtilityRegistrar]");
            sb.AppendLine("    public static class UtilityDecisionCatalog");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterAll(out global::Fdp.Toolkit.Utility.UtilityRegistry registry)");
            sb.AppendLine("        {");
            sb.AppendLine("            registry = new global::Fdp.Toolkit.Utility.UtilityRegistry();");

            foreach (var d in decisions)
            {
                string hexId  = "0x" + d.RawBlueprintId.ToString("X8");
                string kindArg = d.KindName != null
                    ? "global::Fdp.Toolkit.Utility.DecisionKind." + d.KindName
                    : "default(global::Fdp.Toolkit.Utility.DecisionKind)";

                sb.AppendLine("            {");
                sb.AppendLine("                var b = new global::Fdp.Toolkit.Utility.UtilityDecisionBuilder();");
                sb.AppendLine("                global::" + d.FullyQualifiedClassName + ".Build(b);");
                sb.AppendLine("                var attr = new global::Fdp.Toolkit.Utility.UtilityDecisionAttribute(");
                sb.AppendLine("                    " + QuoteString(d.AssetId) + ",");
                sb.AppendLine("                    " + QuoteString(d.DisplayName) + ",");
                sb.AppendLine("                    " + kindArg + ",");
                sb.AppendLine("                    " + QuoteString(d.Category) + ",");
                sb.AppendLine("                    " + FloatLiteral(d.HysteresisBonus) + ");");
                sb.AppendLine("                var def = b.Build(attr);");
                sb.AppendLine("                registry.Register(unchecked((int)" + hexId + "), def, " + FloatLiteral(d.HysteresisBonus) + ");");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // Manifest: static array of UtilityDecisionManifestEntry
            sb.AppendLine("        public static readonly global::Fdp.Toolkit.Utility.UtilityDecisionManifestEntry[] Manifest =");
            sb.AppendLine("        {");
            foreach (var d in decisions)
            {
                string hexId = "0x" + d.RawBlueprintId.ToString("X8");
                sb.AppendLine("            new global::Fdp.Toolkit.Utility.UtilityDecisionManifestEntry("
                    + "unchecked((int)" + hexId + "), "
                    + QuoteString(d.DisplayName) + ", "
                    + (d.ManifestIsFull ? "true" : "false") + ", "
                    + d.ManifestOptionCount + ", "
                    + d.ManifestConsiderCount + "),");
            }
            sb.AppendLine("        };");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string GenerateIds(List<DecisionInfo> decisions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");

            foreach (var d in decisions)
            {
                string hexId = "0x" + d.RawBlueprintId.ToString("X8");
                sb.AppendLine();
                sb.AppendLine("namespace " + d.Namespace);
                sb.AppendLine("{");
                sb.AppendLine("    partial class " + d.ClassName);
                sb.AppendLine("    {");
                sb.AppendLine("        // blueprintId: FNV-1a-32(\"" + d.AssetId + "\") == " + hexId);
                sb.AppendLine("        public const int Id = unchecked((int)" + hexId + ");");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        // ---- Helpers -----------------------------------------------------------

        // 32-bit FNV-1a hash. basis=2166136261, prime=16777619.
        private static uint Fnv1a32(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return hash;
        }

        private static string GetFullNamespace(INamespaceSymbol ns)
        {
            if (ns == null || ns.IsGlobalNamespace) return string.Empty;
            var parent = GetFullNamespace(ns.ContainingNamespace);
            return string.IsNullOrEmpty(parent) ? ns.Name : parent + "." + ns.Name;
        }

        private static string QuoteString(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string FloatLiteral(float f)
        {
            return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        // ---- Data class --------------------------------------------------------

        private class DecisionInfo
        {
            public string AssetId;
            public string DisplayName;
            public string KindName;
            public string Category;
            public float  HysteresisBonus;
            public string ClassName;
            public string FullyQualifiedClassName;
            public string Namespace;
            public uint   RawBlueprintId;
            public bool   ManifestIsFull;
            public int    ManifestOptionCount;
            public int    ManifestConsiderCount;
            public Location             Location;
            public DiagnosticDescriptor ErrorDescriptor;
            public object[]             ErrorArgs;
        }
    }
}
