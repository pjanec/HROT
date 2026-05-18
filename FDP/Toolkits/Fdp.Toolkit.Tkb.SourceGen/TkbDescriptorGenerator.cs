using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.SourceGen
{
    [Generator]
    public class TkbDescriptorGenerator : IIncrementalGenerator
    {
        private const string TkbDescriptorAttributeMetadataName =
            "Fdp.Toolkit.Tkb.Attributes.TkbDescriptorAttribute";

        private static readonly DiagnosticDescriptor DuplicateHierarchicalName = new DiagnosticDescriptor(
            id: "TKB001",
            title: "Duplicate TKB hierarchical name",
            messageFormat: "Multiple types in assembly '{0}' share the TKB hierarchical name '{1}'",
            category: "TkbSourceGen",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Collect all type declaration syntax nodes that have attributes.
            // We collect the syntax nodes and combine with compilation for semantic resolution.
            var candidateSyntax = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is TypeDeclarationSyntax t && t.AttributeLists.Count > 0,
                    transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.Node)
                .Collect();

            var compilationAndTypes = context.CompilationProvider.Combine(candidateSyntax);

            context.RegisterSourceOutput(
                compilationAndTypes,
                static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<TypeDeclarationSyntax> candidateNodes)
        {
            // Resolve the attribute type from the compilation metadata.
            // Returns null if the attribute assembly is not referenced (safe early-out).
            INamedTypeSymbol? attrSymbol = compilation.GetTypeByMetadataName(
                TkbDescriptorAttributeMetadataName);
            if (attrSymbol == null) return;

            var valid = new List<TkbDescriptorInfo>();
            foreach (var typeDecl in candidateNodes)
            {
                SemanticModel model = compilation.GetSemanticModel(typeDecl.SyntaxTree);
                if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol)
                    continue;

                AttributeData? attr = typeSymbol.GetAttributes()
                    .FirstOrDefault(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSymbol));
                if (attr == null) continue;

                if (attr.ConstructorArguments.Length == 0) continue;
                string? hierarchicalName = attr.ConstructorArguments[0].Value as string;
                if (string.IsNullOrEmpty(hierarchicalName)) continue;

                valid.Add(new TkbDescriptorInfo
                {
                    HierarchicalName = hierarchicalName!,
                    FullyQualifiedTypeName = typeSymbol.ToDisplayString()
                });
            }

            if (valid.Count == 0) return;

            string assemblyName = compilation.AssemblyName ?? "Generated";
            string sanitizedName = SanitizeIdentifier(assemblyName);

            // Detect duplicates by hierarchical name (case-insensitive)
            var seen = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            var unique = new List<TkbDescriptorInfo>();
            foreach (var info in valid)
            {
                if (seen.ContainsKey(info.HierarchicalName))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(DuplicateHierarchicalName, Location.None,
                            assemblyName, info.HierarchicalName));
                }
                else
                {
                    seen[info.HierarchicalName] = info.FullyQualifiedTypeName;
                    unique.Add(info);
                }
            }

            string source = GenerateSource(sanitizedName, unique);
            context.AddSource("__TkbDescriptors_" + sanitizedName + ".g.cs", source);
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

        private static string GenerateSource(string sanitizedAssemblyName, List<TkbDescriptorInfo> types)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine();
            sb.AppendLine("internal static class __TkbDescriptors_" + sanitizedAssemblyName);
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("    internal static void Register()");
            sb.AppendLine("    {");

            foreach (var info in types)
            {
                sb.AppendLine("        global::Fdp.Toolkit.Tkb.TkbDescriptorRegistry.RegisterParser(");
                sb.AppendLine("            \"" + info.HierarchicalName + "\",");
                sb.AppendLine("            static (template, partId, jsonElement) =>");
                sb.AppendLine("            {");
                sb.AppendLine("                var dto = global::System.Text.Json.JsonSerializer.Deserialize<global::" + info.FullyQualifiedTypeName + ">(");
                sb.AppendLine("                    jsonElement,");
                sb.AppendLine("                    global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;");
                sb.AppendLine("                template.AddDescriptor(dto, partId);");
                sb.AppendLine("            });");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    internal sealed class TkbDescriptorInfo
    {
        public string HierarchicalName { get; set; } = "";
        public string FullyQualifiedTypeName { get; set; } = "";
    }
}
