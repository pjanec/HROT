using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fdp.Toolkit.Diagnostics.Analyzers
{
    /// <summary>
    /// Source generator that scans for classes decorated with
    /// <c>[GizmoProjector]</c> and emits a <c>GizmoRegistrar.RegisterAll</c> method
    /// for each namespace group found in the compilation being processed.
    ///
    /// <para>Emits diagnostic FDP_002 (warning) when a decorated class does not
    /// implement <c>IStatelessGizmo</c>.</para>
    /// </summary>
    [Generator]
    public sealed class GizmoRegistrarGenerator : ISourceGenerator
    {
        // ── Diagnostics ──────────────────────────────────────────────────────

        private static readonly DiagnosticDescriptor FDP002_NotStateless = new DiagnosticDescriptor(
            id:               "FDP_002",
            title:            "GizmoProjector class must implement IStatelessGizmo",
            messageFormat:    "Type '{0}' is decorated with [GizmoProjector] but does not implement IStatelessGizmo and was not registered.",
            category:         "Fdp.Gizmos",
            defaultSeverity:  DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ── ISyntaxReceiver ───────────────────────────────────────────────────

        private sealed class GizmoSyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0)
                    Candidates.Add(cds);
            }
        }

        // ── ISourceGenerator ──────────────────────────────────────────────────

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new GizmoSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not GizmoSyntaxReceiver receiver)
                return;

            var compilation = context.Compilation;

            // Resolve well-known symbols.
            INamedTypeSymbol? gizmoProjectorAttr = compilation.GetTypeByMetadataName(
                "Fdp.Toolkit.Diagnostics.Gizmos.GizmoProjectorAttribute");
            INamedTypeSymbol? statelessGizmoType = compilation.GetTypeByMetadataName(
                "Fdp.Toolkit.Diagnostics.Gizmos.IStatelessGizmo");
            INamedTypeSymbol? settingsRegistryType = compilation.GetTypeByMetadataName(
                "Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry");

            if (gizmoProjectorAttr == null) return; // attribute assembly not referenced

            // Collect qualifying gizmos grouped by namespace.
            // Key = namespace string, Value = list of (classSymbol, requiresSettings, componentTypeNames[])
            var groups = new Dictionary<string, List<GizmoEntry>>();

            foreach (var cds in receiver.Candidates)
            {
                SemanticModel model = compilation.GetSemanticModel(cds.SyntaxTree);
                if (model.GetDeclaredSymbol(cds) is not INamedTypeSymbol classSymbol) continue;

                // Must have [GizmoProjector] attribute.
                AttributeData? attr = classSymbol.GetAttributes().FirstOrDefault(
                    a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, gizmoProjectorAttr));
                if (attr == null) continue;

                // Must implement IStatelessGizmo.
                if (statelessGizmoType != null &&
                    !ImplementsInterface(classSymbol, statelessGizmoType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FDP002_NotStateless,
                        cds.GetLocation(),
                        classSymbol.ToDisplayString()));
                    continue;
                }

                // Collect required component type names from the attribute constructor args.
                var componentTypeNames = new List<string>();
                if (attr.ConstructorArguments.Length > 0)
                {
                    foreach (var arg in attr.ConstructorArguments)
                    {
                        if (arg.Kind == TypedConstantKind.Array)
                        {
                            foreach (var element in arg.Values)
                            {
                                if (element.Value is INamedTypeSymbol typeArg)
                                    componentTypeNames.Add(typeArg.ToDisplayString());
                            }
                        }
                        else if (arg.Value is INamedTypeSymbol typeArg)
                        {
                            componentTypeNames.Add(typeArg.ToDisplayString());
                        }
                    }
                }

                // Detect constructor that accepts GizmoSettingsRegistry.
                bool requiresSettings = false;
                if (settingsRegistryType != null)
                {
                    requiresSettings = classSymbol.Constructors.Any(c =>
                        c.Parameters.Any(p =>
                            SymbolEqualityComparer.Default.Equals(p.Type, settingsRegistryType)));
                }

                string ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (!groups.TryGetValue(ns, out var list))
                {
                    list = new List<GizmoEntry>();
                    groups[ns] = list;
                }
                list.Add(new GizmoEntry(classSymbol.ToDisplayString(), requiresSettings, componentTypeNames));
            }

            // Emit one source file per namespace group.
            foreach (var kvp in groups)
            {
                string ns       = kvp.Key;
                var entries     = kvp.Value;
                string source   = BuildSource(ns, entries);
                string hintName = ns.Replace('.', '_') + "_GizmoRegistrar.g.cs";
                context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol iface)
        {
            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iface));
        }

        private static string BuildSource(string ns, List<GizmoEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using Fdp.Toolkit.Diagnostics.Gizmos;");
            sb.AppendLine("using Fdp.Toolkit.Diagnostics.Gizmos.Settings;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine("    // Auto-generated by GizmoRegistrarGenerator.");
            sb.AppendLine("    public static partial class GizmoRegistrar");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterAll(");
            sb.AppendLine("            GizmoRegistry gizmoRegistry,");
            sb.AppendLine("            StatelessGizmoRegistry statelessRegistry,");
            sb.AppendLine("            GizmoSettingsRegistry settings)");
            sb.AppendLine("        {");

            foreach (var entry in entries)
            {
                string ctorArgs = entry.RequiresSettings ? "settings" : string.Empty;
                sb.Append($"            statelessRegistry.Register(new {entry.FullTypeName}({ctorArgs}),");
                sb.AppendLine();
                sb.AppendLine($"                new Type[]");
                sb.AppendLine("                {");
                foreach (var comp in entry.ComponentTypeNames)
                    sb.AppendLine($"                    typeof({comp}),");
                sb.AppendLine("                });");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── Inner record ──────────────────────────────────────────────────────

        private sealed class GizmoEntry
        {
            public string FullTypeName { get; }
            public bool RequiresSettings { get; }
            public List<string> ComponentTypeNames { get; }

            public GizmoEntry(string fullTypeName, bool requiresSettings, List<string> componentTypeNames)
            {
                FullTypeName       = fullTypeName;
                RequiresSettings   = requiresSettings;
                ComponentTypeNames = componentTypeNames;
            }
        }
    }
}
