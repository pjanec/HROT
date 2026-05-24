using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fbt.SourceGen
{
    [Generator]
    public class BTreeDefinitionGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor InvalidDefinitionMethod = new DiagnosticDescriptor(
            id: "BTree002",
            title: "Invalid BTreeDefinition method",
            messageFormat: "Method '{0}' annotated with [BTreeDefinition] must be static, return BehaviorTreeBlob, and have no parameters.",
            category: "BTreeSourceGen",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetDefinitionInfo(ctx))
                .Where(static m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(candidateMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        private static BTreeDefinitionInfo? GetDefinitionInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;

            if (symbol == null) return null;

            var definitionAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "BTreeDefinitionAttribute");

            if (definitionAttr == null) return null;

            // Extract tree name from attribute constructor argument
            string? treeName = null;
            if (definitionAttr.ConstructorArguments.Length > 0)
                treeName = definitionAttr.ConstructorArguments[0].Value as string;

            if (string.IsNullOrEmpty(treeName)) return null;

            // Validate: must be static, zero parameters, and return either
            // BehaviorTreeBlob or BTreeBuilder<TBB,TCtx> (builder-returning overload).
            bool returnsBlob = symbol.ReturnType.Name == "BehaviorTreeBlob";
            bool returnsBuilder = symbol.ReturnType is INamedTypeSymbol namedRet
                && namedRet.Name == "BTreeBuilder"
                && namedRet.TypeArguments.Length == 2;

            bool isValid = symbol.IsStatic
                && symbol.Parameters.Length == 0
                && (returnsBlob || returnsBuilder);

            return new BTreeDefinitionInfo
            {
                MethodName = symbol.Name,
                FullyQualifiedTypeName = symbol.ContainingType.ToDisplayString(),
                TreeName = treeName!,
                IsValid = isValid,
                ReturnsBuilder = returnsBuilder
            };
        }

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<BTreeDefinitionInfo?> methods)
        {
            // Report BTree002 diagnostic for invalid methods and skip them
            var valid = new List<BTreeDefinitionInfo>();
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (!m.IsValid)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(InvalidDefinitionMethod, Location.None, m.MethodName));
                }
                else
                {
                    valid.Add(m);
                }
            }

            // Do not emit FbtTreeCatalog.g.cs if no valid [BTreeDefinition] methods found
            if (valid.Count == 0) return;

            string assemblyName = compilation.AssemblyName ?? "Generated";
            string namespaceName = assemblyName + ".Generated";

            var source = GenerateCatalog(valid, namespaceName);
            context.AddSource("FbtTreeCatalog.g.cs", source);
        }

        // Replace any character that is not a letter, digit, or underscore with '_'.
        // Prepend '_' if the name starts with a digit.
        private static string SanitizeIdentifier(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        private static string GenerateCatalog(List<BTreeDefinitionInfo> methods, string namespaceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine();
            sb.AppendLine("namespace " + namespaceName);
            sb.AppendLine("{");
            sb.AppendLine("    public static class FbtTreeCatalog");
            sb.AppendLine("    {");

            foreach (var m in methods)
            {
                string safeName = SanitizeIdentifier(m.TreeName);
                if (m.ReturnsBuilder)
                {
                    // Builder-returning: call .Compile(treeName) in the generated property
                    sb.AppendLine("        public static global::Fbt.BehaviorTreeBlob Get" + safeName + "()");
                    sb.AppendLine("            => global::" + m.FullyQualifiedTypeName + "." + m.MethodName + "().Compile(\"" + m.TreeName + "\");");
                }
                else
                {
                    sb.AppendLine("        public static global::Fbt.BehaviorTreeBlob Get" + safeName + "()");
                    sb.AppendLine("            => global::" + m.FullyQualifiedTypeName + "." + m.MethodName + "();");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    internal class BTreeDefinitionInfo
    {
        public string MethodName { get; set; } = "";
        public string FullyQualifiedTypeName { get; set; } = "";
        public string TreeName { get; set; } = "";
        public bool IsValid { get; set; }
        public bool ReturnsBuilder { get; set; }
    }
}
